using Revisions.Core.Bibliotheque;
using Revisions.Core.Ordre;
using Revisions.Core.Planning;

namespace Revisions.Core.Sessions;

/// <summary>Une nouvelle carte candidate, sans son contenu (le contenu n'est chargé que pour les cartes choisies).</summary>
/// <param name="Position">Rang de la carte dans son pack (ordre du cours).</param>
/// <param name="Prioritaire">Séance marquée « Vu » dans Mes acquis (voir <see cref="Acquis.CalculAcquis.EtatInitial"/>).</param>
public sealed record CandidateNouvelle(string CarteId, string PackId, int Position, bool Prioritaire = false);

/// <summary>Rang de priorité d'une nouvelle carte (du plus urgent au moins urgent).</summary>
public enum RangNouvelle
{
    /// <summary>Contenu d'un devoir dont la date limite est dans les 14 jours.</summary>
    Devoir,
    /// <summary>Séance lue dans l'application ou marquée « Vu / Révisé » dans Mes acquis.</summary>
    Lue,
    /// <summary>Période en cours (ou matière non suivie, ou pas de planning : comme avant).</summary>
    EnCours,
    /// <summary>Période passée : proposée du plus ancien au plus récent, part plafonnée.</summary>
    Passee,
    /// <summary>Pack absent du planning (fiches de référence) : en complément.</summary>
    HorsPlanning,
    /// <summary>Période future, jamais lue : pas de cartes, seulement une suggestion de lecture.</summary>
    NonProposee,
}

/// <summary>Une séance proposée à la lecture sur l'accueil (bloc « Au programme »).</summary>
public sealed record SuggestionLecture(ResumeSeance Seance, string Raison);

/// <summary>
/// Choisit les nouvelles cartes du jour et les séances à lire.
/// Calcul pur, sans base de données : tout est testé dans PlanningTests et OrdreTests.
/// </summary>
public static class ChoixNouvelles
{
    /// <summary>
    /// Nouvelles cartes du jour, dans l'ordre où elles doivent être présentées.
    /// Sans planning (ou interrupteur général coupé) : séances lues d'abord, puis tout le reste,
    /// dans l'ordre du cours et en alternant les matières.
    /// Avec planning : devoir proche › lues › période en cours › périodes passées (au plus la moitié du jour,
    /// tant qu'il reste du contenu en cours) › hors planning. Le contenu futur jamais lu n'est pas proposé.
    /// </summary>
    /// <param name="candidates">Toutes les nouvelles cartes.</param>
    /// <param name="seances">Les séances (pour l'ordre du cours et la matière).</param>
    /// <param name="packsLus">Séances lues dans l'application ou marquées « Vu / Révisé ».</param>
    /// <param name="planning">Planning actif, ou null.</param>
    /// <param name="maximum">Nombre maximal de nouvelles cartes (déjà réduit par les limites du jour).</param>
    public static List<CandidateNouvelle> Choisir(
        IEnumerable<CandidateNouvelle> candidates,
        IReadOnlyList<ResumeSeance> seances,
        IReadOnlySet<string> packsLus,
        PlanningEffectif? planning,
        DateOnly jour,
        int maximum)
    {
        if (maximum <= 0) return [];
        var parId = seances.ToDictionary(s => s.PackId);
        var rangs = new Dictionary<string, (RangNouvelle Rang, DateOnly Cle)>();

        // Les cartes sont rangées par rang, puis triées dans l'ordre du cours.
        var parRang = new Dictionary<RangNouvelle, List<(CandidateNouvelle Carte, ResumeSeance Seance, DateOnly Cle)>>();
        foreach (var carte in candidates)
        {
            if (!parId.TryGetValue(carte.PackId, out var seance)) continue;
            if (!rangs.TryGetValue(carte.PackId, out var rang))
                rangs[carte.PackId] = rang = Rang(carte.PackId, packsLus.Contains(carte.PackId), planning, jour);
            (RangNouvelle Rang, DateOnly Cle) r = carte.Prioritaire && rang.Rang > RangNouvelle.Lue ? (RangNouvelle.Lue, DateOnly.MinValue) : rang;
            if (r.Rang == RangNouvelle.NonProposee) continue;
            if (!parRang.TryGetValue(r.Rang, out var liste)) parRang[r.Rang] = liste = [];
            liste.Add((carte, seance, r.Cle));
        }

        List<CandidateNouvelle> Ordonner(RangNouvelle rang)
        {
            if (!parRang.TryGetValue(rang, out var liste)) return [];
            var triees = liste
                .OrderBy(x => x.Cle)                                    // devoir : date limite ; passé : début de période
                .ThenBy(x => x.Seance, OrdreDuCours.Instance)           // ordre naturel du cours
                .ThenBy(x => x.Carte.Position)                          // ordre des cartes dans le pack
                .ThenBy(x => x.Carte.CarteId, StringComparer.Ordinal);
            return Tourniquet.Entrelacer(triees, x => OrdreDuCours.CleMatiere(x.Seance)).Select(x => x.Carte).ToList();
        }

        var resultat = new List<CandidateNouvelle>();
        void Ajouter(List<CandidateNouvelle> cartes, int nombre) =>
            resultat.AddRange(cartes.Take(Math.Max(0, Math.Min(nombre, maximum - resultat.Count))));

        Ajouter(Ordonner(RangNouvelle.Devoir), maximum);
        Ajouter(Ordonner(RangNouvelle.Lue), maximum);

        // Contenu en cours et périodes passées : les périodes passées ont au plus la moitié des nouvelles du jour,
        // pour que le contenu actuel continue d'avancer. S'il n'y a plus rien en cours, elles prennent la place libre.
        var enCours = Ordonner(RangNouvelle.EnCours);
        var passees = Ordonner(RangNouvelle.Passee);
        int reste = maximum - resultat.Count;
        int plafondPassees = PlafondPassees(maximum);
        int nbEnCours = Math.Min(enCours.Count, reste - Math.Min(passees.Count, Math.Min(plafondPassees, reste)));
        int nbPassees = Math.Min(passees.Count, reste - nbEnCours);
        Ajouter(enCours, nbEnCours);
        Ajouter(passees, nbPassees);

        Ajouter(Ordonner(RangNouvelle.HorsPlanning), maximum);
        return resultat;
    }

