using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Listes de choix des deux menus déroulants (le premier choix, « — », veut dire « comme le niveau au-dessus »).</summary>
public static class ListesAcquis
{
    public static readonly string[] Avancements = ["—", "Pas vu", "Vu", "Révisé"];
    public static readonly string[] Comprehensions = ["—", "Je découvre", "Fragile", "Correct", "Solide"];

    public static Avancement? VersAvancement(int indice) => indice == 0 ? null : (Avancement)(indice - 1);
    public static Comprehension? VersComprehension(int indice) => indice == 0 ? null : (Comprehension)(indice - 1);
    public static int Indice(Avancement? a) => a is null ? 0 : (int)a.Value + 1;
    public static int Indice(Comprehension? c) => c is null ? 0 : (int)c.Value + 1;
}

/// <summary>Une ligne de l'arbre des acquis : un niveau (matière, partie, séquence) ou une séance.</summary>
public partial class NoeudAcquis : ObservableObject
{
    public required string Titre { get; init; }
    /// <summary>Clés des niveaux depuis la matière (une séance se termine par « § » + identifiant du pack).</summary>
    public required List<string> Cles { get; init; }
    /// <summary>Bloc de la matière (« Bloc 1 »), ou null.</summary>
    public string? Etiquette { get; init; }
    public int Profondeur { get; init; }
    public bool EstSeance { get; init; }
    public List<NoeudAcquis> Enfants { get; } = [];

    public string Chemin => CalculAcquis.Chemin(Cles);
    /// <summary>Retrait de la ligne dans la liste à plat du questionnaire (le premier niveau n'est pas décalé).</summary>
    public Thickness Retrait => new(Math.Max(0, Profondeur - 1) * 22, 0, 0, 0);

    /// <summary>Indice dans <see cref="ListesAcquis.Avancements"/> : 0 = hérité.</summary>
    [ObservableProperty] private int _avancement;
    /// <summary>Indice dans <see cref="ListesAcquis.Comprehensions"/> : 0 = hérité.</summary>
    [ObservableProperty] private int _comprehension;
    /// <summary>Acquis effectif, en clair (« Vu · Fragile »), héritage compris.</summary>
    [ObservableProperty] private string _resume = "";
    /// <summary>Case cochée dans le questionnaire (cours sans ordre).</summary>
    [ObservableProperty] private bool _coche;

    /// <summary>Appelé quand l'étudiant change un menu (pas quand on charge les valeurs).</summary>
    internal Action<NoeudAcquis>? Modifie { get; set; }
    internal bool EnChargement { get; set; }

    partial void OnAvancementChanged(int value) { if (!EnChargement) Modifie?.Invoke(this); }
    partial void OnComprehensionChanged(int value) { if (!EnChargement) Modifie?.Invoke(this); }
}

/// <summary>
/// L'arbre des acquis (le même que la bibliothèque : matière › partie › séquence › séance),
/// partagé par l'écran « Mes acquis » et le questionnaire.
/// À chaque changement, l'acquis est enregistré puis les cartes jamais révisées sont recalculées.
/// </summary>
public sealed class ArbreAcquis
{
    private readonly DepotAcquis _depot;
    private readonly IReadOnlyList<ResumeSeance> _seances;
    private readonly DateOnly _jour;
    private readonly Dictionary<string, ReglageAcquis> _reglages;

    public List<NoeudAcquis> Racines { get; } = [];

    public ArbreAcquis(DepotAcquis depot, DepotBibliotheque bibliotheque, DateOnly jour)
    {
        _depot = depot;
        _jour = jour;
        _seances = bibliotheque.ListerSeances(jour);
        _reglages = depot.Charger();
        var racine = ArbreBibliotheque.Construire(_seances);
        foreach (var matiere in racine.Enfants) Racines.Add(Creer(matiere, [], 0));
        Rafraichir();
    }

    private NoeudAcquis Creer(NoeudBibliotheque niveau, List<string> parent, int profondeur)
    {
        var cles = new List<string>(parent) { niveau.Cle };
        var noeud = Preparer(new NoeudAcquis { Titre = niveau.Titre, Cles = cles, Profondeur = profondeur, Etiquette = niveau.Etiquette });
        foreach (var enfant in niveau.Enfants) noeud.Enfants.Add(Creer(enfant, cles, profondeur + 1));
        foreach (var seance in niveau.Seances)
        {
            var clesSeance = new List<string>(cles) { "§" + seance.PackId };
            noeud.Enfants.Add(Preparer(new NoeudAcquis { Titre = seance.Seance, Cles = clesSeance, Profondeur = profondeur + 1, EstSeance = true }));
        }
        return noeud;
    }

