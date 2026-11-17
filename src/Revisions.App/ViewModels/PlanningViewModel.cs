using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Planning;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>
/// Écran « Planning » : la proposition du CNED en frise, par matière.
/// Les dates se changent ici ; elles sont enregistrées en base comme dates personnelles,
/// le fichier du planning n'est jamais modifié.
/// </summary>
public partial class PlanningViewModel : ObservableObject
{
    /// <summary>Largeur de la frise, en pixels.</summary>
    public const double LargeurFrise = 760;
    /// <summary>Hauteur d'une ligne de périodes dans la frise.</summary>
    public const double HauteurLigne = 26;

    private readonly MainViewModel _main;
    private readonly DepotPlanning _depot;
    private readonly DateOnly _jour;
    private readonly DepotBibliotheque _bibliotheque;
    private readonly PlanningCned? _original;
    private bool _chargement = true;

    public bool APlanning => _original is not null;
    public bool SansPlanning => _original is null;
    public string Titre { get; } = "";
    public string? Source { get; }

    /// <summary>Interrupteur général : suivre le planning (priorité des cartes, échéances sur l'accueil).</summary>
    [ObservableProperty]
    private bool _suivre;

    /// <summary>Ne pas tenir compte des quiz d'auto-évaluation.</summary>
    [ObservableProperty]
    private bool _masquerQuiz;

    /// <summary>Message après un import manuel (réussite ou erreurs).</summary>
    [ObservableProperty]
    private string? _messageImport;

    public ObservableCollection<MatierePlanning> Matieres { get; } = [];

    /// <summary>Points à vérifier dans le fichier (dates, préfixes sans pack).</summary>
    public IReadOnlyList<string> Avertissements { get; } = [];
    public bool AAvertissements => Avertissements.Count > 0;

    /// <summary>Repères de mois au-dessus des frises.</summary>
    public IReadOnlyList<RepereMois> Mois { get; } = [];
    /// <summary>Position de la ligne « aujourd'hui » dans la frise (négative si hors de l'année du planning).</summary>
    public double PositionAujourdhui { get; } = -10;

    internal DateOnly DebutFrise { get; }
    internal DateOnly FinFrise { get; }

    public PlanningViewModel(MainViewModel main, EtatApplication etat, DepotPlanning depot, DepotBibliotheque bibliotheque)
    {
        _main = main;
        _depot = depot;
        _jour = etat.Jour;
        _bibliotheque = bibliotheque;

        var lecture = depot.LireActif();
        _original = lecture?.Planning;
        if (_original is null)
        {
            _chargement = false;
            return;
        }

        Titre = _original.Titre;
        Source = depot.SourceActive();
        var reglages = depot.ChargerReglages(_original.Id);
        Suivre = reglages.Suivre;
        MasquerQuiz = reglages.MasquerQuiz;

        var idsDePacks = bibliotheque.ListerSeances(_jour).Select(s => s.PackId).ToList();
        Avertissements = [.. lecture!.Avertissements, .. LecteurPlanning.VerifierCibles(_original, idsDePacks)];

        // Étendue de la frise : du premier lundi au dernier dimanche du planning (dates du fichier et personnelles).
        var effectif = PlanningEffectif.AppliquerSurcharges(_original, reglages.Surcharges);
        var dates = _original.Periodes.SelectMany(p => new[] { p.Debut, p.Fin })
            .Concat(effectif.Periodes.SelectMany(p => new[] { p.Debut, p.Fin }))
            .Concat(_original.Devoirs.Concat(effectif.Devoirs).SelectMany(d => new[] { d.Semaine, d.DateLimite }))
            .ToList();
        DebutFrise = new DateOnly(dates.Min().Year, dates.Min().Month, 1);
        var fin = dates.Max();
        FinFrise = new DateOnly(fin.Year, fin.Month, 1).AddMonths(1).AddDays(-1);

        var mois = new List<RepereMois>();
        for (var m = DebutFrise; m <= FinFrise; m = m.AddMonths(1))
            mois.Add(new RepereMois(m.ToString("MMM", new System.Globalization.CultureInfo("fr-FR")).TrimEnd('.'), Position(m)));
        Mois = mois;
        if (_jour >= DebutFrise && _jour <= FinFrise) PositionAujourdhui = Position(_jour);

        foreach (string matiere in _original.Matieres)
        {
            var periodes = effectif.Periodes.Where(p => p.Matiere == matiere)
                .Select(p => new PeriodePlanning(this, _original.Periodes.First(o => o.Cle == p.Cle), p)).ToList();
            var devoirs = effectif.Devoirs.Where(d => d.Matiere == matiere)
                .Select(d => new DevoirPlanning(this, _original.Devoirs.First(o => o.Cle == d.Cle), d)).ToList();
            Matieres.Add(new MatierePlanning(this, matiere, !reglages.MatieresIgnorees.Contains(matiere), periodes, devoirs));
        }
        foreach (var m in Matieres) m.Placer();
        _chargement = false;
    }