    /// <summary>Part maximale des périodes passées dans les nouvelles cartes du jour : la moitié (arrondie en dessous).</summary>
    public static int PlafondPassees(int maximum) => Math.Max(0, maximum) / 2;

    /// <summary>
    /// Rang d'un pack, avec une clé de tri secondaire (date limite du devoir, ou début de la période passée).
    /// </summary>
    public static (RangNouvelle Rang, DateOnly Cle) Rang(string packId, bool lu, PlanningEffectif? planning, DateOnly jour)
    {
        if (planning is null || !planning.Reglages.Suivre)
            return (lu ? RangNouvelle.Lue : RangNouvelle.EnCours, DateOnly.MinValue);

        var periode = planning.PeriodeDuPack(packId);
        if (periode is null)
        {
            // Matière non suivie : comme sans planning. Fiche de référence : en complément.
            if (lu) return (RangNouvelle.Lue, DateOnly.MinValue);
            return (planning.MatiereIgnoree(packId) ? RangNouvelle.EnCours : RangNouvelle.HorsPlanning, DateOnly.MinValue);
        }

        var moment = PlanningEffectif.MomentDe(periode, jour);
        // Devoir proche : seulement le contenu déjà commencé ou déjà lu (jamais de cartes sur un cours pas encore abordé).
        if (lu || moment != Moment.Future)
        {
            var devoir = planning.DevoirsEnPreparation(jour).FirstOrDefault(d => planning.Couverture(d).Contains(periode));
            if (devoir is not null) return (RangNouvelle.Devoir, devoir.DateLimite);
        }
        if (lu) return (RangNouvelle.Lue, DateOnly.MinValue);
        return moment switch
        {
            Moment.EnCours => (RangNouvelle.EnCours, DateOnly.MinValue),
            Moment.Passee => (RangNouvelle.Passee, periode.Debut),
            _ => (RangNouvelle.NonProposee, DateOnly.MinValue),
        };
    }

