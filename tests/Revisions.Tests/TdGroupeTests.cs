using Revisions.Core.Modeles;
using Revisions.Core.TdGroupe;

namespace Revisions.Tests;

/// <summary>TD en groupe : hasard maison, codes, calculs matriciels, générateurs, vérification, versions.</summary>
public class TdGroupeTests
{
    private static readonly GenerateurMatrices Matrices = new();

    private static Matrice M(params int[][] lignes) => new(lignes);

    // ---- Hasard maison : il ne doit JAMAIS changer -----------------------------------------

    [Fact]
    public void SplitMix64_donne_les_valeurs_de_reference()
    {
        // Valeurs publiées de SplitMix64 pour la graine 0 (implémentation de référence de Vigna).
        var r = new GenerateurAleatoire(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, r.Suivant());
        Assert.Equal(0x6E789E6AA1B965F4UL, r.Suivant());
        Assert.Equal(0x06C45D188009454FUL, r.Suivant());
    }

    [Fact]
    public void Meme_graine_meme_suite_et_bornes_respectees()
    {
        var a = new GenerateurAleatoire(4821);
        var b = new GenerateurAleatoire(4821);
        for (int k = 0; k < 1000; k++)
        {
            int x = a.Entier(-5, 9);
            Assert.Equal(x, b.Entier(-5, 9));
            Assert.InRange(x, -5, 9);
        }
        Assert.NotEqual(0, new GenerateurAleatoire(1).EntierNonNul(-1, 1));
    }

    [Fact]
    public void Tirages_entiers_figes()
    {
        var r = new GenerateurAleatoire(4821);
        int[] tirages = Enumerable.Range(0, 10).Select(_ => r.Entier(0, 9)).ToArray();
        Assert.Equal(TiragesAttendus, tirages);
    }

    private static readonly int[] TiragesAttendus = [6, 6, 0, 1, 2, 3, 1, 5, 8, 0];

    // ---- Codes ---------------------------------------------------------------------------

    [Fact]
    public void Code_ecrit_puis_relu()
    {
        var p = new ParametresTd("MAT", 1, 2, 8, 4821);
        string code = CodeTd.Ecrire(p);
        Assert.Matches(@"^MAT1-2-8-4821\d$", code);
        Assert.Equal(p, CodeTd.Lire(code).Parametres);
    }

    [Fact]
    public void Code_de_seance_avec_identifiant_de_pack()
    {
        var p = new ParametresTd("SEA", 1, 3, 5, 7, "maths-sq1-s03");
        string code = CodeTd.Ecrire(p);
        Assert.StartsWith("SEA1-3-5-0007", code);
        Assert.EndsWith("-maths-sq1-s03", code);
        Assert.Equal(p, CodeTd.Lire(code).Parametres);
    }

