using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Planning;
using Revisions.Core.Sessions;

namespace Revisions.Data;

/// <summary>
/// Tout ce qu'il faut pour composer la journée : cartes dues (avec les cartes avancées avant un devoir),
/// nouvelles cartes dans le bon ordre, séances à lire et prochaines échéances.
/// Lu une fois, puis interrogé autant de fois que nécessaire (accueil, écran de choix, puis lancement de la session).
/// </summary>
public sealed class Journee
{
    /// <summary>Période sur laquelle on calcule le taux de réussite d'une séance (points faibles).</summary>
    public const int JoursHistoriqueReussite = 60;

    private readonly DepotRevisions _revisions;
    private readonly DepotBibliotheque? _bibliotheque;
    private readonly List<CandidateNouvelle> _candidates;
    private readonly bool _nouvellesNonLues;
    private List<CarteAReviser>? _avancees;
    private DonneesSession? _donnees;

    public DateOnly Jour { get; }
    public IReadOnlyList<ResumeSeance> Seances { get; }
    /// <summary>Séances lues dans l'application ou marquées « Vu / Révisé » dans Mes acquis.</summary>
    public IReadOnlySet<string> PacksLus { get; }
    /// <summary>Planning actif (dates personnelles appliquées), ou null : l'application fonctionne alors comme avant.</summary>
    public PlanningEffectif? Planning { get; }
    /// <summary>Vrai si un planning existe et que l'interrupteur « Suivre le planning » est allumé.</summary>
    public bool PlanningSuivi => Planning is { Reglages.Suivre: true };

    internal Journee(DateOnly jour, DepotRevisions revisions, DepotBibliotheque? bibliotheque, IReadOnlyList<ResumeSeance> seances,
        IReadOnlySet<string> packsLus, PlanningEffectif? planning, bool nouvellesNonLues)
    {
        Jour = jour;
        _revisions = revisions;
        _bibliotheque = bibliotheque;
        _nouvellesNonLues = nouvellesNonLues;
        Seances = seances;
        PacksLus = packsLus;
        Planning = planning;
        _candidates = revisions.CandidatesNouvelles();
    }

    // ---- Composition des sessions (types de session, paquets, motifs, cours non lus) ----------

    /// <summary>Données de composition, lues en base à la première demande.</summary>
    public DonneesSession Donnees => _donnees ??= new DonneesSession
    {
        Jour = Jour,
        Seances = Seances,
        PacksVus = PacksLus,
        DernieresLectures = _bibliotheque?.DernieresLectures() ?? [],
        Planning = Planning,
        Nouvelles = _candidates,
        Vues = _revisions.CartesConnues(Jour),
        Reussites = _revisions.ReussiteParSeance(Jour.AddDays(-JoursHistoriqueReussite)),
        NouvellesNonLues = _nouvellesNonLues,
    };

    /// <summary>Choisit les cartes d'une session (sans charger leur contenu).</summary>
    public SessionComposee Composer(DemandeSession demande, int maxCartes, int maxNouvelles) =>
        CompositeurSession.Composer(Donnees, demande, maxCartes, maxNouvelles);

    /// <summary>
    /// Charge le contenu des cartes choisies, dans l'ordre, avec leur motif.
    /// Une carte avancée avant un devoir est présentée comme due aujourd'hui (échéance ramenée à aujourd'hui pour l'affichage
    /// et l'XP) ; FSRS calcule la suite à partir de la date réelle de révision.
    /// </summary>
    public List<CarteAReviser> Charger(SessionComposee session)
    {
        var parId = session.Cartes.ToDictionary(c => c.CarteId);
        return _revisions.ChargerCartes(session.Cartes.Select(c => c.CarteId).ToList())
            .Select(c =>
            {
                var choisie = parId[c.Carte.Id];
                var carte = c with { Motif = choisie.Motif };
                return choisie.Avancee ? carte with { Etat = carte.Etat with { Echeance = Jour } } : carte;
            })
            .ToList();
    }

    /// <summary>Séances à lire pour débloquer de nouvelles cartes (vide si le réglage « cours non lus » est activé).</summary>
    public List<SuggestionLecture> SeancesADebloquer(int nombre = 3) => CompositeurSession.SeancesADebloquer(Donnees, nombre);

    /// <summary>Nombre de cartes dues aujourd'hui, cartes avancées avant un devoir comprises (pour le plan du jour).</summary>
    public int NombreDeDues() => Composer(DemandeSession.DuJour, int.MaxValue, 0).Cartes.Count;

    // ---- Accès plus anciens (sans verrouillage des cours non lus) -------------------------------

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
    /// <param name="nouvellesNonLues">Réglage « Proposer des nouvelles cartes de cours non lus ».</param>
    public Journee Charger(DateOnly jour, bool nouvellesNonLues = false)
    {
        var seances = bibliotheque.ListerSeances(jour);
        var reglagesAcquis = acquis.Charger();
        var lus = seances
            .Where(s => s.Lu || CalculAcquis.Resoudre(reglagesAcquis, ArbreBibliotheque.Chemin(s)).Avancement != Avancement.PasVu)
            .Select(s => s.PackId)
            .ToHashSet();
        return new Journee(jour, revisions, bibliotheque, seances, lus, planning.Effectif(), nouvellesNonLues);
    }
}
