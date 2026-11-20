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
        AppliquerRestaurationEnAttente(chemin);
        _chaineDeConnexion = new SqliteConnectionStringBuilder
        {
            DataSource = chemin,
            DefaultTimeout = 15, // attend un verrou occupé au lieu d'échouer tout de suite
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

    /// <summary>
    /// Met le schéma à jour. Chaque migration se fait sous un verrou d'écriture pris AVANT de lire la version
    /// (BEGIN IMMEDIATE) : si deux processus démarrent ensemble, le second attend, relit la version
    /// et constate que le travail est déjà fait.
    /// </summary>
    private void Migrer()
    {
        using var connexion = Ouvrir();
        connexion.Execute("PRAGMA busy_timeout = 15000;");
        // WAL : écritures plus rapides et plus sûres en cas de coupure.
        connexion.Execute("PRAGMA journal_mode=WAL;");
        // Chemin rapide : base déjà à jour, aucun verrou nécessaire.
        if (connexion.ExecuteScalar<long>("PRAGMA user_version;") >= Migrations.Scripts.Length) return;

        while (true)
        {
            connexion.Execute("BEGIN IMMEDIATE;");
            try
            {
                int version = (int)connexion.ExecuteScalar<long>("PRAGMA user_version;");
                if (version >= Migrations.Scripts.Length) { connexion.Execute("COMMIT;"); return; }
                connexion.Execute(Migrations.Scripts[version]);
                connexion.Execute($"PRAGMA user_version = {version + 1};");
                connexion.Execute("COMMIT;");
            }
            catch
            {
                connexion.Execute("ROLLBACK;");
                throw;
            }
        }
    }

    /// <summary>
    /// Copie de sécurité : une par jour, dans le sous-dossier « sauvegardes », 7 gardées.
    /// VACUUM INTO écrit une copie propre et complète de la base, même pendant qu'elle est utilisée.
    /// </summary>
    public void SauvegarderSiNecessaire(DateOnly jour)
    {
        string dossier = DossierSauvegardes();
        string cible = Path.Combine(dossier, $"revisions-{jour:yyyy-MM-dd}.db");
        if (File.Exists(cible)) return;

        CopierVers(cible);
        foreach (string ancien in Directory.GetFiles(dossier, "revisions-*.db").OrderDescending().Skip(7))
            File.Delete(ancien);
    }

    /// <summary>
    /// Copie de sécurité juste avant une mise à jour de l'application, même si celle du jour existe déjà :
    /// « avant-maj-&lt;version&gt;.db » dans le même dossier « sauvegardes ». Les 3 dernières sont gardées,
    /// sans toucher à la rotation des copies quotidiennes. Renvoie le chemin de la copie.
    /// </summary>
    public string SauvegarderAvantMiseAJour(string versionVisee)
    {
        string dossier = DossierSauvegardes();
        string nom = string.Concat(versionVisee.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        string cible = Path.Combine(dossier, $"avant-maj-{nom}.db");
        if (File.Exists(cible)) File.Delete(cible); // VACUUM INTO refuse d'écraser un fichier existant
        CopierVers(cible);
        foreach (string ancien in Directory.GetFiles(dossier, "avant-maj-*.db").OrderByDescending(File.GetLastWriteTimeUtc).Skip(3))
            File.Delete(ancien);
        return cible;
    }

    /// <summary>
    /// Ferme les connexions gardées en réserve par Microsoft.Data.Sqlite (pool) : plus aucun fichier
    /// de la base ne reste ouvert. À appeler juste avant de quitter pour laisser l'installateur travailler.
    /// </summary>
    public static void FermerConnexions() => SqliteConnection.ClearAllPools();

    // ---- Sauvegarde et restauration de la progression (fichier .atome-sauvegarde) ---------------------------

    /// <summary>Extension du fichier de sauvegarde de la progression (c'est une base SQLite complète et propre).</summary>
    public const string ExtensionSauvegarde = ".atome-sauvegarde";

    /// <summary>
    /// Crée un fichier de sauvegarde de toute la progression (historique FSRS, XP, réglages, acquis, planning,
    /// dates personnelles). Même mécanisme que les copies automatiques : VACUUM INTO, sans arrêter l'application.
    /// Les cours sont dans la copie mais Atome les réimporte de toute façon depuis les packs.
    /// </summary>
    public void SauvegarderProgression(string cible)
    {
        if (File.Exists(cible)) File.Delete(cible); // VACUUM INTO refuse d'écraser un fichier existant
        CopierVers(cible);
    }

    /// <summary>
    /// Sauvegarde automatique dans un dossier choisi (clé USB, OneDrive…) : une par jour au plus,
    /// les 5 dernières sont gardées. Renvoie le chemin créé, ou null si celle du jour existe déjà.
    /// </summary>
    public string? SauvegarderAilleursSiNecessaire(string dossier, DateOnly jour)
    {
        Directory.CreateDirectory(dossier);
        string cible = Path.Combine(dossier, $"atome-progression-{jour:yyyy-MM-dd}{ExtensionSauvegarde}");
        if (File.Exists(cible)) return null;
        SauvegarderProgression(cible);
        foreach (string ancien in Directory.GetFiles(dossier, "atome-progression-*" + ExtensionSauvegarde).OrderDescending().Skip(5))
            File.Delete(ancien);
        return cible;
    }

    /// <summary>
    /// Vérifie qu'un fichier est une sauvegarde utilisable : une vraie base SQLite saine, avec les tables de
    /// progression, et pas plus récente que cette version d'Atome. Renvoie null si tout va bien, sinon la raison.
    /// </summary>
    public static string? VerifierSauvegarde(string fichier)
    {
        if (!File.Exists(fichier)) return "Le fichier n'existe pas.";
        try
        {
            using (var flux = File.OpenRead(fichier))
            {
                var entete = new byte[16];
                if (flux.Read(entete, 0, 16) < 16 || !System.Text.Encoding.ASCII.GetString(entete).StartsWith("SQLite format 3"))
                    return "Ce fichier n'est pas une sauvegarde d'Atome.";
            }
            var chaine = new SqliteConnectionStringBuilder { DataSource = fichier, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var c = new SqliteConnection(chaine);
            c.Open();
            var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();
            foreach (var t in new[] { "profil", "reglage", "etat_carte", "revision", "carte" })
                if (!tables.Contains(t)) return "Ce fichier n'est pas une sauvegarde d'Atome (il manque des données).";
            if (c.ExecuteScalar<long>("PRAGMA user_version;") > Migrations.Scripts.Length)
                return "Cette sauvegarde vient d'une version plus récente d'Atome : mets Atome à jour avant de la restaurer.";
            if (c.ExecuteScalar<string>("PRAGMA quick_check;") != "ok") return "Ce fichier est abîmé.";
            return null;
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            return "Impossible de lire ce fichier.";
        }
    }

    /// <summary>
    /// Première étape d'une restauration : vérifie le fichier, fait une copie de sécurité de la base actuelle
    /// (« avant-restauration-….db », 3 gardées) puis dépose le fichier à côté de la base. Il ne remplace la base
    /// qu'au prochain démarrage (<see cref="AppliquerRestaurationEnAttente"/>), quand plus rien ne l'utilise.
    /// Lève <see cref="InvalidOperationException"/> si le fichier n'est pas valable ; renvoie le chemin de la copie de sécurité.
    /// </summary>
    public string PreparerRestauration(string fichier)
    {
        if (VerifierSauvegarde(fichier) is { } probleme) throw new InvalidOperationException(probleme);
        string dossier = DossierSauvegardes();
        string copie = Path.Combine(dossier, $"avant-restauration-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        CopierVers(copie);
        foreach (string ancien in Directory.GetFiles(dossier, "avant-restauration-*.db").OrderDescending().Skip(3))
            File.Delete(ancien);
        File.Copy(fichier, CheminRestauration(Chemin), overwrite: true);
        return copie;
    }

    private static string CheminRestauration(string chemin) => Path.GetFullPath(chemin) + ".restauration";

    /// <summary>Si une restauration attend, elle remplace la base (et ses fichiers WAL) avant toute ouverture.</summary>
    internal static void AppliquerRestaurationEnAttente(string chemin)
    {
        string attente = CheminRestauration(chemin);
        if (!File.Exists(attente)) return;
        try
        {
            foreach (string suffixe in new[] { "-wal", "-shm" })
                if (File.Exists(chemin + suffixe)) File.Delete(chemin + suffixe);
            File.Move(attente, chemin, overwrite: true);
        }
        catch (IOException) { } // base encore occupée : on réessaiera au prochain démarrage
    }

    private string DossierSauvegardes()
    {
        string dossier = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Chemin))!, "sauvegardes");
        Directory.CreateDirectory(dossier);
        return dossier;
    }

    /// <summary>VACUUM INTO écrit une copie propre et complète de la base, même pendant qu'elle est utilisée.</summary>
    private void CopierVers(string cible)
    {
        using var connexion = Ouvrir();
        connexion.Execute("VACUUM INTO @cible;", new { cible });
    }

    /// <summary>Format des jours dans la base : 2026-11-14 (se trie comme du texte).</summary>
    internal static string Iso(DateOnly jour) => jour.ToString("yyyy-MM-dd");

    internal static DateOnly? Jour(string? texte) =>
        texte is null ? null : DateOnly.ParseExact(texte, "yyyy-MM-dd");
}
