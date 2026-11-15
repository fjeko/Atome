using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une séance dans la bibliothèque, avec ses textes prêts à afficher.</summary>
public sealed class LigneSeance(ResumeSeance seance)
{
    public string PackId => seance.PackId;
    public string Titre => seance.Seance;
    public bool Lu => seance.Lu;
    public bool ADesCartes => seance.NbCartes > 0;
    public double Maitrise => seance.NbCartes == 0 ? 0 : (double)seance.NbMaitrisees / seance.NbCartes;

    public string Detail
    {
        get
        {
            var morceaux = new List<string> { seance.NbCartes == 1 ? "1 carte" : $"{seance.NbCartes} cartes" };
            if (seance.NbDues > 0) morceaux.Add($"{seance.NbDues} à revoir");
            if (seance.NbNouvelles > 0) morceaux.Add($"{seance.NbNouvelles} à découvrir");
            if (seance.NbMaitrisees > 0) morceaux.Add(Texte.Pluriel(seance.NbMaitrisees, "maîtrisée", "maîtrisées"));
            return string.Join("  ·  ", morceaux);
        }
    }
}

/// <summary>Une partie (séquence, compétence) et ses séances.</summary>
public sealed record GroupePartie(string Titre, IReadOnlyList<LigneSeance> Seances);

/// <summary>Une matière et ses parties.</summary>
public sealed record GroupeMatiere(string Titre, string Detail, IReadOnlyList<GroupePartie> Parties, bool Recherche)
{
    // Matières dépliées par l'utilisateur : on s'en souvient tant que l'application est ouverte.
    private static readonly HashSet<string> Ouvertes = [];

    /// <summary>Vrai si la matière est dépliée (toujours pendant une recherche).</summary>
    public bool Ouvert
    {
        get => Recherche || Ouvertes.Contains(Titre);
        set
        {
            if (Recherche) return;
            if (value) Ouvertes.Add(Titre); else Ouvertes.Remove(Titre);
        }
    }
}

/// <summary>Écran Bibliothèque : cours rangés par matière, partie et séance (US2), import (US1).</summary>
public partial class BibliothequeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotBibliotheque _bibliotheque;
    private readonly DepotRevisions _revisions;
    private readonly ImportateurPacks _importateur;
    private List<ResumeSeance> _seances = [];

    [ObservableProperty]
    private IReadOnlyList<GroupeMatiere> _matieres = [];

    /// <summary>Texte de recherche : filtre les séances par titre, partie ou matière.</summary>
    [ObservableProperty]
    private string _recherche = "";

    [ObservableProperty]
    private bool _estVide;

    [ObservableProperty]
    private bool _importEnCours;

    public BibliothequeViewModel(MainViewModel main, EtatApplication etat, DepotBibliotheque bibliotheque,
        DepotRevisions revisions, ImportateurPacks importateur)
    {
        _main = main;
        _etat = etat;
        _bibliotheque = bibliotheque;
        _revisions = revisions;
        _importateur = importateur;
        Charger();
    }

    partial void OnRechercheChanged(string value) => Construire();

    private void Charger()
    {
        _seances = _bibliotheque.ListerSeances(_etat.Jour);
        Construire();
    }

    /// <summary>Regroupe les séances (déjà triées par la requête) en matières puis en parties.</summary>
    private void Construire()
    {
        string filtre = Recherche.Trim();
        bool Correspond(ResumeSeance s) =>
            filtre.Length == 0
            || s.Seance.Contains(filtre, StringComparison.CurrentCultureIgnoreCase)
            || s.Partie.Contains(filtre, StringComparison.CurrentCultureIgnoreCase)
            || s.Matiere.Contains(filtre, StringComparison.CurrentCultureIgnoreCase);

        Matieres = _seances
            .Where(Correspond)
            .GroupBy(s => (s.Bloc, s.Matiere))
            .Select(matiere => new GroupeMatiere(
                // Certaines matières contiennent déjà leur bloc dans leur nom : on ne le répète pas.
                matiere.Key.Bloc is null || matiere.Key.Matiere.StartsWith(matiere.Key.Bloc, StringComparison.CurrentCultureIgnoreCase)
                    ? matiere.Key.Matiere
                    : $"{matiere.Key.Bloc} – {matiere.Key.Matiere}",
                $"{Texte.Pluriel(matiere.Count(), "séance", "séances")}, {Texte.Pluriel(matiere.Sum(s => s.NbCartes), "carte", "cartes")}"
                + (matiere.Sum(s => s.NbDues) is > 0 and var dues ? $", {dues} à revoir" : ""),
                matiere
                    .GroupBy(s => s.Sequence is null ? s.Partie : $"{s.Partie} – {s.Sequence}")
                    .Select(partie => new GroupePartie(partie.Key, partie.Select(s => new LigneSeance(s)).ToList()))
                    .ToList(),
                Recherche: filtre.Length > 0))
            .ToList();
        EstVide = _seances.Count == 0;
    }

    [RelayCommand]
    private void Lire(LigneSeance seance) => _main.OuvrirLecture(seance.PackId);

    /// <summary>Révise toutes les cartes d'une séance, dans l'ordre du cours (US12).</summary>
    [RelayCommand]
    private void Reviser(LigneSeance seance) => _main.DemarrerSession(_revisions.CartesDuPack(seance.PackId), "seance");

    /// <summary>Import manuel d'un ou plusieurs fichiers de pack.</summary>
    [RelayCommand]
    private async Task ImporterAsync()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Importer des packs",
            Filter = "Packs (*.json)|*.json",
            Multiselect = true,
            InitialDirectory = _etat.Reglages.DossierPacks ?? "",
        };
        if (dialogue.ShowDialog() != true) return;

        string[] fichiers = dialogue.FileNames;
        await ImporterAsync(() => fichiers.Select(f => _importateur.ImporterFichier(f, _etat.Reglages.DossierCours)).ToList());
    }

    /// <summary>Relit le dossier des packs (utile quand de nouveaux packs viennent d'y être ajoutés).</summary>
    [RelayCommand]
    private Task ActualiserAsync() => ImporterAsync(_main.ImporterDossier);

    private async Task ImporterAsync(Func<List<ResultatImport>> import)
    {
        ImportEnCours = true;
        try
        {
            var resultats = await Task.Run(import);
            Charger();
            _main.AnnoncerImport(resultats, afficherRapportSiRien: true);
        }
        finally
        {
            ImportEnCours = false;
        }
    }
}
