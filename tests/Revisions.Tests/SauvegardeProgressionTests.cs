using Dapper;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Sauvegarde et restauration de la progression : fichier unique, vérification, copie de sécurité, remplacement au démarrage.</summary>
public sealed class SauvegardeProgressionTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "atome-sauv-" + Guid.NewGuid().ToString("N"));

    public SauvegardeProgressionTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        BaseDeDonnees.FermerConnexions();
        try { Directory.Delete(_dossier, true); } catch (IOException) { }
    }

    private BaseDeDonnees Creer(string nom, int xp)
    {
        var b = new BaseDeDonnees(Path.Combine(_dossier, nom));
        using var c = b.Ouvrir();
        c.Execute("UPDATE profil SET xp_total = @xp WHERE id = 1", new { xp });
        return b;
    }

    private static int Xp(BaseDeDonnees b)
    {
        using var c = b.Ouvrir();
        return c.ExecuteScalar<int>("SELECT xp_total FROM profil");
    }

    [Fact]
    public void Sauvegarde_puis_restauration_remplace_la_base_au_demarrage()
    {
        var source = Creer("a.db", 420);
        string fichier = Path.Combine(_dossier, "ma" + BaseDeDonnees.ExtensionSauvegarde);
        source.SauvegarderProgression(fichier);
        Assert.Null(BaseDeDonnees.VerifierSauvegarde(fichier));

        var cible = Creer("b.db", 7);
        string copie = cible.PreparerRestauration(fichier);
        Assert.True(File.Exists(copie)); // copie de sécurité de l'ancienne base
        Assert.Equal(7, Xp(cible));       // rien n'est remplacé avant le redémarrage

        BaseDeDonnees.FermerConnexions();
        var relancee = new BaseDeDonnees(Path.Combine(_dossier, "b.db"));
        Assert.Equal(420, Xp(relancee));
        Assert.False(File.Exists(Path.Combine(_dossier, "b.db.restauration")));
    }

    [Fact]
    public void Un_fichier_qui_nest_pas_une_sauvegarde_est_refuse()
    {
        string faux = Path.Combine(_dossier, "faux" + BaseDeDonnees.ExtensionSauvegarde);
        File.WriteAllText(faux, "coucou");
        Assert.NotNull(BaseDeDonnees.VerifierSauvegarde(faux));
        Assert.NotNull(BaseDeDonnees.VerifierSauvegarde(Path.Combine(_dossier, "absent.db")));
        var b = Creer("c.db", 1);
        Assert.Throws<InvalidOperationException>(() => b.PreparerRestauration(faux));
    }

    [Fact]
    public void Sauvegarde_automatique_une_par_jour_et_cinq_gardees()
    {
        var b = Creer("d.db", 1);
        string dest = Path.Combine(_dossier, "usb");
        Assert.NotNull(b.SauvegarderAilleursSiNecessaire(dest, new DateOnly(2026, 11, 1)));
        Assert.Null(b.SauvegarderAilleursSiNecessaire(dest, new DateOnly(2026, 11, 1)));
        for (int j = 2; j <= 8; j++) b.SauvegarderAilleursSiNecessaire(dest, new DateOnly(2026, 11, j));
        var fichiers = Directory.GetFiles(dest);
        Assert.Equal(5, fichiers.Length);
        Assert.Contains(fichiers, f => f.EndsWith("2026-11-08" + BaseDeDonnees.ExtensionSauvegarde));
    }
}
