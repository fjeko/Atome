using Revisions.Core.Calculatrice;
using Xunit;

namespace Revisions.Tests;

/// <summary>Calculatrice : rationnels exacts, analyseur d'expressions, matrices, messages d'erreur.</summary>
public class CalculatriceTests
{
    private static MatriceRationnelle M(params int[][] lignes) =>
        new(lignes.Length, lignes[0].Length, (i, j) => lignes[i][j]);

    private static Calculateur Avec(params (string nom, MatriceRationnelle m)[] matrices)
    {
        var c = new Calculateur();
        foreach (var (nom, m) in matrices) c.DefinirMatrice(nom, m);
        return c;
    }

    private static string Calc(string expression, Calculateur? c = null, bool decimale = false) =>
        Formatage.Texte((c ?? new Calculateur()).Evaluer(expression), decimale);

    private static string Erreur(string expression, Calculateur? c = null) =>
        Assert.Throws<ErreurCalcul>(() => (c ?? new Calculateur()).Evaluer(expression)).Message;

    // ---- Rationnels ------------------------------------------------------------------------

    [Fact]
    public void Rationnel_est_toujours_simplifie_avec_denominateur_positif()
    {
        var r = new Rationnel(6, -4);
        Assert.Equal("-3/2", r.EnFraction());
        Assert.Equal("3", new Rationnel(6, 2).EnFraction());
        Assert.Equal("0", new Rationnel(0, 5).EnFraction());
    }

    [Theory]
    [InlineData(1, 2, "0,5")]
    [InlineData(1, 3, "0,3333333333")]
    [InlineData(-7, 4, "-1,75")]
    [InlineData(2, 3, "0,6666666667")]
    [InlineData(5, 1, "5")]
    [InlineData(1, 100, "0,01")]
    public void Decimal_a_la_francaise(int num, int den, string attendu) =>
        Assert.Equal(attendu, new Rationnel(num, den).EnDecimal());

    [Fact]
    public void Rationnel_default_vaut_zero()
    {
        Rationnel zero = default;
        Assert.True(zero.EstZero);
        Assert.Equal("1", (zero + Rationnel.Un).EnFraction());
    }

    [Fact]
    public void Grands_nombres_sans_depassement() =>
        Assert.Equal("1267650600228229401496703205376", Calc("2^100"));

    // ---- Analyseur : nombres et priorités -------------------------------------------------

    [Theory]
    [InlineData("1/3+1/6", "1/2")]
    [InlineData("2+3*4", "14")]
    [InlineData("(2+3)*4", "20")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("2^-1", "1/2")]
    [InlineData("2*-3", "-6")]
    [InlineData("10-4-3", "3")]
    [InlineData("100/10/5", "2")]
    [InlineData("0,5+0.25", "3/4")]
    [InlineData("1,5*2", "3")]
    [InlineData("7÷2", "7/2")]
    [InlineData("3×4−5", "7")]
    [InlineData("(1/2)^3", "1/8")]
    [InlineData("0^0", "1")]
    public void Expressions_exactes(string expression, string attendu) =>
        Assert.Equal(attendu, Calc(expression));

    [Theory]
    [InlineData("2(3+4)", "14")]
    [InlineData("3(2)(4)", "24")]
    [InlineData("(1+1)(2+2)", "8")]
    [InlineData("2 3", null)] // deux nombres collés : pas de multiplication implicite
    public void Multiplication_implicite(string expression, string? attendu)
    {
        if (attendu is null) Assert.Contains("opérateur", Erreur(expression));
        else Assert.Equal(attendu, Calc(expression));
    }

    [Fact]
    public void Bascule_decimale_avec_virgule()
    {
        var v = new Calculateur().Evaluer("1/8+1/4");
        Assert.Equal("3/8", Formatage.Texte(v, decimale: false));
        Assert.Equal("0,375", Formatage.Texte(v, decimale: true));
    }

    // ---- Fonctions et constantes ----------------------------------------------------------

    [Theory]
    [InlineData("sqrt(16)", "4")]
    [InlineData("sqrt(9/4)", "3/2")]
    [InlineData("abs(-3/2)", "3/2")]
    [InlineData("ln(1)", "0")]
    [InlineData("exp(0)", "1")]
    [InlineData("log(100)", "2")]
    [InlineData("log(1/10)", "-1")]
    [InlineData("2sqrt(4)", "4")]
    [InlineData("√(25)", "5")]
    public void Fonctions_exactes(string expression, string attendu) =>
        Assert.Equal(attendu, Calc(expression));