    /// <summary>Position horizontale d'un jour dans la frise.</summary>
    internal double Position(DateOnly jour) =>
        LargeurFrise * (jour.DayNumber - DebutFrise.DayNumber) / Math.Max(1, FinFrise.DayNumber - DebutFrise.DayNumber + 1);

    internal DateOnly Aujourdhui => _jour;
    internal bool QuizMasques => MasquerQuiz;

    partial void OnSuivreChanged(bool value)
    {
        if (!_chargement) _depot.DefinirSuivre(value);
    }

    partial void OnMasquerQuizChanged(bool value)
    {
        if (_chargement) return;
        _depot.DefinirMasquerQuiz(value);
        foreach (var m in Matieres)
            foreach (var d in m.Devoirs) d.ActualiserVisibilite();
    }

    internal void EnregistrerMatiere(string matiere, bool suivie)
    {
        if (!_chargement) _depot.DefinirMatiereSuivie(_original!.Id, matiere, suivie);
    }

    /// <summary>Enregistre une date personnelle, ou l'efface quand on revient aux dates du fichier.</summary>
    internal void EnregistrerSurcharge(string cle, Surcharge? surcharge)
    {
        if (!_chargement) _depot.DefinirSurcharge(_original!.Id, cle, surcharge);
    }

    /// <summary>Remet toutes les dates du fichier.</summary>
    [RelayCommand]
    private void ReinitialiserDates()
    {
        if (_original is null) return;
        _depot.EffacerSurcharges(_original.Id);
        _main.AllerPlanning();
    }

    /// <summary>Import manuel d'un fichier de planning : validé, puis il devient le planning actif.</summary>
    [RelayCommand]
    private void Importer()
    {
        var dialogue = new OpenFileDialog { Title = "Importer un planning", Filter = "Planning (*.json)|*.json" };
        if (dialogue.ShowDialog() != true) return;
        ResultatImportPlanning resultat;
        try
        {
            resultat = _depot.Importer(File.ReadAllText(dialogue.FileName), dialogue.FileName, activer: true);
        }
        catch (IOException e)
        {
            MessageImport = "Le fichier n'a pas pu être lu : " + e.Message;
            return;
        }
        if (!resultat.Enregistre)
        {
            MessageImport = "Ce planning n'a pas été importé :\n• " + string.Join("\n• ", resultat.Lecture.Erreurs);
            return;
        }
        _main.AllerPlanning();
    }

    // ---- Import de l'Excel du CNED ----------------------------------------------------------------

    /// <summary>Aperçu de l'import de l'Excel, affiché avant de valider (null : rien en cours).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApercuVisible))]
    private ApercuImportExcel? _apercu;

    public bool ApercuVisible => Apercu is not null;

