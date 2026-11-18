using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Collections;
using Revisions.Core.Partage;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>
/// Partage du contenu avec un camarade : export d'un fichier .atome et import de celui d'un camarade.
/// Utilisé par les Réglages (export + import) et par la Bibliothèque (import, mis en avant quand elle est vide).
/// Le travail sur les fichiers est dans <see cref="PartageContenu"/> ; ici on gère les dialogues et l'avancement.
/// </summary>
public partial class PartageViewModel(EtatApplication etat, MainViewModel main, DepotBibliotheque bibliotheque) : ObservableObject
{
    /// <summary>Dossier de contenu par défaut d'un camarade : %LOCALAPPDATA%\RevisionsBTS\contenu.</summary>
    public static string DossierContenuParDefaut => Path.Combine(BaseDeDonnees.DossierDonnees, "contenu");

    [ObservableProperty] private bool _inclureDocuments;
    [ObservableProperty] private string _tailleDocuments = "";
    [ObservableProperty] private bool _enCours;
    [ObservableProperty] private double _progression;
    [ObservableProperty] private string? _message;

    /// <summary>Vrai s'il y a un dossier de contenu à exporter (packs/ présent).</summary>
    public bool PeutExporter => RacineActuelle() is not null;

    /// <summary>Dossier qui contient packs/, planning/ et cours/ : c'est le « dossier Cours » des réglages.</summary>
    private string? RacineActuelle()
    {
        string? packs = etat.Reglages.DossierPacks;
        if (string.IsNullOrEmpty(packs) || !Directory.Exists(packs)) return null;
        string? racine = etat.Reglages.DossierCours;
        if (string.IsNullOrEmpty(racine) || !Directory.Exists(racine)) racine = Path.GetDirectoryName(packs.TrimEnd('\\', '/'));
        return racine;
    }

    /// <summary>Calcule la taille des documents d'origine (en arrière-plan : il y a beaucoup de fichiers).</summary>
    public async Task EstimerAsync()
    {
        if (RacineActuelle() is not { } racine) { TailleDocuments = "aucun document trouvé"; return; }
        TailleDocuments = "calcul…";
        long octets = await Task.Run(() => PartageContenu.EstimerTailleDocuments(racine));
        TailleDocuments = octets == 0 ? "aucun document trouvé" : Taille(octets);
    }

    private static string Taille(long octets) =>
        octets >= 1L << 30 ? $"{octets / (double)(1L << 30):0.0} Go" : $"{Math.Max(1, octets >> 20)} Mo";

    // ---- Export ----------------------------------------------------------------------------

    [RelayCommand]
    private async Task ExporterAsync()
    {
        if (RacineActuelle() is not { } racine) return;
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer mon contenu",
            Filter = "Contenu Atome (*.atome)|*.atome",
            FileName = $"contenu-atome-{DateTime.Now:yyyy-MM-dd}.atome",
            DefaultExt = PartageContenu.Extension,
        };
        if (dialogue.ShowDialog() != true) return;

