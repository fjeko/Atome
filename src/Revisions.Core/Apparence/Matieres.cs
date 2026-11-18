using System.Globalization;
using System.Text;

namespace Revisions.Core.Apparence;

/// <summary>
/// Les teintes fixes des matières (cahier de design, section 1). Les quatre dernières servent de repli
/// pour une matière inconnue (pack importé) : elle en reçoit une, toujours la même pour un même nom.
/// </summary>
public enum Teinte
{
    Bleu, Violet, Indigo, Corail, Orange, Ocre, Vert, Rose, Gris,
    Sarcelle, Cyan, Brun, Lime,
}

/// <summary>Repère visuel d'une matière : sa teinte et son icône (caractère de la police Segoe Fluent Icons).</summary>
public sealed record RepereMatiere(Teinte Teinte, string Icone);

/// <summary>
/// Le dictionnaire unique « matière → couleur et icône ». Les matières sont reconnues par des mots de leur nom
/// (et de leur partie pour le C# du bloc 1), si bien qu'un pack d'une autre source reçoit quand même le bon repère.
/// </summary>
public static class ReperesMatieres
{
    // Icônes de Segoe Fluent Icons (Windows 11), présentes aussi dans Segoe MDL2 Assets (Windows 10).
    public const string IconeSupport = "";     // casque
    public const string IconeCode = "";        // code
    public const string IconeApplication = ""; // fenêtre d'application
    public const string IconeBouclier = "";    // bouclier
    public const string IconeMaths = "";       // calculatrice
    public const string IconeTerminal = "";    // invite de commandes
    public const string IconeBatiment = "";    // bâtiment
    public const string IconeLangue = "";      // bulle
    public const string IconeLivre = "";       // livre

    private static readonly RepereMatiere Autre = new(Teinte.Gris, IconeLivre);

    // Dans l'ordre : la première règle dont un mot apparaît dans le nom simplifié gagne.
    private static readonly (string[] Mots, RepereMatiere Repere)[] Regles =
    [
        (["cyber", "securite"], new(Teinte.Corail, IconeBouclier)),
        (["algorithm"], new(Teinte.Ocre, IconeTerminal)),
        (["math"], new(Teinte.Orange, IconeMaths)),
        (["anglais", "english", "langue", "espagnol", "allemand"], new(Teinte.Rose, IconeLangue)),
        (["cejm", "economi", "juridique", "droit", "manag"], new(Teinte.Vert, IconeBatiment)),
        (["slam", "bloc 2", "developpement", "programmation", "logiciel"], new(Teinte.Indigo, IconeApplication)),
        (["support", "bloc 1", "sisr", "reseau", "infrastructure", "services informatiques"], new(Teinte.Bleu, IconeSupport)),
    ];

    /// <summary>Teintes de repli pour une matière inconnue.</summary>
    private static readonly Teinte[] Replis = [Teinte.Sarcelle, Teinte.Cyan, Teinte.Brun, Teinte.Lime];

    /// <summary>Repère d'une matière (et, si on la connaît, de sa partie : « B1.2 Partie I » est le C# du bloc 1).</summary>
    public static RepereMatiere Pour(string? matiere, string? partie = null)
    {
        string nom = Simplifier(matiere);
        if (nom.Length == 0 || nom == "autre") return Autre;

        string p = Simplifier(partie);
        if (nom.Contains("bloc 1") || nom.Contains("support"))
            if (p.StartsWith("b1.2 partie i ") || p.Contains("c#"))
                return new RepereMatiere(Teinte.Violet, IconeCode);

        foreach (var (mots, repere) in Regles)
            if (mots.Any(nom.Contains)) return repere;

        // Inconnue : une teinte de repli choisie par une empreinte stable du nom (FNV-1a),
        // et non par GetHashCode, qui change d'un lancement à l'autre.
        uint empreinte = 2166136261;
        foreach (char c in nom) empreinte = (empreinte ^ c) * 16777619;
        return new RepereMatiere(Replis[empreinte % (uint)Replis.Length], IconeLivre);
    }

    /// <summary>Minuscules sans accents : « Cybersécurité » → « cybersecurite ».</summary>
    private static string Simplifier(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return "";
        var resultat = new StringBuilder(texte.Length);
        foreach (char c in texte.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                resultat.Append(char.ToLowerInvariant(c));
        return resultat.ToString();
    }
}