    /// <summary>
    /// Séances à lire (bloc « Au programme » de l'accueil), seulement quand un planning est suivi.
    /// Séances pas encore lues, de matières suivies : devoir proche, puis période en cours, puis périodes passées
    /// (les plus anciennes d'abord), puis la prochaine période. Une matière à la fois, à tour de rôle.
    /// </summary>
    public static List<SuggestionLecture> SuggestionsDeLecture(
        IReadOnlyList<ResumeSeance> seances,
        IReadOnlySet<string> packsLus,
        PlanningEffectif? planning,
        DateOnly jour,
        int nombre = 3)
    {
        if (planning is null || !planning.Reglages.Suivre || nombre <= 0) return [];
        var preparation = planning.DevoirsEnPreparation(jour);

        var candidates = new List<(int Rang, DateOnly Cle, ResumeSeance Seance, string Raison)>();
        foreach (var s in seances)
        {
            if (packsLus.Contains(s.PackId) || planning.PeriodeDuPack(s.PackId) is not { } periode) continue;
            var devoir = preparation.FirstOrDefault(d => planning.Couverture(d).Contains(periode));
            var moment = PlanningEffectif.MomentDe(periode, jour);
            if (devoir is not null)
                candidates.Add((0, devoir.DateLimite, s, $"Utile pour {Libelles.Devoir(devoir)}"));
            else if (moment == Moment.EnCours)
                candidates.Add((1, DateOnly.MinValue, s, "Au programme en ce moment"));
            else if (moment == Moment.Passee)
                candidates.Add((2, periode.Debut, s, "Prévu plus tôt dans l'année, à ton rythme"));
            else
                candidates.Add((3, periode.Debut, s, $"Bientôt : à partir du {periode.Debut:dd'/'MM}"));
        }

        // Ordre de priorité : rang, puis tourniquet des matières, dans l'ordre du cours à l'intérieur de chaque matière.
        var ordonnees = candidates
            .GroupBy(c => c.Rang).OrderBy(g => g.Key)
            .SelectMany(g => Tourniquet.Entrelacer(
                g.OrderBy(c => c.Cle).ThenBy(c => c.Seance, OrdreDuCours.Instance),
                c => OrdreDuCours.CleMatiere(c.Seance)))
            .ToList();

        // D'abord des matières différentes (pour varier), puis on complète s'il y a moins de matières que de places.
        var choisies = new List<(int Rang, DateOnly Cle, ResumeSeance Seance, string Raison)>();
        foreach (var c in ordonnees)
            if (choisies.Count < nombre && choisies.All(x => OrdreDuCours.CleMatiere(x.Seance) != OrdreDuCours.CleMatiere(c.Seance)))
                choisies.Add(c);
        foreach (var c in ordonnees)
            if (choisies.Count < nombre && !choisies.Contains(c))
                choisies.Add(c);

        // Affichage dans l'ordre de priorité.
        return ordonnees.Where(choisies.Contains).Select(c => new SuggestionLecture(c.Seance, c.Raison)).ToList();
    }
}

/// <summary>Textes neutres pour parler des devoirs (jamais de « retard », jamais de reproche : R1 à R4).</summary>
public static class Libelles
{
    /// <summary>« devoir noté DV1 » ou « quiz d'auto-évaluation QZ1 ».</summary>
    public static string Devoir(Devoir d) => d.Note ? $"le devoir noté {d.Code}" : $"le quiz d'auto-évaluation {d.Code}";

    /// <summary>« Maths – devoir noté DV1, semaine du 30/11 ».</summary>
    public static string Echeance(Devoir d) =>
        $"{d.Matiere} – {(d.Note ? "devoir noté" : "quiz d'auto-évaluation")} {d.Code}, semaine du {d.Semaine:dd'/'MM}";
}
