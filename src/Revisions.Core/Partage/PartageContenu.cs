using System.IO.Compression;
using System.Text.Json;

namespace Revisions.Core.Partage;

/// <summary>Contenu du fichier <c>manifeste.json</c> placé à la racine d'un fichier .atome.</summary>
/// <param name="FormatVersion">Version du format de l'archive (1 pour l'instant).</param>
/// <param name="Application">Toujours « Atome ».</param>
/// <param name="Date">Date de création de l'archive.</param>
/// <param name="NbPacks">Nombre de fichiers de pack (.json) dans packs/.</param>
/// <param name="DocumentsInclus">Vrai si le dossier cours/ (PDF d'origine, etc.) est dans l'archive.</param>
/// <param name="NbDocuments">Nombre de fichiers dans cours/ (0 si non inclus).</param>
/// <param name="Auteur">Nom de l'auteur, facultatif (affiché comme nom de collection à l'import).</param>
public sealed record ManifesteContenu(int FormatVersion, string Application, DateTimeOffset Date, int NbPacks, bool DocumentsInclus, int NbDocuments, string? Auteur = null);

/// <summary>Ce qu'a fait une extraction.</summary>
/// <param name="NbPacks">Packs extraits.</param>
/// <param name="NbFichiers">Fichiers extraits en tout.</param>
/// <param name="DocumentsInclus">Documents d'origine présents dans l'archive.</param>
/// <param name="FichiersPacks">Chemins des fichiers de pack extraits (pour les ranger dans une collection).</param>
/// <param name="Auteur">Auteur indiqué par le manifeste, ou null.</param>
public sealed record ResumeExtraction(int NbPacks, int NbFichiers, bool DocumentsInclus, IReadOnlyList<string> FichiersPacks, string? Auteur = null);

/// <summary>Archive illisible, trop récente ou dangereuse : le message est destiné à l'utilisateur.</summary>
public sealed class ArchiveInvalideException(string message) : Exception(message);

/// <summary>
/// Fabrique et lit les fichiers .atome (de simples .zip) qui servent à transmettre le contenu
/// de l'application (packs, planning, documents d'origine) à un camarade.
///
/// Règle d'or : seuls les dossiers <c>packs/</c>, <c>planning/</c> et (en option) <c>cours/</c> sont
/// copiés. La progression (base SQLite, réglages, historique) n'y entre jamais.
/// </summary>
public static class PartageContenu
{
    public const string Extension = ".atome";
    public const string NomManifeste = "manifeste.json";
    public const int VersionFormat = 1;

    public const string DossierPacks = "packs";
    public const string DossierPlanning = "planning";
    public const string DossierDocuments = "cours";

    // Extensions qui ne doivent jamais partir, même si on les trouvait par erreur dans un de ces dossiers.
    private static readonly string[] ExtensionsInterdites = [".db", ".sqlite", ".sqlite3", ".db-wal", ".db-shm", ".db-journal", ".atome"];

    private static readonly JsonSerializerOptions OptionsJson = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    // ---- Export ----------------------------------------------------------------------------

