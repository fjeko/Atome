using Revisions.Core.Modeles;

namespace Revisions.Core.Planification;

/// <summary>Résultat d'une réponse : le nouvel état de la carte et la probabilité de rappel au moment de répondre.</summary>
public sealed record ResultatPlanification(EtatCarte NouvelEtat, double? Retrievabilite, int IntervalleJours);

/// <summary>
/// Fait le lien entre l'algorithme FSRS (des nombres) et les cartes de l'application (des dates).
/// </summary>
public sealed class Planificateur(Fsrs fsrs)
{
    /// <summary>Applique une note à une carte et calcule sa prochaine échéance.</summary>
    /// <param name="etat">État actuel de la carte.</param>
    /// <param name="note">Note donnée par l'étudiant.</param>
    /// <param name="jour">Jour de révision (voir <see cref="Temps.JourDeRevision"/>).</param>
    /// <param name="retention">Rétention visée, entre 0,80 et 0,95.</param>
    public ResultatPlanification Appliquer(EtatCarte etat, Note note, DateOnly jour, double retention)
    {
        EtatMemoire memoire;
        double? r = null;

        if (etat.EstNouvelle)
        {
            memoire = fsrs.PremiereReponse(note);
        }
        else
        {
            int jours = Math.Max(0, jour.DayNumber - etat.DerniereRevision!.Value.DayNumber);
            r = Fsrs.Retrievabilite(jours, etat.Stabilite!.Value);
            memoire = fsrs.ReponseSuivante(new EtatMemoire(etat.Stabilite.Value, etat.DifficulteFsrs!.Value), jours, note);
        }

        int intervalle = Fsrs.Intervalle(memoire.Stabilite, retention);
        var nouvelEtat = etat with
        {
            Stabilite = memoire.Stabilite,
            DifficulteFsrs = memoire.Difficulte,
            Echeance = jour.AddDays(intervalle),
            DerniereRevision = jour,
            Repetitions = etat.Repetitions + 1,
            Oublis = etat.Oublis + (note == Note.Rate && !etat.EstNouvelle ? 1 : 0),
        };
        return new ResultatPlanification(nouvelEtat, r, intervalle);
    }

    /// <summary>Probabilité de se souvenir d'une carte aujourd'hui (1 pour une carte jamais vue : rien à oublier).</summary>
    public static double RetrievabiliteActuelle(EtatCarte etat, DateOnly jour) =>
        etat.EstNouvelle
            ? 1
            : Fsrs.Retrievabilite(jour.DayNumber - etat.DerniereRevision!.Value.DayNumber, etat.Stabilite!.Value);

    /// <summary>Une carte est dite « maîtrisée » quand sa stabilité atteint 21 jours.</summary>
    public const double StabiliteMaitrise = 21;
}
