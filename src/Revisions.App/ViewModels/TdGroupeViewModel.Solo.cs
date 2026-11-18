using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.TdGroupe;

namespace Revisions.App.ViewModels;

/// <summary>
/// Un thème du TD solo : un générateur intégré (Matrices, Graphes…), les exercices rédigés d'une matière des packs,
/// ou « Tout le domaine » (<see cref="EstTout"/>).
/// </summary>
public sealed record ThemeSolo(string Nom, string Detail, IGenerateurASujets? Generateur = null, string? MatiereRedigee = null, bool EstTout = false);

/// <summary>Une notion d'un thème (un sujet d'exercice, ou une séance pour les exercices rédigés).</summary>
/// <param name="Cles">Sujets retenus ; null = toutes les notions du thème.</param>
public sealed record NotionSolo(string Nom, string Detail, IReadOnlySet<string>? Cles);

/// <summary>Un grand domaine (Mathématiques, Réseau…) et ses thèmes.</summary>
public sealed record DomaineSolo(string Nom, string Detail, IReadOnlyList<ThemeSolo> Themes);

/// <summary>Une durée proposée (« 30 min »).</summary>
public sealed record DureeTd(int Minutes, string Libelle);

/// <summary>
/// TD solo (voir docs/td-solo.md) : thèmes cochés, durée, niveau, correction après chaque exercice
/// ou à la fin comme à l'examen. Les exercices ratés renvoient leurs notions dans les révisions.
/// </summary>
public partial class TdGroupeViewModel
{
    // Réglages gardés le temps que l'application est ouverte.
    private static bool _dernierModeSolo = true;
    private static string? _dernierDomaine, _dernierTheme, _derniereNotion;
    private static int _derniereDuree = 30, _dernierNiveauSolo = 1;
    private static bool? _derniereCorrectionFin, _dernierSeulementLu;

    private List<ExerciceSolo>? _solo;
    /// <summary>Mode examen : réponses gardées de chaque exercice validé, corrigées à la fin.</summary>
    private readonly List<List<QuestionTdViewModel>> _reponses = [];
    private readonly List<bool> _indicesVusExamen = [];
    private bool _passeCorrection;
    private bool _examen;
    private DispatcherTimer? _timerSolo;
    private DateTime _debutSolo;

    /// <summary>Onglet du choix : seul (par défaut) ou à plusieurs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeGroupe))]
    private bool _modeSolo = true;

    public bool ModeGroupe => !ModeSolo;

    /// <summary>Le TD en cours est un TD solo.</summary>
    public bool EstSolo => _solo is not null;

    /// <summary>TD solo corrigé à la fin : on valide chaque exercice sans voir la correction.</summary>
    public bool EnExamen => _examen;

    // ---- Choix en trois niveaux : domaine › thème › notion ----------------------------------

    public IReadOnlyList<DomaineSolo> Domaines { get; private set; } = [];

    [ObservableProperty] private DomaineSolo? _domaineChoisi;
    [ObservableProperty] private IReadOnlyList<ThemeSolo> _themesDuDomaine = [];
    [ObservableProperty] private ThemeSolo? _themeSoloChoisi;
    [ObservableProperty] private IReadOnlyList<NotionSolo> _notionsDuTheme = [];
    [ObservableProperty] private NotionSolo? _notionChoisie;

    /// <summary>La liste des notions a un sens (un seul thème choisi).</summary>
    public bool ChoixNotionPossible => NotionsDuTheme.Count > 1;

    /// <summary>Cartes « exercice » des packs, chargées une fois par matière.</summary>
    private readonly Dictionary<string, List<Revisions.Core.Sessions.CarteAReviser>> _cartesRedigees = [];

    partial void OnDomaineChoisiChanged(DomaineSolo? value)
    {
        if (value is null) return;
        _dernierDomaine = value.Nom;
        ThemesDuDomaine = [new ThemeSolo(value.Themes.Count > 1 ? "Tout le domaine" : value.Themes[0].Nom,
            value.Themes.Count > 1 ? "Un mélange de tous ses thèmes" : value.Themes[0].Detail, EstTout: true), .. value.Themes.Count > 1 ? value.Themes : []];
        ThemeSoloChoisi = ThemesDuDomaine.FirstOrDefault(t => t.Nom == _dernierTheme) ?? ThemesDuDomaine[0];
    }

