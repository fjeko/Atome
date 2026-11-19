using Revisions.Core.TdGroupe;

namespace Revisions.Core.Calculatrice;

/// <summary>
/// Où la calculatrice est disponible : les exercices de mathématiques, sauf ceux dont le but est justement
/// de faire le calcul à la main (somme, produit, puissance de matrices, vérification d'un inverse, M² d'un graphe…).
/// </summary>
public static class RegleCalculatrice
{
    /// <summary>Message affiché quand la calculatrice est grisée.</summary>
    public const string RaisonCalculAMain =
        "Cet exercice t'entraîne justement à faire le calcul à la main : la calculatrice est désactivée ici. " +
        "Elle t'attend sur les exercices suivants, notamment les situations concrètes.";

    /// <summary>Sujets (préfixe du générateur, clé du sujet) dont l'exercice est un calcul à faire à la main.</summary>
    private static readonly HashSet<(string Prefixe, string Cle)> CalculsAMain =
    [
        ("MAT", "SommeDifference"), ("MAT", "Reel"), ("MAT", "Transposee"), ("MAT", "Produit"),
        ("MAT", "NonCommutativite"), ("MAT", "Puissance"),
        ("INV", "verifier"),            // vérifier une matrice inverse = faire le produit AB soi-même
        ("GRA", "chemins"), ("GRA", "fermeture"), // M² et puissances booléennes, à la main
    ];

    /// <summary>Vrai si la matière est les mathématiques (« Mathématiques », « Maths », sans tenir compte des accents ni de la casse).</summary>
    public static bool EstMatiereMaths(string? matiere)
    {
        if (string.IsNullOrWhiteSpace(matiere)) return false;
        string m = new string(matiere.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant();
        return m.StartsWith("math");
    }

    /// <summary>Raison de désactiver la calculatrice pour ce sujet, ou null si elle est permise.</summary>
    public static string? Raison(string prefixe, string cle) => CalculsAMain.Contains((prefixe, cle)) ? RaisonCalculAMain : null;

    /// <summary>Renseigne la matière et la règle de la calculatrice sur un exercice qui ne les a pas encore.</summary>
    public static ExerciceTd Marquer(ExerciceTd e, string matiere, string prefixe, string cle) =>
        e with { Matiere = e.Matiere ?? matiere, SansCalculatrice = e.SansCalculatrice ?? Raison(prefixe, cle) };
}
