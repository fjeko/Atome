using static System.FormattableString; // Invariant($"…") : nombres écrits pareil quelle que soit la langue de Windows

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « Matrices » (SQ1 Calcul matriciel). Les énoncés sont générés, jamais copiés du cours ;
/// le vocabulaire et les notations suivent ceux du cours : dimension n × p, coefficient a_ij,
/// k.A pour la multiplication par un réel, AB pour le produit, I_n pour l'identité.
/// </summary>
/// <remarks>
/// Version 1 du générateur. Toute modification qui change un TD généré (ordre des tirages, bornes,
/// textes…) impose de passer à la version 2 et de garder la version 1 telle quelle tant qu'on veut
/// accepter ses codes. Les tests de déterminisme le rappellent.
/// </remarks>
public sealed class GenerateurMatrices : IGenerateurTd
{
    public string Prefixe => "MAT";
    public string Nom => "Matrices (SQ1 Calcul matriciel)";
    public int VersionActuelle => 1;
    public IReadOnlyCollection<int> VersionsPrisesEnCharge { get; } = [1];

    /// <summary>Noms des niveaux 1, 2 et 3.</summary>
    public static IReadOnlyList<string> NomsNiveaux { get; } = ["Découverte", "Entraînement", "Comme à l'examen"];

    /// <summary>Les sortes d'exercices, dans l'ordre du cours (le TD les présente dans cet ordre).</summary>
    public enum Genre
    {
        Dimension, SommeDifference, Reel, Transposee, Identite, ProduitDefini, Produit, NonCommutativite, Puissance, Contexte,
    }

    // Sections du pack de maths SQ1 vers lesquelles mène « Revoir la notion ».
    private const string SectionDimension = "maths-sq1-s01-s02";
    private const string SectionParticulieres = "maths-sq1-s01-s03";
    private const string SectionSomme = "maths-sq1-s01-s05";
    private const string SectionReel = "maths-sq1-s01-s06";
    private const string SectionProduit = "maths-sq1-s01-s07";
    private const string SectionPieges = "maths-sq1-s01-s08";
    private const string SectionPuissance = "maths-sq1-s01-s09";
    private const string SectionProduitImpossible = "maths-sq1-s03-s02";
    private const string SectionUsine = "maths-sq1-s03-s07";

    public Td Generer(ParametresTd p)
    {
        if (!VersionsPrisesEnCharge.Contains(p.Version))
            throw new TdImpossibleException("Version du générateur de matrices inconnue : mets Atome à jour.");

        var r = new GenerateurAleatoire(GraineInterne(p));
        var exercices = ChoisirGenres(r, p.Nombre).Select(g => Creer(g, r, p.Niveau)).ToList();
        return new Td(p, CodeTd.Ecrire(p), $"Matrices · {NomsNiveaux[p.Niveau - 1].ToLowerInvariant()}",
            exercices, EmpreinteTd.Calculer(exercices));
    }

    /// <summary>
    /// Graine du générateur : la graine du code, le niveau et le nombre d'exercices réunis sur 64 bits,
    /// pour que deux TD de même graine mais de niveaux différents n'aient pas les mêmes tirages.
    /// </summary>
    private static ulong GraineInterne(ParametresTd p) => ((ulong)p.Graine << 16) | ((ulong)p.Niveau << 8) | (ulong)p.Nombre;

    /// <summary>
    /// Choisit les sortes d'exercices : chaque tour complet passe par toutes les sortes, dans l'ordre du cours ;
    /// le dernier tour, incomplet, en tire quelques-unes au hasard (toujours remises dans l'ordre du cours).
    /// </summary>
    public static List<Genre> ChoisirGenres(GenerateurAleatoire r, int nombre)
    {
        var tous = Enum.GetValues<Genre>();
        var choisis = new List<Genre>();
        while (nombre - choisis.Count >= tous.Length) choisis.AddRange(tous);
        int reste = nombre - choisis.Count;
        if (reste > 0)
        {
            var tirage = tous.ToList();
            r.Melanger(tirage);
            choisis.AddRange(tirage.Take(reste).Order());
        }
        return choisis;
    }

    private static ExerciceTd Creer(Genre genre, GenerateurAleatoire r, int niveau) => genre switch
    {
        Genre.Dimension => Dimension(r, niveau),
        Genre.SommeDifference => SommeDifference(r, niveau),
        Genre.Reel => Reel(r, niveau),
        Genre.Transposee => Transposee(r, niveau),
        Genre.Identite => Identite(r, niveau),
        Genre.ProduitDefini => ProduitDefini(r, niveau),
        Genre.Produit => Produit(r, niveau),
        Genre.NonCommutativite => NonCommutativite(r, niveau),
        Genre.Puissance => Puissance(r, niveau),
        _ => Contexte(r, niveau),
    };

