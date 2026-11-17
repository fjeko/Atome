using Revisions.Core.Bibliotheque;
using Revisions.Core.Ordre;
using Revisions.Core.Planning;

namespace Revisions.Core.Sessions;

/// <summary>Les types de session proposés au lancement.</summary>
public enum TypeSession
{
    /// <summary>Session du jour (recommandée) : cartes dues de toutes les matières, puis nouvelles cartes.</summary>
    DuJour,
    /// <summary>Une matière ou une partie choisie dans la bibliothèque.</summary>
    Perimetre,
    /// <summary>Cartes souvent ratées et séances au taux de réussite bas.</summary>
    PointsFaibles,
    /// <summary>Séances lues dans l'application ces 7 derniers jours.</summary>
    LectureRecente,
    /// <summary>Contenu du prochain devoir du planning.</summary>
    Devoir,
}

/// <summary>Ce que l'étudiant a choisi de réviser.</summary>
/// <param name="Packs">Pour <see cref="TypeSession.Perimetre"/> : les séances de la matière ou de la partie choisie.</param>
public sealed record DemandeSession(TypeSession Type, IReadOnlySet<string>? Packs = null)
{
    public static DemandeSession DuJour { get; } = new(TypeSession.DuJour);
    public static DemandeSession PointsFaibles { get; } = new(TypeSession.PointsFaibles);
    public static DemandeSession LectureRecente { get; } = new(TypeSession.LectureRecente);
    public static DemandeSession Devoir { get; } = new(TypeSession.Devoir);
    public static DemandeSession Perimetre(IEnumerable<string> packs) => new(TypeSession.Perimetre, packs.ToHashSet());
}

/// <summary>Réponses données sur les cartes d'une séance (hors première découverte).</summary>
public sealed record ReussiteSeance(int Reponses, int Reussies)
{
    /// <summary>Part des réponses autres que « Raté » (1 s'il n'y a aucune réponse).</summary>
    public double Taux => Reponses == 0 ? 1 : (double)Reussies / Reponses;
}

/// <summary>Une carte retenue pour la session, avec la raison de sa présence.</summary>
/// <param name="Avancee">Carte pas encore due, avancée pour préparer un devoir.</param>
public sealed record CarteChoisie(string CarteId, string PackId, Motif Motif, bool Nouvelle = false, bool Avancee = false);

/// <summary>Une séance concernée par la session « Mes points faibles ».</summary>
/// <param name="NbCartes">Nombre de ses cartes retenues.</param>
/// <param name="FoisRatees">Total des « Raté » de ces cartes.</param>
/// <param name="Taux">Taux de réussite de la séance, ou null sans historique.</param>
public sealed record SeanceFaible(ResumeSeance Seance, int NbCartes, int FoisRatees, double? Taux);

/// <summary>Le résultat : les cartes dans l'ordre de présentation, et de quoi expliquer le choix.</summary>
/// <param name="NouvellesBloquees">Nouvelles cartes du périmètre écartées parce que leur cours n'a pas été lu.</param>
/// <param name="DebloquerUtile">
/// Vrai s'il reste de la place pour des nouvelles cartes mais que des cours non lus les bloquent :
/// l'accueil propose alors des séances à lire.
/// </param>
/// <param name="SeancesFaibles">Séances concernées (session « Mes points faibles » seulement).</param>
/// <param name="Devoir">Devoir préparé (session « Préparer un devoir » seulement).</param>
public sealed record SessionComposee(
    TypeSession Type,
    IReadOnlyList<CarteChoisie> Cartes,
    int NouvellesBloquees,
    bool DebloquerUtile,
    IReadOnlyList<SeanceFaible> SeancesFaibles,
    Devoir? Devoir)
{
    public int NbNouvelles => Cartes.Count(c => c.Nouvelle);
}

/// <summary>Tout ce qu'il faut savoir pour composer une session (lu en base une fois, puis interrogé en mémoire).</summary>
public sealed class DonneesSession
{
    private static readonly HashSet<string> Vide = [];
    private HashSet<string>? _lus;