    /// <summary>
    /// Bouton « Importer l'Excel du CNED » : lit le fichier, le convertit en planning et montre un aperçu.
    /// Rien n'est enregistré avant « Valider l'import ».
    /// </summary>
    [RelayCommand]
    private void ImporterExcel()
    {
        var dialogue = new OpenFileDialog { Title = "Importer l'Excel du CNED", Filter = "Excel (*.xlsx)|*.xlsx" };
        if (dialogue.ShowDialog() != true) return;
        MessageImport = null;
        Apercu = null;

        ResultatExcelCned resultat;
        try
        {
            // FileShare.ReadWrite : le fichier peut être resté ouvert dans Excel.
            using var flux = new FileStream(dialogue.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            resultat = ImporteurExcelCned.Convertir(flux, dialogue.FileName, _jour);
        }
        catch (IOException e)
        {
            MessageImport = "Le fichier n'a pas pu être lu : " + e.Message;
            return;
        }
        if (resultat.Planning is not { } planning)
        {
            MessageImport = "Ce fichier n'a pas été importé :\n• " + string.Join("\n• ", resultat.Erreurs);
            return;
        }

        var ids = _bibliotheque.ListerSeances(_jour).Select(s => s.PackId).ToList();
        var avertissements = resultat.Avertissements.Concat(LecteurPlanning.VerifierCibles(planning, ids)).ToList();
        int conservees = _depot.SurchargesConservees(planning);
        Apercu = new ApercuImportExcel(planning, resultat.Json!, dialogue.FileName, avertissements, conservees, _original?.Titre);
    }

    /// <summary>Valide l'aperçu : le planning remplace l'actif, les dates personnelles des éléments identiques sont gardées.</summary>
    [RelayCommand]
    private void ValiderImportExcel()
    {
        if (Apercu is not { } apercu) return;
        var resultat = _depot.Remplacer(apercu.Json, apercu.Fichier);
        if (!resultat.Enregistre)
        {
            MessageImport = "Ce planning n'a pas été importé :\n• " + string.Join("\n• ", resultat.Lecture.Erreurs);
            Apercu = null;
            return;
        }
        _main.AllerPlanning();
    }

    [RelayCommand]
    private void AnnulerImportExcel() => Apercu = null;
}

/// <summary>Une ligne de l'aperçu : titre de matière ou période.</summary>
public sealed record LigneApercu(string Texte, bool EstTitre);

/// <summary>Ce que l'import de l'Excel va faire, montré avant validation.</summary>
public sealed class ApercuImportExcel
{
    public string Json { get; }
    public string Fichier { get; }
    public string Titre { get; }
    public string Resume { get; }
    public string? Remplacement { get; }
    public IReadOnlyList<LigneApercu> Lignes { get; }
    public IReadOnlyList<string> Avertissements { get; }
    public bool AAvertissements => Avertissements.Count > 0;

    public ApercuImportExcel(PlanningCned planning, string json, string fichier, IReadOnlyList<string> avertissements,
        int surchargesConservees, string? titreActuel)
    {
        Json = json;
        Fichier = fichier;
        Avertissements = avertissements;
        Titre = planning.Titre;

        int sansCartes = planning.Periodes.Count(p => p.Cibles.Count == 0);
        int deduites = planning.Periodes.Count(p => p.Approx);
        Resume = $"{planning.Periodes.Count} périodes ({deduites} avec une fin déduite, {sansCartes} sans cartes) et {planning.Devoirs.Count} devoirs ou quiz, en {planning.Matieres.Count} matières.";
        Remplacement = titreActuel is null
            ? null
            : $"Il remplacera « {titreActuel} ». " + (surchargesConservees == 0
                ? "Aucune date personnelle à reprendre."
                : $"{surchargesConservees} date(s) personnelle(s) sur des éléments identiques seront gardées.");

        var lignes = new List<LigneApercu>();
        foreach (string matiere in planning.Matieres)
        {
            lignes.Add(new LigneApercu(matiere, true));
            foreach (var p in planning.Periodes.Where(p => p.Matiere == matiere).OrderBy(p => p.Debut))
                lignes.Add(new LigneApercu(
                    $"{p.Libelle} : {p.Debut:dd/MM/yyyy} → {p.Fin:dd/MM/yyyy}" + (p.Approx ? " (fin déduite)" : "") + (p.Cibles.Count == 0 ? " (sans cartes)" : ""), false));
            var devoirs = planning.Devoirs.Where(d => d.Matiere == matiere).OrderBy(d => d.Semaine).ToList();
            if (devoirs.Count > 0)
                lignes.Add(new LigneApercu("Devoirs : " + string.Join(", ", devoirs.Select(d => $"{d.Code} ({d.DateLimite:dd/MM})")), false));
        }
        Lignes = lignes;
    }
}

/// <summary>Un nom de mois au-dessus de la frise.</summary>
public sealed record RepereMois(string Nom, double Gauche);

/// <summary>Une matière du planning : son interrupteur, sa frise et ses dates.</summary>
public partial class MatierePlanning : ObservableObject
{
    private readonly PlanningViewModel _ecran;

