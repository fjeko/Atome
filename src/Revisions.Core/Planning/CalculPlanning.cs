namespace Revisions.Core.Planning;

/// <summary>Date changée par l'étudiant pour une période ou un devoir (enregistrée en base, le fichier n'est pas touché).</summary>
/// <param name="Debut">Nouveau début de la période, ou nouvelle semaine du devoir.</param>
/// <param name="Fin">Nouvelle fin de la période (null pour un devoir, ou pour garder la fin du fichier).</param>
public sealed record Surcharge(DateOnly Debut, DateOnly? Fin);

/// <summary>Choix de l'étudiant sur l'écran Planning.</summary>
public sealed record ReglagesPlanning
{
    /// <summary>Interrupteur général « Suivre le planning ».</summary>
    public bool Suivre { get; init; } = true;
    /// <summary>Matières du planning que l'étudiant ne veut pas suivre (elles se comportent comme sans planning).</summary>
    public IReadOnlySet<string> MatieresIgnorees { get; init; } = new HashSet<string>();
    /// <summary>Ne pas tenir compte des quiz d'auto-évaluation (ni affichage, ni préparation).</summary>
    public bool MasquerQuiz { get; init; }
    /// <summary>Dates personnelles, par clé de période ou de devoir.</summary>
    public IReadOnlyDictionary<string, Surcharge> Surcharges { get; init; } = new Dictionary<string, Surcharge>();
}

/// <summary>Où en est une période par rapport à aujourd'hui.</summary>
public enum Moment { Passee, EnCours, Future }

/// <summary>
/// Le planning tel qu'il s'applique pour l'étudiant : surcharges de dates appliquées,
/// matières non suivies et quiz masqués retirés. Toutes les questions « quand ? » passent par ici.
/// </summary>
public sealed class PlanningEffectif
{
    /// <summary>Nombre de jours avant la date limite pendant lesquels on prépare un devoir.</summary>
    public const int JoursDePreparation = 14;

    /// <summary>Probabilité de rappel visée le jour du devoir : en dessous, la carte est avancée.</summary>
    public const double SeuilRetrievabilite = 0.85;

    /// <summary>Planning d'origine avec les dates personnelles (toutes les matières, quiz compris : pour l'écran Planning).</summary>
    public PlanningCned Complet { get; }
    /// <summary>Périodes des matières suivies.</summary>
    public IReadOnlyList<Periode> Periodes { get; }
    /// <summary>Devoirs des matières suivies (sans les quiz s'ils sont masqués), triés par date limite.</summary>
    public IReadOnlyList<Devoir> Devoirs { get; }
    public ReglagesPlanning Reglages { get; }

    // Période de chaque pack déjà cherchée (la recherche est faite des milliers de fois par session).
    private readonly Dictionary<string, Periode?> _cachePeriodes = [];