    // ==== Les exercices ======================================================================

    private static ExerciceTd Dimension(GenerateurAleatoire r, int niveau)
    {
        int n, p;
        do
        {
            n = r.Entier(1, niveau == 1 ? 3 : 4);
            p = r.Entier(niveau == 3 ? 2 : 1, 4);
        } while (n == p); // une matrice non carrée : le piège « lignes / colonnes » a du sens
        var a = Aleatoire(r, n, p, niveau == 1 ? 0 : -9, 9);

        // Deux coefficients différents à lire.
        int i1 = r.Entier(0, n - 1), j1 = r.Entier(0, p - 1);
        int i2, j2;
        do { i2 = r.Entier(0, n - 1); j2 = r.Entier(0, p - 1); } while (i2 == i1 && j2 == j1 && n * p > 1);

        return new ExerciceTd("Dimension et coefficients",
            Invariant($"On considère la matrice\n\n$$A = {a.Latex()}$$"),
            [
                new("a", "Quelle est la dimension de $A$ ?", "dimension de A",
                    ChoixMelanges(r, Dim(n, p), [Dim(p, n), Dim(n, n), Dim(p, p), Dim(n + 1, p), Dim(n, p + 1)]),
                    [
                        "Une matrice $n \\times p$ a $n$ **lignes** et $p$ **colonnes** : on compte toujours les lignes d'abord.",
                        Invariant($"$A$ a {n} ligne{S(n)} et {p} colonne{S(p)} : sa dimension est ${n} \\times {p}$."),
                    ]),
                new("b", Invariant($"Que vaut $a_{{{i1 + 1}{j1 + 1}}}$ ?"), Invariant($"a{i1 + 1}{j1 + 1}"),
                    new ReponseNombre(a[i1, j1]), EtapesCoefficient(a, i1, j1)),
                new("c", Invariant($"Que vaut $a_{{{i2 + 1}{j2 + 1}}}$ ?"), Invariant($"a{i2 + 1}{j2 + 1}"),
                    new ReponseNombre(a[i2, j2]), EtapesCoefficient(a, i2, j2)),
            ],
            "La dimension $n \\times p$ se lit **lignes d'abord, colonnes ensuite**. Le coefficient $a_{ij}$ est sur la ligne $i$ et dans la colonne $j$ (toujours la ligne d'abord).",
            SectionDimension);
    }

    private static List<string> EtapesCoefficient(Matrice a, int i, int j) =>
    [
        Invariant($"$a_{{{i + 1}{j + 1}}}$ : ligne {i + 1}, puis colonne {j + 1}."),
        Invariant($"La ligne {i + 1} de $A$ est $({string.Join(" ; ", Enumerable.Range(0, a.Colonnes).Select(k => a[i, k]))})$ ; son coefficient n°{j + 1} est ${a[i, j]}$. Donc $a_{{{i + 1}{j + 1}}} = {a[i, j]}$."),
    ];

    private static ExerciceTd SommeDifference(GenerateurAleatoire r, int niveau)
    {
        var (n, p) = Dimensions(r, niveau);
        int min = niveau == 1 ? 0 : -9;
        var a = Aleatoire(r, n, p, min, 9);
        var b = Aleatoire(r, n, p, min, 9);

        var questions = new List<QuestionTd>
        {
            new("a", "Calculer $A + B$.", "A + B", new ReponseMatrice(a + b), EtapesTermeATerme(a, b, "+", "A + B")),
            new("b", "Calculer $A - B$.", "A − B", new ReponseMatrice(a - b), EtapesTermeATerme(a, b, "-", "A - B")),
        };
        if (niveau == 3)
        {
            var deuxA = 2 * a;
            questions.Add(new("c", "Calculer $2.A - B$.", "2.A − B", new ReponseMatrice(deuxA - b),
                [
                    Invariant($"On calcule d'abord $2.A = {deuxA.Latex()}$ (chaque coefficient multiplié par 2)."),
                    .. EtapesTermeATerme(deuxA, b, "-", "2.A - B"),
                ]));
        }

        return new ExerciceTd("Somme et différence",
            Invariant($"On donne\n\n$$A = {a.Latex()} \\qquad B = {b.Latex()}$$"),
            questions,
            "On ne peut additionner ou soustraire que des matrices **de même dimension**. On opère coefficient par coefficient, à la même place : $c_{ij} = a_{ij} + b_{ij}$. Attention aux signes : $3 - (-2) = 5$.",
            SectionSomme);
    }

