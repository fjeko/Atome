using System.Globalization;
using System.Text;

namespace Revisions.Core.Bibliotheque;

/// <summary>Petit dessin qui aide à reconnaître une matière d'un coup d'œil.</summary>
public enum Pictogramme
{
    /// <summary>Tour d'ordinateur : support et services informatiques (bloc 1).</summary>
    Ordinateur,
    /// <summary>Fenêtre avec du code : développement d'applications (bloc 2).</summary>
    Code,
    /// <summary>Personne à capuche devant un écran : cybersécurité (bloc 3).</summary>
    Securite,
    /// <summary>Drapeau : langues (bloc 5).</summary>
    Langue,
    /// <summary>Matrice : mathématiques.</summary>
    Maths,
    /// <summary>Organigramme : algorithmique.</summary>
    Algorithme,
    /// <summary>Balance : économie, droit, management (CEJM).</summary>
    Droit,
    /// <summary>Plume : culture générale et expression.</summary>
    Lettres,
    /// <summary>Livre : toute autre matière.</summary>
    Livre,
}

/// <summary>
/// Choisit le pictogramme d'une matière d'après les mots de son nom, et non d'après une liste
/// figée : une nouvelle matière (2e année, autre formation) reçoit ainsi un dessin adapté
/// sans modifier l'application, et au pire le livre par défaut.
/// </summary>
public static class Pictogrammes
{
    // Dans l'ordre : le premier groupe dont un mot apparaît dans le nom de la matière gagne.
    // Les mots sont écrits sans accent ni majuscule (le nom est simplifié de la même façon).
    private static readonly (Pictogramme Icone, string[] Mots)[] Regles =
    [
        (Pictogramme.Securite, ["cyber", "securite"]),
        (Pictogramme.Algorithme, ["algorithm"]),
        (Pictogramme.Maths, ["math"]),
        (Pictogramme.Langue, ["anglais", "english", "espagnol", "allemand", "langue"]),
        (Pictogramme.Droit, ["cejm", "economi", "juridique", "droit", "manag"]),
        (Pictogramme.Lettres, ["culture generale", "expression", "francais"]),
        (Pictogramme.Code, ["slam", "developpement", "programmation", "logiciel"]),
        (Pictogramme.Ordinateur, ["support", "sisr", "reseau", "infrastructure", "services informatiques"]),
    ];

    public static Pictogramme PourMatiere(string? matiere)
    {
        string nom = Simplifier(matiere ?? "");
        foreach (var (icone, mots) in Regles)
            if (mots.Any(nom.Contains)) return icone;
        return Pictogramme.Livre;
    }

    /// <summary>Minuscules sans accents : « Cybersécurité » → « cybersecurite ».</summary>
    private static string Simplifier(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char c in texte.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                resultat.Append(char.ToLowerInvariant(c));
        return resultat.ToString();
    }
}
