using System.Globalization;

namespace Revisions.Core.Apparence;

/// <summary>
/// Une couleur (rouge, vert, bleu), avec les calculs de lisibilité des recommandations WCAG.
/// Elle vit dans le cœur métier, sans dépendre de WPF, pour que les contrastes soient testables.
/// </summary>
public readonly record struct Couleur(byte R, byte G, byte B)
{
    public static readonly Couleur Blanc = new(255, 255, 255);
    public static readonly Couleur Noir = new(0, 0, 0);

    /// <summary>Écriture « #RRGGBB ».</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Hex;

    /// <summary>Lit « #RRGGBB », « RRGGBB » ou « #RGB ». Renvoie faux si le texte n'est pas une couleur.</summary>
    public static bool TryParse(string? texte, out Couleur couleur)
    {
        couleur = default;
        if (string.IsNullOrWhiteSpace(texte)) return false;
        string hex = texte.Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}")); // « abc » → « aabbcc »
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int valeur))
            return false;
        couleur = new Couleur((byte)(valeur >> 16), (byte)(valeur >> 8), (byte)valeur);
        return true;
    }

    /// <summary>Comme <see cref="TryParse"/>, mais lève une exception : pour les couleurs écrites dans le code.</summary>
    public static Couleur Parse(string hex) =>
        TryParse(hex, out var couleur) ? couleur : throw new FormatException($"Couleur invalide : {hex}");

    /// <summary>Luminance relative, de 0 (noir) à 1 (blanc), telle que définie par WCAG.</summary>
    public double Luminance
    {
        get
        {
            static double Canal(byte valeur)
            {
                double v = valeur / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Canal(R) + 0.7152 * Canal(G) + 0.0722 * Canal(B);
        }
    }

    /// <summary>
    /// Rapport de contraste entre deux couleurs, de 1 (identiques) à 21 (noir sur blanc).
    /// Repères WCAG : 4,5 pour du texte normal, 3 pour un élément graphique.
    /// </summary>
    public static double Contraste(Couleur a, Couleur b)
    {
        double claire = Math.Max(a.Luminance, b.Luminance), sombre = Math.Min(a.Luminance, b.Luminance);
        return (claire + 0.05) / (sombre + 0.05);
    }

    /// <summary>Mélange avec une autre couleur. <paramref name="part"/> = 0 : cette couleur ; 1 : l'autre.</summary>
    public Couleur Melanger(Couleur autre, double part)
    {
        part = Math.Clamp(part, 0, 1);
        byte Canal(byte a, byte b) => (byte)Math.Round(a + (b - a) * part);
        return new Couleur(Canal(R, autre.R), Canal(G, autre.G), Canal(B, autre.B));
    }

    /// <summary>
    /// Couleur décrite par sa teinte (0 à 360°), sa saturation et sa luminosité (0 à 1) : le modèle TSL,
    /// plus parlant que le RVB pour fabriquer des nuances d'une même couleur (une tuile pâle, un trait vif…).
    /// </summary>
    public static Couleur DepuisTsl(double teinte, double saturation, double luminosite)
    {
        teinte = ((teinte % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        luminosite = Math.Clamp(luminosite, 0, 1);
        double chroma = (1 - Math.Abs(2 * luminosite - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(teinte / 60 % 2 - 1));
        double m = luminosite - chroma / 2;
        var (r, g, b) = (int)(teinte / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        static byte Octet(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new Couleur(Octet(r + m), Octet(g + m), Octet(b + m));
    }

    /// <summary>Teinte (0 à 360°) et saturation (0 à 1) de cette couleur, dans le modèle TSL.</summary>
    public (double Teinte, double Saturation) Tsl
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d < 1e-9) return (0, 0);
            double l = (max + min) / 2;
            double s = d / (1 - Math.Abs(2 * l - 1));
            double t = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            return ((t + 360) % 360, s);
        }
    }

    /// <summary>Noir ou blanc : celui qui se lit le mieux posé sur cette couleur (texte d'un bouton coloré).</summary>
    public Couleur TexteLisible => Contraste(this, Noir) >= Contraste(this, Blanc) ? Noir : Blanc;

    /// <summary>
    /// Variante de cette couleur assez contrastée pour être lue sur un fond donné : on l'éclaircit
    /// (fond sombre) ou on la fonce (fond clair) petit à petit, jusqu'à atteindre le contraste demandé.
    /// </summary>
    public Couleur AjusterPourContraste(Couleur fond, double minimum = 4.5)
    {
        Couleur cible = fond.Luminance < 0.5 ? Blanc : Noir;
        Couleur resultat = this;
        for (int pas = 1; pas <= 20 && Contraste(resultat, fond) < minimum; pas++)
            resultat = Melanger(cible, pas / 20.0);
        return resultat;
    }
}