    private static ExerciceTd Reel(GenerateurAleatoire r, int niveau)
    {
        var (n, p) = Dimensions(r, niveau);
        var a = Aleatoire(r, n, p, niveau == 1 ? 0 : -6, 9);
        int[] reels = niveau == 1 ? [2, 3, 4, 5] : [2, 3, 4, -1, -2, -3];
        int k = r.Choisir(reels);

        var questions = new List<QuestionTd>
        {
            new("a", Invariant($"Calculer ${k}.A$."), Invariant($"{k}.A"), new ReponseMatrice(k * a), EtapesReel(a, k)),
        };
        if (niveau == 1)
        {
            int k2;
            do k2 = r.Choisir(reels); while (k2 == k);
            questions.Add(new("b", Invariant($"Calculer ${k2}.A$."), Invariant($"{k2}.A"), new ReponseMatrice(k2 * a), EtapesReel(a, k2)));
        }
        else
        {
            var b = Aleatoire(r, n, p, -6, 9);
            var ka = k * a;
            questions.Add(new("b", Invariant($"Avec $B = {b.Latex()}$, calculer ${k}.A + B$."), Invariant($"{k}.A + B"),
                new ReponseMatrice(ka + b),
                [
                    Invariant($"On reprend ${k}.A = {ka.Latex()}$ (question a)."),
                    .. EtapesTermeATerme(ka, b, "+", Invariant($"{k}.A + B")),
                ]));
        }

        return new ExerciceTd("Multiplication par un réel",
            Invariant($"On considère\n\n$$A = {a.Latex()}$$"),
            questions,
            "Pour calculer $k.A$, on multiplie **chaque** coefficient de $A$ par $k$ : $k.A = (k.a_{ij})$. On note avec un point pour ne pas confondre avec le produit de deux matrices.",
            SectionReel);
    }

    private static ExerciceTd Transposee(GenerateurAleatoire r, int niveau)
    {
        int n, p;
        do
        {
            n = r.Entier(niveau == 3 ? 2 : 1, niveau == 1 ? 3 : 4);
            p = r.Entier(2, niveau == 1 ? 3 : 4);
        } while (n == p);
        var a = Aleatoire(r, n, p, niveau == 1 ? 0 : -9, 9);
        var t = a.Transposee();

        var etapes = new List<string> { "Les lignes de $A$ deviennent les colonnes de ${}^t\\!A$, dans le même ordre." };
        for (int i = 0; i < n; i++)
            etapes.Add(Invariant($"Ligne {i + 1} de $A$ : $({string.Join(" ; ", Enumerable.Range(0, p).Select(j => a[i, j]))})$ → colonne {i + 1} de ${{}}^t\\!A$."));
        etapes.Add(Invariant($"D'où ${{}}^t\\!A = {t.Latex()}$."));

        return new ExerciceTd("Transposée",
            Invariant($"La **transposée** d'une matrice $A$, notée ${{}}^t\\!A$, s'obtient en écrivant les lignes de $A$ en colonnes. On considère\n\n$$A = {a.Latex()}$$"),
            [
                new("a", "Quelle est la dimension de ${}^t\\!A$ ?", "dimension de tA",
                    ChoixMelanges(r, Dim(p, n), [Dim(n, p), Dim(n, n), Dim(p, p), Dim(p + 1, n)]),
                    [Invariant($"$A$ est ${n} \\times {p}$ ; en échangeant lignes et colonnes, ${{}}^t\\!A$ est ${p} \\times {n}$.")]),
                new("b", "Écrire ${}^t\\!A$.", "tA", new ReponseMatrice(t), etapes),
            ],
            "La transposée d'une matrice $n \\times p$ est une matrice $p \\times n$ : la ligne 1 devient la colonne 1, la ligne 2 la colonne 2… Le coefficient $a_{ij}$ passe en position $(j, i)$. (Notion en plus du cours SQ1, très utilisée en informatique.)",
            SectionDimension);
    }

