using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>Un projet à ordonnancer : tâches, durées, antécédents, et tout ce que la méthode MPM en tire.</summary>
public sealed class ProjetMpm
{
    /// <summary>Nombre de tâches (sans FIN). La tâche FIN a l'indice <see cref="N"/>.</summary>
    public int N { get; }
    public int[] Durees { get; }
    /// <summary>Antécédents de chaque tâche (indices).</summary>
    public List<int>[] Anteriorites { get; }

    public int[] Niveaux { get; }
    /// <summary>Dates au plus tôt et au plus tard des tâches, FIN comprise (indice N).</summary>
    public int[] Tot { get; }
    public int[] Tard { get; }

    public ProjetMpm(int[] durees, List<int>[] anteriorites)
    {
        N = durees.Length;
        Durees = durees;
        Anteriorites = anteriorites;

        Niveaux = new int[N];
        var ordre = new List<int>();
        var restants = Enumerable.Range(0, N).ToHashSet();
        int niveau = 0;
        while (restants.Count > 0)
        {
            var tour = restants.Where(t => Anteriorites[t].All(a => !restants.Contains(a))).Order().ToList();
            if (tour.Count == 0) throw new InvalidOperationException("Les antériorités forment un circuit.");
            foreach (int t in tour) { Niveaux[t] = niveau; restants.Remove(t); }
            ordre.AddRange(tour);
            niveau++;
        }
        OrdreParNiveaux = ordre;

        Tot = new int[N + 1];
        foreach (int t in ordre)
            Tot[t] = Anteriorites[t].Count == 0 ? 0 : Anteriorites[t].Max(a => Tot[a] + Durees[a]);
        Tot[N] = Enumerable.Range(0, N).Where(t => Successeurs(t).Count == 0).Max(t => Tot[t] + Durees[t]);

        Tard = new int[N + 1];
        Tard[N] = Tot[N];
        foreach (int t in Enumerable.Reverse(ordre))
            Tard[t] = SuccesseursOuFin(t).Min(s => Tard[s]) - Durees[t];
    }

    /// <summary>Tâches rangées par niveau croissant (ordre de calcul des dates au plus tôt).</summary>
    public IReadOnlyList<int> OrdreParNiveaux { get; }

    public int Duree => Tot[N];

    public List<int> Successeurs(int t) => Enumerable.Range(0, N).Where(s => Anteriorites[s].Contains(t)).ToList();

    /// <summary>Successeurs, ou FIN (indice N) pour une tâche sans successeur.</summary>
    public List<int> SuccesseursOuFin(int t) => Successeurs(t) is { Count: > 0 } s ? s : [N];

    public bool EstCritique(int t) => Tot[t] == Tard[t];

    public int MargeTotale(int t) => Tard[t] - Tot[t];

    /// <summary>Marge libre : min des dates au plus tôt des successeurs − tôt − durée (ramenée à 0 si négative).</summary>
    public int MargeLibre(int t) => Math.Max(0, SuccesseursOuFin(t).Min(s => Tot[s]) - Tot[t] - Durees[t]);

    /// <summary>Chemins critiques complets (du début à FIN), chacun terminé par FIN.</summary>
    public List<List<int>> CheminsCritiques()
    {
        var resultat = new List<List<int>>();
        void Explorer(List<int> chemin)
        {
            int t = chemin[^1];
            if (t == N) { resultat.Add([.. chemin]); return; }
            foreach (int s in SuccesseursOuFin(t))
                if (EstCritique(s) && Tot[s] == Tot[t] + Durees[t]) { chemin.Add(s); Explorer(chemin); chemin.RemoveAt(chemin.Count - 1); }
        }
        foreach (int t in Enumerable.Range(0, N).Where(t => Anteriorites[t].Count == 0 && EstCritique(t))) Explorer([t]);
        return resultat;
    }
}

