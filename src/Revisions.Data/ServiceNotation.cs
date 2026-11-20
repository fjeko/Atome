using Revisions.Core.Gamification;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Sessions;

namespace Revisions.Data;

/// <summary>Ce que renvoie une notation : le nouvel état FSRS de la carte et l'XP gagnée.</summary>
public sealed record ResultatNotation(ResultatPlanification Planification, int Xp);

/// <summary>
/// Notation d'une carte, commune à toutes les activités (sessions et parcours « Lire puis contrôler ») :
/// FSRS calcule le nouvel état, l'XP est calculée, puis l'historique, l'état et l'XP sont enregistrés ensemble.
/// Une seule logique de notation : aucune activité ne replanifie une carte autrement.
/// </summary>
public sealed class ServiceNotation(DepotRevisions revisions, Planificateur planificateur)
{
    /// <param name="retour">La carte revient dans la même activité (après un « Raté ») : pas d'XP.</param>
    public ResultatNotation Noter(long sessionId, CarteAReviser carte, Note note, bool retour, DateOnly jour,
        DateTimeOffset maintenant, int dureeMs, double retention)
    {
        bool nouvelle = carte.Etat.EstNouvelle;
        bool due = !nouvelle && carte.Etat.Echeance <= jour;
        var resultat = planificateur.Appliquer(carte.Etat, note, jour, retention);
        int xp = CalculXp.PourReponse(note, due, nouvelle, retour);
        revisions.EnregistrerReponse(sessionId, carte.Etat, resultat, note, jour, maintenant, dureeMs, due || nouvelle, xp);
        return new ResultatNotation(resultat, xp);
    }
}
