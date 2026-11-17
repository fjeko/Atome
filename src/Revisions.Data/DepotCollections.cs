using Dapper;
using Microsoft.Data.Sqlite;
using Revisions.Core.Collections;

namespace Revisions.Data;

/// <summary>Ce qui s'est passé quand on a voulu afficher ou masquer une collection.</summary>
public enum ResultatBascule
{
    Fait,
    /// <summary>Refusé : c'est la seule collection visible.</summary>
    DerniereCollection,
    /// <summary>Aucune collection de ce nom en base.</summary>
    Inconnue,
}

/// <summary>
/// Collections de packs : liste, affichage et masquage.
/// Masquer ne supprime rien : les packs sont cachés, leurs cartes et sections mises à l'écart (archivee = 2),
/// donc absentes de la bibliothèque, des sessions, du planning, des acquis et des statistiques.
/// Réafficher remet tout en place ; les cartes devenues dues entre-temps sont étalées (<see cref="Reaffichage"/>).
/// </summary>
public sealed class DepotCollections(BaseDeDonnees baseDeDonnees)
{
    /// <summary>Les collections présentes en base, dans l'ordre d'affichage.</summary>
    public List<InfoCollection> Lister()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var lignes = connexion.Query<(string Cle, string Nom, long Cours, long Cartes, long Masque)>(
            """
            SELECT c.cle, c.nom, COUNT(DISTINCT p.id), COUNT(ca.id), c.masque
            FROM collection c
            LEFT JOIN pack p ON p.collection = c.cle
            LEFT JOIN carte ca ON ca.pack_id = p.id AND ca.archivee IN (0, 2)
            GROUP BY c.cle
            """);
        return Collections.Trier(lignes.Select(l => new InfoCollection(l.Cle, l.Nom, (int)l.Cours, (int)l.Cartes, l.Masque == 0))).ToList();
    }

    /// <summary>Crée la collection si elle n'existe pas (visible par défaut) et dit si elle est masquée.</summary>
    internal static bool DeclarerEtDireSiMasquee(SqliteConnection connexion, SqliteTransaction? transaction, string cle, string nom)
    {
        connexion.Execute("INSERT OR IGNORE INTO collection (cle, nom) VALUES (@cle, @nom)", new { cle, nom }, transaction);
        return connexion.ExecuteScalar<long>("SELECT masque FROM collection WHERE cle = @cle", new { cle }, transaction) != 0;
    }

    /// <summary>Met à l'écart les cartes et sections d'un pack d'une collection masquée.</summary>
    internal static void MasquerPack(SqliteConnection connexion, SqliteTransaction? transaction, string packId)
    {
        connexion.Execute("UPDATE carte SET archivee = 2 WHERE archivee = 0 AND pack_id = @packId", new { packId }, transaction);
        connexion.Execute("UPDATE section SET archivee = 2 WHERE archivee = 0 AND pack_id = @packId", new { packId }, transaction);
    }

    /// <summary>
    /// Réaffiche une collection masquée, dans la transaction de l'appelant (import manuel ou .atome).
    /// Même effet que <see cref="DefinirVisible"/> : cartes et sections remises en place, retards étalés.
    /// </summary>
    internal static void Reafficher(SqliteConnection connexion, SqliteTransaction transaction, string cle, DateOnly jour, int parJour = 20)
    {
        const string DuPack = "SELECT id FROM pack WHERE collection = @cle";
        string? depuis = connexion.ExecuteScalar<string?>("SELECT masque_depuis FROM collection WHERE cle = @cle", new { cle }, transaction);
        connexion.Execute($"UPDATE carte SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({DuPack})", new { cle }, transaction);
        connexion.Execute($"UPDATE section SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({DuPack})", new { cle }, transaction);
        connexion.Execute("UPDATE collection SET masque = 0, masque_depuis = NULL WHERE cle = @cle", new { cle }, transaction);
        EtalerRetards(connexion, transaction, cle, jour, BaseDeDonnees.Jour(depuis), parJour);
    }

    /// <summary>Affiche ou masque une collection.</summary>
    /// <param name="jour">Aujourd'hui (début du masquage, ou référence pour étaler les échéances).</param>
    /// <param name="parJour">Au réaffichage : nombre maximal de cartes en retard ramenées par jour.</param>
    /// <param name="imposerUneVisible">Refuse de masquer la dernière collection visible.</param>
    public ResultatBascule DefinirVisible(string cle, bool visible, DateOnly jour, int parJour = 20, bool imposerUneVisible = true)
    {
        var toutes = Lister();
        var cible = toutes.FirstOrDefault(c => c.Cle == cle);
        if (cible is null) return ResultatBascule.Inconnue;
        if (!visible && imposerUneVisible && cible.Visible && !Collections.PeutMasquer(toutes, cle)) return ResultatBascule.DerniereCollection;
        if (cible.Visible == visible) return ResultatBascule.Fait;

        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        const string DuPack = "SELECT id FROM pack WHERE collection = @cle";
        if (visible)
        {
            string? depuis = connexion.ExecuteScalar<string?>("SELECT masque_depuis FROM collection WHERE cle = @cle", new { cle }, transaction);
            connexion.Execute($"UPDATE carte SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({DuPack})", new { cle }, transaction);
            connexion.Execute($"UPDATE section SET archivee = 0 WHERE archivee = 2 AND pack_id IN ({DuPack})", new { cle }, transaction);
            connexion.Execute("UPDATE collection SET masque = 0, masque_depuis = NULL WHERE cle = @cle", new { cle }, transaction);
            EtalerRetards(connexion, transaction, cle, jour, BaseDeDonnees.Jour(depuis), parJour);
        }
        else
        {
            connexion.Execute($"UPDATE carte SET archivee = 2 WHERE archivee = 0 AND pack_id IN ({DuPack})", new { cle }, transaction);
            connexion.Execute($"UPDATE section SET archivee = 2 WHERE archivee = 0 AND pack_id IN ({DuPack})", new { cle }, transaction);
            connexion.Execute("UPDATE collection SET masque = 1, masque_depuis = @jour WHERE cle = @cle",
                new { cle, jour = BaseDeDonnees.Iso(jour) }, transaction);
        }
        transaction.Commit();
        return ResultatBascule.Fait;
    }

    /// <summary>Décale les échéances des cartes devenues dues pendant le masquage (rien d'autre n'est modifié).</summary>
    private static void EtalerRetards(SqliteConnection connexion, SqliteTransaction transaction, string cle, DateOnly jour, DateOnly? depuis, int parJour)
    {
        var cartes = connexion.Query<(string Id, string Echeance)>(
                """
                SELECT c.id, e.echeance FROM carte c
                JOIN etat_carte e ON e.carte_id = c.id
                JOIN pack p ON p.id = c.pack_id
                WHERE p.collection = @cle AND c.archivee = 0 AND e.stabilite IS NOT NULL AND e.echeance IS NOT NULL
                """, new { cle }, transaction)
            .Select(l => new EcheanceCarte(l.Id, BaseDeDonnees.Jour(l.Echeance)!.Value))
            .ToList();
        foreach (var c in Reaffichage.Etaler(cartes, jour, depuis, parJour))
            connexion.Execute("UPDATE etat_carte SET echeance = @echeance WHERE carte_id = @id",
                new { echeance = BaseDeDonnees.Iso(c.Echeance), id = c.CarteId }, transaction);
    }
}