    private static ExerciceTd Identite(GenerateurAleatoire r, int niveau)
    {
        int n = niveau switch { 1 => 2, 2 => r.Entier(2, 3), _ => 3 };
        var a = Aleatoire(r, n, n, niveau == 1 ? 0 : -9, 9);
        var id = Matrice.Identite(n);

        var questions = new List<QuestionTd>
        {
            new("a", Invariant($"Écrire la matrice identité $I_{n}$."), Invariant($"I{n}"), new ReponseMatrice(id),
                [Invariant($"$I_{n}$ est carrée d'ordre {n} : des 1 sur la diagonale, des 0 partout ailleurs. $I_{n} = {id.Latex()}$.")]),
            new("b", Invariant($"Calculer $A \\times I_{n}$."), Invariant($"A × I{n}"), new ReponseMatrice(a),
                [
                    Invariant($"Ligne × colonne, par exemple $c_{{11}} = a_{{11}} \\times 1 + a_{{12}} \\times 0{(n == 3 ? " + a_{13} \\times 0" : "")} = a_{{11}} = {a[0, 0]}$."),
                    "Dans chaque calcul, un seul terme est multiplié par 1, les autres par 0 : on retrouve le coefficient de $A$.",
                    Invariant($"$I_{n}$ est l'élément neutre du produit : $A \\times I_{n} = A = {a.Latex()}$."),
                ]),
        };
        if (niveau >= 2)
        {
            var somme = a + 2 * id;
            questions.Add(new("c", Invariant($"Calculer $A + 2.I_{n}$."), Invariant($"A + 2.I{n}"), new ReponseMatrice(somme),
                [
                    Invariant($"$2.I_{n}$ a des 2 sur la diagonale et des 0 ailleurs."),
                    "On ajoute donc 2 aux coefficients de la diagonale de $A$ ($a_{11}$, $a_{22}$…), les autres ne changent pas.",
                    Invariant($"$A + 2.I_{n} = {somme.Latex()}$."),
                ]));
        }

        return new ExerciceTd("Matrice identité",
            Invariant($"On considère\n\n$$A = {a.Latex()}$$"),
            questions,
            "$I_n$ est la matrice carrée d'ordre $n$ avec des 1 sur la diagonale et des 0 ailleurs. C'est l'élément neutre du produit : $A \\times I_n = I_n \\times A = A$.",
            SectionParticulieres);
    }

    private static ExerciceTd ProduitDefini(GenerateurAleatoire r, int niveau)
    {
        int max = niveau == 1 ? 3 : 4;
        string[] noms = ["A", "B", "C", "D"];
        var dims = new (int L, int C)[4];
        var paires = new List<(int X, int Y)>();
        // On recommence tant qu'on n'a pas au moins un produit possible et un impossible : sinon l'exercice n'apprend rien.
        do
        {
            for (int k = 0; k < 4; k++) dims[k] = (r.Entier(1, max), r.Entier(1, max));
            paires.Clear();
            var toutes = new List<(int, int)>();
            for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                if (x != y) toutes.Add((x, y));
            r.Melanger(toutes);
            paires.AddRange(toutes.Take(niveau == 1 ? 3 : 4));
        } while (!paires.Any(q => Defini(q)) || paires.All(q => Defini(q)));

        bool Defini((int X, int Y) q) => dims[q.X].C == dims[q.Y].L;

        var lettres = "abcd";
        var questions = new List<QuestionTd>();
        for (int k = 0; k < paires.Count; k++)
        {
            var (x, y) = paires[k];
            var (n, p) = dims[x];
            var (p2, q) = dims[y];
            string produit = noms[x] + noms[y];
            const string impossible = "Non, il n'existe pas";
            string bonne = Defini(paires[k]) ? Invariant($"Oui, de dimension {n} × {q}") : impossible;
            var distracteurs = new List<string>
            {
                impossible, Invariant($"Oui, de dimension {n} × {q}"), Invariant($"Oui, de dimension {p} × {p2}"),
                Invariant($"Oui, de dimension {q} × {n}"), Invariant($"Oui, de dimension {n} × {p}"), Invariant($"Oui, de dimension {p2} × {q}"),
            };
            string explication = Defini(paires[k])
                ? Invariant($"${noms[x]}$ est ${n} \\times \\mathbf{{{p}}}$ et ${noms[y]}$ est $\\mathbf{{{p2}}} \\times {q}$ : colonnes de ${noms[x]}$ = lignes de ${noms[y]}$ = {p}. Le produit existe et il est de dimension ${n} \\times {q}$ (lignes de ${noms[x]}$, colonnes de ${noms[y]}$).")
                : Invariant($"${noms[x]}$ est ${n} \\times \\mathbf{{{p}}}$ et ${noms[y]}$ est $\\mathbf{{{p2}}} \\times {q}$ : {p} colonne{S(p)} pour ${noms[x]}$ mais {p2} ligne{S(p2)} pour ${noms[y]}$. Le produit ${produit}$ n'existe pas.");
            questions.Add(new(lettres[k].ToString(), Invariant($"Le produit ${produit}$ existe-t-il ? Si oui, quelle est sa dimension ?"),
                produit, ChoixMelanges(r, bonne, distracteurs), [explication]));
        }

        var description = string.Join(", ", Enumerable.Range(0, 4).Select(k => Invariant($"${noms[k]}$ de dimension ${dims[k].L} \\times {dims[k].C}$")));
        return new ExerciceTd("Le produit est-il défini ?",
            $"On considère quatre matrices : {description}.",
            questions,
            "Le produit $MT$ existe seulement si le nombre de **colonnes de la première** est égal au nombre de **lignes de la seconde**. Une matrice $n \\times p$ fois une matrice $p \\times q$ donne une matrice $n \\times q$.",
            SectionProduitImpossible);
    }

