using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Revisions.Core.Modeles;

namespace Revisions.App.ViewModels;

/// <summary>Une case de la grille de saisie d'une matrice.</summary>
public partial class CaseMatrice : ObservableObject
{
    [ObservableProperty] private string _texte = "";
}

/// <summary>
/// Grille pour écrire une matrice (exercice dont la réponse est une matrice), commune à la session et au parcours.
/// Ses cases donnent un texte « ligne par ligne » comparé ensuite à la réponse comme une saisie ordinaire.
/// </summary>
public sealed class SaisieMatrice
{
    public ObservableCollection<ObservableCollection<CaseMatrice>> Lignes { get; } = [];

    /// <summary>Entrée dans une case : l'écran qui possède la grille révèle la réponse.</summary>
    public Action? Valider { get; set; }

    /// <summary>Levé pour placer le curseur dans la première case (touche Tab).</summary>
    public event Action? FocusDemande;

    private SaisieMatrice(int lignes, int colonnes)
    {
        for (int i = 0; i < lignes; i++)
            Lignes.Add(new ObservableCollection<CaseMatrice>(Enumerable.Range(0, colonnes).Select(_ => new CaseMatrice())));
    }

    /// <summary>Une grille à la taille de la matrice de la réponse, ou null si la réponse n'est pas une matrice.</summary>
    public static SaisieMatrice? Pour(Carte carte) =>
        carte.Type == TypeCarte.Exercice && ComparaisonReponse.DimensionsMatrice(carte.Contenu.Reponse) is { } d
            ? new SaisieMatrice(d.Lignes, d.Colonnes)
            : null;

    /// <summary>La matrice écrite, une ligne par ligne de la matrice (vide si aucune case n'est remplie).</summary>
    public string Texte => Lignes.All(l => l.All(c => string.IsNullOrWhiteSpace(c.Texte)))
        ? ""
        : string.Join("\n", Lignes.Select(l => string.Join("   ", l.Select(c => string.IsNullOrWhiteSpace(c.Texte) ? "?" : c.Texte.Trim()))));

    /// <summary>
    /// La matrice écrite en LaTeX, pour l'afficher comme la réponse (avec ses parenthèses), ou null si rien n'est écrit.
    /// Seuls les chiffres, lettres, signes et séparateurs d'une case sont gardés : rien d'autre ne passe dans le rendu.
    /// </summary>
    public string? Latex => Texte.Length == 0 ? null
        : @"$\begin{pmatrix}" + string.Join(@" \\ ", Lignes.Select(l => string.Join(" & ", l.Select(c => Case(c.Texte))))) + @"\end{pmatrix}$";

    private static string Case(string texte)
    {
        string propre = new(texte.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '−' or '+' or '.' or ',' or '/' or ' ').ToArray());
        return propre.Length == 0 ? "?" : propre.Replace("−", "-").Replace(",", "{,}");
    }

    public void DemanderFocus() => FocusDemande?.Invoke();
}
