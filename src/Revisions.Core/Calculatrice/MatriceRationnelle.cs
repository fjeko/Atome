namespace Revisions.Core.Calculatrice;

/// <summary>Erreur de calcul ou de saisie, avec un message en français clair, affiché tel quel à l'étudiant.</summary>
public sealed class ErreurCalcul(string message) : Exception(message);

/// <summary>
/// Matrice de rationnels exacts, non modifiable (jusqu'à 5 × 5 dans l'éditeur, mais sans limite ici).
/// Les méthodes lèvent <see cref="InvalidOperationException"/> si les tailles ne conviennent pas :
/// l'évaluateur vérifie avant et écrit lui-même le message avec le nom des matrices.
/// </summary>
public sealed class MatriceRationnelle : IEquatable<MatriceRationnelle>
{
    private readonly Rationnel[,] _v;

    public int Lignes { get; }
    public int Colonnes { get; }
    public bool EstCarree => Lignes == Colonnes;

    public MatriceRationnelle(int lignes, int colonnes, Func<int, int, Rationnel> coefficient)
    {
        if (lignes < 1 || colonnes < 1) throw new ArgumentException("Une matrice a au moins une ligne et une colonne.");
        Lignes = lignes;
        Colonnes = colonnes;
        _v = new Rationnel[lignes, colonnes];
        for (int i = 0; i < lignes; i++)
        for (int j = 0; j < colonnes; j++)
            _v[i, j] = coefficient(i, j);
    }

    public static MatriceRationnelle Identite(int n) => new(n, n, (i, j) => i == j ? Rationnel.Un : Rationnel.Zero);

    public Rationnel this[int i, int j] => _v[i, j];

    /// <summary>Taille écrite « 2×3 ».</summary>
    public string Taille => $"{Lignes}×{Colonnes}";

    public static MatriceRationnelle operator +(MatriceRationnelle a, MatriceRationnelle b)
    {
        MemeTaille(a, b);
        return new(a.Lignes, a.Colonnes, (i, j) => a[i, j] + b[i, j]);
    }

    public static MatriceRationnelle operator -(MatriceRationnelle a, MatriceRationnelle b)
    {
        MemeTaille(a, b);
        return new(a.Lignes, a.Colonnes, (i, j) => a[i, j] - b[i, j]);
    }

    public static MatriceRationnelle operator -(MatriceRationnelle a) => new(a.Lignes, a.Colonnes, (i, j) => -a[i, j]);

    public static MatriceRationnelle operator *(Rationnel k, MatriceRationnelle a) => new(a.Lignes, a.Colonnes, (i, j) => k * a[i, j]);

    public static MatriceRationnelle operator *(MatriceRationnelle a, MatriceRationnelle b)
    {
        if (a.Colonnes != b.Lignes) throw new InvalidOperationException($"Produit impossible : {a.Taille} par {b.Taille}.");
        return new(a.Lignes, b.Colonnes, (i, j) =>
        {
            var somme = Rationnel.Zero;
            for (int k = 0; k < a.Colonnes; k++) somme += a[i, k] * b[k, j];
            return somme;
        });
    }

    public MatriceRationnelle Transposee() => new(Colonnes, Lignes, (i, j) => _v[j, i]);

    /// <summary>Puissance entière positive ou nulle, par exponentiation rapide.</summary>
    public MatriceRationnelle Puissance(int n)
    {
        if (!EstCarree) throw new InvalidOperationException("Seule une matrice carrée a des puissances.");
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        var resultat = Identite(Lignes);
        var facteur = this;
        while (n > 0)
        {
            if ((n & 1) == 1) resultat *= facteur;
            n >>= 1;
            if (n > 0) facteur *= facteur;
        }
        return resultat;
    }

    /// <summary>Déterminant par élimination de Gauss sur les fractions (exact).</summary>
    public Rationnel Determinant()
    {
        if (!EstCarree) throw new InvalidOperationException("Le déterminant demande une matrice carrée.");
        int n = Lignes;
        var m = (Rationnel[,])_v.Clone();
        var det = Rationnel.Un;
        for (int c = 0; c < n; c++)
        {
            int pivot = -1;
            for (int i = c; i < n; i++) if (!m[i, c].EstZero) { pivot = i; break; }
            if (pivot < 0) return Rationnel.Zero;
            if (pivot != c)
            {
                for (int j = 0; j < n; j++) (m[c, j], m[pivot, j]) = (m[pivot, j], m[c, j]);
                det = -det;
            }
            det *= m[c, c];
            for (int i = c + 1; i < n; i++)
            {
                var f = m[i, c] / m[c, c];
                if (f.EstZero) continue;
                for (int j = c; j < n; j++) m[i, j] -= f * m[c, j];
            }
        }
        return det;
    }

    /// <summary>Inverse par la méthode de Gauss-Jordan ; null si la matrice n'est pas inversible.</summary>
    public MatriceRationnelle? Inverse()
    {
        if (!EstCarree) throw new InvalidOperationException("L'inverse demande une matrice carrée.");
        int n = Lignes;
        var m = (Rationnel[,])_v.Clone();
        var inv = new Rationnel[n, n];
        for (int i = 0; i < n; i++) inv[i, i] = Rationnel.Un;
        for (int c = 0; c < n; c++)
        {
            int pivot = -1;
            for (int i = c; i < n; i++) if (!m[i, c].EstZero) { pivot = i; break; }
            if (pivot < 0) return null;
            if (pivot != c)
                for (int j = 0; j < n; j++)
                {
                    (m[c, j], m[pivot, j]) = (m[pivot, j], m[c, j]);
                    (inv[c, j], inv[pivot, j]) = (inv[pivot, j], inv[c, j]);
                }
            var p = m[c, c];
            for (int j = 0; j < n; j++) { m[c, j] /= p; inv[c, j] /= p; }
            for (int i = 0; i < n; i++)
            {
                if (i == c || m[i, c].EstZero) continue;
                var f = m[i, c];
                for (int j = 0; j < n; j++) { m[i, j] -= f * m[c, j]; inv[i, j] -= f * inv[c, j]; }
            }
        }
        return new(n, n, (i, j) => inv[i, j]);
    }

    private static void MemeTaille(MatriceRationnelle a, MatriceRationnelle b)
    {
        if (a.Lignes != b.Lignes || a.Colonnes != b.Colonnes)
            throw new InvalidOperationException($"Tailles différentes : {a.Taille} et {b.Taille}.");
    }

    public bool Equals(MatriceRationnelle? autre)
    {
        if (autre is null || autre.Lignes != Lignes || autre.Colonnes != Colonnes) return false;
        for (int i = 0; i < Lignes; i++)
        for (int j = 0; j < Colonnes; j++)
            if (_v[i, j] != autre._v[i, j]) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MatriceRationnelle);
    public override int GetHashCode() => HashCode.Combine(Lignes, Colonnes, _v[0, 0]);

    /// <summary>Écriture lisible : « (1 2 ; 3 4) », avec des fractions.</summary>
    public override string ToString() =>
        "(" + string.Join(" ; ", Enumerable.Range(0, Lignes).Select(i =>
            string.Join(" ", Enumerable.Range(0, Colonnes).Select(j => _v[i, j].EnFraction())))) + ")";
}