/// <summary>
/// TD « Ordonnancement » (maths SQ3, méthode MPM) : niveaux des tâches, dates au plus tôt et au plus tard,
/// durée du projet, chemin critique, marges totale et libre. Formules et vocabulaire du cours ;
/// projets tirés au hasard (mises en situation propres à Atome).
/// </summary>
public sealed class GenerateurOrdonnancement : GenerateurASujets
{
    public override string Prefixe => "ORD";
    public override string Matiere => "Mathématiques";
    public override string Theme => "Ordonnancement MPM";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("niveaux", "Niveaux des tâches", "maths-sq3-s01-s03", [4, 5, 7]),
        new("tot", "Dates au plus tôt", "maths-sq3-s01-s04", [6, 8, 12]),
        new("tard", "Dates au plus tard", "maths-sq3-s01-s05", [6, 8, 12]),
        new("critique", "Chemin critique", "maths-sq3-s04-s02", [4, 5, 7]),
        new("marges", "Marges totale et libre", "maths-sq3-s04-s03", [6, 8, 10]),
        new("projet", "Étude complète d'un projet", "maths-sq3-s01-s04", [30, 30, 30], NiveauMin: 3, TypeExamen: true),
    ];

    private static readonly string[] Projets =
    [
        "Une PME installe un nouveau réseau Wi-Fi dans ses locaux.",
        "Une équipe de développeurs met en ligne une application de réservation.",
        "Un lycée renouvelle le parc informatique d'une salle de TP.",
        "Une association organise un salon du jeu vidéo.",
        "Une agence web refond le site d'un client.",
        "Un service informatique migre ses serveurs vers le cloud.",
    ];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau)
    {
        var projet = Tirer(r, niveau, cheminUnique: cle is "critique" or "projet");
        string intro = r.Choisir(Projets) + " Le tableau donne ses tâches, leur durée (en jours) et leurs antécédents.\n\n" + TableauTaches(projet);
        return cle switch
        {
            "niveaux" => Niveaux(projet, intro),
            "tot" => Tot(projet, intro),
            "tard" => Tard(projet, intro),
            "critique" => Critique(projet, intro),
            "marges" => Marges(projet, intro, r),
            "projet" => Complet(projet, intro),
            _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
        };
    }

    // ==== Exercices ==========================================================================

    private static ExerciceTd Niveaux(ProjetMpm p, string intro) => new("Niveaux des tâches", intro,
        [new("a", "Détermine le niveau de chaque tâche.", "niveaux",
            new ReponseMatrice(Matrice.Creer(1, p.N, (_, j) => p.Niveaux[j]), null, Taches(p, fin: false)), EtapesNiveaux(p))],
        "Les tâches sans antécédent sont de niveau 0. On les raye des antécédents ; celles qui n'en ont plus sont de niveau 1, etc.",
        "maths-sq3-s01-s03");

    private static ExerciceTd Tot(ProjetMpm p, string intro) => new("Dates au plus tôt", intro,
        [
            new("a", "Calcule la date au plus tôt de chaque tâche, et de FIN.", "dates au plus tôt",
                new ReponseMatrice(Matrice.Creer(1, p.N + 1, (_, j) => p.Tot[j]), null, Taches(p, fin: true)), EtapesTot(p)),
            new("b", "Quelle est la durée minimale du projet ?", "durée (jours)", new ReponseNombre(p.Duree),
                [Invariant($"C'est la date au plus tôt de FIN : **{p.Duree} jours**.")]),
        ],
        "On calcule de gauche à droite : tôt(X) = max(tôt(P) + durée(P)) sur les antécédents P de X ; 0 pour une tâche sans antécédent. FIN reçoit les tâches sans successeur.",
        "maths-sq3-s01-s04");

    private static ExerciceTd Tard(ProjetMpm p, string intro) => new("Dates au plus tard",
        intro + "\n\nDates au plus tôt déjà calculées :\n\n" + TableauDates(p, tard: false),
        [new("a", "Calcule la date au plus tard de chaque tâche (et de FIN).", "dates au plus tard",
            new ReponseMatrice(Matrice.Creer(1, p.N + 1, (_, j) => p.Tard[j]), null, Taches(p, fin: true)), EtapesTard(p))],
        "On part de FIN (tard = tôt), puis de droite à gauche : tard(X) = min(tard(S)) − durée(X) sur les successeurs S de X.",
        "maths-sq3-s01-s05");

    private static ExerciceTd Critique(ProjetMpm p, string intro)
    {
        var critiques = Enumerable.Range(0, p.N).Where(p.EstCritique).ToList();
        var chemin = p.CheminsCritiques()[0];
        string ecrit = string.Join("-", chemin.Select(t => NomTache(p, t)));
        string sansFin = string.Join("-", chemin.Where(t => t != p.N).Select(t => NomTache(p, t)));
        return new("Chemin critique", intro + "\n\nDates au plus tôt et au plus tard :\n\n" + TableauDates(p, tard: true),
            [
                new("a", "Quelles sont les tâches critiques ?", "tâches critiques",
                    new ReponseTexte(Liste(critiques.Select(t => NomTache(p, t))), FormeTexte.Ensemble, "A, D, F"),
                [
                    "Une tâche est **critique** quand sa date au plus tôt est égale à sa date au plus tard.",
                    $"Tâches critiques : **{Liste(critiques.Select(t => NomTache(p, t)))}**" +
                        (critiques.Count < p.N ? $" (les autres ont tôt < tard : {Liste(Enumerable.Range(0, p.N).Where(t => !p.EstCritique(t)).Select(t => Invariant($"{NomTache(p, t)} {p.Tot[t]} < {p.Tard[t]}")))})." : "."),
                ]),
                new("b", "Écris le chemin critique (tâches dans l'ordre, jusqu'à FIN).", "chemin critique",
                    new ReponseTexte(ecrit, FormeTexte.Chemin, "A-D-F-FIN", [sansFin]),
                [
                    "Le chemin critique enchaîne des tâches critiques, chacune commençant dès que la précédente finit.",
                    $"Chemin critique : **{ecrit.Replace("-", " – ")}**. Sa durée est celle du projet : " +
                        Invariant($"{string.Join(" + ", chemin.Where(t => t != p.N).Select(t => p.Durees[t]))} = {p.Duree} jours."),
                ]),
            ],
            "Tâche critique : tôt = tard (aucun retard possible). Le chemin critique ne passe que par des tâches critiques : c'est le plus long chemin du graphe.",
            "maths-sq3-s04-s02");
    }

    private static ExerciceTd Marges(ProjetMpm p, string intro, GenerateurAleatoire r)
    {
        var candidates = Enumerable.Range(0, p.N).Where(t => !p.EstCritique(t)).ToList();
        int x = candidates.Count > 0 ? r.Choisir(candidates) : r.Entier(0, p.N - 1);
        var succ = p.SuccesseursOuFin(x);
        int brut = succ.Min(s => p.Tot[s]) - p.Tot[x] - p.Durees[x];
        return new("Marges", intro + "\n\nDates au plus tôt et au plus tard :\n\n" + TableauDates(p, tard: true),
            [
                new("a", "Calcule la marge totale de chaque tâche.", "marges totales",
                    new ReponseMatrice(Matrice.Creer(1, p.N, (_, j) => p.MargeTotale(j)), null, Taches(p, fin: false)),
                [
                    "Marge totale = tard − tôt.",
                    string.Join(" ; ", Enumerable.Range(0, p.N).Select(t => Invariant($"{NomTache(p, t)} : {p.Tard[t]} − {p.Tot[t]} = {p.MargeTotale(t)}"))) + ".",
                ]),
                new("b", $"Calcule la marge libre de la tâche {NomTache(p, x)}.", $"marge libre de {NomTache(p, x)}", new ReponseNombre(p.MargeLibre(x)),
                [
                    "Marge libre(X) = min(tôt des successeurs) − tôt(X) − durée(X), ramenée à 0 si le calcul est négatif.",
                    Invariant($"Successeur{S(succ.Count)} de {NomTache(p, x)} : {Liste(succ.Select(s => $"{NomTache(p, s)} (tôt {p.Tot[s]})"))}."),
                    Invariant($"{succ.Min(s => p.Tot[s])} − {p.Tot[x]} − {p.Durees[x]} = {brut}") + (brut < 0 ? " → ramené à **0**." : Invariant($" : marge libre **{brut}**.")),
                ]),
            ],
            "Marge totale = tard − tôt (retard possible sans retarder la fin du projet). Marge libre = min(tôt des successeurs) − tôt − durée (retard possible sans gêner aucune tâche suivante).",
            "maths-sq3-s04-s03");
    }

    private static ExerciceTd Complet(ProjetMpm p, string intro)
    {
        var tot = Tot(p, intro);
        var tard = Tard(p, intro);
        var critique = Critique(p, intro);
        var marges = Marges(p, intro, new GenerateurAleatoire(0));
        return new("Étude complète d'un projet (MPM)", intro + "\n\nTrace le graphe MPM sur papier, puis réponds.",
            [
                tot.Questions[0] with { Lettre = "a" },
                tot.Questions[1] with { Lettre = "b" },
                tard.Questions[0] with { Lettre = "c" },
                critique.Questions[1] with { Lettre = "d" },
                marges.Questions[0] with { Lettre = "e" },
            ],
            "Ordre de travail : niveaux, graphe, dates au plus tôt (gauche → droite), dates au plus tard (droite → gauche), tâches critiques (tôt = tard), marges.",
            "maths-sq3-s01-s04");
    }

    // ==== Corrections ========================================================================

    private static List<string> EtapesNiveaux(ProjetMpm p)
    {
        var etapes = new List<string>();
        int max = p.Niveaux.Max();
        for (int k = 0; k <= max; k++)
        {
            var tour = Enumerable.Range(0, p.N).Where(t => p.Niveaux[t] == k).Select(t => NomTache(p, t)).ToList();
            etapes.Add(k == 0
                ? $"Sans antécédent : {Liste(tour)} → **niveau 0**. On les raye des antécédents."
                : $"N'ont plus d'antécédent : {Liste(tour)} → **niveau {k}**." + (k < max ? " On les raye à leur tour." : ""));
        }
        return etapes;
    }

    private static List<string> EtapesTot(ProjetMpm p)
    {
        var etapes = new List<string> { "On avance par niveaux croissants. Une tâche sans antécédent commence à 0." };
        foreach (var groupe in p.OrdreParNiveaux.GroupBy(t => p.Niveaux[t]))
            etapes.Add(string.Join(" ; ", groupe.Select(t => p.Anteriorites[t].Count == 0
                ? $"{NomTache(p, t)} : 0"
                : Max(p.Anteriorites[t].Select(a => Invariant($"{p.Tot[a]} + {p.Durees[a]} ({NomTache(p, a)})")), p.Tot[t], NomTache(p, t)))) + ".");
        var fin = Enumerable.Range(0, p.N).Where(t => p.Successeurs(t).Count == 0).ToList();
        etapes.Add(Max(fin.Select(a => Invariant($"{p.Tot[a]} + {p.Durees[a]} ({NomTache(p, a)})")), p.Duree, "FIN") + Invariant($" : le projet dure **{p.Duree} jours**."));
        return etapes;
    }

    private static List<string> EtapesTard(ProjetMpm p)
    {
        var etapes = new List<string> { Invariant($"FIN : tard = tôt = {p.Duree}. Puis on remonte de droite à gauche (niveaux décroissants).") };
        foreach (var groupe in p.OrdreParNiveaux.Reverse().GroupBy(t => p.Niveaux[t]))
            etapes.Add(string.Join(" ; ", groupe.Select(t =>
            {
                var succ = p.SuccesseursOuFin(t);
                string calculs = string.Join(", ", succ.Select(s => Invariant($"{p.Tard[s]} − {p.Durees[t]} ({NomTache(p, s)})")));
                return succ.Count == 1
                    ? Invariant($"{NomTache(p, t)} : {calculs} = {p.Tard[t]}")
                    : Invariant($"{NomTache(p, t)} : min({calculs}) = {p.Tard[t]}");
            })) + ".");
        return etapes;
    }

    private static string Max(IEnumerable<string> termes, int resultat, string nom)
    {
        var l = termes.ToList();
        return l.Count == 1 ? Invariant($"{nom} : {l[0]} = {resultat}") : Invariant($"{nom} : max({string.Join(", ", l)}) = {resultat}");
    }

    // ==== Tirage et présentation =============================================================

    /// <summary>
    /// Tire un projet réaliste : 5, 7 ou 9 tâches réparties sur 3 à 5 niveaux. Chaque tâche a un antécédent
    /// au niveau juste avant, parfois un second plus tôt, jamais un antécédent redondant (déjà implicite par un autre).
    /// Au niveau 2 et plus, les lettres sont mélangées. On garde un projet avec des marges et au plus 2 tâches finales.
    /// </summary>
    public static ProjetMpm Tirer(GenerateurAleatoire r, int niveau, bool cheminUnique)
    {
        int n = niveau switch { 1 => 5, 2 => 7, _ => 9 };
        int etages = niveau switch { 1 => 3, 2 => r.Entier(3, 4), _ => r.Entier(4, 5) };
        while (true)
        {
            // Répartition des tâches (positions 0..n-1) par niveau : au moins une par niveau, 1 ou 2 au départ.
            var niveauDe = new int[n];
            int departs = niveau == 1 ? r.Entier(1, 2) : 2;
            for (int k = 0; k < n; k++)
                niveauDe[k] = k < departs ? 0 : k - departs < etages - 1 ? k - departs + 1 : r.Entier(1, etages - 1);
            var positions = Enumerable.Range(0, n).OrderBy(k => niveauDe[k]).ThenBy(k => k).ToList();

            var lettres = Enumerable.Range(0, n).ToList();
            if (niveau >= 2) r.Melanger(lettres);
            int Tache(int position) => lettres[position];

            var durees = new int[n];
            var ant = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            foreach (int k in positions)
            {
                int t = Tache(k);
                durees[t] = r.Entier(1, niveau == 1 ? 9 : 15);
                int nv = niveauDe[k];
                if (nv == 0) continue;
                var juste = positions.Where(j => niveauDe[j] == nv - 1).ToList();
                ant[t].Add(Tache(r.Choisir(juste)));
                var autres = positions.Where(j => niveauDe[j] < nv && !ant[t].Contains(Tache(j))).ToList();
                if (autres.Count > 0 && r.Entier(1, 100) <= (niveau == 1 ? 30 : 50)) ant[t].Add(Tache(r.Choisir(autres)));
            }
            // Retire les antécédents redondants (ancêtres d'un autre antécédent).
            for (int t = 0; t < n; t++)
                ant[t] = ant[t].Where(a => !ant[t].Any(b => b != a && Ancetres(ant, b).Contains(a))).Order().ToList();

            var projet = new ProjetMpm(durees, ant);
            int finales = Enumerable.Range(0, n).Count(t => projet.Successeurs(t).Count == 0);
            int critiques = Enumerable.Range(0, n).Count(projet.EstCritique);
            bool unique = projet.CheminsCritiques().Count == 1;
            if (finales <= 2 && critiques <= n - 2 && (!cheminUnique || unique)) return projet;
        }
    }

    private static HashSet<int> Ancetres(List<int>[] ant, int t)
    {
        var vus = new HashSet<int>();
        var pile = new Stack<int>(ant[t]);
        while (pile.Count > 0)
            if (vus.Add(pile.Peek())) foreach (int a in ant[pile.Pop()]) pile.Push(a);
            else pile.Pop();
        return vus;
    }
    private static string NomTache(ProjetMpm p, int t) => t == p.N ? "FIN" : Lettre(t);

    private static List<string> Taches(ProjetMpm p, bool fin) =>
        [.. Enumerable.Range(0, p.N).Select(Lettre), .. fin ? ["FIN"] : Array.Empty<string>()];

    private static string TableauTaches(ProjetMpm p) => Tableau(["Tâche", "Durée (jours)", "Antécédents"],
        Enumerable.Range(0, p.N).Select(t => (IReadOnlyList<string>)[Lettre(t), Nombre(p.Durees[t]), Liste(p.Anteriorites[t].Select(Lettre), "–")]));

    private static string TableauDates(ProjetMpm p, bool tard)
    {
        var lignes = new List<IReadOnlyList<string>> { (IReadOnlyList<string>)["Au plus tôt", .. p.Tot.Select(Nombre)] };
        if (tard) lignes.Add(["Au plus tard", .. p.Tard.Select(Nombre)]);
        return Tableau(["Tâche", .. Taches(p, fin: true)], lignes);
    }
}
