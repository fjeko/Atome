using System.Globalization;
using System.Text.RegularExpressions;

namespace Revisions.Core.Ordre;

/// <summary>
/// Tri « humain » des libellés du cours :
/// - les suites de chiffres sont comparées comme des nombres (« SQ2 » avant « SQ10 », « B1.2 » avant « B1.10 ») ;
/// - les chiffres romains isolés (I, II, IV, IX…) valent leur nombre (« Partie IX » après « Partie V »).
/// Les lettres sont comparées à la française, sans tenir compte de la casse.
/// </summary>
public static partial class TriNaturel
{
    /// <summary>
    /// Un mot entier écrit en chiffres romains majuscules de 1 à 39 (I, V et X seulement :
    /// on évite ainsi de prendre « CV » ou « DC » pour des nombres).
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?=[IVX])(X{0,3}(?:IX|IV|V?I{0,3}))(?![\p{L}\p{N}])")]
    private static partial Regex MotRomain();

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Le comparateur à utiliser pour trier des libellés.</summary>
    public static IComparer<string> Comparateur { get; } = new ComparateurNaturel();

    /// <summary>Valeur d'un nombre romain valide (I à XXXIX), ou null.</summary>
    public static int? Romain(string texte)
    {
        if (texte.Length == 0) return null;
        var m = MotRomain().Match(texte);
        if (!m.Success || m.Index != 0 || m.Length != texte.Length) return null;
        int total = 0;
        for (int i = 0; i < texte.Length; i++)
        {
            int valeur = Valeur(texte[i]);
            // Un symbole plus petit placé avant un plus grand se soustrait (IV = 4, IX = 9).
            if (i + 1 < texte.Length && valeur < Valeur(texte[i + 1])) total -= valeur;
            else total += valeur;
        }
        return total;
    }

    private static int Valeur(char c) => c switch { 'I' => 1, 'V' => 5, 'X' => 10, _ => 0 };

    /// <summary>Remplace chaque mot en chiffres romains par son nombre : « Partie II – SQ4 » → « Partie 2 – SQ4 ».</summary>
    public static string Normaliser(string texte) =>
        MotRomain().Replace(texte, m => m.Length == 0 ? m.Value : Romain(m.Value)!.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Compare deux libellés selon l'ordre naturel.</summary>
    public static int Comparer(string? a, string? b) => Comparateur.Compare(a, b);

    private sealed class ComparateurNaturel : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            string a = Normaliser(x ?? ""), b = Normaliser(y ?? "");
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    // Deux nombres : on les lit en entier et on compare leur valeur.
                    int debutA = i, debutB = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string nombreA = a[debutA..i].TrimStart('0'), nombreB = b[debutB..j].TrimStart('0');
                    int parNombre = nombreA.Length != nombreB.Length
                        ? nombreA.Length.CompareTo(nombreB.Length)
                        : string.CompareOrdinal(nombreA, nombreB);
                    if (parNombre != 0) return parNombre;
                }
                else
                {
                    // Deux caractères : comparaison française, sans tenir compte de la casse.
                    int parLettre = string.Compare(a, i, b, j, 1, Francais, CompareOptions.IgnoreCase);
                    if (parLettre != 0) return parLettre;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
