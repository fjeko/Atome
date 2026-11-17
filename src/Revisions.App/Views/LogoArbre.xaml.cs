using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.Core.Apparence;

namespace Revisions.App.Views;

/// <summary>
/// Le logo du menu. Ses couleurs d'origine (bleu → violet) conviennent aux thèmes Océan, Crépuscule, Lavande et Clair ;
/// pour les autres thèmes (ou un accent personnalisé), on décale la teinte du logo vers la couleur d'accent.
/// </summary>
public partial class LogoArbre : UserControl
{
    /// <summary>Thèmes pour lesquels le logo garde ses couleurs d'origine.</summary>
    private static readonly HashSet<string> ThemesSansDecalage = ["ocean", "crepuscule", "lavande", "clair"];

    /// <summary>Teinte moyenne du logo d'origine (entre le bleu et le violet), en degrés.</summary>
    private const double TeinteLogo = 230;

    private static byte[]? _pixelsOrigine;
    private static int _largeur, _hauteur;
    private EtatApplication? _etat;

    public LogoArbre()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var services = App.Services;
            _etat = services.GetService<EtatApplication>();
            if (_etat is null) return;
            _etat.ThemeChange += Recolorier;
            Recolorier();
        };
        Unloaded += (_, _) => { if (_etat is not null) _etat.ThemeChange -= Recolorier; };
    }

    private void Recolorier()
    {
        if (_etat is null) return;
        var reglages = _etat.Reglages;
        bool accentPerso = Couleur.TryParse(reglages.CouleurAccent, out _);
        if (!accentPerso && ThemesSansDecalage.Contains(reglages.Theme ?? "")) { Image.Source = Origine(); return; }

        var a = _etat.Palette.Accent;
        double decalage = TeinteDe(a.R, a.G, a.B) - TeinteLogo;
        Image.Source = Decaler(decalage);
    }

    private static BitmapSource Origine() => new BitmapImage(new Uri("pack://application:,,,/Ressources/logo.png"));

    /// <summary>Retourne le logo avec toutes ses teintes décalées de « degres » degrés.</summary>
    private static BitmapSource Decaler(double degres)
    {
        if (_pixelsOrigine is null)
        {
            var source = new FormatConvertedBitmap(Origine(), PixelFormats.Bgra32, null, 0);
            _largeur = source.PixelWidth; _hauteur = source.PixelHeight;
            _pixelsOrigine = new byte[_largeur * _hauteur * 4];
            source.CopyPixels(_pixelsOrigine, _largeur * 4, 0);
        }
        var pixels = (byte[])_pixelsOrigine.Clone();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            double r = pixels[i + 2] / 255.0, g = pixels[i + 1] / 255.0, b = pixels[i] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d < 1e-6) continue; // gris : rien à décaler
            double h = TeinteDe(r, g, b) + degres;
            h = ((h % 360) + 360) % 360;
            double s = d / max, v = max;
            // HSV -> RGB
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r2, double g2, double b2) = (int)(h / 60) switch
            {
                0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
                3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            pixels[i + 2] = (byte)Math.Round((r2 + m) * 255);
            pixels[i + 1] = (byte)Math.Round((g2 + m) * 255);
            pixels[i] = (byte)Math.Round((b2 + m) * 255);
        }
        var resultat = BitmapSource.Create(_largeur, _hauteur, 96, 96, PixelFormats.Bgra32, null, pixels, _largeur * 4);
        resultat.Freeze();
        return resultat;
    }

    private static double TeinteDe(byte r, byte g, byte b) => TeinteDe(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>Teinte (0 à 360°) d'une couleur dont les composantes vont de 0 à 1.</summary>
    private static double TeinteDe(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), d = max - Math.Min(r, Math.Min(g, b));
        if (d < 1e-6) return TeinteLogo;
        double h = max == r ? ((g - b) / d % 6) : max == g ? ((b - r) / d + 2) : ((r - g) / d + 4);
        return ((h * 60) + 360) % 360;
    }
}