    public required DateOnly Jour { get; init; }
    /// <summary>Séances visibles (celles des collections masquées n'y sont pas).</summary>
    public required IReadOnlyList<ResumeSeance> Seances { get; init; }
    /// <summary>Séances marquées « Vu » ou « Révisé » dans Mes acquis (celles lues dans l'application ont <see cref="ResumeSeance.Lu"/>).</summary>
    public IReadOnlySet<string> PacksVus { get; init; } = Vide;
    /// <summary>Dernière ouverture en lecture de chaque séance.</summary>
    public IReadOnlyDictionary<string, DateOnly> DernieresLectures { get; init; } = new Dictionary<string, DateOnly>();
    /// <summary>Planning actif, ou null.</summary>
    public PlanningEffectif? Planning { get; init; }
    /// <summary>Toutes les nouvelles cartes (sans contenu).</summary>
    public IReadOnlyList<CandidateNouvelle> Nouvelles { get; init; } = [];
    /// <summary>Toutes les cartes déjà vues (sans contenu).</summary>
    public IReadOnlyList<CarteVue> Vues { get; init; } = [];
    /// <summary>Taux de réussite par séance (pour les points faibles).</summary>
    public IReadOnlyDictionary<string, ReussiteSeance> Reussites { get; init; } = new Dictionary<string, ReussiteSeance>();
    /// <summary>Réglage « Proposer des nouvelles cartes de cours non lus » (désactivé par défaut).</summary>
    public bool NouvellesNonLues { get; init; }

    /// <summary>Séances « lues » : ouvertes dans l'application, ou marquées Vu / Révisé dans Mes acquis.</summary>
    public IReadOnlySet<string> Lus => _lus ??= [.. PacksVus, .. Seances.Where(s => s.Lu).Select(s => s.PackId)];

    public bool PlanningSuivi => Planning is { Reglages.Suivre: true };

    /// <summary>
    /// Une nouvelle carte est « débloquée » si sa séance a été lue (ou marquée Vu / Révisé, ou la carte rendue prioritaire
    /// depuis Mes acquis), ou si l'étudiant a demandé à voir aussi les cours non lus.
    /// </summary>
    public bool EstDebloquee(CandidateNouvelle c) => NouvellesNonLues || c.Prioritaire || Lus.Contains(c.PackId);
}

/// <summary>
/// Compose les sessions : quelles cartes, dans quel ordre, et pourquoi.
/// Calcul pur, sans base de données (testé dans ChoixSessionTests).
/// Les limites du jour (plafond, nouvelles cartes, mode reprise) arrivent déjà calculées par <see cref="CalculPlanDuJour"/> :
/// elles s'appliquent à tous les types de session.
/// </summary>
public static class CompositeurSession
{
    /// <summary>« Ce que je viens de lire » : séances ouvertes dans l'application ces 7 derniers jours (aujourd'hui compris).</summary>
    public const int JoursLectureRecente = 7;

    /// <summary>Nombre de réponses sur une séance à partir duquel son taux de réussite compte.</summary>
    public const int ReponsesMinimumPourTaux = 5;

    /// <summary>En dessous de ce taux de réussite, une séance est un point faible.</summary>
    public const double TauxFaible = 0.7;

