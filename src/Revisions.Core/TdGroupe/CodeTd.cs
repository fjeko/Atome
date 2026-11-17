using System.Text;
using System.Text.RegularExpressions;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// Code d'un TD, à dicter à l'oral sur Discord. Exemple : <c>MAT1-2-8-48217</c>.
/// <code>
///   MAT1    type de TD (MAT = matrices) suivi de la version du générateur (1)
///   2       niveau (1 à 3)
///   8       nombre d'exercices
///   48217   graine du hasard (4821) suivie d'un chiffre de contrôle (7)
///   [-id]   pour un TD « séance » seulement : l'identifiant du pack (maths-sq1-s03)
/// </code>
/// Le chiffre de contrôle repère une faute de frappe : un chiffre mal entendu donne un code refusé,
/// au lieu d'un TD différent sans que personne ne s'en rende compte.
/// </summary>
public static partial class CodeTd
{
    public const int NiveauMin = 1, NiveauMax = 3;
    public const int NombreMin = 1, NombreMax = 20;
    public const int GraineMax = 9999;

    // Séparateurs acceptés à la saisie : tiret, espace, point, tiret bas (on dicte souvent « MAT1 2 8 … »).
    [GeneratedRegex(@"^([A-Z]{2,5})(\d{1,3})[\s\-._]+(\d)[\s\-._]+(\d{1,2})[\s\-._]+(\d{5})(?:[\s\-._]+([a-z0-9][a-z0-9\-_.]*))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Motif();

    /// <summary>Écrit le code d'un TD (forme canonique : majuscules, tirets).</summary>
    public static string Ecrire(ParametresTd p)
    {
        string debut = Canonique(p);
        int cle = ChiffreDeControle(debut);
        string code = $"{p.Type.ToUpperInvariant()}{p.Version}-{p.Niveau}-{p.Nombre}-{p.Graine:0000}{cle}";
        return p.PackId is null ? code : $"{code}-{p.PackId.ToLowerInvariant()}";
    }

    /// <summary>Lit un code saisi. Renvoie les paramètres, ou un message d'erreur clair.</summary>
    public static (ParametresTd? Parametres, string? Erreur) Lire(string? saisie)
    {
        string texte = (saisie ?? "").Trim();
        if (texte.Length == 0) return (null, "Saisis le code donné par ton camarade.");

        var m = Motif().Match(texte);
        if (!m.Success)
            return (null, "Ce code n'a pas la bonne forme. Il ressemble à MAT1-2-8-48217 : vérifie-le avec ton camarade.");

        string type = m.Groups[1].Value.ToUpperInvariant();
        int version = int.Parse(m.Groups[2].Value);
        int niveau = int.Parse(m.Groups[3].Value);
        int nombre = int.Parse(m.Groups[4].Value);
        string bloc = m.Groups[5].Value;
        int graine = int.Parse(bloc[..4]);
        int cle = bloc[4] - '0';
        string? packId = m.Groups[6].Success ? m.Groups[6].Value.ToLowerInvariant() : null;

        var p = new ParametresTd(type, version, niveau, nombre, graine, packId);
        if (ChiffreDeControle(Canonique(p)) != cle)
            return (null, "Ce code contient une faute de frappe (le dernier chiffre ne correspond pas). Fais-le répéter lentement.");
        if (niveau is < NiveauMin or > NiveauMax || nombre is < NombreMin or > NombreMax || version < 1)
            return (null, "Ce code n'est pas valable (niveau ou nombre d'exercices impossible).");
        return (p, null);
    }

    /// <summary>Texte sur lequel porte le chiffre de contrôle.</summary>
    private static string Canonique(ParametresTd p) =>
        $"{p.Type.ToUpperInvariant()}{p.Version}-{p.Niveau}-{p.Nombre}-{p.Graine:0000}" +
        (p.PackId is null ? "" : "-" + p.PackId.ToLowerInvariant());

    /// <summary>
    /// Chiffre de contrôle : somme pondérée des codes des caractères (poids 1, 3, 7, 9 en boucle), modulo 10.
    /// Les poids sont premiers avec 10 : changer un seul chiffre change toujours le résultat.
    /// </summary>
    public static int ChiffreDeControle(string texte)
    {
        int[] poids = [1, 3, 7, 9];
        int somme = 0;
        byte[] octets = Encoding.UTF8.GetBytes(texte);
        for (int i = 0; i < octets.Length; i++) somme += octets[i] * poids[i % 4];
        return somme % 10;
    }
}
