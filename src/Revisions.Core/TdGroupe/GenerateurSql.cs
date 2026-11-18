using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « SQL (LID) » (bloc 2, B2.3 audit d'une base) : une petite base inventée (matériel d'un magasin,
/// clients et commandes) et des requêtes à exécuter « à la main » : sélection, agrégats, GROUP BY / HAVING,
/// jointure, et choix de la bonne requête. Atome calcule lui-même les résultats.
/// </summary>
public sealed class GenerateurSql : GenerateurASujets
{
    public override string Prefixe => "SQL";
    public override string Matiere => "SLAM";
    public override string Theme => "Requêtes SQL";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("selection", "Sélection avec WHERE", "b2-b23-sq1-s01b-s02", [4, 5, 6]),
        new("agregat", "Fonctions d'agrégat", "b2-b23-sq1-s01b-s03", [4, 5, 6]),
        new("groupby", "GROUP BY et HAVING", "b2-b23-sq1-s01b-s06", [5, 7, 9]),
        new("jointure", "Jointure", "b2-b23-sq1-s01b-s04", [5, 7, 9]),
        new("requete", "Choisir la bonne requête", "b2-b23-sq1-s01b-s06", [3, 4, 5]),
    ];

    private sealed record Article(int Id, string Libelle, string Categorie, int Prix, int Stock);
    private sealed record Client(int Id, string Nom, string Ville);
    private sealed record Commande(int Id, int IdClient, int Montant);

    private static readonly (string Categorie, string[] Libelles)[] Catalogue =
    [
        ("Écran", ["Écran 22\"", "Écran 24\"", "Écran 27\"", "Écran incurvé"]),
        ("Clavier", ["Clavier sans fil", "Clavier mécanique", "Clavier compact"]),
        ("Souris", ["Souris optique", "Souris ergonomique", "Souris gamer"]),
        ("Stockage", ["Clé USB 64 Go", "Disque SSD 1 To", "Disque externe 2 To"]),
        ("Câble", ["Câble HDMI", "Câble RJ45", "Câble USB-C"]),
    ];
    private static readonly string[] Noms = ["Durand", "Martin", "Petit", "Leroy", "Moreau", "Garnier", "Faure", "Roux"];
    private static readonly string[] Villes = ["Lyon", "Nantes", "Lille", "Rennes"];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) => cle switch
    {
        "selection" => Selection(r, niveau),
        "agregat" => Agregat(r, niveau),
        "groupby" => GroupBy(r, niveau),
        "jointure" => Jointure(r, niveau),
        "requete" => Requete(r, niveau),
        _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
    };

    // ==== Exercices ==========================================================================

    private static ExerciceTd Selection(GenerateurAleatoire r, int niveau)
    {
        var articles = Articles(r, niveau);
        List<Article> resultat;
        string condition, explication;
        do
        {
            (condition, explication, var filtre) = Condition(r, articles, niveau);
            resultat = articles.Where(filtre).ToList();
        } while (resultat.Count == 0 || resultat.Count == articles.Count);

        string requete = $"SELECT libelle, prix\nFROM article\nWHERE {condition};";
        return new ExerciceTd("Sélection avec WHERE", Base(articles) + $"\n\n```sql\n{requete}\n```",
            [
                new("a", "Combien de lignes cette requête renvoie-t-elle ?", "nombre de lignes", new ReponseNombre(resultat.Count),
                [
                    $"On garde les lignes où la condition est vraie : {explication}.",
                    "Lignes retenues : " + string.Join(" ; ", resultat.Select(a => Invariant($"{a.Libelle} ({a.Prix} €)"))) + ".",
                    Invariant($"La requête renvoie **{resultat.Count} lignes**."),
                ]),
            ],
            "WHERE garde les lignes pour lesquelles la condition est vraie. AND : les deux conditions ; OR : au moins une ; BETWEEN a AND b : bornes incluses.",
            "b2-b23-sq1-s01b-s02");
    }

    private static ExerciceTd Agregat(GenerateurAleatoire r, int niveau)
    {
        var articles = Articles(r, niveau);
        string categorie = r.Choisir(articles.Select(a => a.Categorie).Distinct().ToList());
        var lignes = articles.Where(a => a.Categorie == categorie).ToList();
        var fonctions = niveau == 1 ? new[] { "COUNT", "MAX", "MIN" } : ["COUNT", "MAX", "MIN", "SUM", "SUM_PRIX"];
        string f = r.Choisir(fonctions);
        var (expression, valeur, detail) = f switch
        {
            "COUNT" => ("COUNT(*)", lignes.Count, "on compte les lignes"),
            "MAX" => ("MAX(prix)", lignes.Max(a => a.Prix), $"plus grand prix parmi {Liste(lignes.Select(a => Nombre(a.Prix)))}"),
            "MIN" => ("MIN(prix)", lignes.Min(a => a.Prix), $"plus petit prix parmi {Liste(lignes.Select(a => Nombre(a.Prix)))}"),
            "SUM" => ("SUM(stock)", lignes.Sum(a => a.Stock), string.Join(" + ", lignes.Select(a => Nombre(a.Stock)))),
            _ => ("SUM(prix * stock)", lignes.Sum(a => a.Prix * a.Stock), string.Join(" + ", lignes.Select(a => Invariant($"{a.Prix} × {a.Stock}")))),
        };
        string requete = $"SELECT {expression}\nFROM article\nWHERE categorie = '{categorie}';";
        return new ExerciceTd("Fonction d'agrégat", Base(articles) + $"\n\n```sql\n{requete}\n```",
            [
                new("a", "Quelle valeur cette requête renvoie-t-elle ?", "résultat", new ReponseNombre(valeur),
                [
                    $"WHERE garde les articles de la catégorie {categorie} : {Liste(lignes.Select(a => a.Libelle))}.",
                    Invariant($"{expression} : {detail}, soit **{valeur}**."),
                ]),
            ],
            "COUNT(*) compte les lignes, SUM additionne, MAX et MIN donnent la plus grande et la plus petite valeur. Le WHERE s'applique **avant** le calcul.",
            "b2-b23-sq1-s01b-s03");
    }

    private static ExerciceTd GroupBy(GenerateurAleatoire r, int niveau)
    {
        var articles = Articles(r, niveau);
        var groupes = articles.GroupBy(a => a.Categorie).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        bool somme = niveau == 3;
        int Valeur(IGrouping<string, Article> g) => somme ? g.Sum(a => a.Stock) : g.Count();
        string agregat = somme ? "SUM(stock)" : "COUNT(*)";
        // Seuil du HAVING : une valeur atteinte par certains groupes mais pas tous (sans HAVING si tous sont égaux).
        var seuils = groupes.Select(Valeur).Distinct().Order().Skip(1).ToList();
        bool avecHaving = niveau >= 2 && seuils.Count > 0;
        int seuil = avecHaving ? r.Choisir(seuils) : 0;
        var gardes = groupes.Where(g => Valeur(g) >= seuil).ToList();
        var cible = r.Choisir(gardes);

        string requete = !avecHaving
            ? $"SELECT categorie, {agregat}\nFROM article\nGROUP BY categorie;"
            : $"SELECT categorie, {agregat}\nFROM article\nGROUP BY categorie\nHAVING {agregat} >= {seuil};";
        int lignes = gardes.Count;
        var calculs = groupes.Select(g => Invariant($"{g.Key} : {(somme && g.Count() > 1 ? string.Join(" + ", g.Select(a => a.Stock)) + " = " : "")}{Valeur(g)}")).ToList();

        return new ExerciceTd("GROUP BY et HAVING", Base(articles) + $"\n\n```sql\n{requete}\n```",
            [
                new("a", "Combien de lignes cette requête renvoie-t-elle ?", "nombre de lignes", new ReponseNombre(lignes),
                [
                    $"GROUP BY categorie fait un groupe par catégorie, puis {agregat} est calculé dans chaque groupe.",
                    string.Join(" ; ", calculs) + ".",
                    !avecHaving
                        ? Invariant($"Une ligne par catégorie : **{lignes}**.")
                        : Invariant($"HAVING garde les groupes où {agregat} ≥ {seuil} : {Liste(gardes.Select(g => g.Key))}, soit **{lignes}** lignes."),
                ]),
                new("b", $"Quelle valeur la requête affiche-t-elle pour la catégorie {cible.Key} ?", cible.Key, new ReponseNombre(Valeur(cible)),
                    [Invariant($"Dans le groupe {cible.Key} : {agregat} = **{Valeur(cible)}**.")]),
            ],
            "GROUP BY regroupe les lignes qui ont la même valeur ; l'agrégat est calculé par groupe. HAVING filtre les **groupes** (après le calcul), WHERE filtre les **lignes** (avant).",
            "b2-b23-sq1-s01b-s06");
    }

    private static ExerciceTd Jointure(GenerateurAleatoire r, int niveau)
    {
        var noms = Noms.ToList();
        r.Melanger(noms);
        var clients = Enumerable.Range(0, niveau == 1 ? 4 : 5).Select(i => new Client(i + 1, noms[i], r.Choisir(Villes))).ToList();
        var commandes = new List<Commande>();
        int nb = niveau == 1 ? 6 : 8;
        for (int k = 0; k < nb; k++) commandes.Add(new Commande(101 + k, r.Choisir(clients).Id, r.Entier(2, 40) * 10));
        // Une ville avec au moins deux commandes si possible (sinon au moins une).
        int CommandesDe(string v) => commandes.Count(c => clients.First(x => x.Id == c.IdClient).Ville == v);
        var villes = clients.Select(c => c.Ville).Distinct().ToList();
        var bonnes = villes.Where(v => CommandesDe(v) >= 2).ToList();
        string ville = r.Choisir(bonnes.Count > 0 ? bonnes : villes.Where(v => CommandesDe(v) >= 1).ToList());

        var jointes = commandes.Select(c => (Commande: c, Client: clients.First(x => x.Id == c.IdClient))).ToList();
        var resultat = jointes.Where(j => j.Client.Ville == ville).ToList();
        int total = resultat.Sum(j => j.Commande.Montant);

        string tables = "Table **client** :\n\n" + Tableau(["idClient", "nom", "ville"],
                clients.Select(c => (IReadOnlyList<string>)[Nombre(c.Id), c.Nom, c.Ville]))
            + "\nTable **commande** :\n\n" + Tableau(["idCommande", "idClient", "montant"],
                commandes.Select(c => (IReadOnlyList<string>)[Nombre(c.Id), Nombre(c.IdClient), Nombre(c.Montant)]));
        string requete = $"SELECT nom, idCommande, montant\nFROM client JOIN commande ON client.idClient = commande.idClient\nWHERE ville = '{ville}';";
        var questions = new List<QuestionTd>
        {
            new("a", "Combien de lignes cette requête renvoie-t-elle ?", "nombre de lignes", new ReponseNombre(resultat.Count),
            [
                "La jointure associe chaque commande à **son** client (même idClient).",
                $"Clients de {ville} : {Liste(clients.Where(c => c.Ville == ville).Select(c => Invariant($"{c.Nom} ({c.Id})")))}.",
                "Commandes retenues : " + string.Join(" ; ", resultat.Select(j => Invariant($"{j.Client.Nom}, n° {j.Commande.Id}, {j.Commande.Montant} €"))) +
                    Invariant($". Soit **{resultat.Count}** ligne{S(resultat.Count)}."),
            ]),
        };
        if (niveau >= 2)
            questions.Add(new("b", $"Si l'on remplace la première ligne par `SELECT SUM(montant)`, quel résultat obtient-on ?", "SUM(montant)",
                new ReponseNombre(total),
                [resultat.Count == 1
                    ? Invariant($"Une seule ligne retenue : **{total}**.")
                    : Invariant($"On additionne les montants des lignes retenues : {string.Join(" + ", resultat.Select(j => j.Commande.Montant))} = **{total}**.")]));

        return new ExerciceTd("Jointure", tables + $"\n```sql\n{requete}\n```", questions,
            "JOIN … ON client.idClient = commande.idClient : chaque ligne de commande est complétée par les colonnes de son client. Le WHERE s'applique ensuite.",
            "b2-b23-sq1-s01b-s04");
    }

    private static ExerciceTd Requete(GenerateurAleatoire r, int niveau)
    {
        var articles = Articles(r, niveau);
        int seuil = r.Entier(2, 3);
        int choix = r.Entier(0, niveau == 1 ? 1 : 2);
        string question, bonne;
        string[] fausses;
        string explication;
        switch (choix)
        {
            case 0:
                int prix = r.Choisir(articles.Select(a => a.Prix).Distinct().Order().Skip(1).ToList());
                question = Invariant($"Afficher le libellé des articles qui coûtent moins de {prix} €, du moins cher au plus cher.");
                bonne = Invariant($"SELECT libelle FROM article WHERE prix < {prix} ORDER BY prix;");
                fausses =
                [
                    Invariant($"SELECT libelle FROM article WHERE prix < {prix} ORDER BY prix DESC;"),
                    Invariant($"SELECT libelle FROM article HAVING prix < {prix} ORDER BY prix;"),
                    Invariant($"SELECT libelle FROM article WHERE prix > {prix} ORDER BY prix;"),
                ];
                explication = "Condition sur des **lignes** → WHERE ; ORDER BY trie par ordre croissant par défaut (DESC = décroissant).";
                break;
            case 1:
                question = "Afficher, pour chaque catégorie, le nombre d'articles.";
                bonne = "SELECT categorie, COUNT(*) FROM article GROUP BY categorie;";
                fausses =
                [
                    "SELECT categorie, COUNT(*) FROM article;",
                    "SELECT categorie, SUM(stock) FROM article GROUP BY categorie;",
                    "SELECT COUNT(categorie) FROM article;",
                ];
                explication = "Un calcul **par catégorie** demande GROUP BY categorie ; COUNT(*) compte les lignes de chaque groupe.";
                break;
            default:
                question = Invariant($"Afficher les catégories qui ont au moins {seuil} articles.");
                bonne = Invariant($"SELECT categorie FROM article GROUP BY categorie HAVING COUNT(*) >= {seuil};");
                fausses =
                [
                    Invariant($"SELECT categorie FROM article WHERE COUNT(*) >= {seuil} GROUP BY categorie;"),
                    Invariant($"SELECT categorie FROM article GROUP BY categorie HAVING COUNT(*) > {seuil};"),
                    Invariant($"SELECT categorie, COUNT(*) FROM article WHERE stock >= {seuil} GROUP BY categorie;"),
                ];
                explication = "Une condition sur un **agrégat** (COUNT, SUM…) se met dans HAVING, jamais dans WHERE. « Au moins » se traduit par >=.";
                break;
        }
        var reponse = ChoixMelanges(r, "`" + bonne + "`", fausses.Select(f => "`" + f + "`"));
        return new ExerciceTd("Choisir la bonne requête",
            $"Base : table **article**(idArticle, libelle, categorie, prix, stock).\n\nObjectif : {question}",
            [
                new("a", "Quelle requête convient ?", "requête", reponse,
                [
                    explication,
                    $"Bonne requête : **{(char)('A' + reponse.Bonne)}**.",
                ]),
            ],
            "WHERE filtre les lignes, GROUP BY regroupe, HAVING filtre les groupes, ORDER BY trie (ASC par défaut, DESC pour décroissant).",
            "b2-b23-sq1-s01b-s06");
    }

    // ==== Données ============================================================================

    private static List<Article> Articles(GenerateurAleatoire r, int niveau)
    {
        var tous = Catalogue.SelectMany(c => c.Libelles.Select(l => (c.Categorie, Libelle: l))).ToList();
        r.Melanger(tous);
        int n = niveau == 1 ? 7 : 9;
        var choisis = tous.Take(n).OrderBy(t => t.Categorie, StringComparer.Ordinal).ThenBy(t => t.Libelle, StringComparer.Ordinal).ToList();
        return choisis.Select((t, i) => new Article(i + 1, t.Libelle, t.Categorie, r.Entier(1, 30) * 5, r.Entier(0, 25))).ToList();
    }

    private static string Base(List<Article> articles) =>
        "Table **article** :\n\n" + Tableau(["idArticle", "libelle", "categorie", "prix", "stock"],
            articles.Select(a => (IReadOnlyList<string>)[Nombre(a.Id), a.Libelle, a.Categorie, Nombre(a.Prix), Nombre(a.Stock)]));

    /// <summary>Condition WHERE : simple au niveau 1, avec AND au niveau 2, avec OR, BETWEEN ou IN au niveau 3.</summary>
    private static (string Sql, string Explication, Func<Article, bool> Filtre) Condition(GenerateurAleatoire r, List<Article> articles, int niveau)
    {
        int prix = r.Choisir(articles.Select(a => a.Prix).ToList());
        int stock = r.Entier(5, 20);
        string cat = r.Choisir(articles.Select(a => a.Categorie).Distinct().ToList());
        string cat2 = r.Choisir(articles.Select(a => a.Categorie).Distinct().Where(c => c != cat).ToList());
        int forme = niveau switch { 1 => r.Entier(0, 1), 2 => r.Entier(2, 3), _ => r.Entier(4, 6) };
        int bas = Math.Min(prix, 75), haut = bas + r.Entier(4, 10) * 5;
        return forme switch
        {
            0 => (Invariant($"prix > {prix}"), Invariant($"prix strictement supérieur à {prix}"), a => a.Prix > prix),
            1 => ($"categorie = '{cat}'", $"catégorie {cat}", a => a.Categorie == cat),
            2 => (Invariant($"categorie = '{cat}' AND stock >= {stock}"), Invariant($"catégorie {cat} **et** stock ≥ {stock}"), a => a.Categorie == cat && a.Stock >= stock),
            3 => (Invariant($"prix <= {prix} AND stock > {stock}"), Invariant($"prix ≤ {prix} **et** stock > {stock}"), a => a.Prix <= prix && a.Stock > stock),
            4 => (Invariant($"categorie = '{cat}' OR prix > {prix}"), Invariant($"catégorie {cat} **ou** prix > {prix}"), a => a.Categorie == cat || a.Prix > prix),
            5 => (Invariant($"prix BETWEEN {bas} AND {haut}"), Invariant($"prix entre {bas} et {haut}, bornes incluses"), a => a.Prix >= bas && a.Prix <= haut),
            _ => ($"categorie IN ('{cat}', '{cat2}') AND stock > 0", $"catégorie {cat} ou {cat2}, **et** stock non nul", a => (a.Categorie == cat || a.Categorie == cat2) && a.Stock > 0),
        };
    }
}