    public static SessionComposee Composer(DonneesSession d, DemandeSession demande, int maxCartes, int maxNouvelles)
    {
        maxCartes = Math.Max(0, maxCartes);
        maxNouvelles = Math.Clamp(maxNouvelles, 0, maxCartes);
        var parPack = d.Seances.ToDictionary(s => s.PackId);
        if (demande.Type == TypeSession.PointsFaibles) return PointsFaibles(d, parPack, maxCartes);

        // ---- Périmètre de la session ----
        Devoir? devoir = demande.Type == TypeSession.Devoir ? ProchainDevoir(d) : null;
        var recents = demande.Type == TypeSession.LectureRecente ? PacksLusRecemment(d) : null;
        bool Dans(string packId) => parPack.ContainsKey(packId) && demande.Type switch
        {
            TypeSession.Perimetre => demande.Packs?.Contains(packId) == true,
            TypeSession.LectureRecente => recents!.Contains(packId),
            TypeSession.Devoir => devoir is not null && d.Planning!.CouvrePack(devoir, packId),
            _ => true,
        };
        if (demande.Type == TypeSession.Devoir && devoir is null)
            return new SessionComposee(demande.Type, [], 0, false, [], null);

        // ---- 1. Cartes à revoir : les plus fragiles d'abord ----
        var jour = d.Jour;
        var revisions = d.Vues
            .Where(v => v.Echeance <= jour && Dans(v.PackId))
            .Select(v => (Vue: v, Rappel: v.Rappel(jour),
                Choisie: new CarteChoisie(v.CarteId, v.PackId, devoir is null ? Motifs.ARevoir(v.DerniereRevision, jour) : Motifs.Devoir(devoir))))
            .ToList();

        if (demande.Type == TypeSession.DuJour)
        {
            // Cartes avancées avant un devoir proche (planning suivi) : comptées comme des cartes dues.
            var avancees = AvanceeDevoir.CartesAAvancer(d.Vues.Where(v => v.Echeance > jour && Dans(v.PackId)), d.Planning, jour).ToHashSet();
            revisions.AddRange(d.Vues.Where(v => avancees.Contains(v.CarteId)).Select(v => (v, v.Rappel(jour),
                new CarteChoisie(v.CarteId, v.PackId, DevoirDuPack(d, v.PackId) is { } dv ? Motifs.Devoir(dv) : Motifs.ARevoir(v.DerniereRevision, jour), Avancee: true))));
        }

        var choisies = revisions
            .OrderBy(r => r.Rappel).ThenBy(r => r.Vue.CarteId, StringComparer.Ordinal)
            .Select(r => r.Choisie)
            .ToList();

        if (devoir is not null)
        {
            // Préparer un devoir : aussi les cartes pas encore dues qu'on risque d'avoir oubliées le jour J.
            choisies.AddRange(d.Vues
                .Where(v => v.Echeance > jour && v.DerniereRevision < jour && Dans(v.PackId)
                            && AvanceeDevoir.RetrievabiliteA(v, devoir.DateLimite) < PlanningEffectif.SeuilRetrievabilite)
                .OrderBy(v => AvanceeDevoir.RetrievabiliteA(v, devoir.DateLimite)).ThenBy(v => v.CarteId, StringComparer.Ordinal)
                .Select(v => new CarteChoisie(v.CarteId, v.PackId, Motifs.Devoir(devoir), Avancee: true)));
        }
        if (choisies.Count > maxCartes) choisies.RemoveRange(maxCartes, choisies.Count - maxCartes);

        // ---- 2. Nouvelles cartes, seulement celles des cours lus (sauf réglage contraire) ----
        int places = Math.Min(maxNouvelles, maxCartes - choisies.Count);
        var candidates = d.Nouvelles.Where(c => Dans(c.PackId)).ToList();
        var debloquees = candidates.Where(d.EstDebloquee).ToList();
        int bloquees = candidates.Count - debloquees.Count;
        var nouvelles = ChoixNouvelles.Choisir(debloquees, d.Seances, d.Lus, d.Planning, jour, places);
        choisies.AddRange(nouvelles.Select(c => new CarteChoisie(c.CarteId, c.PackId, MotifNouvelle(d, parPack[c.PackId], c, devoir), Nouvelle: true)));
        bool debloquerUtile = bloquees > 0 && nouvelles.Count < places;

        // ---- 3. « Ce que je viens de lire » : on complète avec les autres cartes déjà vues de ces cours ----
        if (demande.Type == TypeSession.LectureRecente && choisies.Count < maxCartes)
        {
            choisies.AddRange(d.Vues
                .Where(v => v.Echeance > jour && v.DerniereRevision < jour && Dans(v.PackId))
                .OrderBy(v => v.Rappel(jour)).ThenBy(v => v.CarteId, StringComparer.Ordinal)
                .Take(maxCartes - choisies.Count)
                .Select(v => new CarteChoisie(v.CarteId, v.PackId, Motifs.LectureRecente(v.DerniereRevision, jour))));
        }

        return new SessionComposee(demande.Type, Ordonner(choisies, parPack), bloquees, debloquerUtile, [], devoir);
    }

