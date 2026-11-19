using System.Globalization;

namespace Revisions.Core.Calculatrice;

/// <summary>Résultat d'un calcul : un nombre ou une matrice.</summary>
public abstract record Valeur;

/// <summary>
/// Un nombre. Il est <b>exact</b> (une fraction) tant que les calculs le permettent ; il devient <b>approché</b>
/// (valeur décimale) dès qu'on utilise pi, e, ln, exp ou une racine non entière.
/// </summary>
public sealed record Nombre(Rationnel? Exact, double Approche) : Valeur
{
    public static Nombre De(Rationnel r) => new(r, r.ToDouble());
    public static Nombre De(double d) => new(null, d);

    public bool EstExact => Exact is not null;

    /// <summary>Écriture : fraction exacte (« 1/2 ») ou décimale à la française (« 0,5 »). Un nombre approché est toujours décimal.</summary>
    public string Texte(bool decimale) => Exact is { } r
        ? (decimale ? r.EnDecimal() : r.EnFraction())
        : Approche.ToString("G10", CultureInfo.InvariantCulture).Replace('.', ',');
}

/// <summary>Une matrice de rationnels exacts.</summary>
public sealed record ValeurMatrice(MatriceRationnelle Matrice) : Valeur;

/// <summary>Écriture des résultats pour l'affichage.</summary>
public static class Formatage
{
    /// <summary>Texte d'une valeur : un nombre, ou la matrice écrite « (1 2 ; 3 4) ».</summary>
    public static string Texte(Valeur v, bool decimale) => v switch
    {
        Nombre n => n.Texte(decimale),
        ValeurMatrice m => "(" + string.Join(" ; ", Enumerable.Range(0, m.Matrice.Lignes).Select(i =>
            string.Join(" ", Enumerable.Range(0, m.Matrice.Colonnes).Select(j => Coefficient(m.Matrice[i, j], decimale))))) + ")",
        _ => "",
    };

    /// <summary>Un coefficient de matrice : « 1/2 » ou « 0,5 ».</summary>
    public static string Coefficient(Rationnel r, bool decimale) => decimale ? r.EnDecimal() : r.EnFraction();
}