    public PlanningEffectif(PlanningCned planning, ReglagesPlanning reglages)
    {
        Reglages = reglages;
        Complet = AppliquerSurcharges(planning, reglages.Surcharges);
        Periodes = Complet.Periodes.Where(p => !reglages.MatieresIgnorees.Contains(p.Matiere)).ToList();
        Devoirs = Complet.Devoirs
            .Where(d => !reglages.MatieresIgnorees.Contains(d.Matiere) && !(reglages.MasquerQuiz && d.EstQuiz))
            .OrderBy(d => d.DateLimite).ThenBy(d => d.Matiere, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Remplace les dates par celles choisies par l'étudiant. Une période dont la fin choisie
    /// serait avant le début garde une seule semaine (jamais de période « à l'envers »).
    /// </summary>
    public static PlanningCned AppliquerSurcharges(PlanningCned planning, IReadOnlyDictionary<string, Surcharge> surcharges)
    {
        if (surcharges.Count == 0) return planning;
        var periodes = planning.Periodes.Select(p =>
        {
            if (!surcharges.TryGetValue(p.Cle, out var s)) return p;
            var fin = s.Fin ?? p.Fin;
            if (fin < s.Debut) fin = s.Debut.AddDays(6);
            return p with { Debut = s.Debut, Fin = fin };
        }).ToList();
        var devoirs = planning.Devoirs.Select(d =>
            surcharges.TryGetValue(d.Cle, out var s) ? d with { Semaine = Temps.JourDeRevision.LundiDe(s.Debut) } : d).ToList();
        return planning with { Periodes = periodes, Devoirs = devoirs };
    }

    /// <summary>
    /// Période d'un pack parmi les matières suivies : celle dont le préfixe est le plus long
    /// (le plus précis), puis la plus ancienne. Null si le pack n'est pas au planning, ou si sa matière n'est pas suivie.
    /// </summary>
    public Periode? PeriodeDuPack(string packId)
    {
        if (_cachePeriodes.TryGetValue(packId, out var dejaVue)) return dejaVue;
        Periode? meilleure = null;
        int meilleurPrefixe = -1;
        // On cherche dans TOUTES les périodes : un pack d'une matière non suivie ne doit pas tomber
        // dans une période moins précise d'une autre matière.
        foreach (var p in Complet.Periodes)
        {
            if (!p.Concerne(packId, out int longueur)) continue;
            if (longueur > meilleurPrefixe || (longueur == meilleurPrefixe && p.Debut < meilleure!.Debut))
            {
                meilleure = p;
                meilleurPrefixe = longueur;
            }
        }
        if (meilleure is not null && Reglages.MatieresIgnorees.Contains(meilleure.Matiere)) meilleure = null;
        _cachePeriodes[packId] = meilleure;
        return meilleure;
    }

    /// <summary>Vrai si le pack appartient à une matière du planning que l'étudiant a choisi de ne pas suivre.</summary>
    public bool MatiereIgnoree(string packId)
    {
        if (PeriodeDuPack(packId) is not null) return false;
        return Complet.Periodes.Any(p => p.Concerne(packId, out _) && Reglages.MatieresIgnorees.Contains(p.Matiere));
    }

    public static Moment MomentDe(Periode p, DateOnly jour) =>
        jour < p.Debut ? Moment.Future : jour > p.Fin ? Moment.Passee : Moment.EnCours;

    /// <summary>
    /// Périodes sur lesquelles porte un devoir :
    /// celles de la même matière qui ont commencé au plus tard à sa date limite
    /// et après la date limite du devoir précédent de cette matière (quiz compris).
    /// Pour le premier devoir, tout ce qui a commencé avant sa date limite.
    /// Si la matière a une période « continue » (anglais), le devoir porte sur toutes les périodes de la matière.
    /// </summary>
    public IReadOnlyList<Periode> Couverture(Devoir devoir)
    {
        var periodesMatiere = Complet.Periodes.Where(p => p.Matiere == devoir.Matiere).ToList();
        if (periodesMatiere.Any(p => p.Continu)) return periodesMatiere;

        // Le devoir précédent est cherché parmi TOUS les devoirs de la matière (même les quiz masqués) :
        // masquer les quiz ne change pas ce sur quoi porte un devoir noté.
        DateOnly? precedent = Complet.Devoirs
            .Where(d => d.Matiere == devoir.Matiere && d.DateLimite < devoir.DateLimite)
            .Select(d => (DateOnly?)d.DateLimite)
            .Max();
        return periodesMatiere
            .Where(p => p.Debut <= devoir.DateLimite && (precedent is null || p.Debut > precedent))
            .ToList();
    }

    /// <summary>Vrai si le pack fait partie du contenu d'un devoir.</summary>
    public bool CouvrePack(Devoir devoir, string packId) =>
        PeriodeDuPack(packId) is { } p && Couverture(devoir).Contains(p);

    /// <summary>Devoirs en préparation ce jour-là : la date limite est dans les 14 prochains jours (ou aujourd'hui).</summary>
    public IReadOnlyList<Devoir> DevoirsEnPreparation(DateOnly jour) =>
        Devoirs.Where(d => EnPreparation(d, jour)).ToList();

    public static bool EnPreparation(Devoir d, DateOnly jour) =>
        jour <= d.DateLimite && jour >= d.DateLimite.AddDays(-JoursDePreparation);

    /// <summary>Prochaines échéances (date limite aujourd'hui ou plus tard), les plus proches d'abord. Rien sur les devoirs passés.</summary>
    public IReadOnlyList<Devoir> ProchainesEcheances(DateOnly jour, int nombre) =>
        Devoirs.Where(d => d.DateLimite >= jour).Take(nombre).ToList();
}
