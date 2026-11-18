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

    // ---- Calculs : exemples du cours --------------------------------------------------------

    [Fact]
    public void Mpm_exemple_du_cours()
    {
        // A..H : durées 2,2,2,4,1,2,2,1 ; C:A, D:A, E:B+D, F:D, G:E, H:C+F+G.
        List<int>[] ant = [[], [], [0], [0], [1, 3], [3], [4], [2, 5, 6]];
        var p = new ProjetMpm([2, 2, 2, 4, 1, 2, 2, 1], ant);
        Assert.Equal([0, 0, 2, 2, 6, 6, 7, 9, 10], p.Tot);
        Assert.Equal([0, 4, 7, 2, 6, 7, 7, 9, 10], p.Tard);
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 4], p.Niveaux);
        Assert.Equal(10, p.Duree);
        Assert.Equal(4, p.MargeTotale(1));            // B : 4 − 0
        Assert.Equal(5, p.MargeLibre(2));             // C : 9 − 2 − 2
        var chemin = Assert.Single(p.CheminsCritiques());
        Assert.Equal([0, 3, 4, 6, 7, 8], chemin);     // A-D-E-G-H-FIN
    }

    [Fact]
    public void Graphes_fermeture_et_niveaux()
    {
        // A → B → C → D : la fermeture ajoute A→C, A→D, B→D.
        var g = new bool[4, 4];
        g[0, 1] = g[1, 2] = g[2, 3] = true;
        var chapeau = GenerateurGraphes.FermetureTransitive(GenerateurGraphes.Adjacence(g));
        Assert.Equal(6, chapeau.Coefficients().Sum());
        Assert.Equal(1, chapeau[0, 3]);
        Assert.Equal(0, chapeau[3, 0]);
        Assert.Equal([0, 1, 2, 3], GenerateurGraphes.CalculerNiveaux(g).Niveaux);
        Assert.Single(GenerateurGraphes.TousLesChemins(g, 0, 3));
    }

    [Fact]
    public void Graphes_les_reponses_concordent_avec_les_calculs()
    {
        var gra = new GenerateurGraphes();
        for (ulong graine = 0; graine < 30; graine++)
        {
            var e = gra.CreerExercice("fermeture", new GenerateurAleatoire(graine), 2);
            var m2 = Assert.IsType<ReponseMatrice>(e.Questions[0].Attendu).Valeur;
            var chapeau = Assert.IsType<ReponseMatrice>(e.Questions[1].Attendu).Valeur;
            // M̂ contient M[2] (tout chemin de longueur 2 existe dans la fermeture).
            for (int i = 0; i < m2.Lignes; i++)
            for (int j = 0; j < m2.Colonnes; j++)
                Assert.True(m2[i, j] <= chapeau[i, j]);
        }
    }

    // ---- Sujets ----------------------------------------------------------------------------

    private static readonly IGenerateurASujets[] Tous = [new GenerateurMatrices(), new GenerateurGraphes(), new GenerateurOrdonnancement(), new GenerateurInverse(), new GenerateurIpv4(), new GenerateurDroitsLinux(), new GenerateurSql()];

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
