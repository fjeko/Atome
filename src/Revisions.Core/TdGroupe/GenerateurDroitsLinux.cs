using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « Droits Linux » (bloc 3, droits basiques) : notation rwx et octale, effet d'un chmod symbolique,
/// umask et droits par défaut, qui peut lire, modifier ou entrer. Les utilisateurs et les fichiers sont inventés.
/// </summary>
public sealed class GenerateurDroitsLinux : GenerateurASujets
{
    public override string Prefixe => "DRL";
    public override string Matiere => "Cybersécurité";
    public override string Theme => "Droits Linux";

    private const string SectionUgo = "b3-b33-sq3-s01-s05";
    private const string SectionChmod = "b3-b33-sq3-s01-s06";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("octal", "Notation rwx et octale", SectionChmod, [3, 4, 5]),
        new("chmod", "Effet d'un chmod symbolique", SectionChmod, [3, 5, 6]),
        new("umask", "umask et droits par défaut", SectionChmod, [3, 4, 6]),
        new("acces", "Qui peut faire quoi ?", SectionUgo, [4, 5, 6]),
    ];

    private static readonly string[] Classes = ["propriétaire (u)", "groupe (g)", "autres (o)"];
    private static readonly string[] Fichiers = ["rapport.odt", "budget.ods", "sauvegarde.sh", "notes.txt", "inventaire.csv", "deploiement.sh"];
    private static readonly string[] Dossiers = ["partage", "projets", "compta", "archives", "public"];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) => cle switch
    {
        "octal" => Octal(r, niveau),
        "chmod" => Chmod(r, niveau),
        "umask" => Umask(r, niveau),
        "acces" => Acces(r, niveau),
        _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
    };

    private static ExerciceTd Octal(GenerateurAleatoire r, int niveau)
    {
        int d1 = Droits(r, niveau), d2;
        do d2 = Droits(r, niveau); while (d2 == d1);
        return new ExerciceTd("Notation rwx et octale",
            "Chaque groupe de 3 permissions (propriétaire, groupe, autres) s'écrit avec un chiffre de 0 à 7 : r = 4, w = 2, x = 1.",
            [
                new("a", $"Quelle est la notation octale de `{Rwx(d1)}` ?", "octal", new ReponseTexte(Octal(d1), Exemple: "750"),
                    EtapesVersOctal(d1)),
                new("b", $"Quelles permissions donne `chmod {Octal(d2)} fichier` (notation rwx sur 9 caractères) ?", "rwx",
                    new ReponseTexte(Rwx(d2), Exemple: "rwxr-x---", Variantes: ["-" + Rwx(d2)]),
                    [
                        "Chaque chiffre se décompose en 4 (r) + 2 (w) + 1 (x).",
                        string.Join(" ; ", Enumerable.Range(0, 3).Select(k => Invariant($"{Chiffre(d2, k)} = {Decomposer(Chiffre(d2, k))} → {Rwx(d2).Substring(3 * k, 3)}"))) + ".",
                        $"D'où **{Rwx(d2)}**.",
                    ]),
            ],
            "r = 4, w = 2, x = 1 : on additionne dans chaque groupe. Exemple : rwx = 7, r-x = 5, rw- = 6, r-- = 4.",
            SectionChmod);
    }

    private static ExerciceTd Chmod(GenerateurAleatoire r, int niveau)
    {
        int depart = Droits(r, niveau);
        string fichier = r.Choisir(Fichiers);
        int nb = niveau == 1 ? 1 : 2;
        var commandes = new List<string>();
        var etapes = new List<string> { $"Départ : `{Rwx(depart)}`." };
        int droits = depart;
        for (int k = 0; k < nb; k++)
        {
            // Chaque commande change vraiment quelque chose, et ne défait pas la précédente.
            string qui, quoi;
            char signe;
            int avant = droits;
            do
            {
                qui = r.Choisir(["u", "g", "o", "go", "ug", "a"]);
                signe = r.PileOuFace() ? '+' : '-';
                quoi = r.Choisir(["r", "w", "x", "rw", "rx", "wx", "rwx"]);
                droits = Appliquer(avant, qui, signe, quoi);
            } while (droits == avant || droits == depart || (droits & 0x100) == 0); // le propriétaire garde la lecture
            string commande = $"chmod {qui}{signe}{quoi} {fichier}";
            commandes.Add(commande);
            string cibles = string.Join(", ", qui.Replace("a", "ugo").Select(c => c switch { 'u' => "propriétaire", 'g' => "groupe", _ => "autres" }));
            etapes.Add($"`{commande}` : {(signe == '+' ? "ajoute" : "retire")} {string.Join(", ", quoi.Select(c => c.ToString()))} pour : {cibles}. `{Rwx(avant)}` → `{Rwx(droits)}`.");
        }
        var questions = new List<QuestionTd>
        {
            new("a", "Quelles sont les permissions du fichier après ces commandes ?", "rwx final",
                new ReponseTexte(Rwx(droits), Exemple: "rw-r-----", Variantes: ["-" + Rwx(droits)]), etapes),
        };
        if (niveau >= 2)
            questions.Add(new("b", "Quelle commande chmod en mode octal donne le même résultat ?", "chmod ???",
                new ReponseTexte(Octal(droits), Exemple: "640", Variantes: [$"chmod {Octal(droits)}", $"chmod {Octal(droits)} {fichier}"]),
                EtapesVersOctal(droits)));

        return new ExerciceTd("Effet d'un chmod",
            $"`ls -l` affiche :\n\n```text\n-{Rwx(depart)} 1 alice compta 2048 3 nov. {fichier}\n```\n\nalice exécute dans l'ordre :\n\n```text\n{string.Join("\n", commandes)}\n```",
            questions,
            "Mode symbolique : u (propriétaire), g (groupe), o (autres), a (tous) ; + ajoute, − retire ; les autres permissions ne bougent pas.",
            SectionChmod);
    }

    private static ExerciceTd Umask(GenerateurAleatoire r, int niveau)
    {
        string[] umasks = niveau == 1 ? ["022", "002"] : ["022", "027", "077", "002", "007", "037"];
        int umask = Convert.ToInt32(r.Choisir(umasks), 8);
        int fichier = 0x1B6 & ~umask, dossier = 0x1FF & ~umask; // 666 et 777 en octal
        var questions = new List<QuestionTd>
        {
            new("a", "Quels droits (en octal) aura un nouveau **fichier** ?", "fichier (octal)", new ReponseTexte(Octal(fichier), Exemple: "644"),
                EtapesUmask(0x1B6, umask, "fichier")),
            new("b", "Et un nouveau **répertoire** ?", "répertoire (octal)", new ReponseTexte(Octal(dossier), Exemple: "755"),
                EtapesUmask(0x1FF, umask, "répertoire")),
        };
        if (niveau >= 2)
            questions.Add(new("c", "Écris les droits du nouveau fichier en notation rwx.", "fichier (rwx)",
                new ReponseTexte(Rwx(fichier), Exemple: "rw-r--r--", Variantes: ["-" + Rwx(fichier)]),
                [$"{Octal(fichier)} → **{Rwx(fichier)}**."]));
        return new ExerciceTd("umask et droits par défaut",
            $"Un utilisateur a réglé `umask {Octal(umask)}`.",
            questions,
            "À la création : fichier 666 (rw-rw-rw-), répertoire 777 (rwxrwxrwx). Le umask **retire** ses droits : avec umask 022, un fichier devient 644 et un répertoire 755.",
            SectionChmod);
    }

    private static ExerciceTd Acces(GenerateurAleatoire r, int niveau)
    {
        bool estDossier = niveau >= 2 && r.PileOuFace();
        int droits = Droits(r, Math.Max(2, niveau));
        string nom = estDossier ? r.Choisir(Dossiers) : r.Choisir(Fichiers);
        string ligne = $"{(estDossier ? 'd' : '-')}{Rwx(droits)} 2 alice compta 4096 3 nov. {nom}";

        // bob est dans le groupe compta, chloe n'y est pas (autres). On interroge chacun sur un droit.
        var personnes = new (string Nom, int Classe, string Situation)[]
        {
            ("alice", 0, "la propriétaire"),
            ("bob", 1, "membre du groupe compta"),
            ("chloe", 2, "dans les « autres »"),
        };
        var actions = estDossier
            ? new (int Bit, string Texte)[] { (2, "lister son contenu (ls)"), (0, "y entrer (cd)"), (1, "y créer un fichier") }
            : [(2, "lire le fichier"), (1, "modifier le fichier"), (0, "exécuter le fichier")];
        var questions = new List<QuestionTd>();
        var ordre = Enumerable.Range(0, 3).ToList();
        r.Melanger(ordre);
        foreach (int k in ordre.Take(niveau == 1 ? 2 : 3))
        {
            var p = personnes[k];
            var a = actions[r.Entier(0, actions.Length - 1)];
            string groupe = Rwx(droits).Substring(3 * p.Classe, 3);
            bool peut = estDossier && a.Bit == 1
                ? (droits >> (3 * (2 - p.Classe)) & 0b011) == 0b011  // créer : w ET x sur le répertoire
                : (droits >> (3 * (2 - p.Classe) + a.Bit) & 1) == 1;
            string lettre = a.Bit switch { 2 => "r", 1 => "w", _ => "x" };
            questions.Add(new(((char)('a' + questions.Count)).ToString(), $"L'utilisateur {p.Nom} peut-il {a.Texte} ?", $"{p.Nom} : {a.Texte}", OuiNon(peut),
                [
                    $"{p.Nom} est {p.Situation} : on lit le groupe de droits **{Classes[p.Classe]}** = `{groupe}`.",
                    estDossier && a.Bit == 1
                        ? $"Créer dans un répertoire demande **w et x** : {(peut ? "**oui**." : "**non**, il manque un des deux.")}"
                        : $"Il faut `{lettre}` : {(peut ? "présent, **oui**." : "absent, **non**.")}",
                ]));
        }
        return new ExerciceTd(estDossier ? "Droits sur un répertoire" : "Droits sur un fichier",
            $"`ls -l` affiche :\n\n```text\n{ligne}\n```\n\nalice est la propriétaire ; bob est membre du groupe compta ; chloe n'est pas dans ce groupe.",
            questions,
            estDossier
                ? "Répertoire : r = lister (ls), x = entrer (cd), w = créer ou supprimer dedans (avec x). On lit d'abord à quelle classe appartient l'utilisateur : u, g ou o."
                : "Fichier : r = lire, w = modifier, x = exécuter. On lit le groupe de 3 lettres de la classe de l'utilisateur : propriétaire, groupe ou autres.",
            SectionUgo);
    }

    // ==== Outils =============================================================================

    /// <summary>Droits sur 9 bits, choisis parmi des réglages réalistes (le propriétaire a au moins la lecture).</summary>
    private static int Droits(GenerateurAleatoire r, int niveau)
    {
        int[] courants = new[] { "755", "750", "644", "640", "600", "700", "664", "775", "770", "660", "751", "741", "754", "711", "604", "650", "570", "645" }
            .Select(o => Convert.ToInt32(o, 8)).ToArray();
        return niveau == 1 ? r.Choisir(courants.Take(10).ToArray()) : r.Choisir(courants);
    }

    private static int Appliquer(int droits, string qui, char signe, string quoi)
    {
        int masque = 0;
        foreach (char c in qui.Replace("a", "ugo"))
        {
            int decalage = c switch { 'u' => 6, 'g' => 3, _ => 0 };
            foreach (char d in quoi) masque |= (d switch { 'r' => 4, 'w' => 2, _ => 1 }) << decalage;
        }
        return signe == '+' ? droits | masque : droits & ~masque;
    }

    public static string Rwx(int droits) => new(Enumerable.Range(0, 9).Select(k => (droits >> (8 - k) & 1) == 1 ? "rwx"[k % 3] : '-').ToArray());

    public static string Octal(int droits) => Invariant($"{droits >> 6 & 7}{droits >> 3 & 7}{droits & 7}");

    private static int Chiffre(int droits, int classe) => droits >> (3 * (2 - classe)) & 7;

    private static string Decomposer(int chiffre)
    {
        var parts = new[] { (4, "4"), (2, "2"), (1, "1") }.Where(p => (chiffre & p.Item1) != 0).Select(p => p.Item2).ToList();
        return parts.Count == 0 ? "0" : string.Join(" + ", parts);
    }

    private static List<string> EtapesVersOctal(int droits)
    {
        string rwx = Rwx(droits);
        return
        [
            "On coupe en 3 groupes de 3 lettres et on additionne r = 4, w = 2, x = 1 dans chaque groupe.",
            string.Join(" ; ", Enumerable.Range(0, 3).Select(k => Chiffre(droits, k) == 0 ? Invariant($"{rwx.Substring(3 * k, 3)} = 0") : Invariant($"{rwx.Substring(3 * k, 3)} = {Decomposer(Chiffre(droits, k))} = {Chiffre(droits, k)}"))) + ".",
            $"D'où **{Octal(droits)}**.",
        ];
    }

    private static List<string> EtapesUmask(int base_, int umask, string quoi) =>
    [
        $"Un nouveau {quoi} part de {Octal(base_)} (`{Rwx(base_)}`) ; le umask {Octal(umask)} (`{Rwx(umask)}`) retire ses droits.",
        string.Join(" ; ", Enumerable.Range(0, 3).Select(k =>
            $"{new[] { "propriétaire", "groupe", "autres" }[k]} : `{Rwx(base_).Substring(3 * k, 3)}` moins `{Rwx(umask).Substring(3 * k, 3)}` → `{Rwx(base_ & ~umask).Substring(3 * k, 3)}` ({Chiffre(base_ & ~umask, k)})")) + ".",
        $"D'où **{Octal(base_ & ~umask)}**.",
    ];
}