    public string Nom { get; }
    public IReadOnlyList<PeriodePlanning> Periodes { get; }
    public IReadOnlyList<DevoirPlanning> Devoirs { get; }

    /// <summary>Interrupteur de la matière.</summary>
    [ObservableProperty]
    private bool _suivie;

    /// <summary>Hauteur de la frise de la matière (une ligne par période qui se chevauche, plus la ligne des devoirs).</summary>
    [ObservableProperty]
    private double _hauteurFrise;

    public double HauteurPeriodes => HauteurFrise - 24;

    public MatierePlanning(PlanningViewModel ecran, string nom, bool suivie, List<PeriodePlanning> periodes, List<DevoirPlanning> devoirs)
    {
        _ecran = ecran;
        Nom = nom;
        _suivie = suivie;
        Periodes = periodes;
        Devoirs = devoirs;
        foreach (var p in periodes) p.Matiere = this;
        foreach (var d in devoirs) d.Matiere = this;
    }

    partial void OnSuivieChanged(bool value) => _ecran.EnregistrerMatiere(Nom, value);
    partial void OnHauteurFriseChanged(double value) => OnPropertyChanged(nameof(HauteurPeriodes));

    /// <summary>
    /// Place les périodes dans la frise : chaque période prend la première ligne libre,
    /// pour que deux périodes qui se chevauchent ne se cachent pas.
    /// </summary>
    internal void Placer()
    {
        var finsDesLignes = new List<DateOnly>();
        foreach (var p in Periodes.OrderBy(p => p.Debut))
        {
            int ligne = finsDesLignes.FindIndex(f => f < p.DebutDate);
            if (ligne < 0)
            {
                finsDesLignes.Add(p.FinDate);
                ligne = finsDesLignes.Count - 1;
            }
            else finsDesLignes[ligne] = p.FinDate;
            p.Placer(ligne);
        }
        foreach (var d in Devoirs) d.Placer();
        HauteurFrise = Math.Max(1, finsDesLignes.Count) * PlanningViewModel.HauteurLigne + 24;
    }

    internal PlanningViewModel Ecran => _ecran;
}

/// <summary>Une période : barre de la frise et ses deux dates modifiables.</summary>
public partial class PeriodePlanning : ObservableObject
{
    private readonly PlanningViewModel _ecran;
    private readonly Periode _original;
    internal MatierePlanning Matiere { get; set; } = null!;

    public string Libelle => _original.Libelle;
    /// <summary>Mention « à confirmer » (dates déduites ou répartition supposée).</summary>
    public bool AConfirmer => _original.AConfirmer;
    public string InfoConfirmer => _original.Hypothese ? "à confirmer (répartition supposée)" : "à confirmer (fin déduite)";
    public bool Continu => _original.Continu;
    public bool SansPack => _original.Cibles.Count == 0;

    [ObservableProperty]
    private DateTime? _debut;

    [ObservableProperty]
    private DateTime? _fin;

    [ObservableProperty]
    private double _gauche;

    [ObservableProperty]
    private double _largeur;

    [ObservableProperty]
    private double _haut;

    /// <summary>La période est en cours aujourd'hui (barre mise en avant).</summary>
    [ObservableProperty]
    private bool _enCours;

    /// <summary>Les dates ne sont plus celles du fichier.</summary>
    [ObservableProperty]
    private bool _modifiee;

    internal DateOnly DebutDate => Debut is { } d ? DateOnly.FromDateTime(d) : _original.Debut;
    internal DateOnly FinDate => Fin is { } f ? DateOnly.FromDateTime(f) : _original.Fin;

    public string Dates => $"du {DebutDate:dd/MM/yyyy} au {FinDate:dd/MM/yyyy}";
    public string Bulle => $"{Libelle}, {Dates}" + (AConfirmer ? " (" + InfoConfirmer + ")" : "");

