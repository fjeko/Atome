using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Revisions.Core.Voix;

/// <summary>Moteur qui lit le texte, du plus naturel au plus robotique.</summary>
public enum MoteurVoix
{
    /// <summary>Aucune voix pour cette langue : les boutons « écouter » restent sans effet.</summary>
    Aucun,
    /// <summary>Voix neuronale Piper, téléchargée une fois (anglais uniquement).</summary>
    Piper,
    /// <summary>Synthèse vocale moderne de Windows (WinRT).</summary>
    Windows,
    /// <summary>Ancien moteur vocal de Windows (SAPI), par exemple « Microsoft Zira Desktop ».</summary>
    Sapi,
}

/// <summary>Règles de choix de la voix : quel moteur, pour quels contenus.</summary>
public static class ChoixVoix
{
    /// <summary>Langue de la voix anglaise d'Atome.</summary>
    public const string Anglais = "en-GB";

    /// <summary>
    /// Piper d'abord (s'il est installé et que la langue est l'anglais), sinon la voix moderne de Windows,
    /// sinon l'ancienne (SAPI). Tant que Piper se télécharge, on garde donc la voix de Windows.
    /// </summary>
    public static MoteurVoix Choisir(string langue, bool piperPret, bool voixWindows, bool voixSapi)
    {
        if (piperPret && EstAnglais(langue)) return MoteurVoix.Piper;
        if (voixWindows) return MoteurVoix.Windows;
        if (voixSapi) return MoteurVoix.Sapi;
        return MoteurVoix.Aucun;
    }

    /// <summary>Vrai pour « en », « en-GB », « en-US »…</summary>
    public static bool EstAnglais(string? langue) =>
        langue is not null && (langue.Equals("en", StringComparison.OrdinalIgnoreCase) || langue.StartsWith("en-", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Langue d'écoute d'un pack, ou null s'il ne s'écoute pas : les packs d'anglais (identifiant « en-… »,
    /// ou matière « Anglais… ») ont des boutons 🔊. Aucun champ nouveau n'est nécessaire dans les packs.
    /// </summary>
    public static string? LanguePourPack(string? packId, string? matiere)
    {
        if (packId is not null && packId.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return Anglais;
        if (matiere is not null && matiere.TrimStart().StartsWith("Anglais", StringComparison.OrdinalIgnoreCase)) return Anglais;
        return null;
    }

    /// <summary>Prénoms féminins et masculins des dialogues des cours (les autres rôles se répartissent en alternance).</summary>
    private static readonly HashSet<string> Feminins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Anna", "Jade", "Léa", "Lea", "Emma", "Sarah", "Lucy", "Kate", "Mary", "Julie", "Sophie", "Alice", "Jane", "Lisa",
        "Laura", "Amy", "Emily", "Olivia", "Chloe", "Chloé", "Claire", "Rachel", "Hannah", "Mia", "Zoe", "Zoé", "Nina", "Eva",
        "Linda", "Susan", "Helen", "Grace", "Ella", "Jess", "Jessica", "Manon", "Camille", "Inès", "Ines",
    };
    private static readonly HashSet<string> Masculins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tom", "Max", "John", "Mark", "James", "Paul", "David", "Peter", "Mike", "Ben", "Jack", "Leo", "Léo", "Harry", "Oliver",
        "Dan", "Daniel", "Lucas", "Hugo", "Florian", "Nathan", "Ryan", "Chris", "Steve", "George", "Henry", "Adam", "Luke",
        "Matt", "Nick", "Joe", "Bob", "Bill", "Kevin", "Mr", "Sir",
    };

    /// <summary>
    /// Timbre de chaque personnage d'un dialogue, pour que deux personnes ne parlent pas avec la même voix.
    /// Prénom connu : son timbre ; rôle inconnu (« Waiter », « A », « You ») : le timbre le moins utilisé jusque-là,
    /// en commençant par féminin. Les noms sont donnés dans l'ordre où ils parlent (doublons permis).
    /// </summary>
    public static Dictionary<string, Timbre> AttribuerTimbres(IEnumerable<string> nomsDansLOrdre)
    {
        var resultat = new Dictionary<string, Timbre>(StringComparer.OrdinalIgnoreCase);
        var noms = nomsDansLOrdre.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // D'abord les prénoms connus, pour que les rôles inconnus prennent l'autre timbre.
        foreach (string nom in noms)
        {
            string prenom = nom.Split(' ', '.')[0];
            if (Feminins.Contains(prenom)) resultat[nom] = Timbre.Feminin;
            else if (Masculins.Contains(prenom)) resultat[nom] = Timbre.Masculin;
        }
        foreach (string nom in noms.Where(n => !resultat.ContainsKey(n)))
        {
            int feminins = resultat.Values.Count(t => t == Timbre.Feminin);
            int masculins = resultat.Count - feminins;
            resultat[nom] = feminins <= masculins ? Timbre.Feminin : Timbre.Masculin;
        }
        return resultat;
    }

    /// <summary>Texte tel qu'il est lu : espaces et retours à la ligne réduits à une espace, forme Unicode normalisée.</summary>
    public static string Normaliser(string texte)
    {
        var sortie = new StringBuilder(texte.Length);
        bool espace = false;
        foreach (char c in texte.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c)) { espace = sortie.Length > 0; continue; }
            if (espace) { sortie.Append(' '); espace = false; }
            sortie.Append(c);
        }
        return sortie.ToString();
    }

    /// <summary>
    /// Nom du fichier audio en cache pour une phrase, une voix et une vitesse. La même phrase relue
    /// à la même vitesse retrouve le même fichier : la relecture est immédiate. Les espaces en trop
    /// ne changent pas la clé ; la vitesse est arrondie au centième.
    /// </summary>
    public static string CleCache(string texte, string voix, double vitesse)
    {
        string source = string.Join('|', voix, Math.Round(vitesse, 2).ToString("0.00", CultureInfo.InvariantCulture), Normaliser(texte));
        byte[] empreinte = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexStringLower(empreinte)[..32] + ".wav";
    }

    /// <summary>
    /// Réglage de Piper pour une vitesse : « length_scale » allonge les sons (1,25 = plus lent).
    /// Vitesse bornée entre 0,5 et 2.
    /// </summary>
    public static double EchelleDuree(double vitesse) => Math.Round(1 / Math.Clamp(vitesse, 0.5, 2.0), 3);
}