    [Theory]
    [InlineData(" mat1 2 8 {0} ")]   // minuscules et espaces, comme on le tape après l'avoir entendu
    [InlineData("MAT1.2.8.{0}")]
    [InlineData("Mat1_2_8_{0}")]
    public void Code_saisi_avec_d_autres_separateurs(string modele)
    {
        string bloc = CodeTd.Ecrire(new ParametresTd("MAT", 1, 2, 8, 4821)).Split('-')[3];
        Assert.Equal(new ParametresTd("MAT", 1, 2, 8, 4821), CodeTd.Lire(string.Format(modele, bloc)).Parametres);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAT-2-4821")]          // ancienne idée de forme, sans version ni contrôle
    [InlineData("MAT1-2-8-482")]
    [InlineData("1MAT-2-8-48210")]
    [InlineData("MAT1-2-8-48210 et plus")]
    public void Codes_mal_formes_refuses(string code)
    {
        var (p, erreur) = CodeTd.Lire(code);
        Assert.Null(p);
        Assert.False(string.IsNullOrEmpty(erreur));
    }

    [Fact]
    public void Un_chiffre_mal_entendu_est_detecte()
    {
        string code = CodeTd.Ecrire(new ParametresTd("MAT", 1, 2, 8, 4821));
        // On remplace chaque chiffre, un par un, par chacun des neuf autres : tous ces codes doivent être refusés.
        for (int position = 0; position < code.Length; position++)
        {
            if (!char.IsAsciiDigit(code[position])) continue;
            for (char autre = '0'; autre <= '9'; autre++)
            {
                if (autre == code[position]) continue;
                string faux = code[..position] + autre + code[(position + 1)..];
                Assert.Null(CodeTd.Lire(faux).Parametres);
            }
        }
    }

    [Fact]
    public void Niveau_ou_nombre_impossibles_refuses()
    {
        Assert.Null(CodeTd.Lire(CodeTd.Ecrire(new ParametresTd("MAT", 1, 4, 8, 1))).Parametres);
        Assert.Null(CodeTd.Lire(CodeTd.Ecrire(new ParametresTd("MAT", 1, 2, 0, 1))).Parametres);
    }

    // ---- Compatibilité des versions --------------------------------------------------------

    [Fact]
    public void Version_plus_recente_demande_une_mise_a_jour()
    {
        var catalogue = new CatalogueTd([Matrices]);
        var ouverture = catalogue.Ouvrir(CodeTd.Ecrire(new ParametresTd("MAT", 3, 1, 5, 12)));
        Assert.Null(ouverture.Td);
        Assert.True(ouverture.MiseAJourNecessaire);
        Assert.Contains("Mets Atome à jour", ouverture.Erreur);
    }

    [Fact]
    public void Type_inconnu_demande_une_mise_a_jour()
    {
        var ouverture = new CatalogueTd([Matrices]).Ouvrir(CodeTd.Ecrire(new ParametresTd("GRA", 1, 1, 5, 12)));
        Assert.True(ouverture.MiseAJourNecessaire);
    }

    [Fact]
    public void Version_trop_ancienne_refusee_sans_mise_a_jour()
    {
        var generateur = new GenerateurFactice(actuelle: 3, prises: [2, 3]);
        var ouverture = new CatalogueTd([generateur]).Ouvrir(CodeTd.Ecrire(new ParametresTd("FAC", 1, 1, 5, 12)));
        Assert.Null(ouverture.Td);
        Assert.False(ouverture.MiseAJourNecessaire);
        Assert.Contains("ancienne version", ouverture.Erreur);
        // Une version encore prise en charge, elle, s'ouvre.
        Assert.NotNull(new CatalogueTd([generateur]).Ouvrir(CodeTd.Ecrire(new ParametresTd("FAC", 2, 1, 5, 12))).Td);
    }

    private sealed class GenerateurFactice(int actuelle, int[] prises) : IGenerateurTd
    {
        public string Prefixe => "FAC";
        public string Nom => "Factice";
        public int VersionActuelle => actuelle;
        public IReadOnlyCollection<int> VersionsPrisesEnCharge => prises;
        public Td Generer(ParametresTd p) => new(p, CodeTd.Ecrire(p), "Factice", [], EmpreinteTd.Calculer([]));
    }

    [Fact]
    public void Ouvrir_un_code_redonne_le_meme_td()
    {
        var catalogue = new CatalogueTd([Matrices]);
        var cree = catalogue.Creer(Matrices, 2, 6, 4821);
        var ouvert = catalogue.Ouvrir(cree.Code).Td!;
        Assert.Equal(cree.Code, ouvert.Code);
        Assert.Equal(cree.Empreinte, ouvert.Empreinte);
        Assert.Equal(Resume(cree), Resume(ouvert));
    }

    // ---- Calcul matriciel ----------------------------------------------------------------

    [Fact]
    public void Exemples_du_cours()
    {
        // Somme, multiplication par un réel, produit, non-commutativité, AB = 0 sans A ni B nulles.
        Assert.Equal(M([14, -1], [13, 38]), M([5, 4], [12, 35]) + M([9, -5], [1, 3]));
        Assert.Equal(M([14, -6], [24, 12]), 2 * M([7, -3], [12, 6]));
        Assert.Equal(M([33, 11], [63, 21]), M([1, 5], [3, 9]) * M([3, 1], [6, 2]));
        Assert.Equal(M([12, 6], [17, 18]), M([1, 9], [3, 8]) * M([3, 6], [1, 0]));
        Assert.Equal(M([21, 75], [1, 9]), M([3, 6], [1, 0]) * M([1, 9], [3, 8]));
        Assert.Equal(M([0, 0], [0, 0]), M([6, 1], [6, 1]) * M([-1, 1], [6, -6]));
    }

    [Fact]
    public void Produit_rectangulaire_difference_transposee_puissance()
    {
        var a = M([1, 2, 3], [4, 5, 6]);           // 2 × 3
        var b = M([7, 8], [9, 10], [11, 12]);      // 3 × 2
        Assert.Equal(M([58, 64], [139, 154]), a * b);
        Assert.Equal(3, (b * a).Lignes);
        Assert.Equal(M([1, 4], [2, 5], [3, 6]), a.Transposee());
        Assert.Equal(M([-6, -6, -6], [-6, -6, -6]), a - M([7, 8, 9], [10, 11, 12]));
        var c = M([1, 1], [0, 1]);
        Assert.Equal(M([1, 3], [0, 1]), c.Puissance(3));
        Assert.Equal(Matrice.Identite(2), c.Puissance(0));
        Assert.Equal(c, c * Matrice.Identite(2));
    }

    [Fact]
    public void Operations_impossibles_refusees()
    {
        var a = M([1, 2, 3], [4, 5, 6]);
        Assert.Throws<InvalidOperationException>(() => a * a);
        Assert.Throws<InvalidOperationException>(() => a + a.Transposee());
        Assert.Throws<InvalidOperationException>(() => a.Puissance(2));
        Assert.True(Matrice.ProduitDefini(2, 3, 3, 4));
        Assert.False(Matrice.ProduitDefini(3, 2, 3, 2));
    }

    [Fact]
    public void Ecriture_latex()
    {
        Assert.Equal(@"\begin{pmatrix} 1 & -2 \\ 3 & 4 \end{pmatrix}", M([1, -2], [3, 4]).Latex());
        Assert.Equal("2 × 3", M([1, 2, 3], [4, 5, 6]).Dimension);
    }

    // ---- Générateur de matrices : déterminisme et justesse --------------------------------

    [Fact]
    public void Meme_code_meme_td()
    {
        var p = new ParametresTd("MAT", 1, 2, 10, 4821);
        Assert.Equal(Resume(Matrices.Generer(p)), Resume(Matrices.Generer(p)));
        Assert.NotEqual(Resume(Matrices.Generer(p)), Resume(Matrices.Generer(p with { Graine = 4822 })));
    }

    /// <summary>
    /// Valeurs figées : si ce test casse, le générateur ne produit plus les mêmes TD pour un même code.
    /// Il faut alors créer une version 2 du générateur (en gardant la 1) au lieu de changer ces valeurs.
    /// </summary>
    [Fact]
    public void Td_de_reference_fige()
    {
        var td = Matrices.Generer(new ParametresTd("MAT", 1, 2, 10, 4821));
        Assert.Equal(EmpreinteReference, td.Empreinte);
        Assert.Equal(PremierEnonceReference, td.Exercices[0].Enonce);
        Assert.Equal(ProduitReference, ((ReponseMatrice)td.Exercices[6].Questions[1].Attendu).Valeur.ToString());
    }

    private const string EmpreinteReference = "volcan-panda";
    private const string PremierEnonceReference = "On considère la matrice\n\n$$A = \\begin{pmatrix} -6 & 4 & -2 \\\\ 3 & 6 & -8 \\end{pmatrix}$$";
    private const string ProduitReference = "(18 17 ; 13 -18)";


    // ---- Exercices « hors cours » (option « Inclure les exercices hors cours ») ----------------

    [Fact]
    public void Hors_cours_decoche_aucune_transposee_meme_sur_beaucoup_de_td()
    {
        var catalogue = new CatalogueTd([Matrices]);
        for (int graine = 0; graine < 60; graine++)
            foreach (int nombre in new[] { 3, 9, 10, 12 })
            {
                var td = catalogue.Creer(Matrices, 2, nombre, graine); // option décochée par défaut
                Assert.Equal(2, td.Parametres.Version);
                Assert.DoesNotContain(td.Exercices, e => e.Titre == "Transposée");
            }
    }

    [Fact]
    public void Hors_cours_coche_garde_la_transposee_et_le_meme_td_pour_tous()
    {
        var catalogue = new CatalogueTd([Matrices]);
        var td = catalogue.Creer(Matrices, 2, 10, 4821, inclureHorsCours: true);
        Assert.Equal(1, td.Parametres.Version);
        Assert.Contains(td.Exercices, e => e.Titre == "Transposée");
        var ouvert = catalogue.Ouvrir(td.Code).Td!;
        Assert.Equal(td.Empreinte, ouvert.Empreinte);
        Assert.Equal(Resume(td), Resume(ouvert));
    }

    [Fact]
    public void Hors_cours_decoche_meme_code_meme_td()
    {
        var catalogue = new CatalogueTd([Matrices]);
        var td = catalogue.Creer(Matrices, 3, 8, 1234);
        Assert.StartsWith("MAT2-", td.Code);
        var ouvert = catalogue.Ouvrir(td.Code).Td!;
        Assert.Equal(td.Empreinte, ouvert.Empreinte);
        Assert.Equal(Resume(td), Resume(ouvert));
    }

    [Fact]
    public void Ancien_code_MAT1_reproduit_le_td_d_origine_avec_les_exercices_hors_cours()
    {
        // Même empreinte que la valeur figée de la version 1 : les anciens codes donnent le même TD qu'avant.
        var ouverture = new CatalogueTd([Matrices]).Ouvrir(CodeTd.Ecrire(new ParametresTd("MAT", 1, 2, 10, 4821)));
        Assert.Null(ouverture.Erreur);
        Assert.Equal(EmpreinteReference, ouverture.Td!.Empreinte);
        Assert.Contains(ouverture.Td.Exercices, e => e.Titre == "Transposée");
    }
    [Fact]
    public void Dix_exercices_couvrent_tous_les_genres_dans_l_ordre_du_cours()
    {
        var td = Matrices.Generer(new ParametresTd("MAT", 1, 1, 10, 1));
        Assert.Equal(["Dimension et coefficients", "Somme et différence", "Multiplication par un réel", "Transposée",
            "Matrice identité", "Le produit est-il défini ?", "Produit de deux matrices", "Le produit n'est pas commutatif",
            "Puissance d'une matrice", "En situation : stocks et prix"], td.Exercices.Select(e => e.Titre));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Tous_les_td_sont_coherents(int niveau)
    {
        // Sur beaucoup de graines : réponses cohérentes, nombres raisonnables, corrections présentes.
        for (int graine = 0; graine < 150; graine++)
        {
            var td = Matrices.Generer(new ParametresTd("MAT", 1, niveau, 12, graine));
            Assert.Equal(12, td.Exercices.Count);
            foreach (var exercice in td.Exercices)
            {
                Assert.NotEmpty(exercice.Questions);
                Assert.StartsWith("maths-sq1-", exercice.SectionId);
                for (int k = 0; k < exercice.Questions.Count; k++)
                {
                    var q = exercice.Questions[k];
                    Assert.Equal(((char)('a' + k)).ToString(), q.Lettre);
                    Assert.NotEmpty(q.Correction);
                    switch (q.Attendu)
                    {
                        case ReponseMatrice m:
                            Assert.InRange(m.Valeur.Lignes, 1, 4);
                            Assert.All(m.Valeur.Coefficients(), c => Assert.InRange(c, -200, 700));
                            break;
                        case ReponseChoix c:
                            Assert.InRange(c.Bonne, 0, c.Choix.Count - 1);
                            Assert.Equal(c.Choix.Count, c.Choix.Distinct().Count());
                            break;
                    }
                }
            }
        }
    }

    [Fact]
    public void Les_reponses_des_produits_sont_justes()
    {
        // On relit les matrices de l'énoncé et on refait le calcul indépendamment.
        for (int graine = 0; graine < 50; graine++)
        {
            var td = Matrices.Generer(new ParametresTd("MAT", 1, 3, 10, graine));
            var produit = td.Exercices.Single(e => e.Titre == "Produit de deux matrices");
            var matrices = LireMatrices(produit.Enonce);
            Assert.Equal(matrices[0] * matrices[1], ((ReponseMatrice)produit.Questions[^1].Attendu).Valeur);

            var commutatif = td.Exercices.Single(e => e.Titre == "Le produit n'est pas commutatif");
            var ab = LireMatrices(commutatif.Enonce);
            Assert.NotEqual(ab[0] * ab[1], ab[1] * ab[0]);
        }
    }

    /// <summary>Relit les matrices « \begin{pmatrix} … \end{pmatrix} » d'un énoncé.</summary>
    private static List<Matrice> LireMatrices(string texte) =>
        System.Text.RegularExpressions.Regex.Matches(texte, @"\\begin\{pmatrix\}(.*?)\\end\{pmatrix\}")
            .Select(m => new Matrice(m.Groups[1].Value.Split(@"\\")
                .Select(ligne => (IReadOnlyList<int>)ligne.Split('&').Select(x => int.Parse(x.Trim())).ToList()).ToList()))
            .ToList();

    /// <summary>Tout le contenu d'un TD en texte, pour comparer deux générations.</summary>
    private static string Resume(Td td) => string.Join("\n", td.Exercices.Select(e =>
        e.Enonce + "|" + string.Join("|", e.Questions.Select(q => q.Intitule + "=" + q.Attendu.Texte + ":" + string.Join("/", q.Correction)))));

    // ---- Générateur « séance » -------------------------------------------------------------

    private static ContenuSeance SeanceExemple()
    {
        var cartes = new List<Carte>
        {
            new("p-c3", "p-s1", TypeCarte.Qcm, 1, new ContenuCarte { Question = "Q3 ?", Choix = ["bon", "faux1", "faux2"], BonneReponse = 0, Explication = "car" }),
            new("p-c1", "p-s1", TypeCarte.Exercice, 2, new ContenuCarte { Enonce = "E1", Etapes = ["e1", "e2"], Reponse = "R1" }),
            new("p-c2", "p-s2", TypeCarte.Question, 1, new ContenuCarte { Recto = "?", Verso = "!" }),
            new("p-c4", "p-s2", TypeCarte.Qcm, 3, new ContenuCarte { Question = "Q4 ?", Choix = ["x", "y"], BonneReponse = 1 }),
        };
        return new ContenuSeance("Séance test", cartes, new Dictionary<string, string> { ["p-s1"] = "Section 1" });
    }

    [Fact]
    public void Seance_ne_garde_que_exercices_et_qcm_du_niveau()
    {
        var generateur = new GenerateurSeance(id => id == "pack-test" ? SeanceExemple() : null);
        var td = generateur.Generer(new ParametresTd("SEA", 1, 2, 10, 5, "pack-test"));
        Assert.Equal(2, td.Exercices.Count); // la carte « question » et la QCM de difficulté 3 sont écartées
        Assert.Contains(td.Exercices, e => e.Questions[0].Attendu is ReponseLibre);
        var qcm = td.Exercices.Single(e => e.Titre == "QCM");
        var choix = (ReponseChoix)qcm.Questions[0].Attendu;
        Assert.Equal("bon", choix.Choix[choix.Bonne]);
    }

    [Fact]
    public void Seance_independante_de_l_ordre_des_cartes_en_base()
    {
        var contenu = SeanceExemple();
        var inverse = contenu with { Cartes = contenu.Cartes.Reverse().ToList() };
        var p = new ParametresTd("SEA", 1, 3, 3, 99, "x");
        Assert.Equal(Resume(new GenerateurSeance(_ => contenu).Generer(p)), Resume(new GenerateurSeance(_ => inverse).Generer(p)));
    }

    [Fact]
    public void Seance_absente_message_clair()
    {
        var catalogue = new CatalogueTd([Matrices, new GenerateurSeance(_ => null)]);
        var ouverture = catalogue.Ouvrir(CodeTd.Ecrire(new ParametresTd("SEA", 1, 2, 5, 1, "maths-sq1-s03")));
        Assert.Null(ouverture.Td);
        Assert.Contains("n'est pas dans ta bibliothèque", ouverture.Erreur);
    }

    [Fact]
    public void Contenu_different_empreinte_differente()
    {
        var contenu = SeanceExemple();
        var modifie = contenu with { Cartes = contenu.Cartes.Select(c => c.Id == "p-c1" ? c with { Contenu = c.Contenu with { Reponse = "R1 corrigée" } } : c).ToList() };
        var p = new ParametresTd("SEA", 1, 3, 3, 99, "x");
        Assert.NotEqual(new GenerateurSeance(_ => contenu).Generer(p).Empreinte, new GenerateurSeance(_ => modifie).Generer(p).Empreinte);
    }

    // ---- Vérification de la saisie ---------------------------------------------------------

    [Theory]
    [InlineData("12", 12)]
    [InlineData(" -3 ", -3)]
    [InlineData("\u22127", -7)] // signe moins typographique
    [InlineData("+4", 4)]
    [InlineData("1 000", 1000)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("2,5", null)]
    [InlineData("--2", null)]
    public void Lecture_d_un_entier(string saisie, int? attendu) => Assert.Equal(attendu, VerificationSaisie.LireEntier(saisie));

    [Fact]
    public void Verification_case_par_case()
    {
        var attendue = M([1, -2], [3, 4]);
        var resultat = VerificationSaisie.Matrice(attendue, ["1", "-2", "5", ""]);
        Assert.False(resultat.Juste);
        Assert.False(resultat.Complete);
        Assert.Equal([true, true, false, false], resultat.Cases);
        Assert.True(VerificationSaisie.Matrice(attendue, ["1", "\u22122", " 3", "4"]).Juste);
        Assert.Throws<ArgumentException>(() => VerificationSaisie.Matrice(attendue, ["1"]));
    }

    [Fact]
    public void Verification_nombre_et_choix()
    {
        Assert.True(VerificationSaisie.Nombre(-5, "-5").Juste);
        Assert.False(VerificationSaisie.Nombre(-5, "5").Juste);
        Assert.True(VerificationSaisie.Choix(1, 1).Juste);
        Assert.False(VerificationSaisie.Choix(1, null).Complete);
    }

    [Fact]
    public void Caracteres_autorises_dans_une_case()
    {
        Assert.True(VerificationSaisie.CaractereAutorise('7'));
        Assert.True(VerificationSaisie.CaractereAutorise('-'));
        Assert.False(VerificationSaisie.CaractereAutorise('a'));
        Assert.True(VerificationSaisie.CaractereAutorise(','));
        Assert.True(VerificationSaisie.CaractereAutorise('.'));
    }
    /// <summary>
    /// Saisie d'un négatif (bug signalé : le « - » disparaissait) : les étapes intermédiaires de la frappe
    /// sont acceptées telles quelles, et seule la vérification convertit (virgule ou point).
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData("-0,")]
    [InlineData("3,")]
    [InlineData("-7")]
    [InlineData("-12.5")]
    public void Saisie_intermediaire_acceptee_pendant_la_frappe(string texte) => Assert.True(VerificationSaisie.SaisieAutorisee(texte));

    [Theory]
    [InlineData("-", null)]
    [InlineData("3,", null)]
    [InlineData("-7", -7.0)]
    [InlineData("-0,5", -0.5)]
    [InlineData("-0.5", -0.5)]
    [InlineData("4,0", 4.0)]
    public void Lecture_d_un_nombre_a_la_verification(string saisie, double? attendu) =>
        Assert.Equal((decimal?)attendu, VerificationSaisie.LireNombre(saisie));

    [Fact]
    public void Coefficient_negatif_juste_dans_la_grille()
    {
        var attendue = M([1, 2], [3, -7]);
        Assert.True(VerificationSaisie.Matrice(attendue, ["1", "2", "3", "-7"]).Juste);
        Assert.True(VerificationSaisie.Matrice(attendue, ["1", "2,0", "3", "-7"]).Juste);
        Assert.False(VerificationSaisie.Matrice(attendue, ["1", "2", "3", "-"]).Complete);
        Assert.False(VerificationSaisie.SaisieAutorisee("7a"));
    }

    // ---- Récap et XP ------------------------------------------------------------------------

    [Fact]
    public void Recap_et_xp_jamais_punitifs()
    {
        IssueExercice[] issues = [IssueExercice.PremierCoup, IssueExercice.AvecIndice, IssueExercice.PasEncore, IssueExercice.AvecIndice];
        Assert.Equal("J'ai fait 3/4, dont 2 avec un indice.", RecapTd.Phrase(issues));
        Assert.Equal("J'ai fait 0/1.", RecapTd.Phrase([IssueExercice.PasEncore]));
        Assert.Equal(6 + 4 + 2 + 4, RecapTd.Xp(issues));
        Assert.All(Enum.GetValues<IssueExercice>(), i => Assert.True(RecapTd.Xp(i) > 0));
    }
}
