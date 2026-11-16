using Revisions.Core.Bibliotheque;

namespace Revisions.Tests;

/// <summary>Choix du pictogramme d'une matière d'après son nom.</summary>
public class PictogrammesTests
{
    [Theory]
    // Les sept matières actuelles des packs.
    [InlineData("Bloc 1 – Support et mise à disposition de services informatiques", Pictogramme.Ordinateur)]
    [InlineData("Bloc 2 – SLAM : conception et développement d'applications", Pictogramme.Code)]
    [InlineData("Bloc 3 – Cybersécurité des services informatiques", Pictogramme.Securite)]
    [InlineData("Anglais", Pictogramme.Langue)]
    [InlineData("Algorithmique appliquée", Pictogramme.Algorithme)]
    [InlineData("Mathématiques pour l'informatique", Pictogramme.Maths)]
    [InlineData("CEJM – Culture économique, juridique et managériale", Pictogramme.Droit)]
    // Matières possibles plus tard.
    [InlineData("Culture générale et expression", Pictogramme.Lettres)]
    [InlineData("Bloc 2 – SISR : administration des systèmes et des réseaux", Pictogramme.Ordinateur)]
    [InlineData("MATHÉMATIQUES APPROFONDIES", Pictogramme.Maths)]
    public void Chaque_matiere_recoit_son_pictogramme(string matiere, Pictogramme attendu) =>
        Assert.Equal(attendu, Pictogrammes.PourMatiere(matiere));

    [Theory]
    [InlineData("Histoire de l'art")]
    [InlineData("")]
    [InlineData(null)]
    public void Une_matiere_inconnue_recoit_le_livre(string? matiere) =>
        Assert.Equal(Pictogramme.Livre, Pictogrammes.PourMatiere(matiere));

    [Fact]
    public void Seules_les_tuiles_de_matiere_ont_un_pictogramme()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            new ResumeSeance
            {
                PackId = "p", Bloc = "Bloc 3", Matiere = "Bloc 3 – Cybersécurité des services informatiques",
                Partie = "B3.1 – SQ1 – Cybermenaces", Seance = "S1",
            },
        ]);

        var matiere = racine.Enfants[0];
        Assert.Equal(Pictogramme.Securite, matiere.Icone);
        Assert.Null(matiere.Enfants[0].Icone);             // compétence B3.1
        Assert.Null(matiere.Enfants[0].Enfants[0].Icone);  // séquence SQ1
    }
}