    [Theory]
    [InlineData("sqrt(2)", "1,414213562")]
    [InlineData("pi", "3,141592654")]
    [InlineData("e", "2,718281828")]
    [InlineData("ln(10)", "2,302585093")]
    [InlineData("exp(1)", "2,718281828")]
    [InlineData("2pi", "6,283185307")]
    [InlineData("log(2)", "0,30102999")]
    public void Valeurs_approchees_en_decimal(string expression, string attendu)
    {
        var v = Assert.IsType<Nombre>(new Calculateur().Evaluer(expression));
        Assert.False(v.EstExact);
        Assert.StartsWith(attendu, v.Texte(decimale: false));
    }

    [Fact]
    public void Un_nombre_approche_ne_devient_pas_exact()
    {
        var v = (Nombre)new Calculateur().Evaluer("sqrt(2)*sqrt(2)");
        Assert.False(v.EstExact);
        Assert.InRange(v.Approche, 1.9999999, 2.0000001);
    }

    [Fact]
    public void Ans_reprend_le_dernier_resultat()
    {
        var c = new Calculateur();
        Assert.Equal("1/2", Calc("1/3+1/6", c));
        Assert.Equal("3/2", Calc("Ans+1", c));
        Assert.Equal("3", Calc("2Ans", c));
        Assert.Equal("9", Calc("ans^2", c));
    }

    [Fact]
    public void Ans_sans_resultat_precedent_est_explique() =>
        Assert.Contains("pas encore de résultat", Erreur("Ans+1"));

    // ---- Matrices --------------------------------------------------------------------------

    [Fact]
    public void Somme_difference_et_multiplication_par_un_nombre()
    {
        var c = Avec(("A", M([1, 2], [3, 4])), ("B", M([1, 0], [0, 1])));
        Assert.Equal("(2 2 ; 3 5)", Calc("A+B", c));
        Assert.Equal("(0 2 ; 3 3)", Calc("A-B", c));
        Assert.Equal("(3 6 ; 9 12)", Calc("3A", c));
        Assert.Equal("(1/2 1 ; 3/2 2)", Calc("A/2", c));
        Assert.Equal("(-1 -2 ; -3 -4)", Calc("-A", c));
        Assert.Equal("(1/2 1 ; 3/2 2)", Calc("(1/2)A", c));
    }

    [Fact]
    public void Produit_et_non_commutativite()
    {
        var c = Avec(("A", M([1, 2], [3, 4])), ("B", M([0, 1], [1, 0])));
        Assert.Equal("(2 1 ; 4 3)", Calc("A*B", c));
        Assert.Equal("(3 4 ; 1 2)", Calc("B*A", c));
        Assert.Equal("(2 1 ; 4 3)", Calc("AB", c)); // multiplication implicite de deux matrices
    }

    [Fact]
    public void Produit_de_matrices_rectangulaires()
    {
        var c = Avec(("A", M([1, 2, 3], [4, 5, 6])), ("B", M([1], [0], [2])));
        Assert.Equal("(7 ; 16)", Calc("A*B", c));
    }

    [Fact]
    public void Puissances_et_identite()
    {
        var c = Avec(("A", M([1, 1], [0, 1])));
        Assert.Equal("(1 3 ; 0 1)", Calc("A^3", c));
        Assert.Equal("(1 0 ; 0 1)", Calc("A^0", c));
        Assert.Equal("(1 0 0 ; 0 1 0 ; 0 0 1)", Calc("I3"));
        Assert.Equal("(2 1 ; 0 2)", Calc("A+I2", c));
        Assert.Equal("(1 -3 ; 0 1)", Calc("A^-3", c));
    }

    [Fact]
    public void Inverse_en_fractions_exactes()
    {
        var c = Avec(("A", M([1, 2], [3, 4])));
        Assert.Equal("(-2 1 ; 3/2 -1/2)", Calc("A^-1", c));
        Assert.Equal("(-2 1 ; 3/2 -1/2)", Calc("inv(A)", c));
        Assert.Equal("(1 0 ; 0 1)", Calc("A*inv(A)", c));
    }

    [Fact]
    public void Resolution_de_systeme_par_inverse()
    {
        // x + 2y = 5 ; 3x + 4y = 11  →  (x ; y) = A^-1 B = (1 ; 2)
        var c = Avec(("A", M([1, 2], [3, 4])), ("B", M([5], [11])));
        Assert.Equal("(1 ; 2)", Calc("inv(A)*B", c));
    }

