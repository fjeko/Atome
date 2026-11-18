using Dapper;
using Microsoft.Data.Sqlite;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Deux démarrages simultanés sur la même base ne doivent pas se marcher dessus (bug « table planning already exists »).</summary>
public sealed class MigrationsConcurrentesTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void Deux_migrations_en_meme_temps_donnent_une_base_a_jour()
    {
        // Plusieurs tours : la course ne se produit pas à tous les coups.
        for (int tour = 0; tour < 5; tour++)
        {
            string cible = Path.Combine(_dossier, $"t{tour}.db");
            using var depart = new ManualResetEventSlim();
            var taches = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                depart.Wait();
                return new BaseDeDonnees(cible);
            })).ToArray();
            depart.Set();
            Task.WaitAll(taches);

            using var connexion = taches[0].Result.Ouvrir();
            Assert.Equal(10, connexion.ExecuteScalar<long>("PRAGMA user_version;"));
            Assert.Equal(1, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'planning'"));
        }
    }
}
