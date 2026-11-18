using Dapper;
using Revisions.Core.TdGroupe;

namespace Revisions.Data;

/// <summary>
/// TD solo : historique des exercices (pour pondérer les prochains TD) et remise en révision des notions ratées.
/// Voir docs/td-solo.md.
/// </summary>
public sealed class DepotTd(BaseDeDonnees baseDeDonnees)
{
    /// <summary>Nombre maximal de cartes remises en révision pour un exercice raté.</summary>
    public const int CartesParNotion = 5;

    public void EnregistrerExercice(DateOnly jour, string generateur, string sujet, string? sectionId, IssueExercice issue)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute(
            "INSERT INTO td_exercice (jour, generateur, sujet, section_id, issue) VALUES (@jour, @generateur, @sujet, @sectionId, @issue)",
            new { jour = jour.ToString("yyyy-MM-dd"), generateur, sujet, sectionId, issue = (int)issue });
    }

    /// <summary>Bilan de chaque sujet déjà fait (clé « PREFIXE/sujet ») : nombre de fois, raté lors des 3 dernières.</summary>
    public Dictionary<string, BilanSujet> Historique()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var lignes = connexion.Query<(string Generateur, string Sujet, long Issue)>(
            "SELECT generateur, sujet, issue FROM td_exercice ORDER BY id DESC");
        return lignes
            .GroupBy(l => CompositeurTd.Cle(l.Generateur, l.Sujet))
            .ToDictionary(g => g.Key, g => new BilanSujet(g.Count(), g.Take(3).Any(l => l.Issue == (long)IssueExercice.PasEncore)));
    }

    /// <summary>
    /// Ramène à aujourd'hui l'échéance des cartes déjà vues d'une section (au plus <see cref="CartesParNotion"/>,
    /// les plus proches de leur échéance d'abord) : elles reviennent dans la prochaine révision. La stabilité FSRS ne change pas.
    /// Renvoie le nombre de cartes concernées.
    /// </summary>
    public int RemettreEnRevision(string sectionId, DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Execute(
            """
            UPDATE etat_carte SET echeance = @jour
            WHERE carte_id IN (
              SELECT e.carte_id FROM etat_carte e JOIN carte c ON c.id = e.carte_id
              WHERE c.section_id = @sectionId AND c.archivee = 0
                AND e.stabilite IS NOT NULL AND e.echeance > @jour
              ORDER BY e.echeance
              LIMIT @max)
            """,
            new { sectionId, jour = jour.ToString("yyyy-MM-dd"), max = CartesParNotion });
    }
}
