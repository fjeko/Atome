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
        var noeud = Preparer(new NoeudAcquis { Titre = niveau.Titre, Cles = cles, Profondeur = profondeur });
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

    /// <summary>Enregistre le réglage d'une ligne, recalcule les cartes sans historique, puis remet les textes à jour.</summary>
    private void Modifier(NoeudAcquis noeud)
    {
        var av = ListesAcquis.VersAvancement(noeud.Avancement);
        var co = ListesAcquis.VersComprehension(noeud.Comprehension);
        _depot.Definir(noeud.Chemin, av, co);
        if (av is null && co is null) _reglages.Remove(noeud.Chemin);
        else _reglages[noeud.Chemin] = new ReglageAcquis(av, co);
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
/// Questionnaire de premier lancement, posé par la mascotte : une matière à la fois,
/// « tu as déjà commencé ? » puis, si oui, où il en est dans chaque partie. Se termine par un récapitulatif.
/// </summary>
public sealed partial class QuestionnaireViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DepotAcquis _depot;
    private readonly ArbreAcquis _arbre;
    private int _indice = -1;

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _expression = "content";
    [ObservableProperty] private bool _estQuestion;
    [ObservableProperty] private bool _estDetail;
    [ObservableProperty] private bool _estRecap;
    [ObservableProperty] private bool _detailler;
    [ObservableProperty] private string _recap = "";

    public ObservableCollection<NoeudAcquis> Lignes { get; } = [];

    public QuestionnaireViewModel(MainViewModel main, EtatApplication etat, DepotAcquis depot, DepotBibliotheque bibliotheque)
    {
        _main = main;
        _depot = depot;
        _arbre = new ArbreAcquis(depot, bibliotheque, etat.Jour);
        PasserALaMatiereSuivante();
    }

    private NoeudAcquis Matiere => _arbre.Racines[_indice];

    private void PasserALaMatiereSuivante()
    {
        _indice++;
        EstDetail = false;
        if (_indice >= _arbre.Racines.Count)
        {
            AfficherRecap();
            return;
        }
        Detailler = false;
        EstQuestion = true;
        Expression = _indice % 2 == 0 ? "content" : "clin";
        Message = _indice == 0
            ? $"Salut ! Dis-moi où tu en es, pour que je m'adapte. Tu as déjà commencé « {Matiere.Titre} » ?"
            : $"Et « {Matiere.Titre} », tu as déjà commencé ?";
    }

    private void AfficherRecap()
    {
        EstQuestion = false;
        EstRecap = true;
        Expression = "joie";
        var (pasVu, vu, revise) = _arbre.Bilan();
        Message = "Parfait, c'est noté ! Tu pourras tout changer dans « Mes acquis ».";
        Recap = $"{revise} séance(s) révisée(s), {vu} vue(s), {pasVu} pas encore vue(s).\n"
              + "Les cartes de ce que tu as déjà vu reviendront progressivement, étalées sur plusieurs jours.";
    }

    [RelayCommand]
    private void Oui()
    {
        EstQuestion = false;
        EstDetail = true;
        Expression = "content";
        Message = "Super ! Pour chaque partie, dis-moi si tu l'as vue, et comment tu la sens. Laisse « — » si tu n'as rien à dire.";
        ConstruireLignes();
    }

    [RelayCommand]
    private void Non() => PasserALaMatiereSuivante();

    [RelayCommand]
    private void Suivant() => PasserALaMatiereSuivante();

    partial void OnDetaillerChanged(bool value) { if (EstDetail) ConstruireLignes(); }

    /// <summary>Liste à plat des parties de la matière ; avec « détailler », les séances aussi.</summary>
    private void ConstruireLignes()
    {
        Lignes.Clear();
        var tous = new List<NoeudAcquis>();
        void Parcourir(NoeudAcquis n) { foreach (var e in n.Enfants) { tous.Add(e); Parcourir(e); } }
        Parcourir(Matiere);
        Lignes.Add(Matiere); // première ligne : toute la matière d'un coup

        // Sans « détailler » on s'arrête aux parties et séquences ; si la matière n'a que des séances, on les montre.
        var parties = tous.Where(n => !n.EstSeance).ToList();
        foreach (var n in Detailler || parties.Count == 0 ? tous : parties) Lignes.Add(n);
    }

    [RelayCommand]
    private void PlusTard()
    {
        _depot.MarquerQuestionnaireVu();
        _main.AllerAccueil();
    }

    [RelayCommand]
    private void Terminer()
    {
        _depot.MarquerQuestionnaireVu();
        _main.AllerAccueil();
    }
}
