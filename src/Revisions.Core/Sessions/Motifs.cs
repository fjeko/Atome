using Revisions.Core.Planning;

namespace Revisions.Core.Sessions;

/// <summary>Famille de motifs : pourquoi une carte est dans la session.</summary>
public enum GenreMotif
{
    /// <summary>Carte déjà vue dont l'échéance FSRS est arrivée.</summary>
    ARevoir,
    /// <summary>Nouvelle carte (jamais vue).</summary>
    Nouvelle,
    /// <summary>Carte souvent ratée, ou séance au taux de réussite bas.</summary>
    PointFaible,
    /// <summary>Carte choisie pour préparer un devoir du planning.</summary>
    Devoir,
    /// <summary>Carte d'un cours lu ces derniers jours, revue pour le consolider.</summary>
    LectureRecente,
}

/// <summary>Le motif affiché en petit au-dessus d'une carte (« À revoir · vue il y a 6 jours »).</summary>
public sealed record Motif(GenreMotif Genre, string Texte);

/// <summary>Textes des motifs. Ton neutre : jamais de « retard » ni de reproche (R1).</summary>
public static class Motifs
{
    /// <summary>« aujourd'hui », « hier » ou « il y a 6 jours ».</summary>
    public static string IlYa(DateOnly date, DateOnly jour) => (jour.DayNumber - date.DayNumber) switch
    {
        <= 0 => "aujourd'hui",
        1 => "hier",
        var n => $"il y a {n} jours",
    };

    public static Motif ARevoir(DateOnly derniereRevision, DateOnly jour) =>
        new(GenreMotif.ARevoir, $"À revoir · vue {IlYa(derniereRevision, jour)}");

    public static Motif NouvelleLue { get; } = new(GenreMotif.Nouvelle, "Nouvelle · tu as lu ce cours");
    public static Motif NouvelleVue { get; } = new(GenreMotif.Nouvelle, "Nouvelle · tu as indiqué avoir vu ce cours");
    public static Motif NouvelleAuProgramme { get; } = new(GenreMotif.Nouvelle, "Nouvelle · au programme cette semaine");
    public static Motif NouvelleDecouverte { get; } = new(GenreMotif.Nouvelle, "Nouvelle · à découvrir");

    /// <summary>« Point faible · ratée 3 fois », ou le taux de la séance si la carte elle-même n'a jamais été ratée.</summary>
    public static Motif PointFaible(int foisRatee, double? tauxSeance) =>
        new(GenreMotif.PointFaible, foisRatee > 0
            ? $"Point faible · ratée {foisRatee} fois"
            : $"Point faible · séance réussie à {Pourcentage(tauxSeance ?? 0)}");

    /// <summary>« Préparation du devoir DV1 (semaine du 30/11) » ou « Préparation du quiz QZ2 (semaine du 30/11) ».</summary>
    public static Motif Devoir(Devoir d) =>
        new(GenreMotif.Devoir, $"Préparation du {(d.Note ? "devoir" : "quiz")} {d.Code} (semaine du {d.Semaine:dd'/'MM})");

    public static Motif LectureRecente(DateOnly derniereRevision, DateOnly jour) =>
        new(GenreMotif.LectureRecente, $"Tu viens de lire ce cours · vue {IlYa(derniereRevision, jour)}");

    /// <summary>« 55 % » (arrondi à l'unité).</summary>
    public static string Pourcentage(double taux) => $"{Math.Round(taux * 100):0} %";
}
