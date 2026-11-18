namespace Revisions.Core.Gamification;

/// <summary>Un jour du calendrier de régularité.</summary>
/// <param name="Niveau">Intensité de 0 (rien) à 4 (journée la plus chargée), pour la couleur de la case.</param>
/// <param name="Futur">Jour pas encore arrivé (case vide, sans bordure).</param>
public sealed record JourCalendrier(DateOnly Jour, int Reponses, int Niveau, bool Futur);

/// <summary>
/// Calendrier de régularité (écran Statistiques) : une colonne par semaine, du lundi au dimanche.
/// L'intensité est relative à la journée la plus chargée de la période : on voit la régularité, pas un score.
/// </summary>
public static class Calendrier
{
    public static IReadOnlyList<IReadOnlyList<JourCalendrier>> Construire(
        DateOnly premierLundi, int semaines, IReadOnlyDictionary<DateOnly, int> reponsesParJour, DateOnly aujourdhui)
    {
        int maximum = Math.Max(1, reponsesParJour.Where(p => p.Key >= premierLundi).Select(p => p.Value).DefaultIfEmpty(0).Max());
        return Enumerable.Range(0, semaines)
            .Select(s => (IReadOnlyList<JourCalendrier>)Enumerable.Range(0, 7)
                .Select(j =>
                {
                    var jour = premierLundi.AddDays(7 * s + j);
                    int n = reponsesParJour.GetValueOrDefault(jour);
                    return new JourCalendrier(jour, n, Niveau(n, maximum), jour > aujourdhui);
                }).ToList())
            .ToList();
    }

    /// <summary>0 sans réponse, sinon de 1 à 4 selon la part du maximum (quarts).</summary>
    public static int Niveau(int reponses, int maximum) =>
        reponses <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(4.0 * reponses / Math.Max(1, maximum)), 1, 4);
}
