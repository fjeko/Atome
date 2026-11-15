using Revisions.Core.Modeles;

namespace Revisions.Core.Sessions;

/// <summary>Ce qu'il faut savoir pour décider de la session du jour.</summary>
public sealed record ContexteJour(
    DateOnly Jour,
    DateOnly? DernierJourActif,
    DateOnly? DebutReprise,
    int NbDues,
    int FaitesAujourdhui,
    int NouvellesVuesAujourdhui,
    Reglages Reglages);

/// <summary>La décision : combien de cartes proposer aujourd'hui, et dans quel esprit.</summary>
/// <param name="ModeReprise">Vrai pendant le retour en douceur après une longue pause.</param>
/// <param name="RetourApresPause">Vrai le jour même du retour (pour le message d'accueil).</param>
/// <param name="DebutReprise">Jour où la reprise a commencé (à mémoriser), ou null si elle est finie.</param>
/// <param name="MaxCartes">Nombre de cartes que l'on peut encore proposer aujourd'hui.</param>
/// <param name="MaxNouvelles">Nombre de nouvelles cartes que l'on peut encore proposer aujourd'hui.</param>
/// <param name="PlafondAtteint">Vrai si le quota du jour est déjà rempli.</param>
public sealed record PlanDuJour(
    bool ModeReprise,
    bool RetourApresPause,
    DateOnly? DebutReprise,
    int MaxCartes,
    int MaxNouvelles,
    bool PlafondAtteint);

/// <summary>
/// Règles du « rythme irrégulier » : plafond quotidien (R3), limite de nouvelles cartes (R4)
/// et mode reprise (R2). Calcul pur, sans base de données : facile à tester.
/// </summary>
public static class CalculPlanDuJour
{
    /// <summary>Nombre de cartes ajoutées chaque jour à la session pendant la reprise (10, 15, 20…).</summary>
    public const int PasDeReprise = 5;

    public static PlanDuJour Calculer(ContexteJour c)
    {
        var r = c.Reglages;

        // Retour après une pause : dernière activité il y a 7 jours ou plus (réglable).
        bool retour = c.DernierJourActif is { } dernier
                      && c.Jour.DayNumber - dernier.DayNumber >= r.JoursAvantReprise;
        DateOnly? debut = retour ? c.Jour : c.DebutReprise;

        if (debut is { } d)
        {
            // La session grandit doucement de jour en jour, jusqu'au plafond habituel.
            int joursDeReprise = Math.Max(0, c.Jour.DayNumber - d.DayNumber);
            int taille = r.TailleReprise + PasDeReprise * joursDeReprise;

            // La reprise s'arrête quand le retard est absorbé ou que la taille rejoint le plafond.
            bool termine = taille >= r.PlafondQuotidien || c.NbDues + c.FaitesAujourdhui <= taille;
            if (!termine)
            {
                int reste = Math.Max(0, taille - c.FaitesAujourdhui);
                return new PlanDuJour(true, retour, d, reste, 0, reste == 0);
            }
        }

        int maxCartes = Math.Max(0, r.PlafondQuotidien - c.FaitesAujourdhui);
        int maxNouvelles = Math.Max(0, r.NouvellesParJour - c.NouvellesVuesAujourdhui);
        return new PlanDuJour(false, retour, null, maxCartes, maxNouvelles, maxCartes == 0);
    }
}
