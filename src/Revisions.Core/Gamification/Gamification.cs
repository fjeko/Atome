using Revisions.Core.Modeles;
using Revisions.Core.Temps;

namespace Revisions.Core.Gamification;

/// <summary>Barème d'XP. Règle d'or : l'XP ne fait que monter, on ne retire jamais rien (R1).</summary>
public static class CalculXp
{
    /// <summary>Bonus donné une fois par jour quand l'objectif minimal est atteint.</summary>
    public const int BonusObjectif = 15;

    /// <summary>XP gagnée pour une réponse.</summary>
    /// <param name="note">Note donnée.</param>
    /// <param name="etaitDue">La carte était à revoir aujourd'hui (les révisions en avance ne rapportent rien : G1).</param>
    /// <param name="etaitNouvelle">Première rencontre avec la carte.</param>
    /// <param name="retourDansLaSession">La carte a déjà été vue (et ratée) dans cette session.</param>
    public static int PourReponse(Note note, bool etaitDue, bool etaitNouvelle, bool retourDansLaSession)
    {
        if (retourDansLaSession) return 0;
        if (etaitNouvelle) return 5;
        if (!etaitDue) return 0;
        return note switch
        {
            Note.Rate => 2,        // on récompense l'essai
            Note.Difficile => 12,  // bonus : bonne réponse obtenue avec effort
            Note.Bien => 10,
            Note.Facile => 8,
            _ => 0,
        };
    }
}

/// <summary>Niveaux : le niveau N est atteint à 50 × N × (N + 1) XP (100, 300, 600, 1000…).</summary>
public static class Niveaux
{
    /// <summary>XP totale nécessaire pour atteindre un niveau.</summary>
    public static int XpPourNiveau(int niveau) => 50 * niveau * (niveau + 1);

    /// <summary>Niveau correspondant à une XP totale (0 au départ).</summary>
    public static int NiveauPour(int xp)
    {
        int niveau = 0;
        while (XpPourNiveau(niveau + 1) <= xp) niveau++;
        return niveau;
    }

    /// <summary>Avancement vers le niveau suivant, entre 0 et 1.</summary>
    public static double Progression(int xp)
    {
        int niveau = NiveauPour(xp);
        int debut = XpPourNiveau(niveau), fin = XpPourNiveau(niveau + 1);
        return (double)(xp - debut) / (fin - debut);
    }
}

/// <summary>Résumé de la régularité, compté en semaines (G2).</summary>
/// <param name="Actuelle">Semaines actives consécutives jusqu'à maintenant.</param>
/// <param name="Meilleure">Plus longue série jamais réalisée.</param>
/// <param name="ActivesSurQuatre">Nombre de semaines actives parmi les 4 dernières (objectif : 3).</param>
public sealed record Serie(int Actuelle, int Meilleure, int ActivesSurQuatre);

/// <summary>
/// Série comptée en semaines (lundi → dimanche). Une semaine compte dès qu'une carte y a été révisée.
/// La semaine en cours ne « casse » jamais la série tant qu'elle n'est pas terminée.
/// </summary>
public static class SerieHebdomadaire
{
    public static Serie Calculer(IEnumerable<DateOnly> joursActifs, DateOnly aujourdhui)
    {
        var semaines = joursActifs.Select(JourDeRevision.LundiDe).ToHashSet();
        DateOnly cetteSemaine = JourDeRevision.LundiDe(aujourdhui);

        // Série actuelle : on part de cette semaine si elle est active, sinon de la précédente.
        DateOnly depart = semaines.Contains(cetteSemaine) ? cetteSemaine : cetteSemaine.AddDays(-7);
        int actuelle = 0;
        for (var s = depart; semaines.Contains(s); s = s.AddDays(-7)) actuelle++;

        // Meilleure série : plus longue suite de lundis espacés de 7 jours.
        int meilleure = 0, enCours = 0;
        DateOnly? precedente = null;
        foreach (var s in semaines.Order())
        {
            enCours = precedente is { } p && s.DayNumber - p.DayNumber == 7 ? enCours + 1 : 1;
            meilleure = Math.Max(meilleure, enCours);
            precedente = s;
        }

        int surQuatre = Enumerable.Range(0, 4).Count(i => semaines.Contains(cetteSemaine.AddDays(-7 * i)));
        return new Serie(actuelle, meilleure, surQuatre);
    }
}
