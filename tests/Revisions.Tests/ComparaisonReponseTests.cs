using Revisions.Core.Modeles;
using Xunit;

namespace Revisions.Tests;

/// <summary>Résultat écrit d'un exercice comparé à la réponse : décide si la correction détaillée s'affiche.</summary>
public class ComparaisonReponseTests
{
    private const string Matrice = @"$\begin{pmatrix} 33 & 11 \ 63 & 21 \end{pmatrix}$";

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
