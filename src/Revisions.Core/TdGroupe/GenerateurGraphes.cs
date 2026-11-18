using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « Graphes » (maths SQ2) : lire et écrire une matrice d'adjacence, compter les chemins avec M²,
/// fermeture transitive, niveaux des sommets, chemin optimal d'un graphe pondéré.
/// Les graphes sont tirés au hasard ; les méthodes et notations suivent le cours
/// (M[2] pour la puissance booléenne, M̂ pour la fermeture transitive, méthode des niveaux par suppression).
/// </summary>
public sealed class GenerateurGraphes : GenerateurASujets
{
    public override string Prefixe => "GRA";
    public override string Matiere => "Mathématiques";
    public override string Theme => "Graphes";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("lecture", "Lire une matrice d'adjacence", "maths-sq2-s01-s04", [4, 5, 6]),
        new("matrice", "Écrire la matrice d'adjacence", "maths-sq2-s01-s03", [4, 6, 8]),
        new("chemins", "Compter les chemins avec M²", "maths-sq2-s01-s06", [8, 10, 14]),
        new("fermeture", "Fermeture transitive", "maths-sq2-s04-s05", [10, 12, 18]),
        new("niveaux", "Niveaux des sommets", "maths-sq2-s08-s01", [5, 7, 9]),
        new("optimal", "Chemin optimal d'un graphe pondéré", "maths-sq2-s10-s03", [8, 10, 14]),
    ];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) => cle switch
    {
        "lecture" => Lecture(r, niveau),
        "matrice" => EcrireMatrice(r, niveau),
        "chemins" => Chemins(r, niveau),
        "fermeture" => Fermeture(r, niveau),
        "niveaux" => Niveaux(r, niveau),
        "optimal" => Optimal(r, niveau),
        _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
    };

    // ==== Exercices ==========================================================================

    private static ExerciceTd Lecture(GenerateurAleatoire r, int niveau)
    {
        int n = 3 + niveau; // 4, 5 ou 6 sommets
        var g = Aleatoire(r, n, boucles: niveau >= 2);
        var m = Adjacence(g);
        var noms = Noms(n);
        int arcs = m.Coefficients().Sum();
        int x = ChoisirSommet(r, n, i => Successeurs(g, i).Count > 0);
        int y = ChoisirSommet(r, n, j => j != x && Predecesseurs(g, j).Count > 0, j => Predecesseurs(g, j).Count > 0);
        var boucles = Enumerable.Range(0, n).Where(i => g[i, i]).Select(Lettre).ToList();

        return new ExerciceTd("Lire une matrice d'adjacence",
            $"Voici la matrice d'adjacence $M$ d'un graphe orienté $G$ de sommets {string.Join(", ", noms)} (dans cet ordre) :\n\n$$M = {m.Latex()}$$",
            new List<QuestionTd>
            {
                new("a", "Combien d'arcs le graphe $G$ comporte-t-il ?", "nombre d'arcs", new ReponseNombre(arcs),
                [
                    "Chaque 1 de la matrice est un arc : on compte les 1.",
                    Invariant($"Par ligne : {string.Join(" + ", Enumerable.Range(0, n).Select(i => Successeurs(g, i).Count))} = {arcs}. $G$ a **{arcs} arcs**."),
                ]),
                new("b", $"Quels sont les successeurs de {noms[x]} ?", $"successeurs de {noms[x]}",
                    new ReponseTexte(Liste(Successeurs(g, x).Select(Lettre)), FormeTexte.Ensemble, "B, D"),
                [
                    $"Les successeurs de {noms[x]} se lisent sur la **ligne** de {noms[x]} : chaque 1 indique un arc qui part de {noms[x]}.",
                    $"Ligne {noms[x]} : {LigneTexte(m, x)}. Successeurs de {noms[x]} : **{Liste(Successeurs(g, x).Select(Lettre))}**.",
                ]),
                new("c", $"Quels sont les prédécesseurs de {noms[y]} ?", $"prédécesseurs de {noms[y]}",
                    new ReponseTexte(Liste(Predecesseurs(g, y).Select(Lettre)), FormeTexte.Ensemble, "A, C"),
                [
                    $"Les prédécesseurs de {noms[y]} se lisent sur la **colonne** de {noms[y]} : chaque 1 indique un arc qui arrive en {noms[y]}.",
                    $"Colonne {noms[y]} : {ColonneTexte(m, y)}. Prédécesseurs de {noms[y]} : **{Liste(Predecesseurs(g, y).Select(Lettre))}**.",
                ]),
                new("d", "Le graphe comporte-t-il une boucle ?", "boucle", OuiNon(boucles.Count > 0),
                [
                    "Une boucle est un arc d'un sommet vers lui-même : c'est un 1 sur la **diagonale** de $M$.",
                    boucles.Count > 0
                        ? $"La diagonale contient un 1 pour : {Liste(boucles)}. **Oui**, il y a une boucle sur {Liste(boucles)}."
                        : "La diagonale ne contient que des 0 : **non**, pas de boucle.",
                ]),
            }.Where(q => niveau >= 2 || q.Lettre != "d").ToList(),
            "Ligne d'un sommet = ses **successeurs** (arcs qui partent). Colonne d'un sommet = ses **prédécesseurs** (arcs qui arrivent). Un 1 sur la diagonale = une **boucle**.",
            "maths-sq2-s01-s04");
    }

    private static ExerciceTd EcrireMatrice(GenerateurAleatoire r, int niveau)
    {
        int n = 3 + niveau;
        var g = Aleatoire(r, n, boucles: niveau == 3);
        var m = Adjacence(g);
        var noms = Noms(n);
        string tableau = Tableau(["Sommet", "Successeurs"],
            Enumerable.Range(0, n).Select(i => (IReadOnlyList<string>)[noms[i], Liste(Successeurs(g, i).Select(Lettre), "–")]));

        var etapes = new List<string> { "On remplit la matrice **ligne par ligne** : sur la ligne d'un sommet, on met 1 dans la colonne de chacun de ses successeurs, 0 ailleurs." };
        for (int i = 0; i < n; i++)
            etapes.Add($"Ligne {noms[i]} (successeurs : {Liste(Successeurs(g, i).Select(Lettre), "aucun")}) : {LigneTexte(m, i)}.");
        etapes.Add($"D'où $M = {m.Latex()}$.");

        return new ExerciceTd("Écrire la matrice d'adjacence",
            $"Un graphe orienté $G$ est donné par son tableau des successeurs :\n\n{tableau}",
            [
                new("a", $"Écris la matrice d'adjacence $M$ de $G$ (sommets dans l'ordre {string.Join(", ", noms)}).", "matrice M",
                    new ReponseMatrice(m, noms, noms), etapes),
            ],
            "La matrice d'adjacence a une ligne et une colonne par sommet. $m_{ij} = 1$ s'il y a un arc **du** sommet $i$ **vers** le sommet $j$, sinon 0.",
            "maths-sq2-s01-s03");
    }

    private static ExerciceTd Chemins(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 3 ? 5 : 4;
        bool[,] g;
        Matrice m, m2;
        do
        {
            g = Aleatoire(r, n, boucles: false);
            m = Adjacence(g);
            m2 = m * m;
        } while (m2.Coefficients().Sum() < 3 || m2.Coefficients().Max() < 2 && niveau >= 2);
        var noms = Noms(n);

        // Un couple (X, Y) avec au moins un chemin de longueur 2, de préférence plusieurs.
        var couples = (from i in Enumerable.Range(0, n) from j in Enumerable.Range(0, n) where m2[i, j] > 0 select (i, j)).ToList();
        int max = couples.Max(c => m2[c.i, c.j]);
        var (x, y) = r.Choisir(niveau == 1 ? couples : couples.Where(c => m2[c.i, c.j] == max).ToList());
        int total = m2.Coefficients().Sum();

        var etapesM2 = new List<string> { "$M^2 = M \\times M$ : coefficient $(i, j)$ = ligne $i$ de $M$ × colonne $j$ de $M$." };
        for (int i = 0; i < n; i++)
            etapesM2.Add($"Ligne {noms[i]} : " + string.Join(" ; ", Enumerable.Range(0, n).Select(j =>
                Invariant($"{string.Join("+", Enumerable.Range(0, n).Select(k => Invariant($"{m[i, k]}×{m[k, j]}")))} = {m2[i, j]}"))) + ".");
        etapesM2.Add($"D'où $M^2 = {m2.Latex()}$.");

        var intermediaires = Enumerable.Range(0, n).Where(k => m[x, k] == 1 && m[k, y] == 1).Select(k => $"{noms[x]}-{noms[k]}-{noms[y]}").ToList();
        var questions = new List<QuestionTd>
        {
            new("a", "Calcule $M^2$.", "M²", new ReponseMatrice(m2, noms, noms), etapesM2),
            new("b", $"Combien y a-t-il de chemins de longueur 2 de {noms[x]} à {noms[y]} ?", $"chemins {noms[x]} → {noms[y]}",
                new ReponseNombre(m2[x, y]),
            [
                "Le coefficient $(i, j)$ de $M^2$ donne le **nombre de chemins de longueur 2** de $i$ à $j$.",
                Invariant($"Ligne {noms[x]}, colonne {noms[y]} de $M^2$ : **{m2[x, y]}**. Ce sont : {Liste(intermediaires)}."),
            ]),
            new("c", "Combien le graphe a-t-il de chemins de longueur 2 au total ?", "total longueur 2", new ReponseNombre(total),
            [
                "On additionne **tous** les coefficients de $M^2$.",
                Invariant($"Somme des lignes : {string.Join(" + ", Enumerable.Range(0, n).Select(i => Enumerable.Range(0, n).Sum(j => m2[i, j])))} = **{total}**."),
            ]),
        };
        if (niveau >= 2)
        {
            var m3 = m2 * m;
            int a = r.Entier(0, n - 1), b = r.Entier(0, n - 1);
            questions.Add(new("d", $"Combien y a-t-il de chemins de longueur 3 de {noms[a]} à {noms[b]} ?", $"longueur 3 {noms[a]} → {noms[b]}",
                new ReponseNombre(m3[a, b]),
            [
                "Le nombre de chemins de longueur 3 se lit dans $M^3 = M^2 \\times M$ : il suffit de calculer le coefficient demandé.",
                Invariant($"Ligne {noms[a]} de $M^2$ × colonne {noms[b]} de $M$ : {string.Join(" + ", Enumerable.Range(0, n).Select(k => Invariant($"{m2[a, k]}×{m[k, b]}")))} = **{m3[a, b]}**."),
            ]));
        }

        return new ExerciceTd("Chemins et puissances de M",
            $"Voici la matrice d'adjacence $M$ d'un graphe orienté (sommets dans l'ordre {string.Join(", ", noms)}) :\n\n$$M = {m.Latex()}$$",
            questions,
            "Le coefficient $(i, j)$ de $M^k$ est le **nombre de chemins de longueur k** allant de $i$ à $j$. Pour $M^2$, on fait le produit $M \\times M$ (ligne × colonne).",
            "maths-sq2-s01-s06");
    }

    private static ExerciceTd Fermeture(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 3 ? 5 : 4;
        bool[,] g;
        Matrice m, chapeau;
        do
        {
            g = Aleatoire(r, n, boucles: false, densite: 30);
            m = Adjacence(g);
            chapeau = FermetureTransitive(m);
        } while (chapeau.Coefficients().Sum() == m.Coefficients().Sum() || chapeau.Coefficients().All(v => v == 1));
        var noms = Noms(n);
        var puissances = new List<Matrice> { m };
        for (int k = 2; k <= n; k++) puissances.Add(ProduitBooleen(puissances[^1], m));
        var m2 = puissances[1];
        int ajoutes = chapeau.Coefficients().Sum() - m.Coefficients().Sum();

        var etapesM2 = new List<string> { "Produit **booléen** : on calcule comme un produit ordinaire, mais avec $1 + 1 = 1$ (dès qu'un terme vaut 1, le coefficient vaut 1)." };
        for (int i = 0; i < n; i++)
            etapesM2.Add($"Ligne {noms[i]} : " + string.Join(" ; ", Enumerable.Range(0, n).Select(j =>
                Invariant($"{string.Join("+", Enumerable.Range(0, n).Select(k => Invariant($"{m[i, k]}·{m[k, j]}")))} → {m2[i, j]}"))) + ".");
        etapesM2.Add($"D'où $M^{{[2]}} = {m2.Latex()}$.");

        var etapesChapeau = new List<string>();
        for (int k = 2; k < n; k++)
            etapesChapeau.Add($"$M^{{[{k + 1}]}} = M^{{[{k}]}} \\otimes M = {puissances[k].Latex()}$.");
        etapesChapeau.Add(Invariant($"$\\hat{{M}} = M \\oplus M^{{[2]}} \\oplus \\dots \\oplus M^{{[{n}]}}$ : on met 1 dès qu'**une** des matrices a un 1 à cet endroit."));
        etapesChapeau.Add($"D'où $\\hat{{M}} = {chapeau.Latex()}$.");

        var nouveaux = (from i in Enumerable.Range(0, n) from j in Enumerable.Range(0, n)
                        where chapeau[i, j] == 1 && m[i, j] == 0 select $"({noms[i]}, {noms[j]})").ToList();

        return new ExerciceTd("Fermeture transitive",
            $"Voici la matrice d'adjacence $M$ d'un graphe orienté $G$ (sommets dans l'ordre {string.Join(", ", noms)}) :\n\n$$M = {m.Latex()}$$",
            [
                new("a", "Calcule la matrice booléenne $M^{[2]}$.", "M[2]", new ReponseMatrice(m2, noms, noms), etapesM2),
                new("b", Invariant($"Déduis-en la matrice $\\hat{{M}}$ de la fermeture transitive de $G$ (calcule aussi $M^{{[3]}}$… jusqu'à $M^{{[{n}]}}$)."), "M̂",
                    new ReponseMatrice(chapeau, noms, noms), etapesChapeau),
                new("c", "Combien d'arcs faut-il ajouter à $G$ pour obtenir sa fermeture transitive ?", "arcs ajoutés", new ReponseNombre(ajoutes),
                [
                    "Ce sont les 1 de $\\hat{M}$ qui sont des 0 dans $M$.",
                    Invariant($"Nouveaux arcs : {Liste(nouveaux)}. Il faut en ajouter **{ajoutes}**."),
                ]),
            ],
            Invariant($"$M^{{[k]}}$ indique par un 1 qu'il existe **au moins un** chemin de longueur k. Avec {n} sommets : $\\hat{{M}} = M \\oplus M^{{[2]}} \\oplus \\dots \\oplus M^{{[{n}]}}$ (addition booléenne : $1 \\oplus 1 = 1$)."),
            "maths-sq2-s04-s05");
    }

    private static ExerciceTd Niveaux(GenerateurAleatoire r, int niveau)
    {
        int n = 4 + niveau; // 5, 6 ou 7 sommets
        var g = SansCircuit(r, n, melanger: true);
        var noms = Noms(n);
        var (niveaux, tours) = CalculerNiveaux(g);
        var m = Adjacence(g);
        var zero = Enumerable.Range(0, n).Where(i => niveaux[i] == 0).Select(Lettre).ToList();

        string presentation = niveau == 3
            ? "Un graphe orienté sans circuit est donné par son tableau des successeurs :\n\n" + Tableau(["Sommet", "Successeurs"],
                Enumerable.Range(0, n).Select(i => (IReadOnlyList<string>)[noms[i], Liste(Successeurs(g, i).Select(Lettre), "–")]))
            : $"Voici la matrice d'adjacence d'un graphe orienté sans circuit (sommets dans l'ordre {string.Join(", ", noms)}) :\n\n$$M = {m.Latex()}$$";

        var etapes = new List<string>
        {
            "Tableau des prédécesseurs (lu " + (niveau == 3 ? "à partir des successeurs" : "dans les **colonnes** de $M$") + ") : " +
                string.Join(" ; ", Enumerable.Range(0, n).Select(j => $"{noms[j]} : {Liste(Predecesseurs(g, j).Select(Lettre), "–")}")) + ".",
        };
        for (int k = 0; k < tours.Count; k++)
            etapes.Add(k == 0
                ? $"Sans prédécesseur : {Liste(tours[k].Select(Lettre))} → **niveau 0**. On les supprime du tableau."
                : $"Il ne reste plus de prédécesseur à {Liste(tours[k].Select(Lettre))} → **niveau {k}**." + (k < tours.Count - 1 ? " On les supprime." : ""));

        return new ExerciceTd("Niveaux des sommets", presentation,
            [
                new("a", "Détermine le niveau de chaque sommet.", "niveaux",
                    new ReponseMatrice(Matrice.Creer(1, n, (_, j) => niveaux[j]), null, noms), etapes),
                new("b", "Quels sommets sont de niveau 0 ?", "niveau 0", new ReponseTexte(Liste(zero), FormeTexte.Ensemble, "A, C"),
                [
                    $"Ce sont les sommets **sans prédécesseur** : **{Liste(zero)}**.",
                ]),
            ],
            "Méthode des niveaux : les sommets sans prédécesseur sont de niveau 0. On les supprime partout ; ceux qui n'ont plus de prédécesseur sont de niveau 1, et ainsi de suite.",
            "maths-sq2-s08-s01");
    }

    private static ExerciceTd Optimal(GenerateurAleatoire r, int niveau)
    {
        int n = niveau == 1 ? 5 : 6;
        bool[,] g;
        int[,] poids;
        List<(List<int> Chemin, int Valeur)> chemins;
        while (true)
        {
            g = SansCircuit(r, n, melanger: false, densite: 45);
            poids = new int[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (g[i, j]) poids[i, j] = r.Entier(1, niveau == 1 ? 9 : 20);
            chemins = TousLesChemins(g, 0, n - 1).Select(c => (c, Valeur(c, poids))).ToList();
            if (chemins.Count is < 3 or > 8) continue;
            int min = chemins.Min(c => c.Valeur), max = chemins.Max(c => c.Valeur);
            if (chemins.Count(c => c.Valeur == min) == 1 && chemins.Count(c => c.Valeur == max) == 1) break;
        }
        var noms = Noms(n);
        var meilleur = chemins.MinBy(c => c.Valeur);
        var pire = chemins.MaxBy(c => c.Valeur);
        string Ecrire(List<int> c) => string.Join("-", c.Select(Lettre));

        string tableau = Tableau(["Départ \\ Arrivée", .. noms],
            Enumerable.Range(0, n).Select(i => (IReadOnlyList<string>)[noms[i], .. Enumerable.Range(0, n).Select(j => g[i, j] ? Nombre(poids[i, j]) : "–")]));
        var liste = chemins.OrderBy(c => c.Valeur).Select(c =>
            Invariant($"{Ecrire(c.Chemin)} : {string.Join(" + ", c.Chemin.Zip(c.Chemin.Skip(1), (a, b) => poids[a, b]))} = {c.Valeur}")).ToList();

        var questions = new List<QuestionTd>
        {
            new("a", $"Quelle est la durée minimale pour aller de A à {noms[n - 1]} ?", "durée minimale", new ReponseNombre(meilleur.Valeur),
            [
                Invariant($"Méthode du BTS : on **liste tous les chemins** de A à {noms[n - 1]} avec leur valeur ({chemins.Count} chemins)."),
                .. liste.Select(l => "- " + l),
                Invariant($"La plus petite valeur est **{meilleur.Valeur}**."),
            ]),
            new("b", $"Par quel trajet ? (écris les sommets dans l'ordre)", "trajet minimal",
                new ReponseTexte(Ecrire(meilleur.Chemin), FormeTexte.Chemin, "A-C-E"),
            [
                $"Le chemin de valeur minimale est **{Ecrire(meilleur.Chemin)}**.",
            ]),
        };
        if (niveau == 3)
            questions.Add(new("c", $"Et la durée maximale d'un trajet de A à {noms[n - 1]} ?", "durée maximale", new ReponseNombre(pire.Valeur),
            [
                Invariant($"Dans la même liste, la plus grande valeur est **{pire.Valeur}** ({Ecrire(pire.Chemin)})."),
            ]));

        return new ExerciceTd("Chemin optimal",
            $"Le tableau donne la durée (en heures) des liaisons directes entre des sites A, B… Une case « – » : pas de liaison directe.\n\n{tableau}",
            questions,
            "Un chemin a pour valeur la **somme des poids** de ses arcs. Au BTS, on liste tous les chemins possibles avec leur valeur, puis on garde le plus petit (ou le plus grand).",
            "maths-sq2-s10-s03");
    }

    // ==== Calculs (publics pour les tests) ===================================================

    public static Matrice Adjacence(bool[,] g) => Matrice.Creer(g.GetLength(0), g.GetLength(0), (i, j) => g[i, j] ? 1 : 0);

    public static Matrice ProduitBooleen(Matrice a, Matrice b) =>
        Matrice.Creer(a.Lignes, b.Colonnes, (i, j) => Enumerable.Range(0, a.Colonnes).Any(k => a[i, k] == 1 && b[k, j] == 1) ? 1 : 0);

    /// <summary>M̂ = M ⊕ M[2] ⊕ … ⊕ M[n].</summary>
    public static Matrice FermetureTransitive(Matrice m)
    {
        var somme = m;
        var puissance = m;
        for (int k = 2; k <= m.Lignes; k++)
        {
            puissance = ProduitBooleen(puissance, m);
            var s = somme;
            var p = puissance;
            somme = Matrice.Creer(m.Lignes, m.Colonnes, (i, j) => s[i, j] == 1 || p[i, j] == 1 ? 1 : 0);
        }
        return somme;
    }

    /// <summary>Niveaux par la méthode du cours, et les sommets trouvés à chaque tour.</summary>
    public static (int[] Niveaux, List<List<int>> Tours) CalculerNiveaux(bool[,] g)
    {
        int n = g.GetLength(0);
        var niveaux = Enumerable.Repeat(-1, n).ToArray();
        var tours = new List<List<int>>();
        while (niveaux.Contains(-1))
        {
            var tour = Enumerable.Range(0, n).Where(j => niveaux[j] == -1 &&
                Enumerable.Range(0, n).All(i => !g[i, j] || i == j || niveaux[i] != -1)).ToList();
            if (tour.Count == 0) throw new InvalidOperationException("Le graphe a un circuit.");
            foreach (int j in tour) niveaux[j] = tours.Count;
            tours.Add(tour);
        }
        return (niveaux, tours);
    }

    public static List<List<int>> TousLesChemins(bool[,] g, int depart, int arrivee)
    {
        var resultat = new List<List<int>>();
        void Explorer(List<int> chemin)
        {
            int dernier = chemin[^1];
            if (dernier == arrivee) { resultat.Add([.. chemin]); return; }
            for (int j = 0; j < g.GetLength(0); j++)
                if (g[dernier, j] && !chemin.Contains(j)) { chemin.Add(j); Explorer(chemin); chemin.RemoveAt(chemin.Count - 1); }
        }
        Explorer([depart]);
        return resultat;
    }

    private static int Valeur(List<int> chemin, int[,] poids) => chemin.Zip(chemin.Skip(1), (a, b) => poids[a, b]).Sum();

    // ==== Tirage des graphes =================================================================

    /// <summary>Graphe quelconque : chaque arc existe avec la probabilité <paramref name="densite"/> %, chaque sommet a au moins un arc.</summary>
    private static bool[,] Aleatoire(GenerateurAleatoire r, int n, bool boucles, int densite = 35)
    {
        while (true)
        {
            var g = new bool[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                g[i, j] = i == j ? boucles && r.Entier(1, 100) <= 12 : r.Entier(1, 100) <= densite;
            bool chacunRelie = Enumerable.Range(0, n).All(i => Enumerable.Range(0, n).Any(k => g[i, k] || g[k, i]));
            if (chacunRelie) return g;
        }
    }

    /// <summary>
    /// Graphe sans circuit : arcs seulement « vers l'avant » dans un ordre caché. Avec <paramref name="melanger"/>,
    /// les lettres sont redistribuées (l'ordre alphabétique ne trahit plus les niveaux). Chaque sommet a un arc.
    /// </summary>
    private static bool[,] SansCircuit(GenerateurAleatoire r, int n, bool melanger, int densite = 35)
    {
        while (true)
        {
            var ordre = Enumerable.Range(0, n).ToList();
            if (melanger) r.Melanger(ordre);
            var g = new bool[n, n];
            for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (r.Entier(1, 100) <= densite) g[ordre[a], ordre[b]] = true;
            bool chacunRelie = Enumerable.Range(0, n).All(i => Enumerable.Range(0, n).Any(k => g[i, k] || g[k, i]));
            // Sans mélange (chemin optimal) : A est la seule source, le dernier sommet le seul puits.
            bool bornes = melanger || Enumerable.Range(1, n - 1).All(j => Enumerable.Range(0, n).Any(i => g[i, j]))
                                   && Enumerable.Range(0, n - 1).All(i => Enumerable.Range(0, n).Any(j => g[i, j]));
            if (chacunRelie && bornes) return g;
        }
    }

    /// <summary>Un sommet qui remplit la condition (à défaut, la condition de secours ; il en existe toujours un).</summary>
    private static int ChoisirSommet(GenerateurAleatoire r, int n, Func<int, bool> condition, Func<int, bool>? secours = null)
    {
        var possibles = Enumerable.Range(0, n).Where(condition).ToList();
        if (possibles.Count == 0 && secours is not null) possibles = Enumerable.Range(0, n).Where(secours).ToList();
        return r.Choisir(possibles);
    }

    private static List<int> Successeurs(bool[,] g, int i) => Enumerable.Range(0, g.GetLength(0)).Where(j => g[i, j]).ToList();
    private static List<int> Predecesseurs(bool[,] g, int j) => Enumerable.Range(0, g.GetLength(0)).Where(i => g[i, j]).ToList();
    private static List<string> Noms(int n) => Enumerable.Range(0, n).Select(Lettre).ToList();
    private static string LigneTexte(Matrice m, int i) => string.Join(" ", Enumerable.Range(0, m.Colonnes).Select(j => m[i, j]));
    private static string ColonneTexte(Matrice m, int j) => string.Join(" ", Enumerable.Range(0, m.Lignes).Select(i => m[i, j]));
}
