using Revisions.Core.Modeles;
using Xunit;

namespace Revisions.Tests;

/// <summary>Cartes qui demandent d'écrire du code : elles reçoivent une vraie zone de code.</summary>
public class DetectionCodeTests
{
    private static Carte Question(string recto, string verso) =>
        new("c1", "s1", TypeCarte.Question, 2, new ContenuCarte { Recto = recto, Verso = verso });

    private static Carte Exercice(string enonce, string reponse, params string[] etapes) =>
        new("c2", "s1", TypeCarte.Exercice, 2, new ContenuCarte { Enonce = enonce, Reponse = reponse, Etapes = [.. etapes] });

    [Fact]
    public void Un_bloc_de_code_dans_la_reponse_suffit()
    {
        var carte = Exercice("Réécris le traitement d'un menu avec un `switch`.",
            "```csharp\nswitch (choix)\n{\n    default: break;\n}\n```", "Lire le choix.");
        Assert.True(DetectionCode.AttendDuCode(carte, "Bloc 1 - Support"));
    }

    [Fact]
    public void Un_bloc_de_code_dans_une_etape_suffit() =>
        Assert.True(DetectionCode.AttendDuCode(Exercice("Que vaut x ?", "5", "```\nx = 2 + 3\n```")));

    [Theory]
    [InlineData("Écris la requête SQL qui liste les clients.", "SELECT * FROM client;", null)]
    [InlineData("Complète le code pour afficher 10 fois Bonjour.", "Une boucle for.", null)]
    [InlineData("Corrige la condition `if (x = 3)`.", "Il faut ==.", null)]
    [InlineData("Écris l'instruction qui affiche Bonjour.", "Console.WriteLine", null)]
    [InlineData("Écris une boucle qui compte de 1 à 10.", "Une boucle.", "Algorithmique")]
    [InlineData("Écris la déclaration d'un entier âge.", "`int age = 0;`", null)]
    public void Verbe_et_indice_de_programmation(string recto, string verso, string? matiere) =>
        Assert.True(DetectionCode.AttendDuCode(Question(recto, verso), matiere));

    [Theory]
    [InlineData("Que fait le code suivant ?", "Il affiche 3.", "Algorithmique")]          // pas de verbe de consigne
    [InlineData("Écris la définition d'une entreprise.", "Une organisation…", "CEJM")]  // verbe sans indice de code
    [InlineData("Complète la phrase : le chiffre d'affaires est…", "Le total des ventes.", "CEJM")]
    [InlineData("Quel mot-clé déclare un entier en C# ?", "`int`", "Bloc 1 - Support")]   // réponse courte
    [InlineData("Calcule 2 + 3.", "$5$", "Mathématiques")]
    public void Cartes_ordinaires(string recto, string verso, string? matiere) =>
        Assert.False(DetectionCode.AttendDuCode(Question(recto, verso), matiere));

    [Fact]
    public void Un_qcm_n_attend_jamais_de_code()
    {
        var qcm = new Carte("q", "s1", TypeCarte.Qcm, 1, new ContenuCarte
        {
            Question = "Écris… lequel est juste ?", Choix = ["```a```", "b"], BonneReponse = 0, Explication = "```\nx;\n```",
        });
        Assert.False(DetectionCode.AttendDuCode(qcm, "Algorithmique"));
    }

    [Theory]
    [InlineData("Bloc 2 - SLAM", true)]
    [InlineData("Algorithmique", true)]
    [InlineData("Mathématiques", false)]
    [InlineData(null, false)]
    public void Matieres_de_programmation(string? matiere, bool attendu) =>
        Assert.Equal(attendu, DetectionCode.EstMatiereDeCode(matiere));
}

/// <summary>Règles d'édition de la zone de code : indentation gardée, Tab et Maj+Tab.</summary>
public class EditionCodeTests
{
    [Fact]
    public void Entree_garde_l_indentation() =>
        Assert.Equal("\n    ", EditionCode.NouvelleLigne("{\n    x = 1;", "{\n    x = 1;".Length));

    [Fact]
    public void Entree_apres_une_accolade_ajoute_un_cran() =>
        Assert.Equal("\n        ", EditionCode.NouvelleLigne("    switch (choix)\n    {", "    switch (choix)\n    {".Length));

    [Fact]
    public void Entree_au_debut_du_texte() => Assert.Equal("\n", EditionCode.NouvelleLigne("", 0));

    [Fact]
    public void Lignes_touchees_par_la_selection()
    {
        const string texte = "a\nbb\nccc";
        Assert.Equal((2, 4), EditionCode.Lignes(texte, 3, 0));       // curseur dans « bb »
        Assert.Equal((0, 4), EditionCode.Lignes(texte, 1, 2));       // de « a » à « bb »
        Assert.Equal((2, 4), EditionCode.Lignes(texte, 2, 3));       // « bb\n » : la ligne suivante n'est pas touchée
        Assert.Equal((5, 8), EditionCode.Lignes(texte, 8, 0));       // fin du texte
    }

    [Fact]
    public void Indenter_et_desindenter()
    {
        Assert.Equal("    a\n    b", EditionCode.Indenter("a\nb"));
        Assert.Equal("a\n  b\nc", EditionCode.Desindenter("    a\n      b\n\tc"));
        Assert.Equal("a", EditionCode.Desindenter("  a"));
    }
}
