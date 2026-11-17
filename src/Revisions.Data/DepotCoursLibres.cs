using Dapper;

namespace Revisions.Data;

/// <summary>
/// Cours libres livrés avec Atome : chargement, masquage et réaffichage.
/// Les packs sont lus en lecture seule dans le dossier d'installation ; un réimport (mise à jour d'Atome)
/// les met à jour avec la logique habituelle, sans perdre la progression (ids stables « libre-… »).
/// Masquer ne supprime rien : les packs sont cachés, leurs cartes et sections mises à l'écart (archivee = 2),
/// et tout revient tel quel quand on réactive. Les packs privés ne sont jamais touchés.
/// </summary>
public sealed class DepotCoursLibres(BaseDeDonnees baseDeDonnees, ImportateurPacks importateur)
{
    private const string Libre = "SELECT id FROM pack WHERE id LIKE 'libre-%'";

    /// <summary>Nombre de packs en base qui ne sont pas des cours libres (packs privés de l'étudiant).</summary>
    public int NombrePacksPrives()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM pack WHERE id NOT LIKE 'libre-%'");
    }

    /// <summary>Nombre de cours libres actuellement visibles.</summary>
    public int NombreVisibles()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM pack WHERE id LIKE 'libre-%' AND masque = 0");
    }

    /// <summary>Charge (ou met à jour) les cours libres depuis le dossier donné, et les rend visibles.</summary>
    public List<ResultatImport> Activer(string dossierPacksLibres)
    {
        Reafficher();
        return importateur.ImporterDossier(dossierPacksLibres, null);
    }

    /// <summary>Rend visibles les cours libres déjà en base (sans relire le dossier).</summary>
    public void Reafficher()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        connexion.Execute($"UPDATE carte SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({Libre})", transaction: transaction);
        connexion.Execute($"UPDATE section SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({Libre})", transaction: transaction);
        connexion.Execute("UPDATE pack SET masque = 0 WHERE id LIKE 'libre-%'", transaction: transaction);
        transaction.Commit();
    }

    /// <summary>Cache les cours libres : plus de lecture, de cartes ni de statistiques ; la progression est conservée.</summary>
    public void Masquer()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        connexion.Execute($"UPDATE carte SET archivee = 2 WHERE archivee = 0 AND pack_id IN ({Libre})", transaction: transaction);
        connexion.Execute($"UPDATE section SET archivee = 2 WHERE archivee = 0 AND pack_id IN ({Libre})", transaction: transaction);
        connexion.Execute("UPDATE pack SET masque = 1 WHERE id LIKE 'libre-%'", transaction: transaction);
        transaction.Commit();
    }
}
