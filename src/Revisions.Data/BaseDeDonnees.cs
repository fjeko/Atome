using Dapper;
using Microsoft.Data.Sqlite;

namespace Revisions.Data;

/// <summary>
/// Point d'entrée vers la base SQLite : ouvre les connexions et met le schéma à jour.
/// La version du schéma est gardée par SQLite lui-même (PRAGMA user_version) :
/// chaque migration n'est appliquée qu'une fois, dans l'ordre.
/// </summary>
public sealed class BaseDeDonnees
{
    private readonly string _chaineDeConnexion;

    /// <summary>Chemin du fichier de base.</summary>
    public string Chemin { get; }

    public BaseDeDonnees(string chemin)
    {
        Chemin = chemin;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(chemin))!);
        _chaineDeConnexion = new SqliteConnectionStringBuilder
        {
            DataSource = chemin,
            ForeignKeys = true, // SQLite ne vérifie les clés étrangères que si on le demande
        }.ToString();
        Migrer();
    }

    /// <summary>Emplacement par défaut : %LOCALAPPDATA%\RevisionsBTS\revisions.db.</summary>
    public static string CheminParDefaut =>
        Path.Combine(DossierDonnees, "revisions.db");

    /// <summary>Dossier des données de l'application pour l'utilisateur Windows courant.</summary>
    /// <remarks>
    /// Pour les essais, la variable d'environnement REVISIONS_DONNEES permet d'utiliser un autre
    /// dossier, sans toucher aux vraies données.
    /// </remarks>
    public static string DossierDonnees =>
        Environment.GetEnvironmentVariable("REVISIONS_DONNEES") is { Length: > 0 } dossier
            ? dossier
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevisionsBTS");

    /// <summary>Ouvre une connexion. À utiliser avec « using » pour qu'elle soit refermée.</summary>
    public SqliteConnection Ouvrir()
    {
        var connexion = new SqliteConnection(_chaineDeConnexion);
        connexion.Open();
        return connexion;
    }

    private void Migrer()
    {
        using var connexion = Ouvrir();
        // WAL : écritures plus rapides et plus sûres en cas de coupure.
        connexion.Execute("PRAGMA journal_mode=WAL;");
        long version = connexion.ExecuteScalar<long>("PRAGMA user_version;");
        for (int i = (int)version; i < Migrations.Scripts.Length; i++)
        {
            using var transaction = connexion.BeginTransaction();
            connexion.Execute(Migrations.Scripts[i], transaction: transaction);
            connexion.Execute($"PRAGMA user_version = {i + 1};", transaction: transaction);
            transaction.Commit();
        }
    }

    /// <summary>
    /// Copie de sécurité : une par jour, dans le sous-dossier « sauvegardes », 7 gardées.
    /// VACUUM INTO écrit une copie propre et complète de la base, même pendant qu'elle est utilisée.
    /// </summary>
    public void SauvegarderSiNecessaire(DateOnly jour)
    {
        string dossier = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Chemin))!, "sauvegardes");
        Directory.CreateDirectory(dossier);
        string cible = Path.Combine(dossier, $"revisions-{jour:yyyy-MM-dd}.db");
        if (File.Exists(cible)) return;

        using (var connexion = Ouvrir())
            connexion.Execute("VACUUM INTO @cible;", new { cible });

        foreach (string ancien in Directory.GetFiles(dossier, "revisions-*.db").OrderDescending().Skip(7))
            File.Delete(ancien);
    }

    /// <summary>Format des jours dans la base : 2026-11-14 (se trie comme du texte).</summary>
    internal static string Iso(DateOnly jour) => jour.ToString("yyyy-MM-dd");

    internal static DateOnly? Jour(string? texte) =>
        texte is null ? null : DateOnly.ParseExact(texte, "yyyy-MM-dd");
}