    /// <summary>Charge les valeurs déjà enregistrées dans la ligne et branche son déclencheur.</summary>
    private NoeudAcquis Preparer(NoeudAcquis noeud)
    {
        noeud.EnChargement = true;
        if (_reglages.TryGetValue(noeud.Chemin, out var r))
        {
            noeud.Avancement = ListesAcquis.Indice(r.Avancement);
            noeud.Comprehension = ListesAcquis.Indice(r.Comprehension);
        }
        noeud.EnChargement = false;
        noeud.Modifie = Modifier;
        return noeud;
    }

    /// <summary>Un menu a changé : enregistre, recalcule les cartes sans historique, remet les textes à jour.</summary>
    private void Modifier(NoeudAcquis noeud) =>
        Definir(noeud, ListesAcquis.VersAvancement(noeud.Avancement), ListesAcquis.VersComprehension(noeud.Comprehension),
                recalculer: true, majMenus: false);

    /// <summary>
    /// Règle l'acquis d'un niveau (en base et à l'écran). Pour régler beaucoup de niveaux d'un coup,
    /// passer « recalculer: false » puis appeler <see cref="Recalculer"/> une seule fois.
    /// </summary>
    public void Definir(NoeudAcquis noeud, Avancement? av, Comprehension? co, bool recalculer = true, bool majMenus = true)
    {
        _depot.Definir(noeud.Chemin, av, co);
        if (av is null && co is null) _reglages.Remove(noeud.Chemin);
        else _reglages[noeud.Chemin] = new ReglageAcquis(av, co);
        if (majMenus)
        {
            noeud.EnChargement = true; // les menus changent, mais sans relancer l'enregistrement
            noeud.Avancement = ListesAcquis.Indice(av);
            noeud.Comprehension = ListesAcquis.Indice(co);
            noeud.EnChargement = false;
        }
        if (recalculer) Recalculer();
        else Rafraichir();
    }

    /// <summary>Recalcule l'état des cartes jamais révisées d'après les acquis, puis rafraîchit les textes.</summary>
    public void Recalculer()
    {
        _depot.Recalculer(_jour, _seances);
        Rafraichir();
    }

    /// <summary>Recalcule le texte « acquis effectif » de chaque ligne.</summary>
    private void Rafraichir()
    {
        void Parcourir(NoeudAcquis n)
        {
            n.Resume = Texte(CalculAcquis.Resoudre(_reglages, n.Cles));
            foreach (var e in n.Enfants) Parcourir(e);
        }
        foreach (var r in Racines) Parcourir(r);
    }

    private static string Texte(AcquisEffectif acquis) => CalculAcquis.ComprehensionEffective(acquis) switch
    {
        null => "Pas vu",
        { } c => $"{(acquis.Avancement == Avancement.Revise ? "Révisé" : "Vu")} · {ListesAcquis.Comprehensions[ListesAcquis.Indice(c)]}",
    };

    /// <summary>Nombre de séances pas vues, vues et révisées (pour le récapitulatif).</summary>
    public (int PasVu, int Vu, int Revise) Bilan()
    {
        int pasVu = 0, vu = 0, revise = 0;
        foreach (var s in _seances)
        {
            switch (CalculAcquis.Resoudre(_reglages, ArbreBibliotheque.Chemin(s)).Avancement)
            {
                case Avancement.Vu: vu++; break;
                case Avancement.Revise: revise++; break;
                default: pasVu++; break;
            }
        }
        return (pasVu, vu, revise);
    }
}

/// <summary>Écran « Mes acquis » : l'arbre complet, modifiable à tout moment.</summary>
public sealed partial class AcquisViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public List<NoeudAcquis> Racines { get; }
    public bool Vide => Racines.Count == 0;

    public AcquisViewModel(MainViewModel main, EtatApplication etat, DepotAcquis depot, DepotBibliotheque bibliotheque)
    {
        _main = main;
        Racines = new ArbreAcquis(depot, bibliotheque, etat.Jour).Racines;
    }

    [RelayCommand]
    private void RefaireQuestionnaire() => _main.AllerQuestionnaire();
}