    private static ExerciceTd Produit(GenerateurAleatoire r, int niveau)
    {
        Matrice a, b;
        switch (niveau)
        {
            case 1:
                a = Aleatoire(r, 2, 2, 0, 5);
                b = Aleatoire(r, 2, 2, 0, 5);
                break;
            case 2:
                a = Aleatoire(r, 2, 3, -3, 5);
                b = Aleatoire(r, 3, 2, -3, 5);
                break;
            default:
                a = Aleatoire(r, 3, 3, -2, 4);
                b = Aleatoire(r, 3, 3, -2, 4);
                break;
        }
        var ab = a * b;
        var questions = new List<QuestionTd>();
        // La dimension du produit n'a d'intérêt que pour des matrices rectangulaires (niveau 2).
        if (niveau == 2)
        {
            questions.Add(new("a", "Quelle est la dimension de $AB$ ?", "dimension de AB",
                ChoixMelanges(r, Dim(a.Lignes, b.Colonnes), [Dim(a.Colonnes, b.Lignes), Dim(b.Colonnes, a.Lignes), Dim(a.Lignes, a.Colonnes), Dim(b.Lignes, b.Colonnes), Dim(a.Lignes + 1, b.Colonnes)]),
                [Invariant($"$A$ est ${a.Lignes} \\times {a.Colonnes}$ et $B$ est ${b.Lignes} \\times {b.Colonnes}$ : colonnes de $A$ = lignes de $B$ = {a.Colonnes}, le produit existe. Il a les lignes de $A$ et les colonnes de $B$ : ${a.Lignes} \\times {b.Colonnes}$.")]));
        }
        questions.Add(new(questions.Count == 0 ? "a" : "b", "Calculer $AB$.", "AB", new ReponseMatrice(ab), EtapesProduit(a, b, "A", "B", "a", "b")));
        return new ExerciceTd("Produit de deux matrices",
            Invariant($"On donne\n\n$$A = {a.Latex()} \\qquad B = {b.Latex()}$$"),
            questions,
            "Le coefficient $c_{ij}$ de $AB$ s'obtient avec la **ligne $i$ de $A$** et la **colonne $j$ de $B$** : on multiplie terme à terme, puis on additionne. Par exemple $c_{12} = a_{11}b_{12} + a_{12}b_{22} + \\dots$",
            SectionProduit);
    }

    private static ExerciceTd NonCommutativite(GenerateurAleatoire r, int niveau)
    {
        int min = niveau switch { 1 => 0, 2 => -3, _ => -5 };
        int max = niveau == 3 ? 6 : 5;
        Matrice a, b;
        do
        {
            a = Aleatoire(r, 2, 2, min, max);
            b = Aleatoire(r, 2, 2, min, max);
        } while (a * b == b * a); // on veut un exemple où l'ordre change le résultat

        var ab = a * b;
        var ba = b * a;
        int i0 = 0, j0 = 0;
        while (ab[i0, j0] == ba[i0, j0]) { j0++; if (j0 == 2) { j0 = 0; i0++; } }

        return new ExerciceTd("Le produit n'est pas commutatif",
            Invariant($"On donne\n\n$$A = {a.Latex()} \\qquad B = {b.Latex()}$$"),
            [
                new("a", "Calculer $AB$.", "AB", new ReponseMatrice(ab), EtapesProduit(a, b, "A", "B", "a", "b")),
                new("b", "Calculer $BA$.", "BA", new ReponseMatrice(ba), EtapesProduit(b, a, "B", "A", "b", "a")),
                new("c", "A-t-on $AB = BA$ ?", "AB = BA ?", new ReponseChoix(["Oui", "Non"], 1),
                    [
                        Invariant($"En position $({i0 + 1}, {j0 + 1})$ : ${ab[i0, j0]}$ dans $AB$, mais ${ba[i0, j0]}$ dans $BA$. Donc $AB \\ne BA$."),
                        "Le produit de matrices **n'est pas commutatif** : en général $AB \\ne BA$, il faut toujours respecter l'ordre écrit.",
                    ]),
            ],
            "Calcule les deux produits séparément, en respectant l'ordre : pour $AB$, lignes de $A$ × colonnes de $B$ ; pour $BA$, lignes de $B$ × colonnes de $A$.",
            SectionPieges);
    }

