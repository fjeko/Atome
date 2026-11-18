using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.TdGroupe;

namespace Revisions.App.ViewModels;

/// <summary>Un thème proposé pour le TD solo (case à cocher).</summary>
public sealed partial class ThemeSolo(IGenerateurASujets? generateur, string matiere, string nom, string detail, string? matiereRedigee) : ObservableObject
{
    /// <summary>Générateur intégré, ou null pour les exercices rédigés d'une matière (chargés à la création du TD).</summary>
    public IGenerateurASujets? Generateur { get; } = generateur;
    public string Matiere { get; } = matiere;
    public string Nom { get; } = nom;
    /// <summary>Petite ligne sous le nom (« 6 sujets », « 42 exercices »).</summary>
    public string Detail { get; } = detail;
    /// <summary>Pour les exercices rédigés : la matière des packs.</summary>
    public string? MatiereRedigee { get; } = matiereRedigee;

    [ObservableProperty] private bool _coche;
}

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
    private static HashSet<string>? _derniersThemes;
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

    public ObservableCollection<ThemeSolo> ThemesSolo { get; } = [];

    /// <summary>Thèmes générés par Atome (correction automatique).</summary>
    public IEnumerable<ThemeSolo> ThemesGeneres => ThemesSolo.Where(t => t.Generateur is not null);

    /// <summary>Exercices rédigés des packs, par matière (auto-évalués).</summary>
    public IEnumerable<ThemeSolo> ThemesRediges => ThemesSolo.Where(t => t.Generateur is null);

    public bool ADesRediges => ThemesRediges.Any();

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
        foreach (var g in _catalogue.Generateurs.OfType<IGenerateurASujets>())
            ThemesSolo.Add(new ThemeSolo(g, g.Matiere, g.Theme, $"{g.Matiere} · {g.Sujets.Count} sortes d'exercices", null));
        foreach (var (matiere, nombre) in _depotTd.MatieresAvecExercices())
            ThemesSolo.Add(new ThemeSolo(null, matiere, matiere, $"{nombre} exercice{(nombre > 1 ? "s" : "")} rédigé{(nombre > 1 ? "s" : "")}", matiere));
        foreach (var t in ThemesSolo)
            t.Coche = _derniersThemes?.Contains(t.Nom) ?? t.Matiere == "Mathématiques" && t.Generateur is not null;

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
        var coches = ThemesSolo.Where(t => t.Coche).ToList();
        _derniersThemes = coches.Select(t => t.Nom).ToHashSet();
        if (coches.Count == 0) { MessageErreur = "Coche au moins un thème."; return; }

        var generateurs = new List<IGenerateurASujets>();
        var sections = _depotTd.Sections();
        foreach (var t in coches)
        {
            if (t.Generateur is not null) { generateurs.Add(t.Generateur); continue; }
            var cartes = _revisions.CartesExerciceDeLaMatiere(t.MatiereRedigee!).Select(c => c.Carte).ToList();
            var titres = cartes.Select(c => c.SectionId).Distinct()
                .Where(sections.ContainsKey).ToDictionary(s => s, s => sections[s].Titre);
            generateurs.Add(new GenerateurRediges(t.MatiereRedigee!, cartes, titres));
        }

        var lus = _bibliotheque.DernieresLectures().Keys.ToHashSet();
        Func<IGenerateurASujets, SujetTd, bool>? permis = SeulementLu && ADesSeancesLues
            ? (_, s) => s.SectionId is { } id && sections.TryGetValue(id, out var sec) && lus.Contains(sec.PackId)
            : null;
        int niveau = Math.Clamp(NiveauSolo + 1, 1, 3);
        var exercices = CompositeurTd.Composer(new DemandeTdSolo(generateurs, DureeChoisie.Minutes, niveau, permis, _depotTd.Historique()),
            new GenerateurAleatoire((ulong)Random.Shared.NextInt64()));
        if (exercices.Count == 0)
        {
            MessageErreur = permis is not null
                ? "Aucun exercice pour ces thèmes dans les séances que tu as lues. Lis la séance du cours, ou décoche « Seulement ce que j'ai lu »."
                : "Aucun exercice disponible pour ces thèmes à ce niveau.";
            return;
        }

        _solo = exercices;
        _examen = CorrectionALaFin;
        _passeCorrection = false;
        _reponses.Clear();
        _indicesVusExamen.Clear();
        NotionsARevoir = null;
        string themes = string.Join(", ", exercices.Select(e => e.Theme).Distinct());
        var parametres = new ParametresTd("SOLO", 1, niveau, exercices.Count, 0);
        var td = new Td(parametres, "", $"TD solo · {DureeChoisie.Libelle} · {themes}", exercices.Select(e => e.Exercice).ToList(), "");
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
