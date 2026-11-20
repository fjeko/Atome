using Revisions.Core.Modeles;
using Xunit;

namespace Revisions.Tests;

/// <summary>Résultat écrit d'un exercice comparé à la réponse : décide si la correction détaillée s'affiche.</summary>
public class ComparaisonReponseTests
{
    private const string Matrice = @"$\begin{pmatrix} 33 & 11 \\ 63 & 21 \end{pmatrix}$";

    [Theory]
    [InlineData("33 11 63 21", Matrice, true)]
    [InlineData("33 11\n63 21", Matrice, true)]
    [InlineData("((33,11),(63,21))", Matrice, true)] // virgules entre les nombres
    [InlineData("33 ; 11 ; 63 ; 21", Matrice, true)]
    [InlineData("33 11 63 20", Matrice, false)]
    [InlineData("11 33 63 21", Matrice, false)]
    [InlineData("", Matrice, false)]
    [InlineData("0,5", "$x = 0.5$", true)] // virgule décimale
    [InlineData("-3", @"$-3$", true)]
    [InlineData("1/2", @"$\frac{1}{2}$", true)]
    [InlineData("Ethernet", "ethernet.", true)]
    [InlineData("Wi-Fi", "Ethernet", false)]
    public void Compare_les_resultats(string saisie, string reponse, bool attendu) =>
        Assert.Equal(attendu, ComparaisonReponse.MemeResultat(saisie, reponse));
}

/// <summary>Taille de la grille proposée pour écrire une matrice.</summary>
public class DimensionsMatriceTests
{
    [Fact]
    public void Lit_la_taille_de_la_premiere_matrice()
    {
        Assert.Equal((3, 2), ComparaisonReponse.DimensionsMatrice(@"$C+D = \begin{pmatrix} 14 & 10 \\ 11 & 11 \\ 8 & 18 \end{pmatrix}$"));
        Assert.Equal((2, 3), ComparaisonReponse.DimensionsMatrice(@"$\begin{bmatrix}1&2&3\\4&5&6\\\end{bmatrix}$"));
        Assert.Null(ComparaisonReponse.DimensionsMatrice("$x = 4$"));
        Assert.Null(ComparaisonReponse.DimensionsMatrice(null));
    }

    [Fact]
    public void La_grille_remplie_donne_le_meme_resultat_que_la_matrice()
    {
        const string reponse = @"$\begin{pmatrix} 14 & 10 \\ 11 & 11 \\ 8 & 18 \end{pmatrix}$";
        Assert.True(ComparaisonReponse.MemeResultat("14   10\n11   11\n8   18", reponse));
        Assert.False(ComparaisonReponse.MemeResultat("14   10\n11   ?\n8   18", reponse));
    }
}