    private static ExerciceTd Puissance(GenerateurAleatoire r, int niveau)
    {
        var a = niveau switch
        {
            1 => Aleatoire(r, 2, 2, 0, 3),
            2 => Aleatoire(r, 2, 2, -2, 3),
            _ => Aleatoire(r, 3, 3, -1, 2),
        };
        int n = a.Lignes;
        var a2 = a * a;
        var questions = new List<QuestionTd>
        {
            new("a", "Calculer $A^2$.", "A²", new ReponseMatrice(a2),
                ["$A^2 = A \\times A$ : c'est un produit de matrices, ligne × colonne (et non les coefficients au carré).",
                 .. EtapesProduit(a, a, "A", "A", null, null, "A^2")]),
        };
        if (niveau == 2)
        {
            var a3 = a2 * a;
            questions.Add(new("b", "Calculer $A^3$.", "A³", new ReponseMatrice(a3),
                [Invariant($"$A^3 = A^2 \\times A$, avec $A^2 = {a2.Latex()}$ (question a)."),
                 .. EtapesProduit(a2, a, "A^2", "A", null, null, "A^3")]));
        }
        else
        {
            questions.Add(new("b", "Que vaut $A^0$ ?", "A⁰", new ReponseMatrice(Matrice.Identite(n)),
                [Invariant($"Par convention, $A^0 = I_{n}$, la matrice identité : $A^0 = {Matrice.Identite(n).Latex()}$.")]));
        }

        return new ExerciceTd("Puissance d'une matrice",
            Invariant($"On considère la matrice carrée\n\n$$A = {a.Latex()}$$"),
            questions,
            "$A^2 = A \\times A$ et $A^3 = A^2 \\times A$ : ce sont des **produits** de matrices (ligne × colonne). Par convention, $A^0 = I_n$ et $A^1 = A$.",
            SectionPuissance);
    }