    partial void OnThemeSoloChoisiChanged(ThemeSolo? value)
    {
        if (value is null) return;
        _dernierTheme = value.Nom;
        var theme = value.EstTout && DomaineChoisi!.Themes.Count == 1 ? DomaineChoisi.Themes[0] : value;
        var notions = new List<NotionSolo> { new("Toutes les notions", "Dans l'ordre du cours, comme une feuille de révision", null) };
        if (!theme.EstTout && theme.Generateur is { } g)
            notions.AddRange(g.Sujets.Select(s => new NotionSolo(s.Nom,
                s.NiveauMin > 1 ? "Niveau « Comme à l'examen » seulement" : $"Environ {s.MinutesPour(2)} min par exercice", new HashSet<string> { s.Cle })));
        else if (!theme.EstTout && theme.MatiereRedigee is { } matiere)
            notions.AddRange(CartesRedigees(matiere).GroupBy(c => (c.PackId, c.Seance)).Select(gr =>
                new NotionSolo(gr.Key.Seance, $"{gr.Count()} exercice{(gr.Count() > 1 ? "s" : "")}", gr.Select(c => c.Carte.Id).ToHashSet())));
        NotionsDuTheme = notions;
        NotionChoisie = notions.FirstOrDefault(n => n.Nom == _derniereNotion) ?? notions[0];
        OnPropertyChanged(nameof(ChoixNotionPossible));
    }

    partial void OnNotionChoisieChanged(NotionSolo? value) { if (value is not null) _derniereNotion = value.Nom; }

    private List<Revisions.Core.Sessions.CarteAReviser> CartesRedigees(string matiere)
    {
        if (!_cartesRedigees.TryGetValue(matiere, out var cartes))
            _cartesRedigees[matiere] = cartes = _revisions.CartesExerciceDeLaMatiere(matiere);
        return cartes;
    }

    public IReadOnlyList<DureeTd> Durees { get; } =
        [new(15, "15 min"), new(30, "30 min"), new(60, "1 h"), new(120, "2 h (comme à l'examen)")];

    public IReadOnlyList<string> NiveauxSolo => GenerateurASujets.NomsNiveaux;

    [ObservableProperty] private DureeTd _dureeChoisie = null!;
    [ObservableProperty] private int _niveauSolo;

    /// <summary>Correction à la fin (comme à l'examen) plutôt qu'après chaque exercice.</summary>
    [ObservableProperty] private bool _correctionALaFin;

    /// <summary>Seulement les notions des séances déjà ouvertes en lecture.</summary>
    [ObservableProperty] private bool _seulementLu;

    /// <summary>Il y a au moins une séance lue (sinon la case n'a pas de sens).</summary>
    public bool ADesSeancesLues { get; private set; }

    /// <summary>Chrono global du TD solo (« 12 min / 30 min »), un simple repère.</summary>
    [ObservableProperty] private string? _chrono;

    /// <summary>Fin d'un TD solo : notions renvoyées dans les révisions.</summary>
    [ObservableProperty] private string? _notionsARevoir;

    /// <summary>Texte du bouton de la phase « On cherche ».</summary>
    public string TexteVerifier => EnExamen && !_passeCorrection ? "Valider et continuer" : "Vérifier";

    partial void OnModeSoloChanged(bool value) { _dernierModeSolo = value; MessageErreur = null; }
    partial void OnNiveauSoloChanged(int value) { if (value >= 0) _dernierNiveauSolo = value; }
    partial void OnSeulementLuChanged(bool value) => _dernierSeulementLu = value;
    partial void OnCorrectionALaFinChanged(bool value) => _derniereCorrectionFin = value;

    partial void OnDureeChoisieChanged(DureeTd value)
    {
        _derniereDuree = value.Minutes;
        // Par défaut : correction au fil de l'eau pour un TD court, à la fin pour un TD long (sauf choix de l'étudiant).
        if (_derniereCorrectionFin is null) _correctionALaFin = value.Minutes >= 60;
        OnPropertyChanged(nameof(CorrectionALaFin));
    }