    /// <summary>Fichiers d'un dossier de la racine (sous-dossiers compris), sans rien de ce qui est interdit.</summary>
    private static List<string> Lister(string racine, string sousDossier)
    {
        string dossier = Path.Combine(racine, sousDossier);
        if (!Directory.Exists(dossier)) return [];
        return Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
            .Where(f => !ExtensionsInterdites.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Taille totale (en octets) du dossier cours/, pour l'afficher avant l'export.</summary>
    public static long EstimerTailleDocuments(string racine) =>
        Lister(racine, DossierDocuments).Sum(f => new FileInfo(f).Length);

    /// <summary>Crée l'archive. Le fichier n'apparaît à son emplacement final que si tout s'est bien passé.</summary>
    /// <param name="racine">Dossier qui contient packs/, planning/ et cours/ (« dossier Cours »).</param>
    /// <param name="destination">Fichier .atome à créer.</param>
    /// <param name="avecDocuments">Inclure aussi cours/.</param>
    /// <param name="progression">Avancement de 0 à 1 (facultatif).</param>
    public static ManifesteContenu Exporter(string racine, string destination, bool avecDocuments,
        IProgress<double>? progression = null, CancellationToken annulation = default)
    {
        var packs = Lister(racine, DossierPacks);
        var planning = Lister(racine, DossierPlanning);
        var documents = avecDocuments ? Lister(racine, DossierDocuments) : [];
        int nbPacks = packs.Count(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var manifeste = new ManifesteContenu(VersionFormat, "Atome", DateTimeOffset.Now, nbPacks, avecDocuments, documents.Count);

        var tous = packs.Concat(planning).Concat(documents).ToList();
        long total = Math.Max(1, tous.Sum(f => new FileInfo(f).Length));
        long fait = 0;

        string temporaire = destination + ".partiel";
        try
        {
            using (var flux = new FileStream(temporaire, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(flux, ZipArchiveMode.Create))
            {
                var entreeManifeste = zip.CreateEntry(NomManifeste, CompressionLevel.Optimal);
                using (var s = entreeManifeste.Open()) JsonSerializer.Serialize(s, manifeste, OptionsJson);

                foreach (string fichier in tous)
                {
                    annulation.ThrowIfCancellationRequested();
                    // Chemin dans l'archive : relatif à la racine, avec des « / » (c'est la norme des zip).
                    string nom = Path.GetRelativePath(racine, fichier).Replace('\\', '/');
                    // Les PDF et ZIP sont déjà compressés : inutile de perdre du temps à réessayer.
                    bool dejaCompresse = fichier.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                                         || fichier.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                    var entree = zip.CreateEntry(nom, dejaCompresse ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    using (var source = File.OpenRead(fichier))
                    using (var cible = entree.Open())
                        source.CopyTo(cible);
                    fait += new FileInfo(fichier).Length;
                    progression?.Report((double)fait / total);
                }
            }
            File.Move(temporaire, destination, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaire)) File.Delete(temporaire);
            throw;
        }
        progression?.Report(1);
        return manifeste;
    }

    // ---- Import ----------------------------------------------------------------------------

    /// <summary>Lit et valide le manifeste d'une archive.</summary>
    /// <exception cref="ArchiveInvalideException">Ce n'est pas un fichier Atome, ou il est trop récent.</exception>
    public static ManifesteContenu LireManifeste(string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            var entree = zip.GetEntry(NomManifeste) ?? throw new ArchiveInvalideException("Ce fichier n'est pas un contenu Atome (manifeste.json introuvable).");
            ManifesteContenu? manifeste;
            using (var s = entree.Open()) manifeste = JsonSerializer.Deserialize<ManifesteContenu>(s, OptionsJson);
            if (manifeste is null || manifeste.Application != "Atome")
                throw new ArchiveInvalideException("Ce fichier n'est pas un contenu Atome.");
            if (manifeste.FormatVersion is < 1 or > VersionFormat)
                throw new ArchiveInvalideException("Ce fichier a été créé par une version plus récente d'Atome : mets l'application à jour.");
            return manifeste;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
        {
            throw new ArchiveInvalideException("Impossible de lire ce fichier : " + e.Message);
        }
    }

    /// <summary>
    /// Chemin de destination d'une entrée, ou null si l'entrée est à ignorer (dossier vide, fichier hors
    /// packs/planning/cours). Lève une exception si le chemin est dangereux (zip-slip).
    /// </summary>
    public static string? CheminSur(string nomEntree, string cible)
    {
        if (nomEntree.Length == 0 || nomEntree.EndsWith('/')) return null; // entrée « dossier »

        // Refuse tout chemin absolu (« /x », « \x », « C:\x ») et toute remontée (« .. »).
        string[] parties = nomEntree.Split('/', '\\');
        if (Path.IsPathRooted(nomEntree) || nomEntree.Contains(':') || parties.Any(p => p == ".."))
            throw new ArchiveInvalideException($"Fichier refusé, chemin dangereux : « {nomEntree} ».");

        string premier = parties[0];
        if (premier is not (DossierPacks or DossierPlanning or DossierDocuments)) return null; // ex. manifeste.json

        // Dernier garde-fou : le chemin final doit rester dans le dossier cible.
        string racine = Path.GetFullPath(cible).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string complet = Path.GetFullPath(Path.Combine(racine, string.Join(Path.DirectorySeparatorChar, parties)));
        if (!complet.StartsWith(racine, StringComparison.OrdinalIgnoreCase))
            throw new ArchiveInvalideException($"Fichier refusé, il sortirait du dossier de destination : « {nomEntree} ».");
        if (ExtensionsInterdites.Any(e => complet.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return null;
        return complet;
    }

    /// <summary>
    /// Extrait l'archive dans <paramref name="cible"/> (packs/, planning/, cours/). Les fichiers de même nom sont
    /// remplacés, les autres sont laissés tels quels. Tous les chemins sont vérifiés AVANT d'écrire quoi que ce soit.
    /// </summary>
    public static ResumeExtraction Extraire(string archive, string cible,
        IProgress<double>? progression = null, CancellationToken annulation = default)
    {
        var manifeste = LireManifeste(archive);
        using var zip = ZipFile.OpenRead(archive);

        // 1) Validation de toute l'archive.
        var aExtraire = new List<(ZipArchiveEntry Entree, string Chemin)>();
        foreach (var entree in zip.Entries)
            if (CheminSur(entree.FullName, cible) is { } chemin) aExtraire.Add((entree, chemin));

        // 2) Extraction.
        long total = Math.Max(1, aExtraire.Sum(x => x.Entree.Length));
        long fait = 0;
        var fichiersPacks = new List<string>();
        foreach (var (entree, chemin) in aExtraire)
        {
            annulation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            entree.ExtractToFile(chemin, overwrite: true);
            if (entree.FullName.StartsWith(DossierPacks + "/") && chemin.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fichiersPacks.Add(chemin);
            fait += entree.Length;
            progression?.Report((double)fait / total);
        }
        progression?.Report(1);
        return new ResumeExtraction(fichiersPacks.Count, aExtraire.Count, manifeste.DocumentsInclus, fichiersPacks, manifeste.Auteur);
    }
}
