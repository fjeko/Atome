using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « Inverse et systèmes » (maths SQ1, séances 4 à 6) : vérifier qu'une matrice est l'inverse d'une autre,
/// calculer un inverse à la calculatrice, résoudre un système linéaire avec la matrice inverse.
/// Les matrices tirées ont un déterminant de ±1 : leurs inverses sont à coefficients entiers (saisie simple).
/// </summary>
public sealed class GenerateurInverse : GenerateurASujets
{
    public override string Prefixe => "INV";
    public override string Matiere => "Mathématiques";
    public override string Theme => "Inverse et systèmes";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("verifier", "Vérifier une matrice inverse", "maths-sq1-s04-s01", [5, 7, 10]),
        new("calculatrice", "Inverse à la calculatrice", "maths-sq1-s04-s02", [4, 5, 6]),
        new("systeme", "Système linéaire et matrice inverse", "maths-sq1-s04-s06", [8, 12, 15]),
        new("examen-production", "Programme de production", "maths-sq1-s04-s06", [10, 12, 15], TypeExamen: true),
        new("examen-codes", "Codes calculés par une formule", "maths-sq1-s04-s06", [10, 12, 15], NiveauMin: 2, TypeExamen: true),
    ];

    private static readonly string[] Inconnues = ["x", "y", "z"];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) => cle switch
    {
        "verifier" => Verifier(r, niveau),
        "calculatrice" => Calculatrice(r, niveau),
        "systeme" => Systeme(r, niveau),
        "examen-production" => ExamenProduction(r, niveau),
        "examen-codes" => ExamenCodes(r),
        _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
    };

    private static ExerciceTd Verifier(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 1 ? 2 : 3;
        var (a, inverse) = Inversible(r, n);
        bool vraie = r.PileOuFace();
        var b = inverse;
        if (!vraie)
        {
            int i = r.Entier(0, n - 1), j = r.Entier(0, n - 1), delta = r.EntierNonNul(-2, 2);
            b = Matrice.Creer(n, n, (k, l) => k == i && l == j ? inverse[k, l] + delta : inverse[k, l]);
        }
        var produit = a * b;
        return new ExerciceTd("La matrice B est-elle l'inverse de A ?",
            $"On donne $A = {a.Latex()}$ et $B = {b.Latex()}$.",
            [
                new("a", "Calcule $AB$.", "AB", new ReponseMatrice(produit), GenerateurMatrices.EtapesProduit(a, b, "A", "B", "a", "b")),
                new("b", "$B$ est-elle la matrice inverse de $A$ ?", "B = A⁻¹ ?", OuiNon(vraie),
                [
                    Invariant($"$B$ est l'inverse de $A$ si $AB = I_{n}$ (et alors aussi $BA = I_{n}$)."),
                    vraie
                        ? Invariant($"$AB = I_{n}$ : **oui**, $B = A^{{-1}}$.")
                        : Invariant($"$AB \\neq I_{n}$ (un coefficient n'est pas celui de l'identité) : **non**, $B$ n'est pas l'inverse de $A$."),
                ]),
            ],
            Invariant($"Deux matrices carrées sont inverses l'une de l'autre si leur produit vaut la matrice identité $I_{n}$ (des 1 sur la diagonale, des 0 ailleurs)."),
            "maths-sq1-s04-s01");
    }

    private static ExerciceTd Calculatrice(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 1 ? 2 : 3;
        var (a, inverse) = Inversible(r, n);
        var etapes = new List<string>
        {
            "À la calculatrice : on saisit $A$ dans l'éditeur de matrices, puis on calcule $A^{-1}$ (touche $x^{-1}$).",
            Invariant($"Résultat : $A^{{-1}} = {inverse.Latex()}$."),
            Invariant($"Vérification (toujours utile) : $A \\times A^{{-1}} = {(a * inverse).Latex()} = I_{n}$."),
        };
        return new ExerciceTd("Inverse d'une matrice",
            $"Soit $A = {a.Latex()}$.",
            [new("a", "Calcule $A^{-1}$ (utilise ta calculatrice).", "A⁻¹", new ReponseMatrice(inverse), etapes)],
            "La calculatrice donne $A^{-1}$ directement. Pense à vérifier : $A \\times A^{-1}$ doit donner l'identité.",
            "maths-sq1-s04-s02");
    }

    private static ExerciceTd Systeme(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 1 ? 2 : 3;
        var (a, inverse) = Inversible(r, n);
        var x = Matrice.Creer(n, 1, (_, _) => r.Entier(-6, 9));
        var y = a * x;
        string systeme = "\\begin{cases} " + string.Join(" \\\\ ", Enumerable.Range(0, n).Select(i => Equation(a, i) + Invariant($" = {y[i, 0]}"))) + " \\end{cases}";
        string inconnues = string.Join(", ", Inconnues.Take(n));
        var noms = Inconnues.Take(n).ToList();

        return new ExerciceTd("Résoudre un système avec la matrice inverse",
            $"On considère le système :\n\n$$(S) \\quad {systeme}$$\n\nIl s'écrit $AX = Y$ avec $X = \\begin{{pmatrix}} {string.Join(" \\\\ ", noms)} \\end{{pmatrix}}$. On admet que $A^{{-1}} = {inverse.Latex()}$.",
            [
                new("a", "Écris la matrice $A$ du système.", "A", new ReponseMatrice(a),
                [
                    $"Ligne $i$ de $A$ : les coefficients de {inconnues} dans l'équation $i$ (0 si l'inconnue manque).",
                    $"$A = {a.Latex()}$.",
                ]),
                new("b", "Écris la matrice colonne $Y$.", "Y", new ReponseMatrice(y),
                [
                    "$Y$ contient les seconds membres, dans l'ordre des équations.",
                    $"$Y = {y.Latex()}$.",
                ]),
                new("c", "Calcule $X = A^{-1}Y$ (solution du système).", "X", new ReponseMatrice(x, null, null),
                [
                    "$AX = Y$ donne, en multipliant à gauche par $A^{-1}$ : $X = A^{-1}Y$ (l'ordre compte !).",
                    .. GenerateurMatrices.EtapesProduit(inverse, y, "A^{-1}", "Y", null, null, "X"),
                    "Solution : " + string.Join(", ", Enumerable.Range(0, n).Select(i => Invariant($"${noms[i]} = {x[i, 0]}$"))) + ".",
                ]),
            ],
            "Le système s'écrit $AX = Y$. Si $A$ est inversible : $X = A^{-1}Y$ (on multiplie **à gauche** par $A^{-1}$).",
            "maths-sq1-s04-s06");
    }

    // ==== Type examen ========================================================================

    /// <summary>Matrices 2 × 2 à coefficients positifs et de déterminant ±1 (inverse entière), pour les situations concrètes.</summary>
    private static readonly int[][] Positives =
    [
        [2, 1, 1, 1], [3, 2, 1, 1], [2, 3, 1, 2], [3, 1, 2, 1], [4, 3, 1, 1], [5, 3, 3, 2], [3, 5, 1, 2], [4, 1, 3, 1], [5, 2, 2, 1], [2, 5, 1, 3],
    ];

    private static readonly (string Intro, string[] Produits, string[] Lignes)[] Productions =
    [
        ("Une entreprise assemble deux modèles d'ordinateurs, P1 et P2.", ["P1", "P2"], ["Coût des composants (centaines d'€)", "Heures de montage"]),
        ("Un atelier fabrique deux types de serveurs, S1 et S2.", ["S1", "S2"], ["Coût des pièces (centaines d'€)", "Heures de travail"]),
        ("Une société produit deux modèles de routeurs, R1 et R2.", ["R1", "R2"], ["Coût des éléments (dizaines d'€)", "Heures de test"]),
    ];

    /// <summary>
    /// Type examen : un tableau de production donne $A$ ; on calcule $Y = AX$ pour une semaine, puis on retrouve
    /// la production d'une autre semaine à partir des totaux, avec $X = A^{-1}Y$.
    /// </summary>
    private static ExerciceTd ExamenProduction(GenerateurAleatoire r, int niveau)
    {
        var (intro, produits, lignes) = r.Choisir(Productions);
        var c = r.Choisir(Positives);
        var a = Matrice.Creer(2, 2, (i, j) => c[2 * i + j]);
        var inverse = Inverse(a);
        int pas = niveau == 1 ? 5 : 1;
        var x1 = Matrice.Creer(2, 1, (_, _) => r.Entier(2, 12) * pas);
        var x2 = Matrice.Creer(2, 1, (_, _) => r.Entier(2, 12) * pas);
        var y1 = a * x1;
        var y2 = a * x2;
        string tableau = $"| | {produits[0]} | {produits[1]} |\n| --- | --- | --- |\n" +
            string.Concat(Enumerable.Range(0, 2).Select(i => Invariant($"| {lignes[i]} | {a[i, 0]} | {a[i, 1]} |\n")));

        return new ExerciceTd("Programme de production",
            $"{intro} Pour un appareil de chaque modèle, le tableau donne :\n\n{tableau}\n" +
            $"Une production de $x$ {produits[0]} et $y$ {produits[1]} se note $X = \\begin{{pmatrix}} x \\\\ y \\end{{pmatrix}}$. " +
            $"Elle occasionne un coût $c$ et un nombre d'heures $t$, notés $Y = \\begin{{pmatrix}} c \\\\ t \\end{{pmatrix}}$. On note $A$ la matrice du tableau : $Y = AX$.",
            [
                new("a", Invariant($"Une semaine, l'entreprise produit {x1[0, 0]} {produits[0]} et {x1[1, 0]} {produits[1]}. Calcule $Y$ (coût, puis heures)."), "Y",
                    new ReponseMatrice(y1), GenerateurMatrices.EtapesProduit(a, x1, "A", "X", "a", "x", "Y")),
                new("b", $"On admet que $A^{{-1}} = {inverse.Latex()}$. Une autre semaine, le coût vaut {y2[0, 0]} et les heures {y2[1, 0]}. Combien d'appareils de chaque modèle ont été produits ?", "X",
                    new ReponseMatrice(x2),
                    [
                        "$Y = AX$ donne $X = A^{-1}Y$ (on multiplie à gauche par $A^{-1}$).",
                        .. GenerateurMatrices.EtapesProduit(inverse, y2, "A^{-1}", "Y", null, null, "X"),
                        Invariant($"L'entreprise a produit **{x2[0, 0]} {produits[0]}** et **{x2[1, 0]} {produits[1]}**."),
                    ]),
            ],
            "Le tableau donne directement $A$ (une colonne par modèle). Pour revenir des totaux $Y$ à la production $X$ : $X = A^{-1}Y$.",
            "maths-sq1-s04-s06", TypeExamen: true);
    }

    /// <summary>
    /// Type examen : le code du client n vaut x + n·y + n²·z ; trois codes connus donnent un système 3 × 3
    /// (matrice fixe, inverse donnée), qu'on résout pour calculer ensuite d'autres codes.
    /// </summary>
    private static ExerciceTd ExamenCodes(GenerateurAleatoire r)
    {
        int x = r.Entier(1, 9), y = r.Entier(1, 9), z = r.Entier(1, 9);
        int Code(int n) => x + n * y + n * n * z;
        var m = Matrice.Creer(3, 3, (i, j) => (int)Math.Pow(i + 1, j));
        var codes = Matrice.Creer(3, 1, (i, _) => Code(i + 1));
        var solution = Matrice.Creer(3, 1, (i, _) => new[] { x, y, z }[i]);
        int n4 = r.Entier(4, 9);
        const string inverseLatex = "\\begin{pmatrix} 3 & -3 & 1 \\\\ -2{,}5 & 4 & -1{,}5 \\\\ 0{,}5 & -1 & 0{,}5 \\end{pmatrix}";
        return new ExerciceTd("Codes clients calculés par une formule",
            "Pour que ses clients accèdent à leurs factures, un service attribue à chacun un code. Le code du client numéro $n$ vaut " +
            "$x + n\\,y + n^2 z$, où $x$, $y$ et $z$ sont trois nombres inconnus.\n\n" +
            Invariant($"Le client n° 1 a pour code {codes[0, 0]}, le client n° 2 a pour code {codes[1, 0]} et le client n° 3 a pour code {codes[2, 0]}."),
            [
                new("a", "Le système vérifié par $x$, $y$, $z$ s'écrit $MX = Y$. Écris la matrice $M$.", "M", new ReponseMatrice(m),
                    [
                        "Client $n$ : $1 \\times x + n \\times y + n^2 \\times z$ = code. Chaque ligne de $M$ est donc $(1 \\;\\; n \\;\\; n^2)$.",
                        $"$M = {m.Latex()}$ et $Y = {codes.Latex()}$.",
                    ]),
                new("b", $"On admet que $M^{{-1}} = {inverseLatex}$. Calcule $X$ ($x$, puis $y$, puis $z$).", "X", new ReponseMatrice(solution),
                    [
                        "$X = M^{-1}Y$ (à la calculatrice, ou ligne par colonne).",
                        Invariant($"$x = 3 \\times {codes[0, 0]} - 3 \\times {codes[1, 0]} + 1 \\times {codes[2, 0]} = {x}$"),
                        Invariant($"$y = -2{{,}}5 \\times {codes[0, 0]} + 4 \\times {codes[1, 0]} - 1{{,}}5 \\times {codes[2, 0]} = {y}$"),
                        Invariant($"$z = 0{{,}}5 \\times {codes[0, 0]} - 1 \\times {codes[1, 0]} + 0{{,}}5 \\times {codes[2, 0]} = {z}$"),
                    ]),
                new("c", Invariant($"Quel est le code du client n° {n4} ?"), Invariant($"code n° {n4}"), new ReponseNombre(Code(n4)),
                    [Invariant($"${x} + {n4} \\times {y} + {n4}^2 \\times {z} = {x} + {n4 * y} + {n4 * n4 * z} = {Code(n4)}$.")]),
            ],
            "Chaque code connu donne une équation : $x + n\\,y + n^2 z = \\text{code}$. Trois équations → système $MX = Y$ → $X = M^{-1}Y$.",
            "maths-sq1-s04-s06", TypeExamen: true);
    }

    // ==== Outils =============================================================================

    /// <summary>Écrit l'équation de la ligne i : « 2x - y + 3z ».</summary>
    private static string Equation(Matrice a, int i)
    {
        var texte = new System.Text.StringBuilder();
        for (int j = 0; j < a.Colonnes; j++)
        {
            int c = a[i, j];
            if (c == 0) continue;
            string signe = c < 0 ? " - " : texte.Length > 0 ? " + " : "";
            if (texte.Length == 0 && c < 0) signe = "-";
            int abs = Math.Abs(c);
            texte.Append(signe).Append(abs == 1 ? "" : Invariant($"{abs}")).Append(Inconnues[j]);
        }
        return texte.ToString();
    }

    /// <summary>
    /// Matrice n × n de déterminant ±1 (opérations élémentaires sur l'identité), sans coefficient nul sur une ligne entière,
    /// avec ses coefficients et ceux de son inverse entre −9 et 9.
    /// </summary>
    public static (Matrice A, Matrice Inverse) Inversible(GenerateurAleatoire r, int n)
    {
        while (true)
        {
            var m = new int[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1;
            for (int etape = 0; etape < n + 2; etape++)
            {
                int i = r.Entier(0, n - 1), j = r.Entier(0, n - 2);
                if (j >= i) j++;
                int c = r.EntierNonNul(-2, 2);
                for (int k = 0; k < n; k++) m[i, k] += c * m[j, k];
            }
            if (r.PileOuFace()) for (int k = 0; k < n; k++) m[0, k] = -m[0, k];
            var a = Matrice.Creer(n, n, (i, j) => m[i, j]);
            var inverse = Inverse(a);
            int zeros = a.Coefficients().Count(v => v == 0);
            bool bornes = a.Coefficients().Concat(inverse.Coefficients()).All(v => Math.Abs(v) <= 9);
            if (bornes && zeros <= n - 1 && a != Matrice.Identite(n)) return (a, inverse);
        }
    }

    /// <summary>Inverse d'une matrice 2 × 2 ou 3 × 3 de déterminant ±1 (comatrice transposée / déterminant).</summary>
    public static Matrice Inverse(Matrice a)
    {
        int n = a.Lignes;
        if (n == 2)
        {
            int det2 = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
            return Matrice.Creer(2, 2, (i, j) => (i, j) switch
            {
                (0, 0) => a[1, 1], (0, 1) => -a[0, 1], (1, 0) => -a[1, 0], _ => a[0, 0],
            } * det2); // det = ±1 : diviser ou multiplier par det revient au même
        }
        int Mineur(int i, int j)
        {
            var l = Enumerable.Range(0, 3).Where(k => k != i).ToArray();
            var c = Enumerable.Range(0, 3).Where(k => k != j).ToArray();
            return a[l[0], c[0]] * a[l[1], c[1]] - a[l[0], c[1]] * a[l[1], c[0]];
        }
        int det = Enumerable.Range(0, 3).Sum(j => (j % 2 == 0 ? 1 : -1) * a[0, j] * Mineur(0, j));
        if (Math.Abs(det) != 1) throw new InvalidOperationException("Le déterminant doit valoir ±1.");
        return Matrice.Creer(3, 3, (i, j) => ((i + j) % 2 == 0 ? 1 : -1) * Mineur(j, i) * det);
    }
}
