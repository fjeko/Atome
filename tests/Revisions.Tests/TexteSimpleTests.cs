using Revisions.Core.Packs;

namespace Revisions.Tests;

/// <summary>Extraits en texte ordinaire (liste des cartes à revoir en fin de session).</summary>
public class TexteSimpleTests
{
    [Theory]
    [InlineData("À quelle condition peut-on calculer $A \\times B$ ?", "À quelle condition peut-on calculer A × B ?")]
    [InlineData("$A = \\begin{pmatrix} 4 & 12 \\\\ 2 & 5 \\end{pmatrix}$ et $C$", "A = [matrice] et C")]
    [InlineData("Si $x \\leq 3$ alors $y \\neq 0$", "Si x ≤ 3 alors y ≠ 0")]
    [InlineData("Calculer $\\frac{1}{2} + \\dfrac{3}{4}$", "Calculer 1/2 + 3/4")]
    [InlineData("Le mot-clé **`for`** répète", "Le mot-clé for répète")]
    [InlineData("Que fait ce code ?\n\n```csharp\nint a = 1;\n```", "Que fait ce code ? [code]")]
    [InlineData("$\\text{coût} \\to \\infty$", "coût → ∞")]
    [InlineData("  plusieurs\n\n lignes  ", "plusieurs lignes")]
    public void Le_latex_et_le_markdown_deviennent_du_texte_ordinaire(string source, string attendu) =>
        Assert.Equal(attendu, TexteSimple.Extrait(source, 200));

    [Fact]
    public void Un_texte_trop_long_est_coupe_avec_des_points_de_suspension()
    {
        string extrait = TexteSimple.Extrait(new string('a', 300), 110);
        Assert.Equal(111, extrait.Length);
        Assert.EndsWith("…", extrait);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_texte_vide_donne_un_extrait_vide(string? source) =>
        Assert.Equal("", TexteSimple.Extrait(source, 110));
}