    [Fact]
    public void Determinant_exact()
    {
        var c = Avec(("A", M([1, 2], [3, 4])), ("B", M([2, 0, 1], [1, 3, 2], [1, 1, 2])), ("C", M([1, 2], [2, 4])));
        Assert.Equal("-2", Calc("det(A)", c));
        Assert.Equal("0", Calc("det(C)", c));
        Assert.Equal("6", Calc("det(B)", c));
        Assert.Equal("-8", Calc("det(2A)", c));
        Assert.Equal("1", Calc("det(I5)"));
    }

    [Fact]
    public void Transposee_deux_ecritures()
    {
        var c = Avec(("A", M([1, 2, 3], [4, 5, 6])));
        Assert.Equal("(1 4 ; 2 5 ; 3 6)", Calc("A^T", c));
        Assert.Equal("(1 4 ; 2 5 ; 3 6)", Calc("trans(A)", c));
        Assert.Equal("(14 32 ; 32 77)", Calc("A*A^T", c));
    }

    [Fact]
    public void Matrice_en_decimal()
    {
        var c = Avec(("A", M([1, 2], [3, 4])));
        Assert.Equal("(-2 1 ; 1,5 -0,5)", Calc("A^-1", c, decimale: true));
    }

    [Fact]
    public void Matrices_gardees_en_memoire_et_remplacables()
    {
        var c = Avec(("A", M([1])));
        Assert.Equal("(1)", Calc("A", c));
        c.DefinirMatrice("A", M([7, 8]));
        Assert.Equal("(7 8)", Calc("A", c));
        c.SupprimerMatrice("A");
        Assert.Contains("n'est pas définie", Erreur("A", c));
    }

    [Fact]
    public void Grosse_puissance_reste_exacte()
    {
        var c = Avec(("F", M([1, 1], [1, 0])));
        // Fibonacci : F^30 contient F30 = 832040
        Assert.Equal("(1346269 832040 ; 832040 514229)", Calc("F^30", c));
    }

    // ---- Erreurs claires, en français -------------------------------------------------------

    [Fact]
    public void Produit_impossible_cite_les_noms_et_les_tailles()
    {
        var c = Avec(("A", M([1, 2, 3], [4, 5, 6])), ("B", M([1, 2, 3], [4, 5, 6])));
        var message = Erreur("A*B", c);
        Assert.Contains("Les tailles de A (2×3) et B (2×3) ne permettent pas le produit A×B", message);
        Assert.Contains("3 colonnes", message);
        Assert.Contains("2 lignes", message);
    }

    [Fact]
    public void Somme_de_tailles_differentes()
    {
        var c = Avec(("A", M([1, 2])), ("B", M([1], [2])));
        var message = Erreur("A+B", c);
        Assert.Contains("A (1×2)", message);
        Assert.Contains("B (2×1)", message);
        Assert.Contains("même taille", message);
    }

    [Fact]
    public void Nombre_plus_matrice()
    {
        var c = Avec(("A", M([1, 2])));
        Assert.Contains("un nombre et une matrice ne s'ajoutent pas", Erreur("A+1", c));
    }

    [Fact]
    public void Matrice_non_inversible_ou_non_carree()
    {
        var c = Avec(("A", M([1, 2], [2, 4])), ("R", M([1, 2, 3], [4, 5, 6])));
        Assert.Contains("A n'est pas inversible : son déterminant vaut 0", Erreur("A^-1", c));
        Assert.Contains("A n'est pas inversible", Erreur("inv(A)", c));
        Assert.Contains("matrice carrée", Erreur("inv(R)", c));
        Assert.Contains("n'existe que pour une matrice carrée", Erreur("det(R)", c));
        Assert.Contains("matrice carrée a des puissances", Erreur("R^2", c));
    }

    [Fact]
    public void Matrice_non_definie_est_expliquee()
    {
        var message = Erreur("A+B");
        Assert.Contains("La matrice A n'est pas définie", message);
    }

    [Fact]
    public void Division_par_zero_et_par_une_matrice()
    {
        var c = Avec(("A", M([1, 2], [3, 4])));
        Assert.Contains("Division par zéro", Erreur("1/0"));
        Assert.Contains("Division par zéro", Erreur("1/(2-2)"));
        Assert.Contains("Division par zéro", Erreur("A/0", c));
        Assert.Contains("On ne divise pas par une matrice (A)", Erreur("1/A", c));
        Assert.Contains("inv(A)", Erreur("1/A", c));
    }