    /// <summary>Met les cartes en paquets de matière (voir <see cref="Paquets"/>).</summary>
    private static List<CarteChoisie> Ordonner(IReadOnlyList<CarteChoisie> cartes, IReadOnlyDictionary<string, ResumeSeance> parPack) =>
        Paquets.Composer(cartes, c => OrdreDuCours.CleMatiere(parPack[c.PackId]), c => parPack[c.PackId].Partie);

    /// <summary>
    /// « Mes points faibles » : les cartes déjà vues souvent ratées et celles des séances au taux de réussite bas,
    /// les plus ratées d'abord, puis les plus fragiles. Pas de nouvelle carte.
    /// </summary>
    private static SessionComposee PointsFaibles(DonneesSession d, IReadOnlyDictionary<string, ResumeSeance> parPack, int maxCartes)
    {
        // Le taux d'une séance ne compte (et ne s'affiche) qu'à partir de quelques réponses.
        double? Taux(string packId) => d.Reussites.TryGetValue(packId, out var r) && r.Reponses >= ReponsesMinimumPourTaux ? r.Taux : null;
        var seancesFaibles = d.Reussites
            .Where(kv => kv.Value.Reponses >= ReponsesMinimumPourTaux && kv.Value.Taux < TauxFaible)
            .Select(kv => kv.Key)
            .ToHashSet();

        var cartes = d.Vues
            .Where(v => parPack.ContainsKey(v.PackId) && (v.FoisRatee > 0 || seancesFaibles.Contains(v.PackId)))
            .OrderByDescending(v => v.FoisRatee).ThenBy(v => v.Rappel(d.Jour)).ThenBy(v => v.CarteId, StringComparer.Ordinal)
            .Take(maxCartes)
            .ToList();

        var seances = cartes
            .GroupBy(v => v.PackId)
            .Select(g => new SeanceFaible(parPack[g.Key], g.Count(), g.Sum(v => v.FoisRatee), Taux(g.Key)))
            .OrderByDescending(s => s.FoisRatees).ThenBy(s => s.Taux ?? 1).ThenBy(s => s.Seance, OrdreDuCours.Instance)
            .ToList();

        var choisies = cartes.Select(v => new CarteChoisie(v.CarteId, v.PackId, Motifs.PointFaible(v.FoisRatee, Taux(v.PackId)))).ToList();
        return new SessionComposee(TypeSession.PointsFaibles, Ordonner(choisies, parPack), 0, false, seances, null);
    }

    /// <summary>Motif d'une nouvelle carte : pourquoi elle arrive maintenant.</summary>
    private static Motif MotifNouvelle(DonneesSession d, ResumeSeance seance, CandidateNouvelle carte, Devoir? devoirSession)
    {
        if (devoirSession is not null) return Motifs.Devoir(devoirSession);
        if (ChoixNouvelles.Rang(seance.PackId, d.Lus.Contains(seance.PackId), d.Planning, d.Jour).Rang == RangNouvelle.Devoir
            && DevoirDuPack(d, seance.PackId) is { } devoir)
            return Motifs.Devoir(devoir);
        if (seance.Lu) return Motifs.NouvelleLue;
        if (d.Lus.Contains(seance.PackId) || carte.Prioritaire) return Motifs.NouvelleVue;
        if (d.PlanningSuivi && d.Planning!.PeriodeDuPack(seance.PackId) is { } p && PlanningEffectif.MomentDe(p, d.Jour) == Moment.EnCours)
            return Motifs.NouvelleAuProgramme;
        return Motifs.NouvelleDecouverte;
    }