        string destination = dialogue.FileName;
        bool documents = InclureDocuments;
        EnCours = true;
        Progression = 0;
        Message = null;
        try
        {
            // « Progress » ramène l'avancement sur le fil de l'interface : la fenêtre reste fluide.
            var avancement = new Progress<double>(v => Progression = v);
            var manifeste = await Task.Run(() => PartageContenu.Exporter(racine, destination, documents, avancement));
            Message = $"Fichier créé : {Texte.Pluriel(manifeste.NbPacks, "pack", "packs")}"
                      + (manifeste.DocumentsInclus ? $" et {Texte.Pluriel(manifeste.NbDocuments, "document", "documents")}" : ", sans les documents")
                      + $". Envoie-le à ton camarade : {destination}";
        }
        catch (Exception e)
        {
            Journal.Erreur("Export du contenu", e);
            Message = "L'export a échoué : " + e.Message;
        }
        finally
        {
            EnCours = false;
        }
    }

    // ---- Import ----------------------------------------------------------------------------

    [RelayCommand]
    private async Task ImporterAsync()
    {
        var dialogue = new OpenFileDialog { Title = "Importer du contenu", Filter = "Contenu Atome (*.atome;*.zip)|*.atome;*.zip" };
        if (dialogue.ShowDialog() != true) return;
        await ImporterFichierAsync(dialogue.FileName);
    }

    /// <summary>
    /// Importe un fichier .atome déjà choisi : vérification, fusionner ou nouveau dossier, extraction, récapitulatif.
    /// Appelé par les Réglages et par le bouton « Importer… » de la Bibliothèque (qui accepte aussi les .atome).
    /// </summary>
    public async Task ImporterFichierAsync(string archive)
    {
        EnCours = true;
        Progression = 0;
        Message = null;
        try
        {
            // 1) Le manifeste est vérifié avant de poser la moindre question.
            var manifeste = await Task.Run(() => PartageContenu.LireManifeste(archive));

            // 2) Où extraire ? Si un dossier de packs valide existe déjà, on demande.
            string cible = DossierContenuParDefaut;
            bool changerReglages = true;
            if (RacineActuelle() is { } racine && DossierPacksValide())
            {
                string? choix = Views.ServiceDialogue.Demander("Importer du contenu",
                    "Tu as déjà un dossier de packs :\n" + etat.Reglages.DossierPacks +
                    "\n\nFusionner : le contenu reçu est ajouté à ce dossier (les fichiers de même nom sont mis à jour).\n" +
                    "Nouveau dossier : le contenu reçu va dans un nouveau dossier que l'application utilise désormais. L'ancien n'est pas touché.",
                    new Views.ChoixDialogue("Annuler", "annuler", Annulation: true),
                    new Views.ChoixDialogue("Nouveau dossier", "remplacer"),
                    new Views.ChoixDialogue("Fusionner", "fusionner", Principal: true));
                if (choix is null or "annuler") return;
                // On ne peut fusionner que si le dossier des packs est bien « <racine>\packs ».
                if (choix == "fusionner" && EstSousLaRacine(racine))
                {
                    cible = racine;
                    changerReglages = false;
                }
                else if (choix == "fusionner")
                {
                    Views.ServiceDialogue.Informer("Importer du contenu",
                        "Ton dossier des packs n'est pas rangé comme celui du contenu partagé (un dossier « packs » à côté de « cours »), la fusion n'est pas possible. Le contenu va dans un nouveau dossier.");
                }
            }

            // 3) Extraction (sans bloquer l'interface).
            var avancement = new Progress<double>(v => Progression = v);
            var resume = await Task.Run(() => PartageContenu.Extraire(archive, cible, avancement));

            // 4) Les réglages pointent vers le nouveau dossier (« packs » dedans, et le dossier Cours = la racine).
            if (changerReglages)
            {
                etat.Reglages.DossierPacks = Path.Combine(cible, PartageContenu.DossierPacks);
                etat.Reglages.DossierCours = cible;
                etat.Enregistrer();
            }

            // 5) Import habituel (la progression est conservée) puis récapitulatif.
            // Les nouveaux cours forment une collection à part (nom de l'auteur si le fichier le donne, sinon nom du fichier).
            await main.ApresImportContenuAsync(resume.FichiersPacks,
                Collections.CleImportee(resume.Auteur ?? Path.GetFileNameWithoutExtension(archive)),
                Collections.NomImporte(resume.Auteur, archive));
            int cartes = bibliotheque.ListerSeances(etat.Jour).Sum(s => s.NbCartes);
            Message = $"Contenu importé : {Texte.Pluriel(resume.NbPacks, "pack", "packs")}, {Texte.Pluriel(cartes, "carte", "cartes")}, "
                      + (resume.DocumentsInclus ? "documents d'origine inclus." : "sans les documents d'origine (le bouton « Voir le PDF » ne marchera pas).");
            Views.ServiceDialogue.Informer("Importer du contenu", Message);
        }
        catch (ArchiveInvalideException e)
        {
            Message = e.Message;
            Views.ServiceDialogue.Informer("Importer du contenu", e.Message);
        }
        catch (Exception e)
        {
            Journal.Erreur("Import du contenu", e);
            Message = "L'import a échoué : " + e.Message;
        }
        finally
        {
            EnCours = false;
        }
    }

    private bool DossierPacksValide() =>
        etat.Reglages.DossierPacks is { Length: > 0 } d && Directory.Exists(d)
        && Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories).Any();

    private bool EstSousLaRacine(string racine) =>
        string.Equals(Path.GetFullPath(Path.Combine(racine, PartageContenu.DossierPacks)).TrimEnd('\\'),
                      Path.GetFullPath(etat.Reglages.DossierPacks!).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