    public PeriodePlanning(PlanningViewModel ecran, Periode original, Periode effective)
    {
        _ecran = ecran;
        _original = original;
        _debut = effective.Debut.ToDateTime(TimeOnly.MinValue);
        _fin = effective.Fin.ToDateTime(TimeOnly.MinValue);
        _modifiee = effective.Debut != original.Debut || effective.Fin != original.Fin;
    }

    partial void OnDebutChanged(DateTime? value) => Changer();
    partial void OnFinChanged(DateTime? value) => Changer();

    private void Changer()
    {
        if (Matiere is null) return; // construction
        // Une fin avant le début n'a pas de sens : on garde au moins une semaine.
        if (FinDate < DebutDate)
        {
            Fin = DebutDate.AddDays(6).ToDateTime(TimeOnly.MinValue);
            return;
        }
        Modifiee = DebutDate != _original.Debut || FinDate != _original.Fin;
        _ecran.EnregistrerSurcharge(_original.Cle, Modifiee ? new Surcharge(DebutDate, FinDate) : null);
        OnPropertyChanged(nameof(Dates));
        OnPropertyChanged(nameof(Bulle));
        Matiere.Placer();
    }

    /// <summary>Revient aux dates du fichier.</summary>
    [RelayCommand]
    private void Reinitialiser()
    {
        Debut = _original.Debut.ToDateTime(TimeOnly.MinValue);
        Fin = _original.Fin.ToDateTime(TimeOnly.MinValue);
    }

    internal void Placer(int ligne)
    {
        Gauche = _ecran.Position(DebutDate);
        Largeur = Math.Max(4, _ecran.Position(FinDate.AddDays(1)) - Gauche);
        Haut = ligne * PlanningViewModel.HauteurLigne;
        EnCours = _ecran.Aujourdhui >= DebutDate && _ecran.Aujourdhui <= FinDate;
    }
}

/// <summary>Un devoir : repère dans la frise et sa semaine modifiable.</summary>
public partial class DevoirPlanning : ObservableObject
{
    private readonly PlanningViewModel _ecran;
    private readonly Devoir _original;
    internal MatierePlanning Matiere { get; set; } = null!;

    public string Code => _original.Code;
    public bool EstQuiz => _original.EstQuiz;
    public string Type => _original.Note ? "Devoir noté" : "Quiz d'auto-évaluation";
    public string? Remarque => _original.Remarque;

    /// <summary>Un jour de la semaine du devoir (ramené au lundi à l'enregistrement).</summary>
    [ObservableProperty]
    private DateTime? _semaine;

    [ObservableProperty]
    private double _gauche;

    [ObservableProperty]
    private bool _visible = true;

    [ObservableProperty]
    private bool _modifie;

    internal DateOnly Lundi => Revisions.Core.Temps.JourDeRevision.LundiDe(Semaine is { } s ? DateOnly.FromDateTime(s) : _original.Semaine);

    public string Texte => $"semaine du {Lundi:dd/MM/yyyy}";
    public string Bulle => $"{Type} {Code}, {Texte}";

    public DevoirPlanning(PlanningViewModel ecran, Devoir original, Devoir effectif)
    {
        _ecran = ecran;
        _original = original;
        _semaine = effectif.Semaine.ToDateTime(TimeOnly.MinValue);
        _modifie = effectif.Semaine != original.Semaine;
        _visible = !(original.EstQuiz && ecran.QuizMasques);
    }

    partial void OnSemaineChanged(DateTime? value)
    {
        if (Matiere is null) return;
        Modifie = Lundi != _original.Semaine;
        _ecran.EnregistrerSurcharge(_original.Cle, Modifie ? new Surcharge(Lundi, null) : null);
        OnPropertyChanged(nameof(Texte));
        OnPropertyChanged(nameof(Bulle));
        Placer();
    }

    [RelayCommand]
    private void Reinitialiser() => Semaine = _original.Semaine.ToDateTime(TimeOnly.MinValue);

    internal void Placer() => Gauche = _ecran.Position(Lundi.AddDays(6)) - 5;

    internal void ActualiserVisibilite() => Visible = !(EstQuiz && _ecran.QuizMasques);
}