    [Fact]
    public void Domaines_des_fonctions()
    {
        Assert.Contains("racine carrée d'un nombre négatif", Erreur("sqrt(-4)"));
        Assert.Contains("strictement positif", Erreur("ln(0)"));
        Assert.Contains("strictement positif", Erreur("log(-1)"));
        Assert.Contains("n'est pas un nombre réel", Erreur("(-8)^(1/3)"));
    }

    [Fact]
    public void Valeur_approchee_ne_multiplie_pas_une_matrice()
    {
        var c = Avec(("A", M([1])));
        Assert.Contains("valeur approchée", Erreur("piA", c));
    }

    [Theory]
    [InlineData("", "Écris un calcul")]
    [InlineData("2+", "incomplet")]
    [InlineData("(2+3", "parenthèse fermante")]
    [InlineData("2+3)", "en trop")]
    [InlineData("2+*3", "Il manque une valeur")]
    [InlineData("*3", "Il manque une valeur")]
    [InlineData("2$3", "Je ne comprends pas le caractère")]
    [InlineData("foo(2)", "n'est pas connu")]
    [InlineData("sqrt 4", "parenthèses")]
    [InlineData("I", "taille")]
    [InlineData("I0", "I1 à I10")]
    [InlineData("T", "transposée")]
    public void Erreurs_de_saisie_en_francais(string expression, string extrait) =>
        Assert.Contains(extrait, Erreur(expression));

    [Fact]
    public void Une_erreur_ne_change_pas_Ans()
    {
        var c = new Calculateur();
        Calc("5", c);
        Erreur("1/0", c);
        Assert.Equal("5", Calc("Ans", c));
    }

    [Fact]
    public void Exposant_de_matrice_trop_grand_ou_non_entier()
    {
        var c = Avec(("A", M([1, 1], [0, 1])));
        Assert.Contains("entier", Erreur("A^(1/2)", c));
        Assert.Contains("entier", Erreur("A^100000", c));
    }
}

/// <summary>Garde-fou : où la calculatrice est permise ou grisée dans les TD.</summary>
public class CalculatriceTdTests
{
    [Theory]
    [InlineData("Mathématiques", true)]
    [InlineData("mathematiques", true)]
    [InlineData("Maths", true)]
    [InlineData("Algorithmique (Python)", false)]
    [InlineData("Anglais", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Seules_les_maths_ont_la_calculatrice(string? matiere, bool attendu) =>
        Assert.Equal(attendu, Revisions.Core.Calculatrice.RegleCalculatrice.EstMatiereMaths(matiere));


    [Fact]
    public void Td_de_matrices_calcul_a_la_main_grise_et_situations_permises()
    {
        var td = new Revisions.Core.TdGroupe.GenerateurMatrices().Generer(new(("MAT"), 2, 3, 12, 1234));
        Assert.All(td.Exercices, e => Assert.Equal("Mathématiques", e.Matiere));
        Assert.Contains(td.Exercices, e => e.Titre.StartsWith("Produit") && e.SansCalculatrice is not null);
        Assert.Contains(td.Exercices, e => e.Titre.StartsWith("Somme") && e.SansCalculatrice is not null);
        Assert.Contains(td.Exercices, e => e.SansCalculatrice is null); // situation concrète, dimension…
        Assert.All(td.Exercices.Where(e => e.SansCalculatrice is not null), e => Assert.Contains("à la main", e.SansCalculatrice));
    }

    [Fact]
    public void Td_inverse_la_verification_est_grisee_mais_pas_l_inverse_a_la_calculatrice()
    {
        var g = new Revisions.Core.TdGroupe.GenerateurInverse();
        var td = g.Generer(new("INV", 1, 3, 5, 99));
        Assert.Equal(5, td.Exercices.Count);
        Assert.Contains(td.Exercices, e => e.Titre == "Inverse d'une matrice" && e.SansCalculatrice is null);
        Assert.Contains(td.Exercices, e => e.Titre.Contains("est-elle l'inverse") && e.SansCalculatrice is not null);
    }

    [Fact]
    public void Les_regles_ne_changent_pas_le_contenu_des_td_partages()
    {
        // Les nouveaux champs ne font pas partie de l'empreinte : deux camarades gardent la même.
        var a = new Revisions.Core.TdGroupe.GenerateurMatrices().Generer(new("MAT", 2, 2, 6, 4321));
        var sansRegle = Revisions.Core.TdGroupe.EmpreinteTd.Calculer(a.Exercices.Select(e => e with { Matiere = null, SansCalculatrice = null }));
        Assert.Equal(sansRegle, a.Empreinte);
    }
}
