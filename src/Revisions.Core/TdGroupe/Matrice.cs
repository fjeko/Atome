using System.Globalization;
using System.Text;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// Matrice d'entiers, non modifiable. Notations du cours (SQ1) : dimension n × p (lignes d'abord),
/// coefficient a_ij (ligne i, colonne j), indices comptés à partir de 1 dans les textes.
/// </summary>
/// <remarks>En C#, les indices commencent à 0 : <c>m[0, 0]</c> est le coefficient a_11 du cours.</remarks>
public sealed class Matrice : IEquatable<Matrice>
{
    private readonly int[,] _valeurs;

    public int Lignes { get; }
    public int Colonnes { get; }

    /// <summary>Crée une matrice à partir de ses lignes : <c>new Matrice([[1, 2], [3, 4]])</c>.</summary>
    public Matrice(IReadOnlyList<IReadOnlyList<int>> lignes)
    {
        if (lignes.Count == 0 || lignes[0].Count == 0) throw new ArgumentException("Une matrice a au moins une ligne et une colonne.");
        Lignes = lignes.Count;
        Colonnes = lignes[0].Count;
        _valeurs = new int[Lignes, Colonnes];
        for (int i = 0; i < Lignes; i++)
        {
            if (lignes[i].Count != Colonnes) throw new ArgumentException("Toutes les lignes doivent avoir la même longueur.");
            for (int j = 0; j < Colonnes; j++) _valeurs[i, j] = lignes[i][j];
        }
    }

    private Matrice(int[,] valeurs)
    {
        _valeurs = valeurs;
        Lignes = valeurs.GetLength(0);
        Colonnes = valeurs.GetLength(1);
    }

    /// <summary>Construit une matrice coefficient par coefficient : <paramref name="coefficient"/>(i, j) avec i, j à partir de 0.</summary>
    public static Matrice Creer(int lignes, int colonnes, Func<int, int, int> coefficient)
    {
        var valeurs = new int[lignes, colonnes];
        for (int i = 0; i < lignes; i++)
        for (int j = 0; j < colonnes; j++)
            valeurs[i, j] = coefficient(i, j);
        return new Matrice(valeurs);
    }

    /// <summary>Matrice identité I_n : des 1 sur la diagonale, des 0 ailleurs.</summary>
    public static Matrice Identite(int n) => Creer(n, n, (i, j) => i == j ? 1 : 0);

    /// <summary>Coefficient ligne i, colonne j (indices à partir de 0).</summary>
    public int this[int i, int j] => _valeurs[i, j];

    public bool EstCarree => Lignes == Colonnes;

    /// <summary>Dimension écrite comme dans le cours : « 2 × 3 ».</summary>
    public string Dimension => $"{Lignes} × {Colonnes}";

    /// <summary>Tous les coefficients, ligne après ligne (ordre de saisie dans la grille).</summary>
    public IEnumerable<int> Coefficients()
    {
        for (int i = 0; i < Lignes; i++)
        for (int j = 0; j < Colonnes; j++)
            yield return _valeurs[i, j];
    }

    // ---- Opérations du cours ---------------------------------------------------------------

    /// <summary>Somme : seulement entre matrices de même dimension.</summary>
    public static Matrice operator +(Matrice a, Matrice b)
    {
        VerifierMemeDimension(a, b);
        return Creer(a.Lignes, a.Colonnes, (i, j) => a[i, j] + b[i, j]);
    }

    /// <summary>Différence : seulement entre matrices de même dimension.</summary>
    public static Matrice operator -(Matrice a, Matrice b)
    {
        VerifierMemeDimension(a, b);
        return Creer(a.Lignes, a.Colonnes, (i, j) => a[i, j] - b[i, j]);
    }

    /// <summary>Multiplication par un réel (ici un entier) : chaque coefficient est multiplié par k.</summary>
    public static Matrice operator *(int k, Matrice a) => Creer(a.Lignes, a.Colonnes, (i, j) => k * a[i, j]);

    /// <summary>
    /// Produit AB : défini si le nombre de colonnes de A est égal au nombre de lignes de B.
    /// Coefficient c_ij = ligne i de A × colonne j de B, terme à terme, puis on additionne.
    /// </summary>
    public static Matrice operator *(Matrice a, Matrice b)
    {
        if (!ProduitDefini(a.Lignes, a.Colonnes, b.Lignes, b.Colonnes))
            throw new InvalidOperationException($"Produit impossible : {a.Dimension} par {b.Dimension}.");
        return Creer(a.Lignes, b.Colonnes, (i, j) =>
        {
            int somme = 0;
            for (int k = 0; k < a.Colonnes; k++) somme += a[i, k] * b[k, j];
            return somme;
        });
    }

    /// <summary>Le produit d'une matrice n × p par une matrice p' × q existe si p = p'. Il est alors de dimension n × q.</summary>
    public static bool ProduitDefini(int lignesA, int colonnesA, int lignesB, int colonnesB) => colonnesA == lignesB;

    /// <summary>Transposée : les lignes deviennent les colonnes (le coefficient a_ij passe en position j, i).</summary>
    public Matrice Transposee() => Creer(Colonnes, Lignes, (i, j) => this[j, i]);

    /// <summary>Puissance A^n (n ≥ 0) pour une matrice carrée ; A^0 = I.</summary>
    public Matrice Puissance(int n)
    {
        if (!EstCarree) throw new InvalidOperationException("Seule une matrice carrée a des puissances.");
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        var resultat = Identite(Lignes);
        for (int k = 0; k < n; k++) resultat *= this;
        return resultat;
    }

    private static void VerifierMemeDimension(Matrice a, Matrice b)
    {
        if (a.Lignes != b.Lignes || a.Colonnes != b.Colonnes)
            throw new InvalidOperationException($"Dimensions différentes : {a.Dimension} et {b.Dimension}.");
    }

    // ---- Affichage -------------------------------------------------------------------------

    /// <summary>Écriture LaTeX, avec des parenthèses comme dans le cours : \begin{pmatrix} … \end{pmatrix}.</summary>
    public string Latex()
    {
        var texte = new StringBuilder(@"\begin{pmatrix} ");
        for (int i = 0; i < Lignes; i++)
        {
            if (i > 0) texte.Append(@" \\ ");
            for (int j = 0; j < Colonnes; j++)
            {
                if (j > 0) texte.Append(" & ");
                texte.Append(this[i, j].ToString(CultureInfo.InvariantCulture));
            }
        }
        return texte.Append(@" \end{pmatrix}").ToString();
    }

    /// <summary>Écriture en texte simple, ligne par ligne : « (1 2 ; 3 4) ».</summary>
    public override string ToString() =>
        "(" + string.Join(" ; ", Enumerable.Range(0, Lignes).Select(i =>
            string.Join(" ", Enumerable.Range(0, Colonnes).Select(j => this[i, j].ToString(CultureInfo.InvariantCulture))))) + ")";

    // ---- Égalité : même dimension et mêmes coefficients deux à deux -------------------------

    public bool Equals(Matrice? autre) =>
        autre is not null && autre.Lignes == Lignes && autre.Colonnes == Colonnes
        && Coefficients().SequenceEqual(autre.Coefficients());

    public override bool Equals(object? obj) => Equals(obj as Matrice);

    /// <summary>« == » compare les coefficients (et pas seulement l'objet en mémoire).</summary>
    public static bool operator ==(Matrice? a, Matrice? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Matrice? a, Matrice? b) => !(a == b);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Lignes);
        hash.Add(Colonnes);
        foreach (int c in Coefficients()) hash.Add(c);
        return hash.ToHashCode();
    }
}
