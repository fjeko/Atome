using Revisions.Core.Planification;
using Revisions.Core.Planning;

namespace Revisions.Core.Sessions;

/// <summary>Une carte déjà vue, réduite à ce qu'il faut pour décider de l'avancer.</summary>
public sealed record CarteVue(string CarteId, string PackId, double Stabilite, DateOnly DerniereRevision, DateOnly Echeance);

/// <summary>
/// Préparation d'un devoir : pendant les 14 jours avant la date limite, une carte du contenu du devoir
/// dont l'échéance tombe APRÈS la date limite, et qu'on risquerait d'avoir oubliée ce jour-là
/// (probabilité de rappel prévue à la date limite &lt; 0,85), est avancée.
/// Les cartes sont étalées sur les jours restants : chaque jour, on en avance au plus
/// ⌈restantes ÷ jours restants⌉, les plus fragiles d'abord.
/// Après la date limite, plus rien n'est avancé : FSRS reprend la main tout seul.
/// Le plafond quotidien reste prioritaire : les cartes avancées passent par la même limite que les cartes dues.
/// </summary>
public static class AvanceeDevoir
{
    /// <summary>Probabilité de rappel prévue à une date donnée.</summary>
    public static double RetrievabiliteA(CarteVue c, DateOnly date) =>
        Fsrs.Retrievabilite(Math.Max(0, date.DayNumber - c.DerniereRevision.DayNumber), c.Stabilite);

    /// <summary>Identifiants des cartes à avancer aujourd'hui, les plus fragiles d'abord.</summary>
    public static List<string> CartesAAvancer(IEnumerable<CarteVue> cartesVues, PlanningEffectif? planning, DateOnly jour)
    {
        if (planning is null || !planning.Reglages.Suivre) return [];
        var devoirs = planning.DevoirsEnPreparation(jour);
        if (devoirs.Count == 0) return [];

        var cartes = cartesVues.ToList();
        var dejaPrises = new HashSet<string>();
        var resultat = new List<string>();

        // Le devoir le plus proche passe en premier : une carte commune à deux devoirs compte pour le plus proche.
        foreach (var devoir in devoirs.OrderBy(d => d.DateLimite))
        {
            var limite = devoir.DateLimite;
            var aAvancer = cartes
                .Where(c => !dejaPrises.Contains(c.CarteId)
                            && c.Echeance > limite                 // sinon elle reviendra d'elle-même avant le devoir
                            && c.DerniereRevision < jour           // déjà revue aujourd'hui : on n'insiste pas
                            && planning.CouvrePack(devoir, c.PackId)
                            && RetrievabiliteA(c, limite) < PlanningEffectif.SeuilRetrievabilite)
                .OrderBy(c => RetrievabiliteA(c, limite))
                .ThenBy(c => c.CarteId, StringComparer.Ordinal)
                .ToList();
            if (aAvancer.Count == 0) continue;

            int joursRestants = limite.DayNumber - jour.DayNumber + 1;      // aujourd'hui compris
            int parJour = (aAvancer.Count + joursRestants - 1) / joursRestants;
            foreach (var c in aAvancer.Take(parJour))
            {
                dejaPrises.Add(c.CarteId);
                resultat.Add(c.CarteId);
            }
            // Les autres cartes de ce devoir ne doivent pas être prises par un devoir plus lointain aujourd'hui.
            foreach (var c in aAvancer) dejaPrises.Add(c.CarteId);
        }
        return resultat;
    }
}