    private static ExerciceTd Contexte(GenerateurAleatoire r, int niveau)
    {
        string[] boutiques = ["Lyon", "Lille", "Nantes"];
        string[] articles = ["cahiers", "stylos", "classeurs"];
        string[] unitaires = ["cahier", "stylo", "classeur"];
        int nb = niveau == 3 ? 3 : 2;          // nombre de boutiques
        int na = niveau == 1 ? 2 : 3;          // nombre d'articles

        // Stocks : on veut des valeurs de stock toutes différentes, pour que la question « laquelle ? » ait une réponse.
        Matrice q, prix, valeur;
        do
        {
            q = Aleatoire(r, nb, na, niveau == 1 ? 1 : 2, niveau == 1 ? 9 : 20);
            if (niveau == 3)
            {
                // Deux colonnes : prix d'achat, puis prix de vente (plus élevé).
                var achat = Enumerable.Range(0, na).Select(_ => r.Entier(1, 6)).ToArray();
                var vente = achat.Select(x => x + r.Entier(1, 4)).ToArray();
                prix = Matrice.Creer(na, 2, (i, j) => j == 0 ? achat[i] : vente[i]);
            }
            else prix = Aleatoire(r, na, 1, 1, niveau == 1 ? 5 : 9);
            valeur = q * prix;
        } while (niveau < 3 && Enumerable.Range(0, nb).Select(i => valeur[i, 0]).Distinct().Count() < nb);

        var tableau = new System.Text.StringBuilder("| Stock | " + string.Join(" | ", articles.Take(na)) + " |\n| --- |" + string.Concat(Enumerable.Repeat(" --- |", na)) + "\n");
        for (int i = 0; i < nb; i++)
            tableau.Append(Invariant($"| {boutiques[i]} | {string.Join(" | ", Enumerable.Range(0, na).Select(j => q[i, j]))} |\n"));

        string listePrix = niveau == 3
            ? string.Join(", ", Enumerable.Range(0, na).Select(j => Invariant($"{unitaires[j]} : acheté {prix[j, 0]} €, vendu {prix[j, 1]} €")))
            : string.Join(", ", Enumerable.Range(0, na).Select(j => Invariant($"{unitaires[j]} {prix[j, 0]} €")));
        string nomPrix = niveau == 3 ? "la matrice des prix (une ligne par article ; colonne 1 : achat, colonne 2 : vente)" : "la matrice colonne des prix unitaires";

        string enonce = Invariant($"Une petite chaîne de papeteries a {nb} boutiques. Le tableau donne le stock de chaque article.\n\n{tableau}\nPrix unitaires : {listePrix}.\n\n") +
                        Invariant($"On note $Q = {q.Latex()}$ la matrice des stocks et $P = {prix.Latex()}$ {nomPrix}.");

        var etapesProduit = new List<string>
        {
            Invariant($"$Q$ est ${nb} \\times {na}$ et $P$ est ${na} \\times {prix.Colonnes}$ : le produit $QP$ existe et il est ${nb} \\times {prix.Colonnes}$."),
        };
        for (int i = 0; i < nb; i++)
        for (int j = 0; j < prix.Colonnes; j++)
        {
            string termes = string.Join(" + ", Enumerable.Range(0, na).Select(k => Invariant($"{q[i, k]} \\times {prix[k, j]}")));
            string sens = prix.Colonnes == 1 ? "valeur du stock" : j == 0 ? "coût d'achat du stock" : "prix de vente du stock";
            etapesProduit.Add(Invariant($"{boutiques[i]} ({sens}) : $c_{{{i + 1}{j + 1}}} = {termes} = {valeur[i, j]}$ €."));
        }
        etapesProduit.Add(Invariant($"D'où $QP = {valeur.Latex()}$."));

        var questions = new List<QuestionTd>();
        char lettre = 'a';
        if (niveau >= 2)
        {
            questions.Add(new((lettre++).ToString(), "Quelle est la dimension du produit $QP$ ?", "dimension de QP",
                ChoixMelanges(r, Dim(nb, prix.Colonnes), [Dim(na, na), Dim(prix.Colonnes, nb), Dim(nb, na), Dim(na, prix.Colonnes)]),
                [Invariant($"$Q$ est ${nb} \\times {na}$ et $P$ est ${na} \\times {prix.Colonnes}$ : on garde les lignes de $Q$ et les colonnes de $P$, soit ${nb} \\times {prix.Colonnes}$.")]));
        }
        questions.Add(new((lettre++).ToString(), "Calculer $QP$.", "QP", new ReponseMatrice(valeur), etapesProduit));

        if (niveau == 3)
        {
            int benefice = valeur[0, 1] - valeur[0, 0];
            questions.Add(new((lettre++).ToString(),
                Invariant($"La boutique de {boutiques[0]} vend tout son stock. Quel est son bénéfice, en euros ?"), "bénéfice (€)",
                new ReponseNombre(benefice),
                [
                    "La ligne 1 de $QP$ concerne la boutique de " + boutiques[0] + " : coût d'achat du stock, puis prix de vente du stock.",
                    Invariant($"Bénéfice = vente − achat = ${valeur[0, 1]} - {valeur[0, 0]} = {benefice}$ €."),
                ]));
        }
        else
        {
            int meilleure = Enumerable.Range(0, nb).MaxBy(i => valeur[i, 0]);
            questions.Add(new((lettre++).ToString(), "Quelle boutique a le stock de plus grande valeur ?", "boutique",
                new ReponseChoix(boutiques.Take(nb).ToList(), meilleure),
                [Invariant($"Chaque ligne de $QP$ donne la valeur du stock d'une boutique. La plus grande est ${valeur[meilleure, 0]}$ € : c'est {boutiques[meilleure]}.")]));
        }

        return new ExerciceTd("En situation : stocks et prix", enonce, questions,
            "Écris le produit dans l'ordre où les dimensions vont ensemble : stocks × prix. Chaque coefficient du résultat = quantités d'une boutique × prix unitaires, terme à terme, additionnés.",
            SectionUsine);
    }

    // ==== Outils ============================================================================

    /// <summary>Matrice aux coefficients tirés entre min et max, ligne après ligne.</summary>
    private static Matrice Aleatoire(GenerateurAleatoire r, int n, int p, int min, int max)
    {
        var valeurs = new int[n * p];
        for (int k = 0; k < valeurs.Length; k++) valeurs[k] = r.Entier(min, max);
        return Matrice.Creer(n, p, (i, j) => valeurs[i * p + j]);
    }

