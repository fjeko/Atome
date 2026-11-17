namespace Revisions.Core.MisesAJour;

/// <summary>
/// Numéro de version au format SemVer (https://semver.org) : MAJEURE.MINEURE.CORRECTIF[-préversion][+build].
/// Exemples : « 1.2.0 », « v1.2.0 » (le « v » des tags Git est accepté), « 1.3.0-beta.2 », « 0.2.0+a1b2c3 ».
/// </summary>
/// <remarks>
/// Règles de comparaison (SemVer 2.0) :
/// <list type="bullet">
/// <item>on compare d'abord majeure, puis mineure, puis correctif, comme des nombres (1.10.0 &gt; 1.9.0) ;</item>
/// <item>une préversion passe AVANT la version finale (1.2.0-beta &lt; 1.2.0) ;</item>
/// <item>la partie « +build » est ignorée.</item>
/// </list>
/// </remarks>
public sealed record VersionSemantique(int Majeure, int Mineure, int Correctif, string? PreVersion = null)
    : IComparable<VersionSemantique>
{
    /// <summary>Vrai pour une préversion (« 1.3.0-beta »).</summary>
    public bool EstPreVersion => PreVersion is not null;

    /// <summary>
    /// Lit un numéro de version. Accepte un « v » au début et 2 ou 3 nombres (« 1.2 » = « 1.2.0 ») ;
    /// un 4e nombre (« 1.2.0.0 », format des assemblys .NET) est accepté s'il vaut 0.
    /// </summary>
    public static bool TryParse(string? texte, out VersionSemantique version)
    {
        version = new VersionSemantique(0, 0, 0);
        if (string.IsNullOrWhiteSpace(texte)) return false;
        string s = texte.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        // « +build » : sans effet sur la comparaison.
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string? pre = null;
        int tiret = s.IndexOf('-');
        if (tiret >= 0)
        {
            pre = s[(tiret + 1)..];
            s = s[..tiret];
            // Identifiants de préversion : lettres, chiffres et tirets, séparés par des points, jamais vides.
            if (pre.Length == 0 || pre.Split('.').Any(id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
                return false;
        }

        string[] morceaux = s.Split('.');
        if (morceaux.Length is < 2 or > 4) return false;
        var nombres = new int[morceaux.Length];
        for (int i = 0; i < morceaux.Length; i++)
        {
            if (morceaux[i].Length == 0 || !morceaux[i].All(char.IsAsciiDigit) || !int.TryParse(morceaux[i], out nombres[i]))
                return false;
        }
        if (nombres.Length == 4 && nombres[3] != 0) return false;

        version = new VersionSemantique(nombres[0], nombres[1], nombres.Length > 2 ? nombres[2] : 0, pre);
        return true;
    }

    public int CompareTo(VersionSemantique? autre)
    {
        if (autre is null) return 1;
        int c = Majeure.CompareTo(autre.Majeure);
        if (c != 0) return c;
        c = Mineure.CompareTo(autre.Mineure);
        if (c != 0) return c;
        c = Correctif.CompareTo(autre.Correctif);
        if (c != 0) return c;

        // Même numéro : la version finale l'emporte sur toute préversion.
        if (PreVersion is null) return autre.PreVersion is null ? 0 : 1;
        if (autre.PreVersion is null) return -1;
        return ComparerPreVersions(PreVersion, autre.PreVersion);
    }

    /// <summary>
    /// SemVer §11 : identifiants comparés un par un ; les nombres comme des nombres, le texte dans l'ordre ASCII,
    /// un nombre passe avant du texte, et une liste plus courte passe avant (alpha &lt; alpha.1).
    /// </summary>
    private static int ComparerPreVersions(string a, string b)
    {
        string[] x = a.Split('.'), y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool nx = long.TryParse(x[i], out long vx) && x[i].All(char.IsAsciiDigit);
            bool ny = long.TryParse(y[i], out long vy) && y[i].All(char.IsAsciiDigit);
            int c = (nx, ny) switch
            {
                (true, true) => vx.CompareTo(vy),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(x[i], y[i]),
            };
            if (c != 0) return Math.Sign(c);
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool operator >(VersionSemantique a, VersionSemantique b) => a.CompareTo(b) > 0;
    public static bool operator <(VersionSemantique a, VersionSemantique b) => a.CompareTo(b) < 0;
    public static bool operator >=(VersionSemantique a, VersionSemantique b) => a.CompareTo(b) >= 0;
    public static bool operator <=(VersionSemantique a, VersionSemantique b) => a.CompareTo(b) <= 0;

    /// <summary>Forme courte, sans « v » : « 1.2.0 » ou « 1.3.0-beta.2 ».</summary>
    public override string ToString() =>
        $"{Majeure}.{Mineure}.{Correctif}" + (PreVersion is null ? "" : "-" + PreVersion);
}
