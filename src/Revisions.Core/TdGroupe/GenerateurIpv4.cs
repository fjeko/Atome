using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// TD « Adressage IPv4 » (bloc 1, fiche savoirs IPv4) : masque en notation CIDR et décimale pointée,
/// adresse du réseau (ET logique), adresse de diffusion, nombre d'hôtes et plage attribuable, même réseau ou non.
/// Les adresses sont des adresses privées tirées au hasard.
/// </summary>
public sealed class GenerateurIpv4 : GenerateurASujets
{
    public override string Prefixe => "IPV";
    public override string Matiere => "Réseau";
    public override string Theme => "Adressage IPv4";

    private const string SectionMasque = "b1-b12p2-sq2-s02-f1-s03";
    private const string SectionReseau = "b1-b12p2-sq2-s02-f1-s04";
    private const string SectionPlage = "b1-b12p2-sq2-s02-f1-s06";

    public override IReadOnlyList<SujetTd> Sujets { get; } =
    [
        new("masque", "Masque : /n et décimale pointée", SectionMasque, [3, 4, 5]),
        new("reseau", "Adresse du réseau", SectionReseau, [3, 5, 6]),
        new("plage", "Diffusion, nombre d'hôtes et plage", SectionPlage, [4, 6, 8]),
        new("meme", "Même réseau ?", SectionReseau, [3, 5, 6]),
    ];

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) => cle switch
    {
        "masque" => Masque(r, niveau),
        "reseau" => Reseau(r, niveau),
        "plage" => Plage(r, niveau),
        "meme" => Meme(r, niveau),
        _ => throw new ArgumentException($"Sujet inconnu : {cle}", nameof(cle)),
    };

    private static ExerciceTd Masque(GenerateurAleatoire r, int niveau)
    {
        int n1 = LongueurMasque(r, niveau);
        int n2;
        do n2 = LongueurMasque(r, niveau); while (n2 == n1 && niveau > 1);
        var questions = new List<QuestionTd>
        {
            new("a", Invariant($"Écris le masque /{n1} en décimale pointée."), Invariant($"masque /{n1}"),
                new ReponseTexte(Ecrire(MasqueDe(n1)), Exemple: "255.255.0.0"), EtapesMasque(n1)),
            new("b", $"Le masque {Ecrire(MasqueDe(n2))} s'écrit /n en notation CIDR. Que vaut n ?", "n",
                new ReponseNombre(n2),
                [
                    "On écrit chaque octet en binaire et on compte les **1** (ils sont tous à gauche).",
                    string.Join(" . ", Octets(MasqueDe(n2)).Select(Binaire)) + Invariant($" : **{n2}** bits à 1, soit /{n2}."),
                ]),
        };
        if (niveau >= 2)
        {
            int dernier = r.Choisir([128, 192, 224, 240, 248, 252, 254, 193, 160, 250]);
            int zeros = 255 - dernier;                    // bits à 0 du masque, en bas de l'octet
            bool valide = (zeros & (zeros + 1)) == 0;      // vrai si ces 0 sont tous à droite (000…0111)
            questions.Add(new("c", Invariant($"255.255.255.{dernier} est-il un masque valide ?"), "masque valide ?", OuiNon(valide),
                [
                    Invariant($"{dernier} s'écrit {Binaire((byte)dernier)} en binaire."),
                    valide ? "Les 1 sont continus à gauche : **oui**, c'est un masque valide."
                           : "Les 1 ne sont pas continus (un 0 au milieu) : **non**, ce n'est pas un masque.",
                ]));
        }
        return new ExerciceTd("Le masque de sous-réseau", "Le masque est une suite continue de 1 (à gauche) puis de 0, sur 32 bits.",
            questions,
            "La notation /n donne le **nombre de bits à 1** du masque. Valeurs d'un octet : 10000000 = 128, 11000000 = 192, 11100000 = 224, 11110000 = 240, 11111000 = 248, 11111100 = 252, 11111110 = 254, 11111111 = 255.",
            SectionMasque);
    }

    private static ExerciceTd Reseau(GenerateurAleatoire r, int niveau)
    {
        var (ip, n) = Adresse(r, niveau);
        uint reseau = ip & MasqueDe(n);
        return new ExerciceTd("Adresse du réseau",
            Invariant($"Un poste a l'adresse **{Ecrire(ip)}/{n}**."),
            [
                new("a", "Quelle est l'adresse du réseau de ce poste ?", "adresse du réseau",
                    new ReponseTexte(Ecrire(reseau), Exemple: "192.168.1.0"), EtapesEt(ip, n)),
            ],
            "Adresse du réseau = adresse **ET** masque, bit à bit (1 ET 1 = 1, sinon 0). Avec un masque rond (/8, /16, /24) : on garde les octets à 255 et on met 0 ailleurs.",
            SectionReseau);
    }

    private static ExerciceTd Plage(GenerateurAleatoire r, int niveau)
    {
        var (ip, n) = Adresse(r, niveau);
        uint masque = MasqueDe(n);
        uint reseau = ip & masque, diffusion = reseau | ~masque;
        long hotes = (1L << (32 - n)) - 2;
        int bitsHote = 32 - n;
        return new ExerciceTd("Diffusion et plage d'adresses",
            Invariant($"On considère le réseau qui contient l'adresse **{Ecrire(ip)}/{n}** (adresse du réseau : {Ecrire(reseau)})."),
            [
                new("a", "Quelle est l'adresse de diffusion (broadcast) de ce réseau ?", "diffusion",
                    new ReponseTexte(Ecrire(diffusion), Exemple: "192.168.1.255"),
                    [
                        Invariant($"La diffusion a **tous les bits d'hôte à 1** : les {bitsHote} derniers bits."),
                        Invariant($"Réseau {Ecrire(reseau)} + bits d'hôte à 1 → **{Ecrire(diffusion)}**."),
                    ]),
                new("b", "Combien de machines peut-on adresser dans ce réseau ?", "nombre d'hôtes", new ReponseNombre((int)hotes),
                    [
                        Invariant($"{bitsHote} bits d'hôte donnent $2^{{{bitsHote}}} = {hotes + 2}$ adresses."),
                        Invariant($"On retire l'adresse du réseau et celle de diffusion : $2^{{{bitsHote}}} - 2 = {hotes}$ hôtes."),
                    ]),
                new("c", "Quelle est la première adresse attribuable à une machine ?", "première adresse",
                    new ReponseTexte(Ecrire(reseau + 1), Exemple: "192.168.1.1"),
                    [Invariant($"Juste après l'adresse du réseau : **{Ecrire(reseau + 1)}**.")]),
                new("d", "Et la dernière ?", "dernière adresse",
                    new ReponseTexte(Ecrire(diffusion - 1), Exemple: "192.168.1.254"),
                    [Invariant($"Juste avant l'adresse de diffusion : **{Ecrire(diffusion - 1)}**.")]),
            ],
            "L'adresse du réseau (bits d'hôte à 0) et celle de diffusion (bits d'hôte à 1) ne s'attribuent pas : les hôtes vont de réseau + 1 à diffusion − 1, soit $2^{32-n} - 2$ machines.",
            SectionPlage);
    }

    private static ExerciceTd Meme(GenerateurAleatoire r, int niveau)
    {
        var (ip1, n) = Adresse(r, niveau);
        uint masque = MasqueDe(n);
        bool meme = r.PileOuFace();
        uint ip2;
        do
        {
            uint hote = (uint)r.Entier(1, (int)Math.Min(int.MaxValue, (long)(~masque) - 1));
            uint reseau = ip1 & masque;
            if (!meme)
            {
                // Un réseau voisin : on change un bit de la partie réseau, près de la frontière.
                int bit = 32 - n + r.Entier(0, Math.Min(3, n - 1)); // un des 4 derniers bits de la partie réseau
                reseau ^= 1u << bit;
            }
            ip2 = reseau | (hote & ~masque);
        } while (ip2 == ip1 || ((ip1 & masque) == (ip2 & masque)) != meme);
        uint r1 = ip1 & masque, r2 = ip2 & masque;
        return new ExerciceTd("Même réseau ?",
            Invariant($"Deux postes ont les adresses **{Ecrire(ip1)}** et **{Ecrire(ip2)}**, avec le masque /{n} ({Ecrire(masque)})."),
            [
                new("a", "Ces deux postes sont-ils dans le même réseau ?", "même réseau ?", OuiNon(meme),
                    [
                        "Deux adresses sont dans le même réseau si elles donnent la **même adresse réseau** (adresse ET masque).",
                        Invariant($"{Ecrire(ip1)} ET {Ecrire(masque)} = {Ecrire(r1)}."),
                        Invariant($"{Ecrire(ip2)} ET {Ecrire(masque)} = {Ecrire(r2)}."),
                        meme ? "Même adresse réseau : **oui**, même réseau." : "Adresses réseau différentes : **non**, il faut un routeur pour communiquer.",
                    ]),
            ],
            "Calcule l'adresse du réseau de chaque poste (adresse ET masque) et compare-les.",
            SectionReseau);
    }

    // ==== Outils =============================================================================

    /// <summary>Longueur de masque : /8, /16 ou /24 au niveau 1 ; de /17 à /30 ensuite (plus difficile au niveau 3).</summary>
    private static int LongueurMasque(GenerateurAleatoire r, int niveau) => niveau switch
    {
        1 => r.Choisir([8, 16, 24]),
        2 => r.Choisir([16, 20, 22, 24, 25, 26, 27, 28]),
        _ => r.Entier(17, 30),
    };

    /// <summary>Adresse privée et longueur de masque cohérente (10/8, 172.16/12, 192.168/16).</summary>
    private static (uint Ip, int N) Adresse(GenerateurAleatoire r, int niveau)
    {
        int n = LongueurMasque(r, niveau);
        int plage = n < 12 ? 0 : n < 16 ? r.Entier(0, 1) : r.Entier(0, 2);
        uint ip = plage switch
        {
            0 => Construire(10, r.Entier(0, 255), r.Entier(0, 255), r.Entier(1, 254)),
            1 => Construire(172, r.Entier(16, 31), r.Entier(0, 255), r.Entier(1, 254)),
            _ => Construire(192, 168, r.Entier(0, 255), r.Entier(1, 254)),
        };
        uint masque = MasqueDe(n);
        // Ni l'adresse du réseau ni celle de diffusion : un vrai poste.
        if ((ip & ~masque) == 0) ip += 1;
        if ((ip | masque) == uint.MaxValue) ip -= 1;
        return (ip, n);
    }

    private static uint Construire(int a, int b, int c, int d) => ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | (uint)d;

    public static uint MasqueDe(int n) => n == 0 ? 0 : uint.MaxValue << (32 - n);

    public static string Ecrire(uint ip) => string.Join(".", Octets(ip));

    private static byte[] Octets(uint ip) => [(byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip];

    private static string Binaire(byte octet) => Convert.ToString(octet, 2).PadLeft(8, '0');

    private static List<string> EtapesMasque(int n)
    {
        var octets = Octets(MasqueDe(n));
        return
        [
            Invariant($"/{n} : {n} bits à 1 puis {32 - n} bits à 0, groupés par octets de 8 bits."),
            string.Join(" . ", octets.Select(Binaire)),
            "En décimal : " + string.Join(" . ", octets.Select(o => Invariant($"{o}"))) + Invariant($", soit **{Ecrire(MasqueDe(n))}**."),
        ];
    }

    /// <summary>Correction du ET logique : octets ronds recopiés ou mis à 0, l'octet coupé détaillé en binaire.</summary>
    private static List<string> EtapesEt(uint ip, int n)
    {
        var o = Octets(ip);
        var m = Octets(MasqueDe(n));
        var res = Octets(ip & MasqueDe(n));
        var etapes = new List<string> { Invariant($"Masque /{n} = {Ecrire(MasqueDe(n))}. On fait adresse ET masque, octet par octet.") };
        var ronds = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            if (m[k] == 255) ronds.Add(Invariant($"octet {k + 1} : masque 255 → on garde {o[k]}"));
            else if (m[k] == 0) ronds.Add(Invariant($"octet {k + 1} : masque 0 → 0"));
            else etapes.Add(Invariant($"Octet {k + 1} (masque {m[k]}) : {Binaire(o[k])} ET {Binaire(m[k])} = {Binaire(res[k])}, soit **{res[k]}**."));
        }
        etapes.Insert(1, string.Join(" ; ", ronds) + ".");
        etapes.Add(Invariant($"Adresse du réseau : **{Ecrire(ip & MasqueDe(n))}**."));
        return etapes;
    }
}