    /// <summary>Devoir en préparation le plus proche qui porte sur ce pack (planning suivi), ou null.</summary>
    private static Devoir? DevoirDuPack(DonneesSession d, string packId) =>
        d.PlanningSuivi
            ? d.Planning!.DevoirsEnPreparation(d.Jour).OrderBy(dv => dv.DateLimite).FirstOrDefault(dv => d.Planning.CouvrePack(dv, packId))
            : null;

    /// <summary>
    /// Prochain devoir du planning (date limite aujourd'hui ou plus tard) qui porte sur au moins une séance de la bibliothèque,
    /// ou null s'il n'y a pas de planning.
    /// </summary>
    public static Devoir? ProchainDevoir(DonneesSession d)
    {
        if (d.Planning is not { } planning) return null;
        var prochains = planning.ProchainesEcheances(d.Jour, int.MaxValue);
        return prochains.FirstOrDefault(dv => d.Seances.Any(s => planning.CouvrePack(dv, s.PackId))) ?? prochains.FirstOrDefault();
    }

    /// <summary>Séances ouvertes en lecture ces <see cref="JoursLectureRecente"/> derniers jours.</summary>
    public static HashSet<string> PacksLusRecemment(DonneesSession d)
    {
        var depuis = d.Jour.AddDays(-(JoursLectureRecente - 1));
        return d.DernieresLectures.Where(kv => kv.Value >= depuis && kv.Value <= d.Jour).Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>Toutes les séances d'un niveau de la bibliothèque (sous-niveaux compris).</summary>
    public static HashSet<string> PacksDe(NoeudBibliotheque noeud)
    {
        var packs = noeud.Seances.Select(s => s.PackId).ToHashSet();
        foreach (var enfant in noeud.Enfants) packs.UnionWith(PacksDe(enfant));
        return packs;
    }

    /// <summary>
    /// Séances à lire pour débloquer de nouvelles cartes (1 à <paramref name="nombre"/>), parmi celles qui ont des cartes bloquées.
    /// Avec un planning suivi : la logique du bloc « Au programme » (devoir proche, période en cours…).
    /// Sinon, ou si le planning ne propose rien : la première séance non lue de chaque matière, dans l'ordre du cours,
    /// les matières déjà commencées d'abord.
    /// </summary>
    public static List<SuggestionLecture> SeancesADebloquer(DonneesSession d, int nombre = 3)
    {
        if (nombre <= 0 || d.NouvellesNonLues) return [];
        var bloquees = d.Nouvelles.Where(c => !d.EstDebloquee(c)).Select(c => c.PackId).ToHashSet();
        var seances = d.Seances.Where(s => bloquees.Contains(s.PackId)).ToList();
        if (seances.Count == 0) return [];

        var parPlanning = ChoixNouvelles.SuggestionsDeLecture(seances, d.Lus, d.Planning, d.Jour, nombre);
        if (parPlanning.Count > 0) return parPlanning;

        var commencees = d.Seances.Where(s => d.Lus.Contains(s.PackId)).Select(OrdreDuCours.CleMatiere).ToHashSet();
        return seances
            .Order(OrdreDuCours.Instance)
            .GroupBy(OrdreDuCours.CleMatiere)
            .Select(g => g.First())
            .OrderBy(s => commencees.Contains(OrdreDuCours.CleMatiere(s)) ? 0 : 1)
            .ThenBy(s => s, OrdreDuCours.Instance)
            .Take(nombre)
            .Select(s => new SuggestionLecture(s, commencees.Contains(OrdreDuCours.CleMatiere(s))
                ? "La suite de ce que tu as lu"
                : "Pour commencer cette matière"))
            .ToList();
    }
}
