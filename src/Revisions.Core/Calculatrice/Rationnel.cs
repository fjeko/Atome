using System.Globalization;
using System.Numerics;
using System.Text;

namespace Revisions.Core.Calculatrice;

/// <summary>
/// Nombre rationnel exact : une fraction numérateur / dénominateur, toujours simplifiée, dénominateur &gt; 0.
/// Les entiers sont des « BigInteger » : pas de dépassement, même pour 2^200 ou une puissance de matrice.
/// </summary>
public readonly struct Rationnel : IEquatable<Rationnel>, IComparable<Rationnel>
{
    public BigInteger Numerateur { get; }
    public BigInteger Denominateur { get; }

    public static readonly Rationnel Zero = new(0);
    public static readonly Rationnel Un = new(1);

    public Rationnel(BigInteger numerateur) : this(numerateur, BigInteger.One) { }

    public Rationnel(BigInteger numerateur, BigInteger denominateur)
    {
        if (denominateur.IsZero) throw new DivideByZeroException("Dénominateur nul.");
        if (denominateur.Sign < 0) (numerateur, denominateur) = (-numerateur, -denominateur);
        var pgcd = BigInteger.GreatestCommonDivisor(numerateur, denominateur);
        if (!pgcd.IsOne && !pgcd.IsZero) (numerateur, denominateur) = (numerateur / pgcd, denominateur / pgcd);
        Numerateur = numerateur;
        Denominateur = denominateur;
    }

    // Une valeur par défaut (default) vaut 0 : le dénominateur 0 de la structure est lu comme 1.
    private BigInteger Den => Denominateur.IsZero ? BigInteger.One : Denominateur;

    public bool EstZero => Numerateur.IsZero;
    public bool EstEntier => Den.IsOne;
    public int Signe => Numerateur.Sign;

    public static implicit operator Rationnel(int n) => new(n);

    public static Rationnel operator +(Rationnel a, Rationnel b) => new(a.Numerateur * b.Den + b.Numerateur * a.Den, a.Den * b.Den);
    public static Rationnel operator -(Rationnel a, Rationnel b) => new(a.Numerateur * b.Den - b.Numerateur * a.Den, a.Den * b.Den);
    public static Rationnel operator *(Rationnel a, Rationnel b) => new(a.Numerateur * b.Numerateur, a.Den * b.Den);
    public static Rationnel operator /(Rationnel a, Rationnel b)
    {
        if (b.EstZero) throw new DivideByZeroException();
        return new(a.Numerateur * b.Den, a.Den * b.Numerateur);
    }
    public static Rationnel operator -(Rationnel a) => new(-a.Numerateur, a.Den);

    /// <summary>Puissance entière (négative permise si la base n'est pas nulle).</summary>
    public Rationnel Puissance(int n)
    {
        if (n == 0) return Un;
        if (n < 0)
        {
            if (EstZero) throw new DivideByZeroException();
            return new Rationnel(Den, Numerateur).Puissance(-n);
        }
        return new Rationnel(BigInteger.Pow(Numerateur, n), BigInteger.Pow(Den, n));
    }

    /// <summary>10^k, avec k entier (négatif : 1/10^|k|).</summary>
    public static Rationnel DixPuissance(int k) =>
        k >= 0 ? new Rationnel(BigInteger.Pow(10, k)) : new Rationnel(BigInteger.One, BigInteger.Pow(10, -k));

    public double ToDouble()
    {
        double n = (double)Numerateur, d = (double)Den;
        if (!double.IsInfinity(n) && !double.IsInfinity(d)) return n / d;
        // Très grands entiers : on passe par les logarithmes pour ne pas diviser deux infinis.
        return Math.Exp(BigInteger.Log(BigInteger.Abs(Numerateur)) - BigInteger.Log(Den)) * Signe;
    }

    /// <summary>Racine carrée exacte si numérateur et dénominateur sont des carrés parfaits ; sinon null.</summary>
    public Rationnel? RacineExacte()
    {
        if (Signe < 0) return null;
        var n = RacineEntiere(Numerateur);
        var d = RacineEntiere(Den);
        if (n is null || d is null) return null;
        return new Rationnel(n.Value, d.Value);
    }

    private static BigInteger? RacineEntiere(BigInteger x)
    {
        if (x.IsZero) return BigInteger.Zero;
        var r = (BigInteger)Math.Sqrt((double)x);
        for (var essai = r - 2; essai <= r + 2; essai++)
            if (essai.Sign >= 0 && essai * essai == x) return essai;
        return null;
    }

    // ---- Écriture ---------------------------------------------------------------------------

    /// <summary>Fraction : « 3 », « -1/2 ».</summary>
    public string EnFraction() => EstEntier
        ? Numerateur.ToString(CultureInfo.InvariantCulture)
        : $"{Numerateur.ToString(CultureInfo.InvariantCulture)}/{Den.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Valeur décimale à la française (virgule), arrondie à <paramref name="chiffres"/> chiffres, sans zéros inutiles.</summary>
    public string EnDecimal(int chiffres = 10)
    {
        if (EstEntier) return Numerateur.ToString(CultureInfo.InvariantCulture);
        var echelle = BigInteger.Pow(10, chiffres);
        var absolu = BigInteger.Abs(Numerateur) * echelle;
        var q = BigInteger.DivRem(absolu, Den, out var reste);
        if (reste * 2 >= Den) q += 1; // arrondi au plus proche
        var texte = q.ToString(CultureInfo.InvariantCulture).PadLeft(chiffres + 1, '0');
        var entier = texte[..^chiffres];
        var frac = texte[^chiffres..].TrimEnd('0');
        var s = new StringBuilder();
        if (Signe < 0 && !q.IsZero) s.Append('-');
        s.Append(entier);
        if (frac.Length > 0) s.Append(',').Append(frac);
        return s.ToString();
    }

    public override string ToString() => EnFraction();

    public bool Equals(Rationnel autre) => Numerateur == autre.Numerateur && Den == autre.Den;
    public override bool Equals(object? obj) => obj is Rationnel r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Numerateur, Den);
    public int CompareTo(Rationnel autre) => (Numerateur * autre.Den).CompareTo(autre.Numerateur * Den);
    public static bool operator ==(Rationnel a, Rationnel b) => a.Equals(b);
    public static bool operator !=(Rationnel a, Rationnel b) => !a.Equals(b);
}
