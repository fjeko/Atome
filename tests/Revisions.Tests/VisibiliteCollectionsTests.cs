using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Collections;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>
/// Une seule source de vérité pour la visibilité (collection.masque) : migration 9 sur la base cassée de l'étudiant,
/// règle « au moins une collection réellement visible », import manuel vers une collection masquée.
/// </summary>
public sealed class VisibiliteCollectionsTests : IDisposable
{
    private static readonly DateOnly Jour = new(2026, 11, 18);
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-visib-" + Guid.NewGuid().ToString("N"));
    private readonly string _chemin;
    private readonly FakeTimeProvider _horloge = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    public VisibiliteCollectionsTests()
    {
        Directory.CreateDirectory(_dossier);
        _chemin = Path.Combine(_dossier, "test.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private string Fichier(string nom, string id) => Path.Combine(_dossier, nom).Ecrire(PackExemple.Json.Replace("maths-sq1-s03", id));

    private static List<string> Ids(BaseDeDonnees bdd) => new DepotBibliotheque(bdd).ListerSeances(Jour).Select(s => s.PackId).Order().ToList();

    /// <summary>
    /// Base dans l'état diagnostiqué chez l'étudiant avant la migration 9 : « libres » apparemment active mais ses packs
    /// sont masqués par pack.masque, « mes-packs » masquée, réglage cours_libres = 0, cartes et sections mises à l'écart.
    /// </summary>
    private void FabriquerBaseCassee(string reglageCoursLibres = "0")
    {
        var bdd = new BaseDeDonnees(_chemin);
        var importateur = new ImportateurPacks(bdd, _horloge);
        importateur.ImporterFichier(Fichier("l.json", "libre-maths-s03"), null);
        importateur.ImporterFichier(Fichier("p.json", "perso-s03"), null);
        using var c = bdd.Ouvrir();
        // De la progression à conserver.
        c.Execute("UPDATE etat_carte SET stabilite = 5, difficulte_fsrs = 5, echeance = '2026-11-20', derniere_revision = '2026-11-17', repetitions = 2");
        c.Execute("UPDATE pack SET masque = 1");
        c.Execute("UPDATE carte SET archivee = 2");
        c.Execute("UPDATE section SET archivee = 2");
        c.Execute("UPDATE collection SET masque = 0 WHERE cle = 'libres'");
        c.Execute("UPDATE collection SET masque = 1, masque_depuis = '2026-11-17' WHERE cle = 'mes-packs'");
        c.Execute("INSERT OR REPLACE INTO reglage (cle, valeur) VALUES ('cours_libres', @v)", new { v = reglageCoursLibres });
        c.Execute("PRAGMA user_version = 8");   // la migration 9 n'a pas encore tourné
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void La_migration_repare_la_bibliotheque_vide_sans_perdre_la_progression()
    {
        FabriquerBaseCassee();
        var bdd = new BaseDeDonnees(_chemin);   // lance la migration 9

        Assert.Equal(["perso-s03"], Ids(bdd));
        using var c = bdd.Ouvrir();
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM pack WHERE masque <> 0"));
        // « Cours libres » suit l'ancien réglage (0 = masqué) ; « Mes packs », seule restante, est réactivée.
        Assert.Equal(1, c.ExecuteScalar<long>("SELECT masque FROM collection WHERE cle = 'libres'"));
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT masque FROM collection WHERE cle = 'mes-packs'"));
        // Cartes et sections suivent leur collection ; aucune ligne de progression n'a bougé.
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM carte WHERE pack_id = 'perso-s03' AND archivee <> 0"));
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM section WHERE pack_id = 'perso-s03' AND archivee <> 0"));
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM carte WHERE pack_id = 'libre-maths-s03' AND archivee <> 2"));
        Assert.Equal(c.ExecuteScalar<long>("SELECT COUNT(*) FROM carte"),
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM etat_carte WHERE stabilite = 5 AND repetitions = 2"));
        // Le réglage faisait doublon : retiré.
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM reglage WHERE cle = 'cours_libres'"));
    }

    [Fact]
    public void La_migration_garde_les_cours_libres_visibles_si_le_reglage_le_dit()
    {
        FabriquerBaseCassee("1");
        var bdd = new BaseDeDonnees(_chemin);

        // libres visible (réglage = 1), mes-packs reste masquée : la règle « au moins une » est déjà satisfaite.
        Assert.Equal(["libre-maths-s03"], Ids(bdd));
    }

    [Fact]
    public void Si_tout_est_masque_la_migration_reactive_mes_packs()
    {
        FabriquerBaseCassee();
        using (var c = new BaseDeDonnees(_chemin).Ouvrir())
        {
            c.Execute("UPDATE collection SET masque = 1");
            c.Execute("PRAGMA user_version = 8");
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(["perso-s03"], Ids(new BaseDeDonnees(_chemin)));
    }

    [Fact]
    public void Une_collection_vide_ne_compte_pas_comme_active()
    {
        var liste = new List<InfoCollection>
        {
            new("libres", "Cours libres", 10, 50, true),
            new("vide", "Vide", 0, 0, true),       // visible mais sans aucun cours : ne montre rien
            new("mes-packs", "Mes packs", 5, 20, false),
        };
        Assert.False(Collections.PeutMasquer(liste, "libres"));   // la seule qui montre vraiment quelque chose
        Assert.True(Collections.PeutMasquer(liste, "vide"));
    }

    [Fact]
    public void On_ne_peut_pas_masquer_la_derniere_collection_reellement_visible()
    {
        var bdd = new BaseDeDonnees(_chemin);
        var importateur = new ImportateurPacks(bdd, _horloge);
        importateur.ImporterFichier(Fichier("l.json", "libre-maths-s03"), null);
        importateur.ImporterFichier(Fichier("p.json", "perso-s03"), null);
        var depot = new DepotCollections(bdd);
        Assert.Equal(ResultatBascule.Fait, depot.DefinirVisible("libres", false, Jour));
        Assert.Equal(ResultatBascule.DerniereCollection, depot.DefinirVisible("mes-packs", false, Jour));
        Assert.Equal(["perso-s03"], Ids(bdd));
    }

    [Fact]
    public void Un_import_manuel_reaffiche_sa_collection_masquee()
    {
        var bdd = new BaseDeDonnees(_chemin);
        var importateur = new ImportateurPacks(bdd, _horloge);
        var depot = new DepotCollections(bdd);
        importateur.ImporterFichier(Fichier("p.json", "perso-s03"), null);
        importateur.ImporterFichier(Fichier("m1.json", "manuel-s03"), null, null, Collections.Manuel, Collections.NomManuel);
        depot.DefinirVisible(Collections.Manuel, false, Jour);
        Assert.Equal(["perso-s03"], Ids(bdd));

        // Import automatique (scan du dossier) : la collection masquée reste masquée.
        var auto = importateur.ImporterFichier(Fichier("m2.json", "manuel2-s03"), null, null, Collections.Manuel, Collections.NomManuel);
        Assert.Null(auto.CollectionReaffichee);
        Assert.DoesNotContain("manuel2-s03", Ids(bdd));

        // Import voulu par l'étudiant : la collection est réaffichée, et le résultat le signale.
        var manuel = importateur.ImporterFichier(Fichier("m3.json", "manuel3-s03"), null, null, Collections.Manuel, Collections.NomManuel, reafficherSiMasquee: true);
        Assert.Equal(Collections.NomManuel, manuel.CollectionReaffichee);
        Assert.Equal(["manuel-s03", "manuel2-s03", "manuel3-s03", "perso-s03"], Ids(bdd));
    }

    [Fact]
    public void Reimporter_un_fichier_deja_connu_reaffiche_aussi_sa_collection()
    {
        var bdd = new BaseDeDonnees(_chemin);
        var importateur = new ImportateurPacks(bdd, _horloge);
        var depot = new DepotCollections(bdd);
        importateur.ImporterFichier(Fichier("p.json", "perso-s03"), null);
        string fichier = Fichier("m.json", "manuel-s03");
        importateur.ImporterFichier(fichier, null, null, Collections.Manuel, Collections.NomManuel);
        depot.DefinirVisible(Collections.Manuel, false, Jour);

        var r = importateur.ImporterFichier(fichier, null, null, Collections.Manuel, Collections.NomManuel, reafficherSiMasquee: true);
        Assert.Equal(StatutImport.Inchange, r.Statut);
        Assert.Equal(Collections.NomManuel, r.CollectionReaffichee);
        Assert.Contains("manuel-s03", Ids(bdd));
    }
}
