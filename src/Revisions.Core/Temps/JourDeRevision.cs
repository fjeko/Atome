namespace Revisions.Core.Temps;

/// <summary>
/// Le « jour de révision » commence à 4 h du matin : réviser à 1 h du matin compte pour la veille.
/// Toutes les échéances et les statistiques utilisent ce jour, jamais l'heure exacte.
/// </summary>
public static class JourDeRevision
{
    public const int HeureDeDebut = 4;

    /// <summary>Jour de révision correspondant à l'instant présent de l'horloge donnée.</summary>
    public static DateOnly Aujourdhui(TimeProvider horloge) =>
        DateOnly.FromDateTime(horloge.GetLocalNow().DateTime.AddHours(-HeureDeDebut));

    /// <summary>Lundi de la semaine qui contient ce jour (les semaines vont du lundi au dimanche).</summary>
    public static DateOnly LundiDe(DateOnly jour) =>
        jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
}
