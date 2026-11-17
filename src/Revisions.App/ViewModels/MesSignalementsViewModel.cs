using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Signalements;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une ligne de la liste « Mes signalements ».</summary>
public sealed record LigneSignalement(Signalement Signalement)
{
    public string Motif => VocabulaireSignalement.Libelle(Signalement.Motif);
    public string Titre => Signalement.Cible == CibleSignalement.Carte
        ? $"Carte · {Signalement.Seance}"
        : $"Section · {Signalement.Titre}";
    public string Date => Signalement.DateHeure.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Etat => Signalement.Exporte ? "Exporté" : "Nouveau";
    public string? Commentaire => Signalement.Commentaire;
    /// <summary>Début du texte signalé, pour reconnaître la carte.</summary>
    public string Extrait => Core.Packs.TexteSimple.Extrait(Signalement.TexteRecto, 120);
}

/// <summary>
/// Écran « Mes signalements » : la liste, la suppression et l'export vers un fichier JSON
/// (format : docs/FORMAT-SIGNALEMENTS.md) à envoyer pour que les packs soient corrigés.
/// </summary>
public sealed partial class MesSignalementsViewModel : ObservableObject
{
    private const string CleAuteur = "auteur";

    private readonly DepotSignalements _depot;
    private readonly DepotProfil _profil;
    private readonly EtatApplication _etat;
    private readonly MainViewModel _main;
    private readonly bool _initialise;

    public ObservableCollection<LigneSignalement> Lignes { get; } = [];

    /// <summary>Prénom ou pseudo mis dans le fichier et dans son nom (facultatif).</summary>
    [ObservableProperty] private string _auteur;

    [ObservableProperty] private string? _message;

    public bool EstVide => Lignes.Count == 0;

    public MesSignalementsViewModel(DepotSignalements depot, DepotProfil profil, EtatApplication etat, MainViewModel main)
    {
        _depot = depot;
        _profil = profil;
        _etat = etat;
        _main = main;
        _auteur = profil.LireValeur(CleAuteur) ?? "";
        _initialise = true;
        Recharger();
    }

    /// <summary>Le prénom est enregistré dans les réglages dès qu'on le change.</summary>
    partial void OnAuteurChanged(string value)
    {
        if (_initialise) _profil.EcrireValeur(CleAuteur, value.Trim());
    }

    private void Recharger()
    {
        Lignes.Clear();
        foreach (var s in _depot.Lister()) Lignes.Add(new LigneSignalement(s));
        OnPropertyChanged(nameof(EstVide));
        ExporterCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Supprimer(LigneSignalement ligne)
    {
        _depot.Supprimer(ligne.Signalement.Id);
        Message = null;
        Recharger();
    }

    [RelayCommand(CanExecute = nameof(PeutExporter))]
    private void Exporter()
    {
        var signalements = _depot.Lister();
        if (signalements.Count == 0) return;

        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer mes signalements",
            Filter = "Signalements (*.json)|*.json",
            FileName = ExportSignalements.NomFichier(Auteur, _etat.Jour),
            DefaultExt = ".json",
        };
        if (dialogue.ShowDialog() != true) return;

        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        File.WriteAllText(dialogue.FileName,
            ExportSignalements.Creer(signalements, Auteur, _etat.Maintenant, version),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var nouveaux = signalements.Where(s => !s.Exporte).ToList();
        Message = $"C'est fait : {signalements.Count} signalement(s) dans {Path.GetFileName(dialogue.FileName)}. Envoie ce fichier à la personne qui s'occupe des cours.";
        if (nouveaux.Count > 0 && MessageBox.Show(
                $"Fichier enregistré. Marquer ces {nouveaux.Count} signalement(s) comme exportés ?\n\n(Ils restent dans la liste ; tu pourras les supprimer si tu veux.)",
                "Signalements exportés", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _depot.MarquerExportes(nouveaux.Select(s => s.Id));
            Recharger();
        }
    }

    private bool PeutExporter() => Lignes.Count > 0;

    [RelayCommand]
    private void Retour() => _main.AllerReglages();
}
