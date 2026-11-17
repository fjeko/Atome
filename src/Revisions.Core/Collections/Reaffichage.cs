namespace Revisions.Core.Collections;

/// <summary>Échéance d'une carte déjà vue.</summary>
public sealed record EcheanceCarte(string CarteId, DateOnly Echeance);

/// <summary>
/// Réafficher une collection masquée sans « avalanche » : les cartes devenues dues pendant qu'elle était masquée
/// sont étalées sur les jours qui viennent, au plus <c>parJour</c> par jour, les plus en retard d'abord.
/// Seule l'échéance bouge : la mémoire de la carte (stabilité, historique) reste intacte.
/// </summary>
public static class Reaffichage
{
    /// <summary>
    /// Nouvelles échéances des cartes à décaler (celles qui n'ont pas besoin de changer ne sont pas renvoyées).
    /// </summary>
    /// <param name="cartes">Cartes déjà vues de la collection.</param>
    /// <param name="jour">Aujourd'hui.</param>
    /// <param name="masqueDepuis">Jour où la collection a été masquée (null si inconnu : toutes les cartes en retard comptent).
    /// Les cartes déjà en retard à ce moment-là l'étaient avant le masquage : on n'y touche pas.</param>
    /// <param name="parJour">Nombre maximal de cartes ramenées par jour.</param>
    public static List<EcheanceCarte> Etaler(IEnumerable<EcheanceCarte> cartes, DateOnly jour, DateOnly? masqueDepuis, int parJour)
    {
        parJour = Math.Max(1, parJour);
        var enRetard = cartes
            .Where(c => c.Echeance <= jour && (masqueDepuis is null || c.Echeance > masqueDepuis))
            .OrderBy(c => c.Echeance).ThenBy(c => c.CarteId, StringComparer.Ordinal)
            .ToList();
        // Rien à étaler tant que le lot tient dans la journée.
        if (enRetard.Count <= parJour) return [];

        var resultat = new List<EcheanceCarte>();
        for (int i = parJour; i < enRetard.Count; i++)
            resultat.Add(enRetard[i] with { Echeance = jour.AddDays(i / parJour) });
        return resultat;
    }
}