/// <summary>
/// Questionnaire de premier lancement, posé par la mascotte. Il est fait pour être rapide :
/// 1. « Tu pars de zéro ? » : si oui, c'est fini en un clic ;
/// 2. sinon, pour chaque matière : pas commencé / un peu / tout vu ;
/// 3. « un peu » : un seul clic sur la dernière partie vue (le cours est dans l'ordre : tout ce qui précède est vu) ;
/// 4. une seule question de ressenti (je découvre, fragile, correct, solide) pour tout ce qui a été vu.
/// Le détail séance par séance se règle ensuite, à son rythme, dans « Mes acquis ».
/// </summary>
public sealed partial class QuestionnaireViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DepotAcquis _depot;
    private readonly ArbreAcquis _arbre;
    private int _indice = -1;
    /// <summary>Les parties de la matière en cours, dans l'ordre du cours.</summary>
    private List<NoeudAcquis> _parties = [];
    /// <summary>Les parties proposées pour « jusqu'où » (seulement celles qui sont numérotées).</summary>
    private List<NoeudAcquis> _partiesAffichees = [];
    /// <summary>Ce à quoi s'applique la question de ressenti : les parties vues, ou toute la matière.</summary>
    private List<NoeudAcquis> _concernes = [];

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _expression = "content";
    /// <summary>Petit repère au-dessus de la mascotte : « Bloc 1 · matière 2 sur 7 ».</summary>
    [ObservableProperty] private string _repere = "";
    [ObservableProperty] private bool _estIntro;
    [ObservableProperty] private bool _estQuestion;
    [ObservableProperty] private bool _estJusqua;
    [ObservableProperty] private bool _estChoix;
    [ObservableProperty] private bool _estRessenti;
    [ObservableProperty] private bool _estRecap;
    [ObservableProperty] private string _recap = "";

    public ObservableCollection<NoeudAcquis> Parties { get; } = [];

    public QuestionnaireViewModel(MainViewModel main, EtatApplication etat, DepotAcquis depot, DepotBibliotheque bibliotheque)
    {
        _main = main;
        _depot = depot;
        _arbre = new ArbreAcquis(depot, bibliotheque, etat.Jour);
        ChangerEtape(intro: true);
        Expression = "joie";
        Message = "Salut, moi c'est Atome ! Une minute pour que je cale tes premières cartes sur ton niveau. Tu pars de zéro ?";
    }

    private NoeudAcquis Matiere => _arbre.Racines[_indice];

    /// <summary>N'affiche qu'une seule étape à la fois.</summary>
    private void ChangerEtape(bool intro = false, bool question = false, bool jusqua = false, bool choix = false, bool ressenti = false, bool recap = false)
    {
        EstIntro = intro; EstQuestion = question; EstJusqua = jusqua; EstChoix = choix; EstRessenti = ressenti; EstRecap = recap;
    }

    // ---- 1. Intro ------------------------------------------------------------------------

    /// <summary>« Je pars de zéro » : rien à régler, on passe directement à l'accueil.</summary>
    [RelayCommand]
    private void PartirDeZero() => Terminer();

    [RelayCommand]
    private void DejaBosse() => MatiereSuivante();

    // ---- 2. Une matière après l'autre -----------------------------------------------------

    private void MatiereSuivante()
    {
        _indice++;
        if (_indice >= _arbre.Racines.Count)
        {
            AfficherRecap();
            return;
        }
        var m = Matiere;
        _parties = m.Enfants.Count > 0 ? [.. m.Enfants] : [];
        Repere = $"{(string.IsNullOrWhiteSpace(m.Etiquette) ? "" : m.Etiquette + " · ")}matière {_indice + 1} sur {_arbre.Racines.Count}";
        ChangerEtape(question: true);
        Expression = _indice % 2 == 0 ? "content" : "clin";
        Message = $"« {m.Titre} » : où en es-tu ?";
    }

    [RelayCommand]
    private void PasEncore() => MatiereSuivante();

    /// <summary>« Tout est vu » : toute la matière, puis la question de ressenti.</summary>
    [RelayCommand]
    private void ToutVu()
    {
        _concernes = [Matiere];
        PoserQuestionRessenti();
    }

    /// <summary>Début d'un titre qui montre un vrai ordre dans le cours : « B1.2 », « Thème 3 », « SQ3 ».</summary>
    private static readonly System.Text.RegularExpressions.Regex Numerote =
        new(@"^(B\d+\.\d+|Th[èe]me\s+\d+|SQ\s?\d+|S[ée]quence\s+\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// « Un peu » : l'étudiant dit ce qu'il a vu.
    /// Cours numérotés (séquences) : un clic sur la dernière partie vue suffit, car on les suit dans l'ordre.
    /// Cours sans ordre (l'anglais, par exemple, est une suite de thèmes indépendants) : il coche les parties vues.
    /// </summary>
    [RelayCommand]
    private void UnPeu()
    {
        var numerotees = _parties.Where(p => Numerote.IsMatch(p.Titre)).ToList();
        // Le cours est « dans l'ordre » si au moins deux parties sur trois sont numérotées.
        if (_parties.Count > 0 && numerotees.Count * 3 >= _parties.Count * 2)
        {
            _partiesAffichees = numerotees;
            Parties.Clear();
            foreach (var p in numerotees) Parties.Add(p);
            ChangerEtape(jusqua: true);
            Message = "Clique sur la dernière partie que tu as vue. Je compte tout ce qui est avant comme vu.";
        }
        else
        {
            _partiesAffichees = _parties;
            Parties.Clear();
            foreach (var p in _parties) { p.Coche = false; Parties.Add(p); }
            ChangerEtape(choix: true);
            Message = "Coche les parties que tu as déjà vues, dans l'ordre que tu veux.";
        }
        Expression = "content";
    }

    /// <summary>Cours sans ordre : valide les parties cochées.</summary>
    [RelayCommand]
    private void ValiderChoix()
    {
        foreach (var p in _parties) _arbre.Definir(p, p.Coche ? Avancement.Vu : null, null, recalculer: false);
        _concernes = _parties.Where(p => p.Coche).ToList();
        if (_concernes.Count == 0) { _arbre.Recalculer(); MatiereSuivante(); }
        else PoserQuestionRessenti();
    }

    // ---- 3. Jusqu'où ----------------------------------------------------------------------

    /// <summary>La partie donnée est la dernière vue : elle et toutes les précédentes passent à « vu ».</summary>
    [RelayCommand]
    private void JusquIci(NoeudAcquis derniere)
    {
        int fin = _partiesAffichees.IndexOf(derniere);
        if (fin < 0) return;
        for (int i = 0; i < _partiesAffichees.Count; i++)
            _arbre.Definir(_partiesAffichees[i], i <= fin ? Avancement.Vu : null, null, recalculer: false);
        _concernes = _partiesAffichees.Take(fin + 1).ToList();
        PoserQuestionRessenti();
    }

    // ---- 4. Ressenti ----------------------------------------------------------------------

    private void PoserQuestionRessenti()
    {
        ChangerEtape(ressenti: true);
        Expression = "clin";
        Message = "Et ce que tu as vu, tu le sens comment ? (Pas de bonne réponse, c'est juste pour régler le rythme des révisions.)";
    }

    /// <summary>Applique le ressenti (1 = je découvre … 4 = solide) à tout ce qui a été vu, puis passe à la matière suivante.</summary>
    [RelayCommand]
    private void Ressenti(string indice)
    {
        var niveau = (Comprehension)(int.Parse(indice) - 1);
        // À partir de « correct », on considère que la matière a été révisée, pas seulement vue.
        var avancement = niveau >= Comprehension.Correct ? Avancement.Revise : Avancement.Vu;
        foreach (var n in _concernes) _arbre.Definir(n, avancement, niveau, recalculer: false);
        _arbre.Recalculer();
        MatiereSuivante();
    }

    // ---- Fin ------------------------------------------------------------------------------

    private void AfficherRecap()
    {
        ChangerEtape(recap: true);
        Repere = "";
        Expression = "joie";
        var (pasVu, vu, revise) = _arbre.Bilan();
        Message = "Parfait, c'est noté ! Tes premières révisions seront adaptées à ça.";
        Recap = $"{revise} séance(s) révisée(s), {vu} vue(s), {pasVu} pas encore vue(s).\n"
              + "Ce que tu connais déjà reviendra petit à petit, étalé sur plusieurs jours. "
              + "Pour affiner séance par séance, ouvre « Mes acquis » dans le menu.";
    }

    [RelayCommand]
    private void PlusTard() => Terminer();

    [RelayCommand]
    private void Terminer()
    {
        _depot.MarquerQuestionnaireVu();
        _main.AllerAccueil();
    }
}