    private void InitialiserSolo()
    {
        _modeSolo = _dernierModeSolo;

        // Un domaine par matière des générateurs (dans l'ordre du catalogue), puis les exercices rédigés des packs.
        var domaines = _catalogue.Generateurs.OfType<IGenerateurASujets>()
            .GroupBy(g => g.Matiere)
            .Select(gr => new DomaineSolo(gr.Key, string.Join(", ", gr.Select(g => g.Theme)),
                gr.Select(g => new ThemeSolo(g.Theme, $"{g.Sujets.Count} notions", g)).ToList()))
            .ToList();
        var rediges = _depotTd.MatieresAvecExercices()
            .Select(m => new ThemeSolo(m.Matiere, $"{m.Nombre} exercice{(m.Nombre > 1 ? "s" : "")} rédigé{(m.Nombre > 1 ? "s" : "")}", MatiereRedigee: m.Matiere))
            .ToList();
        if (rediges.Count > 0)
            domaines.Add(new DomaineSolo("Exercices rédigés de mes packs", "Tu compares toi-même avec la correction", rediges));
        Domaines = domaines;
        DomaineChoisi = domaines.FirstOrDefault(d => d.Nom == _dernierDomaine) ?? domaines[0];

        ADesSeancesLues = _bibliotheque.DernieresLectures().Count > 0;
        _seulementLu = _dernierSeulementLu ?? ADesSeancesLues;
        _niveauSolo = _dernierNiveauSolo;
        _dureeChoisie = Durees.FirstOrDefault(d => d.Minutes == _derniereDuree) ?? Durees[1];
        _correctionALaFin = _derniereCorrectionFin ?? _dureeChoisie.Minutes >= 60;
    }

    /// <summary>Onglet « Seul » ou « À plusieurs ».</summary>
    [RelayCommand]
    private void ChoisirMode(string mode) => ModeSolo = mode == "solo";

    /// <summary>Compose et lance un TD solo.</summary>
    [RelayCommand]
    private void CreerSolo()
    {
        MessageErreur = null;
        if (DomaineChoisi is null || ThemeSoloChoisi is null) return;
        var themes = ThemeSoloChoisi.EstTout ? DomaineChoisi.Themes : [ThemeSoloChoisi];

        var generateurs = new List<IGenerateurASujets>();
        var sections = _depotTd.Sections();
        foreach (var t in themes)
        {
            if (t.Generateur is not null) { generateurs.Add(t.Generateur); continue; }
            var cartes = CartesRedigees(t.MatiereRedigee!).Select(c => c.Carte).ToList();
            var titres = cartes.Select(c => c.SectionId).Distinct()
                .Where(sections.ContainsKey).ToDictionary(s => s, s => sections[s].Titre);
            generateurs.Add(new GenerateurRediges(t.MatiereRedigee!, cartes, titres));
        }

        var lus = _bibliotheque.DernieresLectures().Keys.ToHashSet();
        bool filtreLu = SeulementLu && ADesSeancesLues;
        var cles = NotionChoisie?.Cles;
        Func<IGenerateurASujets, SujetTd, bool>? permis = filtreLu || cles is not null
            ? (_, s) => (cles is null || cles.Contains(s.Cle))
                        && (!filtreLu || s.SectionId is { } id && sections.TryGetValue(id, out var sec) && lus.Contains(sec.PackId))
            : null;
        int niveau = Math.Clamp(NiveauSolo + 1, 1, 3);
        var exercices = CompositeurTd.Composer(new DemandeTdSolo(generateurs, DureeChoisie.Minutes, niveau, permis, _depotTd.Historique()),
            new GenerateurAleatoire((ulong)Random.Shared.NextInt64()));
        if (exercices.Count == 0)
        {
            MessageErreur = filtreLu
                ? "Aucun exercice ici dans les séances que tu as lues. Lis la séance du cours, ou décoche « Seulement ce que j'ai lu »."
                : "Aucun exercice disponible ici à ce niveau (le « projet complet » demande le niveau « Comme à l'examen »).";
            return;
        }

        _solo = exercices;
        _examen = CorrectionALaFin;
        _passeCorrection = false;
        _reponses.Clear();
        _indicesVusExamen.Clear();
        NotionsARevoir = null;
        string titre = cles is not null ? NotionChoisie!.Nom : ThemeSoloChoisi.EstTout ? DomaineChoisi.Nom : ThemeSoloChoisi.Nom;
        var parametres = new ParametresTd("SOLO", 1, niveau, exercices.Count, 0);
        var td = new Td(parametres, "", $"TD · {titre} · {DureeChoisie.Libelle}", exercices.Select(e => e.Exercice).ToList(), "");
        OnPropertyChanged(nameof(EstSolo));
        OnPropertyChanged(nameof(EnExamen));
        OnPropertyChanged(nameof(TexteVerifier));
        DemarrerChrono();
        Commencer(td);
    }

