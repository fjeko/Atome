using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Planning;
using Revisions.Core.Sessions;

namespace Revisions.Data;

/// <summary>
/// Tout ce qu'il faut pour composer la journée : cartes dues (avec les cartes avancées avant un devoir),
/// nouvelles cartes dans le bon ordre, séances à lire et prochaines échéances.
/// Lu une fois, puis interrogé autant de fois que nécessaire (accueil, puis lancement de la session).
/// </summary>
public sealed class Journee
{
    private readonly DepotRevisions _revisions;
    private readonly List<CandidateNouvelle> _candidates;
    private List<CarteAReviser>? _avancees;

    public DateOnly Jour { get; }
    public IReadOnlyList<ResumeSeance> Seances { get; }
    /// <summary>Séances lues dans l'application ou marquées « Vu / Révisé » dans Mes acquis.</summary>
    public IReadOnlySet<string> PacksLus { get; }
    /// <summary>Planning actif (dates personnelles appliquées), ou null : l'application fonctionne alors comme avant.</summary>
    public PlanningEffectif? Planning { get; }
    /// <summary>Vrai si un planning existe et que l'interrupteur « Suivre le planning » est allumé.</summary>
    public bool PlanningSuivi => Planning is { Reglages.Suivre: true };

    internal Journee(DateOnly jour, DepotRevisions revisions, IReadOnlyList<ResumeSeance> seances,
        IReadOnlySet<string> packsLus, PlanningEffectif? planning)
    {
        Jour = jour;
        _revisions = revisions;
        Seances = seances;
        PacksLus = packsLus;
        Planning = planning;
        _candidates = revisions.CandidatesNouvelles();
    }

    /// <summary>Nouvelles cartes à proposer (au plus <paramref name="maximum"/>), dans l'ordre de présentation.</summary>
    public List<CarteAReviser> Nouvelles(int maximum) =>
        _revisions.ChargerCartes(ChoixNouvelles.Choisir(_candidates, Seances, PacksLus, Planning, Jour, maximum)
            .Select(c => c.CarteId).ToList());

    /// <summary>Nombre de nouvelles cartes disponibles (sans charger leur contenu).</summary>
    public int NombreDeNouvelles(int maximum) =>
        ChoixNouvelles.Choisir(_candidates, Seances, PacksLus, Planning, Jour, maximum).Count;

    /// <summary>
    /// Cartes avancées avant un devoir. Elles sont présentées comme des cartes dues aujourd'hui
    /// (échéance ramenée à aujourd'hui pour l'affichage et l'XP) ; FSRS calcule la suite à partir de la date réelle de révision.
    /// </summary>
    public IReadOnlyList<CarteAReviser> Avancees() =>
        _avancees ??= _revisions.ChargerCartes(AvanceeDevoir.CartesAAvancer(_revisions.CartesVuesNonDues(Jour), Planning, Jour))
            .Select(c => c with { Etat = c.Etat with { Echeance = Jour } })
            .ToList();

    /// <summary>Cartes à revoir aujourd'hui : les cartes dues et les cartes avancées.</summary>
    public List<CarteAReviser> Dues() => [.. _revisions.CartesDues(Jour), .. Avancees()];

    /// <summary>Séances à lire (bloc « Au programme »).</summary>
    public List<SuggestionLecture> Suggestions(int nombre = 3) =>
        ChoixNouvelles.SuggestionsDeLecture(Seances, PacksLus, Planning, Jour, nombre);

    /// <summary>Prochaines échéances (rien si le planning n'est pas suivi).</summary>
    public IReadOnlyList<Devoir> Echeances(int nombre = 3) =>
        PlanningSuivi ? Planning!.ProchainesEcheances(Jour, nombre) : [];
}

/// <summary>Fabrique la <see cref="Journee"/> à partir des dépôts.</summary>
public sealed class SelectionDuJour(DepotRevisions revisions, DepotBibliotheque bibliotheque, DepotAcquis acquis, DepotPlanning planning)
{
    public Journee Charger(DateOnly jour)
    {
        var seances = bibliotheque.ListerSeances(jour);
        var reglagesAcquis = acquis.Charger();
        var lus = seances
            .Where(s => s.Lu || CalculAcquis.Resoudre(reglagesAcquis, ArbreBibliotheque.Chemin(s)).Avancement != Avancement.PasVu)
            .Select(s => s.PackId)
            .ToHashSet();
        return new Journee(jour, revisions, seances, lus, planning.Effectif());
    }
}
