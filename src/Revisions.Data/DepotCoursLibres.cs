using Dapper;
using Revisions.Core.Collections;

namespace Revisions.Data;

/// <summary>
/// Cours libres livrés avec Atome : chargement, masquage et réaffichage (c'est la collection « Cours libres »).
/// Les packs sont lus en lecture seule dans le dossier d'installation ; un réimport (mise à jour d'Atome)
/// les met à jour avec la logique habituelle, sans perdre la progression (ids stables « libre-… »).
/// Masquer ne supprime rien : voir <see cref="DepotCollections"/>. Les packs privés ne sont jamais touchés.
/// </summary>
public sealed class DepotCoursLibres(BaseDeDonnees baseDeDonnees, ImportateurPacks importateur)
{
    private readonly DepotCollections _collections = new(baseDeDonnees);

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
    public List<ResultatImport> Activer(string dossierPacksLibres, DateOnly? jour = null, int parJour = 20)
    {
        Reafficher(jour, parJour);
        return importateur.ImporterDossier(dossierPacksLibres, null);
    }

    /// <summary>Rend visibles les cours libres déjà en base (sans relire le dossier).</summary>
    public void Reafficher(DateOnly? jour = null, int parJour = 20) =>
        _collections.DefinirVisible(Collections.Libres, true, jour ?? DateOnly.FromDateTime(DateTime.Now), parJour, imposerUneVisible: false);

    /// <summary>Cache les cours libres : plus de lecture, de cartes ni de statistiques ; la progression est conservée.</summary>
    public void Masquer(DateOnly? jour = null) =>
        _collections.DefinirVisible(Collections.Libres, false, jour ?? DateOnly.FromDateTime(DateTime.Now), imposerUneVisible: false);
}