    // ==== Mode examen : valider sans corriger, puis tout corriger à la fin =====================

    private void ValiderSansCorriger()
    {
        _reponses.Add(Questions.ToList());
        _indicesVusExamen.Add(_indiceVu);
        _index++;
        if (_index < _td!.Exercices.Count) AllerAuPret();
        else CommencerPasseCorrection();
    }

    private void CommencerPasseCorrection()
    {
        ArreterChrono();
        _passeCorrection = true;
        _index = 0;
        _issues.Clear();
        OnPropertyChanged(nameof(TexteVerifier));
        AfficherCorrectionSauvee();
    }

    /// <summary>Remet à l'écran les réponses gardées de l'exercice courant, puis les vérifie et montre la correction.</summary>
    private void AfficherCorrectionSauvee()
    {
        Avancement = $"Correction {_index + 1} / {_reponses.Count}";
        Progression = (double)_index / _reponses.Count;
        TitreExercice = Exercice.Titre;
        Questions.Clear();
        foreach (var q in _reponses[_index]) Questions.Add(q);
        _indiceVu = _indicesVusExamen[_index];
        IndiceDisponible = !_indiceVu;
        NotionDisponible = Exercice.SectionId is { } s && _bibliotheque.PackDeLaSection(s) is not null;
        OnPropertyChanged(nameof(TexteSuivant));
        Phase = PhaseTd.Recherche;
        Verifier();
    }

    // ==== Fin : historique et remise en révision ==============================================

    private void TerminerSolo()
    {
        ArreterChrono();
        var jour = _etat.Jour;
        var notions = new List<string>();
        for (int i = 0; i < _issues.Count && i < _solo!.Count; i++)
        {
            var e = _solo[i];
            _depotTd.EnregistrerExercice(jour, e.Prefixe, e.Sujet.Cle, e.Sujet.SectionId, _issues[i]);
            if (_issues[i] == IssueExercice.PasEncore && e.Sujet.SectionId is { } section && _depotTd.RemettreEnRevision(section, jour) > 0)
                notions.Add(e.Sujet.Nom);
        }
        NotionsARevoir = notions.Count == 0
            ? null
            : "Ces notions reviennent dans tes prochaines révisions : " + string.Join(", ", notions.Distinct()) + ".";
        Journal.Info("TD solo", $"{_issues.Count} exercices faits, {notions.Count} notion(s) remise(s) en révision.");
    }

    /// <summary>Remet à zéro l'état du TD solo (retour au choix).</summary>
    private void OublierSolo()
    {
        ArreterChrono();
        _solo = null;
        _examen = false;
        _passeCorrection = false;
        _reponses.Clear();
        _indicesVusExamen.Clear();
        OnPropertyChanged(nameof(EstSolo));
        OnPropertyChanged(nameof(EnExamen));
        OnPropertyChanged(nameof(TexteVerifier));
    }

    // ==== Chrono global =======================================================================

    private void DemarrerChrono()
    {
        ArreterChrono();
        _debutSolo = DateTime.Now;
        int prevu = DureeChoisie.Minutes;
        Chrono = $"0 min / {Libelle(prevu)}";
        _timerSolo = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timerSolo.Tick += (_, _) =>
        {
            int ecoule = (int)(DateTime.Now - _debutSolo).TotalMinutes;
            // Rien ne se passe au-delà : c'est seulement un repère.
            Chrono = $"{Libelle(ecoule)} / {Libelle(prevu)}";
        };
        _timerSolo.Start();
    }

    private void ArreterChrono()
    {
        _timerSolo?.Stop();
        _timerSolo = null;
        Chrono = null;
    }

    private static string Libelle(int minutes) => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";
}
