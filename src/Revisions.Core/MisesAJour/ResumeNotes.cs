using System.Text.RegularExpressions;

namespace Revisions.Core.MisesAJour;

/// <summary>
/// Transforme les notes de version (Markdown de GitHub) en quelques lignes de texte simple
/// pour le bandeau : pas de titres, pas de liens, des puces « • ».
/// </summary>
public static partial class ResumeNotes
{
    /// <summary>
    /// Résumé des notes. Si les notes ont une rubrique « Nouveautés » (ou « What's Changed », celle que GitHub
    /// génère), on commence après son titre : le début des notes d'Atome explique l'installation, inutile ici.
    /// </summary>
    /// <param name="markdown">Notes de version (champ body de la Release).</param>
    /// <param name="maxLignes">Nombre maximal de lignes gardées.</param>
    public static IReadOnlyList<string> Resumer(string? markdown, int maxLignes = 5)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        string[] lignes = markdown.Replace("\r", "").Split('\n');

        int debut = Array.FindIndex(lignes, l => TitreNouveautes().IsMatch(l));
        var resultat = new List<string>();
        for (int i = debut + 1; i < lignes.Length && resultat.Count < maxLignes; i++)
        {
            string ligne = lignes[i].Trim();
            if (ligne.Length == 0 || ligne.StartsWith("<!--") || ligne.StartsWith("---")) continue;
            // Les titres secondaires et le lien « Full Changelog » n'apprennent rien à l'utilisateur.
            if (ligne.StartsWith('#') || ligne.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;

            string texte = Nettoyer(ligne);
            if (texte.Length > 0) resultat.Add(texte);
        }
        return resultat;
    }

    /// <summary>Retire la mise en forme Markdown d'une ligne.</summary>
    internal static string Nettoyer(string ligne)
    {
        bool puce = PuceListe().IsMatch(ligne);
        string texte = PuceListe().Replace(ligne, "");
        texte = Image().Replace(texte, "");
        texte = Lien().Replace(texte, "$1");                 // [texte](adresse) → texte
        texte = AuteurGitHub().Replace(texte, "");           // « by @x in https://…/pull/3 » (notes générées)
        texte = texte.Replace("**", "").Replace("__", "").Replace("`", "");
        texte = texte.Trim();
        if (texte.Length > 160) texte = texte[..157].TrimEnd() + "…";
        return texte.Length == 0 ? "" : (puce ? "• " : "") + texte;
    }

    [GeneratedRegex(@"^\s*#{1,6}\s*(nouveaut|what'?s changed|changements)", RegexOptions.IgnoreCase)]
    private static partial Regex TitreNouveautes();

    [GeneratedRegex(@"^\s*([-*+]|\d+[.)])\s+")]
    private static partial Regex PuceListe();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Lien();

    [GeneratedRegex(@"\s+by @[\w-]+( in https?://\S+)?\s*$")]
    private static partial Regex AuteurGitHub();
}
