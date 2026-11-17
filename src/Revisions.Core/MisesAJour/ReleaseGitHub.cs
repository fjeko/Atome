using System.Text.Json;

namespace Revisions.Core.MisesAJour;

/// <summary>Un fichier joint à une Release GitHub (« asset »).</summary>
/// <param name="Nom">Nom du fichier, par exemple « Atome-Setup-1.2.0.exe ».</param>
/// <param name="UrlTelechargement">Adresse de téléchargement direct (champ browser_download_url).</param>
/// <param name="Taille">Taille en octets annoncée par GitHub.</param>
public sealed record FichierRelease(string Nom, string UrlTelechargement, long Taille);

/// <summary>
/// Ce qu'Atome retient d'une Release GitHub : son numéro, ses notes et ses fichiers.
/// Lu depuis la réponse de l'API (GET /repos/{proprietaire}/{depot}/releases/latest).
/// </summary>
/// <param name="Tag">Nom du tag, par exemple « v1.2.0 ».</param>
/// <param name="Version">Numéro lu dans le tag.</param>
/// <param name="Titre">Titre de la Release (« Atome 1.2.0 »).</param>
/// <param name="Notes">Notes de version, en Markdown (champ body).</param>
/// <param name="PageWeb">Page de la Release sur github.com (champ html_url).</param>
/// <param name="Brouillon">Vrai pour un brouillon (jamais proposé).</param>
/// <param name="PreVersion">Vrai pour une pré-version (jamais proposée).</param>
public sealed record ReleaseGitHub(
    string Tag,
    VersionSemantique Version,
    string Titre,
    string Notes,
    string PageWeb,
    bool Brouillon,
    bool PreVersion,
    IReadOnlyList<FichierRelease> Fichiers)
{
    /// <summary>
    /// Lit le JSON renvoyé par l'API GitHub. Renvoie null si le JSON est illisible ou si le tag
    /// n'est pas un numéro de version : on préfère ne rien proposer plutôt que proposer n'importe quoi.
    /// </summary>
    public static ReleaseGitHub? Lire(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var racine = document.RootElement;
            if (racine.ValueKind != JsonValueKind.Object) return null;

            string? tag = Texte(racine, "tag_name");
            if (!VersionSemantique.TryParse(tag, out var version)) return null;

            var fichiers = new List<FichierRelease>();
            if (racine.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? nom = Texte(asset, "name");
                    string? url = Texte(asset, "browser_download_url");
                    if (nom is null || url is null) continue;
                    long taille = asset.TryGetProperty("size", out var t) && t.TryGetInt64(out long n) ? n : 0;
                    fichiers.Add(new FichierRelease(nom, url, taille));
                }
            }

            return new ReleaseGitHub(
                tag!,
                version,
                Texte(racine, "name") ?? tag!,
                Texte(racine, "body") ?? "",
                Texte(racine, "html_url") ?? "",
                Booleen(racine, "draft"),
                Booleen(racine, "prerelease"),
                fichiers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Réécrit la Release en JSON, au format de l'API (pour la garder en mémoire entre deux lancements).</summary>
    public string EnJson() => JsonSerializer.Serialize(new
    {
        tag_name = Tag,
        name = Titre,
        body = Notes,
        html_url = PageWeb,
        draft = Brouillon,
        prerelease = PreVersion,
        assets = Fichiers.Select(f => new { name = f.Nom, browser_download_url = f.UrlTelechargement, size = f.Taille }),
    });

    private static string? Texte(JsonElement objet, string nom) =>
        objet.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : null;

    private static bool Booleen(JsonElement objet, string nom) =>
        objet.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.True;
}
