using System.Text.RegularExpressions;

namespace Revisions.Core.Packs;

/// <summary>
/// Transforme un texte Markdown + LaTeX en texte ordinaire, pour les endroits qui ne savent pas
/// afficher les formules (une ligne de liste, par exemple). Ce n'est pas un vrai rendu : le but
/// est seulement d'obtenir un extrait lisible, sans « \begin{pmatrix} » ni « $ ».
/// </summary>
public static partial class TexteSimple
{
    [GeneratedRegex(@"\\begin\{[a-zA-Z]*matrix\*?\}.*?\\end\{[a-zA-Z]*matrix\*?\}", RegexOptions.Singleline)]
    private static partial Regex Matrice();

    [GeneratedRegex(@"\\begin\{[a-zA-Z*]+\}.*?\\end\{[a-zA-Z*]+\}", RegexOptions.Singleline)]
    private static partial Regex AutreBloc();

    [GeneratedRegex(@"\\d?frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}")]
    private static partial Regex Fraction();

    [GeneratedRegex(@"\\(?:text|mathrm|mathbf|mathit|textbf|operatorname)\s*\{([^{}]*)\}")]
    private static partial Regex TexteDansFormule();

    [GeneratedRegex(@"\\[a-zA-Z]+")]
    private static partial Regex Commande();

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex BlocDeCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();

    // Commandes LaTeX courantes et leur équivalent en caractères ordinaires.
    private static readonly (string Latex, string Texte)[] Symboles =
    [
        (@"\times", "×"), (@"\cdot", "·"), (@"\div", "÷"), (@"\pm", "±"),
        (@"\leq", "≤"), (@"\le", "≤"), (@"\geq", "≥"), (@"\ge", "≥"), (@"\neq", "≠"), (@"\ne", "≠"),
        (@"\approx", "≈"), (@"\infty", "∞"), (@"\Rightarrow", "⇒"), (@"\Leftrightarrow", "⇔"),
        (@"\rightarrow", "→"), (@"\to", "→"), (@"\leftarrow", "←"),
        (@"\in", "∈"), (@"\notin", "∉"), (@"\cup", "∪"), (@"\cap", "∩"), (@"\subset", "⊂"),
        (@"\forall", "∀"), (@"\exists", "∃"), (@"\sum", "Σ"), (@"\sqrt", "√"), (@"\ldots", "…"), (@"\dots", "…"),
        (@"\alpha", "α"), (@"\beta", "β"), (@"\lambda", "λ"), (@"\pi", "π"), (@"\Delta", "Δ"),
    ];

    /// <summary>Texte ordinaire, sur une seule ligne, coupé à <paramref name="longueurMax"/> caractères.</summary>
    public static string Extrait(string? source, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        string texte = source;

        // Ce qui ne peut pas tenir sur une ligne est remplacé par un mot.
        texte = BlocDeCode().Replace(texte, " [code] ");
        texte = Matrice().Replace(texte, "[matrice]");
        texte = AutreBloc().Replace(texte, "[formule]");

        // Fractions et textes dans les formules : on garde le contenu.
        texte = Fraction().Replace(texte, "$1/$2");
        texte = TexteDansFormule().Replace(texte, "$1");

        // Symboles connus (les plus longs d'abord, pour que « \leq » ne soit pas pris pour « \le »).
        foreach (var (latex, remplacement) in Symboles.OrderByDescending(s => s.Latex.Length))
            texte = Regex.Replace(texte, Regex.Escape(latex) + "(?![a-zA-Z])", remplacement);

        // Le reste des commandes disparaît, ainsi que les signes de mise en forme.
        texte = Commande().Replace(texte, " ");
        texte = texte.Replace("$", "").Replace("{", "").Replace("}", "").Replace(@"\\", " ").Replace(@"\", "")
            .Replace("**", "").Replace("`", "");

        texte = Espaces().Replace(texte, " ").Trim();
        return texte.Length <= longueurMax ? texte : texte[..longueurMax].TrimEnd() + "…";
    }
}
