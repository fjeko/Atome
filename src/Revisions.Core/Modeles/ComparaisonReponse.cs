using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Revisions.Core.Modeles;

/// <summary>
/// Compare le résultat écrit d'un exercice à la réponse du pack, pour décider s'il faut montrer la correction détaillée.
/// C'est une aide à l'affichage, jamais une note imposée : c'est toujours l'étudiant qui se note.
/// </summary>
public static partial class ComparaisonReponse
{
    /// <summary>
    /// Vrai si la saisie donne le même résultat que la réponse.
    /// Réponse avec des nombres (matrice, calcul…) : mêmes nombres dans le même ordre, quelle que soit l'écriture
    /// (« 33 11 63 21 » vaut <c>$\begin{pmatrix}33 &amp; 11\\63 &amp; 21\end{pmatrix}$</c>, « 0,5 » vaut « 0.5 »).
    /// Sans nombre : même texte, sans tenir compte des majuscules, des accents, des espaces ni de la ponctuation.
    /// </summary>
    public static bool MemeResultat(string? saisie, string? reponse)
    {
        if (string.IsNullOrWhiteSpace(saisie) || string.IsNullOrWhiteSpace(reponse)) return false;
        string reponseSansLatex = CommandeLatex().Replace(reponse, " ");
        var attendus = Nombres(reponseSansLatex);
        // La virgule de la saisie peut être décimale (« 0,5 ») ou séparer des nombres (« 33,11,63,21 ») : les deux lectures sont essayées.
        if (attendus.Count > 0) return attendus.SequenceEqual(Nombres(saisie)) || attendus.SequenceEqual(Nombres(saisie.Replace(',', ' ')));
        return Normaliser(saisie) == Normaliser(reponseSansLatex);
    }

    /// <summary>
    /// Dimensions de la première matrice de la réponse (<c>\begin{pmatrix} … \end{pmatrix}</c>, ou bmatrix, vmatrix…),
    /// pour proposer une grille de saisie de cette taille. Null si la réponse n'a pas de matrice.
    /// </summary>
    public static (int Lignes, int Colonnes)? DimensionsMatrice(string? reponse)
    {
        if (string.IsNullOrEmpty(reponse)) return null;
        var m = Matrice().Match(reponse);
        if (!m.Success) return null;
        var lignes = m.Groups[1].Value.Split(@"\\").Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lignes.Count == 0) return null;
        int colonnes = lignes.Max(l => l.Split('&').Length);
        return lignes.Count <= 8 && colonnes <= 8 ? (lignes.Count, colonnes) : null;
    }

    [GeneratedRegex(@"\\begin\{[pbvBV]?matrix\}(.*?)\\end\{", RegexOptions.Singleline)]
    private static partial Regex Matrice();

    private static List<decimal> Nombres(string texte) =>
        Nombre().Matches(texte)
            .Select(m => decimal.Parse(m.Value.Replace(',', '.').Replace('−', '-'), NumberStyles.Number, CultureInfo.InvariantCulture))
            .ToList();

    private static string Normaliser(string texte)
    {
        var sb = new StringBuilder();
        foreach (char c in texte.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // Un nombre, avec son signe moins s'il est collé (« -3 », « −3 »), et une partie décimale éventuelle.
    [GeneratedRegex(@"[-−]?\d+(?:[.,]\d+)?")]
    private static partial Regex Nombre();

    // Commandes LaTeX (\begin, \frac, \times…) : elles ne contiennent pas de résultat.
    [GeneratedRegex(@"\\[a-zA-Z]+")]
    private static partial Regex CommandeLatex();
}