    /// <summary>Dimension selon le niveau : 2 × 2, puis 2 × 3 ou 3 × 2, puis 3 × 3.</summary>
    private static (int N, int P) Dimensions(GenerateurAleatoire r, int niveau) => niveau switch
    {
        1 => (2, 2),
        2 => r.PileOuFace() ? (2, 3) : (3, 2),
        _ => (3, 3),
    };

    private static string Dim(int n, int p) => Invariant($"{n} × {p}");

    private static string S(int n) => n > 1 ? "s" : "";

    /// <summary>Nombre écrit dans un calcul : les négatifs entre parenthèses, « 3 × (−2) ».</summary>
    private static string T(int x) => x < 0 ? Invariant($"({x})") : Invariant($"{x}");

    /// <summary>
    /// Propositions d'un choix : la bonne, puis jusqu'à trois autres différentes, mélangées.
    /// </summary>
    private static ReponseChoix ChoixMelanges(GenerateurAleatoire r, string bonne, IEnumerable<string> autres)
    {
        var choix = new List<string> { bonne };
        foreach (string autre in autres)
        {
            if (choix.Count == 4) break;
            if (!choix.Contains(autre)) choix.Add(autre);
        }
        r.Melanger(choix);
        return new ReponseChoix(choix, choix.IndexOf(bonne));
    }

    /// <summary>Correction d'une somme ou d'une différence : une étape par ligne.</summary>
    private static List<string> EtapesTermeATerme(Matrice a, Matrice b, string operateur, string nomResultat)
    {
        var etapes = new List<string> { "Même dimension : l'opération est possible. On opère coefficient par coefficient, à la même place." };
        var resultat = operateur == "+" ? a + b : a - b;
        for (int i = 0; i < a.Lignes; i++)
        {
            var calculs = Enumerable.Range(0, a.Colonnes).Select(j =>
                Invariant($"$c_{{{i + 1}{j + 1}}} = {a[i, j]} {operateur} {T(b[i, j])} = {resultat[i, j]}$"));
            etapes.Add(Invariant($"Ligne {i + 1} : ") + string.Join(" ; ", calculs) + ".");
        }
        etapes.Add(Invariant($"D'où ${nomResultat} = {resultat.Latex()}$."));
        return etapes;
    }

    /// <summary>Correction de k.A : une étape par ligne.</summary>
    private static List<string> EtapesReel(Matrice a, int k)
    {
        var etapes = new List<string> { Invariant($"On multiplie chaque coefficient de $A$ par ${k}$.") };
        for (int i = 0; i < a.Lignes; i++)
            etapes.Add(Invariant($"Ligne {i + 1} : ") + string.Join(" ; ",
                Enumerable.Range(0, a.Colonnes).Select(j => Invariant($"${T(k)} \\times {T(a[i, j])} = {k * a[i, j]}$"))) + ".");
        etapes.Add(Invariant($"D'où ${k}.A = {(k * a).Latex()}$."));
        return etapes;
    }

    /// <summary>
    /// Correction d'un produit, coefficient par coefficient, façon tableau :
    /// « $c_{12} = a_{11}b_{12} + a_{12}b_{22} = 1 \times 1 + 5 \times 2 = 11$ ».
    /// Sans lettres (puissances), on écrit « ligne 1 de A × colonne 2 de A ».
    /// </summary>
    private static List<string> EtapesProduit(Matrice x, Matrice y, string nomX, string nomY, string? lettreX, string? lettreY, string? nomResultat = null)
    {
        var resultat = x * y;
        var etapes = new List<string>();
        for (int i = 0; i < resultat.Lignes; i++)
        for (int j = 0; j < resultat.Colonnes; j++)
        {
            string numerique = string.Join(" + ", Enumerable.Range(0, x.Colonnes).Select(k => Invariant($"{T(x[i, k])} \\times {T(y[k, j])}")));
            string debut = lettreX is not null && lettreY is not null
                ? Invariant($"$c_{{{i + 1}{j + 1}}} = ") + string.Join(" + ", Enumerable.Range(0, x.Colonnes).Select(k =>
                    Invariant($"{lettreX}_{{{i + 1}{k + 1}}}{lettreY}_{{{k + 1}{j + 1}}}"))) + " = "
                : Invariant($"Ligne {i + 1} de ${nomX}$ × colonne {j + 1} de ${nomY}$ : $c_{{{i + 1}{j + 1}}} = ");
            etapes.Add(debut + numerique + Invariant($" = {resultat[i, j]}$"));
        }
        etapes.Add(Invariant($"D'où ${nomResultat ?? nomX + nomY} = {resultat.Latex()}$."));
        return etapes;
    }
}
