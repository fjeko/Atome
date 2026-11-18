using Revisions.Core.TdGroupe;

namespace Revisions.Tests;

/// <summary>TD solo : réponses texte, sujets des générateurs.</summary>
public class TdSoloTests
{
    // ---- Réponses texte --------------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.0", " 192.168.1.0 ", true)]
    [InlineData("192.168.1.0", "192.168.1.1", false)]
    [InlineData("rwxr-x---", "RWXR-X---", true)]
    [InlineData("rwxr-x---", "rwxr-x--", false)]
    public void Texte_exact(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue), saisie).Juste);

    [Theory]
    [InlineData("A-C-F-FIN", "a c f fin", true)]
    [InlineData("A-C-F-FIN", "A → C → F → FIN", true)]
    [InlineData("A-C-F-FIN", "ACFFIN", true)]
    [InlineData("A-C-F-FIN", "A-F-C-FIN", false)]
    public void Texte_chemin(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue, FormeTexte.Chemin), saisie).Juste);

    [Theory]
    [InlineData("B, D", "D B", true)]
    [InlineData("B, D", "BD", true)]
    [InlineData("B, D", "B", false)]
    public void Texte_ensemble(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue, FormeTexte.Ensemble), saisie).Juste);

    [Fact]
    public void Texte_variantes_et_saisie_vide()
    {
        var r = new ReponseTexte("rwxr-x---", Variantes: ["-rwxr-x---"]);
        Assert.True(VerificationSaisie.Texte(r, "-rwxr-x---").Juste);
        var vide = VerificationSaisie.Texte(r, "  ");
        Assert.False(vide.Juste);
        Assert.False(vide.Complete);
    }

    // ---- Sujets ----------------------------------------------------------------------------

    private static readonly IGenerateurASujets[] Tous = [new GenerateurMatrices()];

    public static TheoryData<string> Generateurs() => [.. Tous.Select(g => g.Prefixe)];

    private static IGenerateurASujets Gen(string prefixe) => Tous.Single(g => g.Prefixe == prefixe);

    [Theory]
    [MemberData(nameof(Generateurs))]
    public void Chaque_sujet_genere_des_exercices_complets_a_chaque_niveau(string prefixe)
    {
        var g = Gen(prefixe);
        Assert.NotEmpty(g.Sujets);
        Assert.Equal(g.Sujets.Count, g.Sujets.Select(s => s.Cle).Distinct().Count());
        foreach (var sujet in g.Sujets)
        for (int niveau = sujet.NiveauMin; niveau <= 3; niveau++)
        for (ulong graine = 0; graine < 40; graine++)
        {
            var e = g.CreerExercice(sujet.Cle, new GenerateurAleatoire(graine * 7919 + (ulong)niveau), niveau);
            Assert.False(string.IsNullOrWhiteSpace(e.Enonce), $"{g.Prefixe}/{sujet.Cle}");
            Assert.NotEmpty(e.Questions);
            foreach (var q in e.Questions)
            {
                Assert.NotEmpty(q.Correction);
                Assert.False(string.IsNullOrWhiteSpace(q.Attendu.Texte), $"{g.Prefixe}/{sujet.Cle} {q.Lettre}");
                if (q.Attendu is ReponseTexte t) Assert.True(VerificationSaisie.Texte(t, t.Attendue).Juste, $"{g.Prefixe}/{sujet.Cle} : {t.Attendue}");
                if (q.Attendu is ReponseMatrice m)
                {
                    if (m.EnTetesColonnes is { } c) Assert.Equal(m.Valeur.Colonnes, c.Count);
                    if (m.EnTetesLignes is { } l) Assert.Equal(m.Valeur.Lignes, l.Count);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Generateurs))]
    public void Meme_graine_meme_td_en_groupe(string prefixe)
    {
        var g = Gen(prefixe);
        var p = new ParametresTd(g.Prefixe, g.VersionActuelle == 2 ? 2 : 1, 2, 8, 1234);
        Assert.Equal(g.Generer(p).Empreinte, g.Generer(p).Empreinte);
    }
}
