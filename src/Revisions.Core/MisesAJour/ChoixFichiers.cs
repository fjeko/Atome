namespace Revisions.Core.MisesAJour;

/// <summary>Le dépôt GitHub public d'Atome, d'où viennent les nouvelles versions.</summary>
public static class DepotAtome
{
    public const string Proprietaire = "fjeko";
    public const string Nom = "Atome";

    /// <summary>Adresse de l'API qui renvoie la dernière Release publiée (ni brouillon, ni pré-version).</summary>
    public static readonly Uri DerniereRelease = new($"https://api.github.com/repos/{Proprietaire}/{Nom}/releases/latest");

    /// <summary>Page des Releases, pour un téléchargement à la main.</summary>
    public const string PageReleases = $"https://github.com/{Proprietaire}/{Nom}/releases";

    /// <summary>Invitation permanente au serveur Discord « Atome – Communauté » (signaler un bug, une coquille, une idée).
    /// Lien public : une seule constante, utilisée par le menu latéral et par l'Aide.</summary>
    public const string LienDiscord = "https://discord.gg/PkXXNhDvTm";
}

/// <summary>Les deux fichiers nécessaires à une mise à jour automatique.</summary>
/// <param name="Setup">L'installateur « Atome-Setup-&lt;version&gt;.exe ».</param>
/// <param name="Empreinte">Son empreinte « Atome-Setup-&lt;version&gt;.exe.sha256 », ou null si elle n'a pas été publiée.</param>
public sealed record FichiersMiseAJour(FichierRelease Setup, FichierRelease? Empreinte);

/// <summary>Choisit, dans une Release, les fichiers à télécharger et vérifie leurs adresses.</summary>
public static class ChoixFichiers
{
    /// <summary>Nom attendu de l'installateur pour une version : « Atome-Setup-1.2.0.exe ».</summary>
    public static string NomSetup(VersionSemantique version) => $"Atome-Setup-{version}.exe";

    /// <summary>
    /// Trouve l'installateur de la version annoncée par le tag, et son fichier d'empreinte.
    /// Renvoie null s'il n'y a pas d'installateur (ou s'il n'est pas hébergé par le dépôt d'Atome) :
    /// on ne propose alors que la page de la Release.
    /// </summary>
    public static FichiersMiseAJour? Trouver(ReleaseGitHub release)
    {
        string nom = NomSetup(release.Version);
        var setup = release.Fichiers.FirstOrDefault(f => string.Equals(f.Nom, nom, StringComparison.OrdinalIgnoreCase));
        if (setup is null || !AdresseDeConfiance(setup.UrlTelechargement)) return null;

        var empreinte = release.Fichiers.FirstOrDefault(f =>
            string.Equals(f.Nom, nom + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (empreinte is not null && !AdresseDeConfiance(empreinte.UrlTelechargement)) empreinte = null;
        return new FichiersMiseAJour(setup, empreinte);
    }

    /// <summary>
    /// Vrai si l'adresse est un téléchargement de Release du dépôt d'Atome, en HTTPS
    /// (https://github.com/fjeko/Atome/releases/download/…). GitHub redirige ensuite vers son
    /// stockage : c'est HttpClient qui suit la redirection, toujours en HTTPS.
    /// </summary>
    public static bool AdresseDeConfiance(string? adresse)
    {
        if (!Uri.TryCreate(adresse, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps
               && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith($"/{DepotAtome.Proprietaire}/{DepotAtome.Nom}/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vrai si l'adresse est une page web du dépôt d'Atome (avant de l'ouvrir dans le navigateur).</summary>
    public static bool PageDeConfiance(string? adresse) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{DepotAtome.Proprietaire}/{DepotAtome.Nom}/", StringComparison.OrdinalIgnoreCase);
}
