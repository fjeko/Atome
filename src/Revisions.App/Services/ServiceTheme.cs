using System.Windows;
using System.Windows.Media;
using Revisions.Core.Apparence;

namespace Revisions.App.Services;

/// <summary>
/// Applique le système de design (docs/design-atome.md) à l'interface WPF : couleurs de la charte
/// (neutres, accent, couleurs de sens et de matière), tailles de texte et fond des fenêtres.
/// Le contenu web reçoit la même palette par VueMarkdown.
/// </summary>
/// <remarks>
/// Toutes les couleurs sont rangées dans les ressources de l'application sous des noms fixes
/// (« Pinceau.Texte », « Ton.Succes.Pale »…) : les écrans les utilisent en DynamicResource, si bien qu'un
/// changement de thème s'applique tout de suite partout. Le thème Fluent de WPF habille les contrôles
/// standard (zones de saisie, listes déroulantes) : on lui donne aussi nos couleurs, sous ses propres noms.
/// Comme WPF peut réécrire l'accent (nouvelle fenêtre, changement dans Windows), on le repose à chaque fenêtre ouverte.
/// </remarks>
public static class ServiceTheme
{
    /// <summary>Palette en cours (utile aux fenêtres ouvertes plus tard).</summary>
    public static Palette? PaletteCourante { get; private set; }

    /// <summary>Charte complète en cours.</summary>
    public static Charte? CharteCourante { get; private set; }

    /// <summary>Vrai si on a déjà écrit notre couleur d'accent (il faudra alors la retirer proprement).</summary>
    private static bool _accentPose;

    /// <summary>Noms des ressources que nous avons écrites, pour pouvoir toutes les retirer.</summary>
    private static readonly HashSet<string> ClesPosees = [];

    // Échelle typographique du cahier de design (en pixels, avant la taille du texte choisie).
    private static readonly (string Cle, double Taille)[] Tailles =
    [
        ("Taille.Meta", 12),
        ("Taille.Interface", 14),
        ("Taille.Corps", 16),
        ("Taille.SousTitre", 18),
        ("Taille.Titre", 22),
        ("Taille.Salut", 28),
        ("Taille.Chiffre", 40),
        ("Taille.Icone", 16),
        ("Taille.IconeGrande", 20),
        // Taille de texte des contrôles standard du thème Fluent.
        ("ControlContentThemeFontSize", 14),
    ];

    private static void Poser(ResourceDictionary ressources, string cle, object valeur)
    {
        ressources[cle] = valeur;
        ClesPosees.Add(cle);
    }

    public static Color VersWpf(Couleur c, byte opacite = 255) => Color.FromArgb(opacite, c.R, c.G, c.B);

    /// <summary>Couleur d'accent actuelle de Windows.</summary>
    public static Couleur AccentWindows()
    {
        var c = SystemParameters.WindowGlassColor;
        return new Couleur(c.R, c.G, c.B);
    }

    /// <summary>Applique la palette à toute l'application.</summary>
    /// <param name="palette">Couleurs à appliquer.</param>
    /// <param name="modeImpose">Vrai : sombre ; faux : clair ; null : suivre Windows.</param>
    /// <param name="echelle">Taille du texte choisie (1 = 100 %).</param>
    public static void Appliquer(Palette palette, bool? modeImpose, double echelle = 1)
    {
        PaletteCourante = palette;
        CharteCourante = Charte.Calculer(palette);
        var application = Application.Current;
        var mode = modeImpose switch
        {
            true => ThemeMode.Dark,
            false => ThemeMode.Light,
            null => ThemeMode.System,
        };

        // Si on avait imposé un accent et qu'on rend la main à Windows, on recharge le thème
        // de zéro : c'est WPF qui remet alors les couleurs du système.
        if (_accentPose && !palette.AccentImpose)
        {
            // On retire tout ce qu'on avait écrit, sinon nos valeurs masqueraient celles du thème.
            foreach (string cle in ClesPosees) application.Resources.Remove(cle);
            ClesPosees.Clear();
            application.ThemeMode = ThemeMode.None;
            _accentPose = false;
        }
        application.ThemeMode = mode;

        PoserAccent();
        foreach (var (cle, valeur) in Ressources(CharteCourante))
            Poser(application.Resources, cle, valeur);
        foreach (var (cle, taille) in Tailles)
            Poser(application.Resources, cle, Math.Round(taille * echelle, 1));
        foreach (Window fenetre in application.Windows) PoserFond(fenetre);
    }

    /// <summary>
    /// Tous les pinceaux d'une charte, sous leurs noms de ressource. Sert à l'application entière,
    /// et à la Galerie pour montrer côte à côte un thème clair et un thème sombre.
    /// </summary>
    public static IEnumerable<(string Cle, object Valeur)> Ressources(Charte c)
    {
        // Neutres.
        yield return ("Pinceau.Fond", Pinceau(c.Fond));
        yield return ("Pinceau.Surface", Pinceau(c.Surface));
        yield return ("Pinceau.Surelevee", Pinceau(c.Surelevee));
        yield return ("Pinceau.Bordure", Pinceau(c.Bordure));
        yield return ("Pinceau.Texte", Pinceau(c.Texte));
        yield return ("Pinceau.TexteSecondaire", Pinceau(c.TexteSecondaire));
        yield return ("Pinceau.TexteDiscret", Pinceau(c.TexteDiscret));
        // Voiles de survol et d'appui : le texte en transparence, posé sur n'importe quel fond.
        yield return ("Pinceau.Voile", Pinceau(c.Texte, (byte)(c.Sombre ? 22 : 14)));
        yield return ("Pinceau.VoileFort", Pinceau(c.Texte, (byte)(c.Sombre ? 40 : 28)));
        // Fond des contrôles secondaires (boutons, zones) : un peu plus marqué que la surface.
        yield return ("Pinceau.Controle", Pinceau(c.Surface.Melanger(c.Texte, c.Sombre ? 0.07 : 0.04)));
        yield return ("Couleur.Ombre", VersWpf(Couleur.Noir));

        // Accent.
        yield return ("Pinceau.Accent", Pinceau(c.Accent));
        yield return ("Pinceau.SurAccent", Pinceau(c.SurAccent));
        yield return ("Pinceau.AccentTexte", Pinceau(c.AccentTexte));
        yield return ("Pinceau.AccentTrait", Pinceau(c.AccentTrait));
        // Calendrier de régularité : une seule teinte (l'accent), du plus clair au plus soutenu.
        yield return ("Pinceau.Niveau1", Pinceau(c.Surface.Melanger(c.AccentTrait, 0.30)));
        yield return ("Pinceau.Niveau2", Pinceau(c.Surface.Melanger(c.AccentTrait, 0.52)));
        yield return ("Pinceau.Niveau3", Pinceau(c.Surface.Melanger(c.AccentTrait, 0.76)));
        yield return ("Pinceau.Niveau4", Pinceau(c.AccentTrait));

        // Tons : couleurs de sens et de matière, trois nuances chacune (voir Controles/Ton.cs).
        foreach (var (nom, n) in TousLesTons(c))
        {
            yield return ($"Ton.{nom}.Pale", Pinceau(n.Pale));
            yield return ($"Ton.{nom}.Trait", Pinceau(n.Trait));
            yield return ($"Ton.{nom}.Texte", Pinceau(n.Texte));
        }

        // Les mêmes couleurs sous les noms du thème Fluent (contrôles standard, anciens écrans).
        yield return ("TextFillColorPrimaryBrush", Pinceau(c.Texte));
        yield return ("TextFillColorSecondaryBrush", Pinceau(c.TexteSecondaire));
        yield return ("TextFillColorTertiaryBrush", Pinceau(c.TexteDiscret));
        yield return ("CardBackgroundFillColorDefaultBrush", Pinceau(c.Surface));
        yield return ("CardStrokeColorDefaultBrush", Pinceau(c.Bordure));
        yield return ("ControlStrongStrokeColorDefaultBrush", Pinceau(c.TexteDiscret));
    }

    /// <summary>Les tons nommés : Info, Succes, Attention, Erreur, puis une teinte par matière (Bleu, Violet…).</summary>
    public static IEnumerable<(string Nom, Nuances Nuances)> TousLesTons(Charte c)
    {
        yield return ("Info", c.Info);
        yield return ("Succes", c.Succes);
        yield return ("Attention", c.Attention);
        yield return ("Erreur", c.Erreur);
        yield return ("Facile", c.Facile);
        foreach (var (teinte, nuances) in c.Matieres) yield return (teinte.ToString(), nuances);
    }

    /// <summary>
    /// Donne à une fenêtre le fond et l'accent du thème. À appeler dans le constructeur de chaque fenêtre.
    /// </summary>
    public static void Habiller(Window fenetre)
    {
        PoserFond(fenetre);
        fenetre.SetResourceReference(Window.FontFamilyProperty, "Police.Interface");
        fenetre.SetResourceReference(Window.FontSizeProperty, "Taille.Interface");
        fenetre.SetResourceReference(Window.ForegroundProperty, "Pinceau.Texte");
        // WPF réapplique le thème Fluent quand une fenêtre s'ouvre : on repose notre accent juste après.
        fenetre.Loaded += (_, _) => PoserAccent();
        fenetre.ContentRendered += (_, _) => PoserAccent();
    }

    /// <summary>
    /// Sans fond imposé (thèmes Clair, Sombre, Comme Windows), on laisse celui du thème Fluent :
    /// l'effet Mica de Windows 11, ou un fond uni sous Windows 10.
    /// </summary>
    private static void PoserFond(Window fenetre)
    {
        if (PaletteCourante?.Fond is { } fond)
            fenetre.Background = Pinceau(fond);
        else
            fenetre.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
    }

    private static SolidColorBrush Pinceau(Couleur couleur, byte opacite = 255)
    {
        var pinceau = new SolidColorBrush(VersWpf(couleur, opacite));
        pinceau.Freeze(); // un pinceau figé est plus rapide et partageable
        return pinceau;
    }

    /// <summary>Écrit la couleur d'accent de la palette sous les noms utilisés par le thème Fluent.</summary>
    private static void PoserAccent()
    {
        if (PaletteCourante is not { AccentImpose: true } palette) return;
        var r = Application.Current.Resources;
        Couleur accent = palette.Accent, surAccent = palette.TexteSurAccent;
        Couleur accentTexte = CharteCourante?.AccentTexte ?? palette.AccentTexte;

        // Couleurs de base dont le thème dérive ses pinceaux.
        Poser(r, "SystemAccentColor", VersWpf(accent));
        Poser(r, "SystemAccentColorPrimary", VersWpf(accent));
        Poser(r, "SystemAccentColorSecondary", VersWpf(accent));
        Poser(r, "SystemAccentColorTertiary", VersWpf(accent));
        Poser(r, "SystemAccentColorLight1", VersWpf(accent.Melanger(Couleur.Blanc, 0.15)));
        Poser(r, "SystemAccentColorLight2", VersWpf(accent.Melanger(Couleur.Blanc, 0.30)));
        Poser(r, "SystemAccentColorLight3", VersWpf(accent.Melanger(Couleur.Blanc, 0.45)));
        Poser(r, "SystemAccentColorDark1", VersWpf(accent.Melanger(Couleur.Noir, 0.15)));
        Poser(r, "SystemAccentColorDark2", VersWpf(accent.Melanger(Couleur.Noir, 0.30)));
        Poser(r, "SystemAccentColorDark3", VersWpf(accent.Melanger(Couleur.Noir, 0.45)));

        // Remplissages (boutons principaux, sélection, barres, curseurs).
        Poser(r, "SystemAccentBrush", Pinceau(accent));
        Poser(r, "SystemAccentColorPrimaryBrush", Pinceau(accent));
        Poser(r, "SystemAccentColorSecondaryBrush", Pinceau(accent));
        Poser(r, "SystemAccentColorTertiaryBrush", Pinceau(accent));
        Poser(r, "SystemFillColorAttentionBrush", Pinceau(accent));
        Poser(r, "AccentFillColorDefaultBrush", Pinceau(accent));
        Poser(r, "AccentFillColorSecondaryBrush", Pinceau(accent, 230));
        Poser(r, "AccentFillColorTertiaryBrush", Pinceau(accent, 204));
        Poser(r, "AccentFillColorSelectedTextBackgroundBrush", Pinceau(accent));
        Poser(r, "AccentButtonBackground", Pinceau(accent));
        Poser(r, "AccentButtonBackgroundPointerOver", Pinceau(accent, 230));
        Poser(r, "AccentButtonBackgroundPressed", Pinceau(accent, 204));
        // Élément sélectionné d'une liste : accent en transparence, pour que le texte (blanc en sombre,
        // noir en clair) reste lisible quelle que soit la couleur choisie.
        Poser(r, "ListBoxItemSelectedBackgroundThemeBrush", Pinceau(accent, 90));
        Poser(r, "ListBoxItemSelectedBackgroundPointerOverThemeBrush", Pinceau(accent, 110));
        Poser(r, "ListBoxItemSelectedBackgroundPressedThemeBrush", Pinceau(accent, 70));
        Poser(r, "ListBoxItemBackgroundSelectedPressedThemeBrush", Pinceau(accent, 70));
        Poser(r, "ProgressBarForeground", Pinceau(accent));
        Poser(r, "SliderThumbBackground", Pinceau(accent));
        Poser(r, "SliderThumbBackgroundPointerOver", Pinceau(accent, 230));
        Poser(r, "ListViewItemPillFillBrush", Pinceau(accent));
        Poser(r, "ComboBoxItemPillFillBrush", Pinceau(accent));

        // Texte posé sur l'accent : noir ou blanc, selon ce qui se lit le mieux.
        Poser(r, "TextOnAccentFillColorPrimaryBrush", Pinceau(surAccent));
        Poser(r, "TextOnAccentFillColorSecondaryBrush", Pinceau(surAccent, 180));
        Poser(r, "TextOnAccentFillColorSelectedTextBrush", Pinceau(surAccent));
        Poser(r, "AccentButtonForeground", Pinceau(surAccent));
        Poser(r, "AccentButtonForegroundPointerOver", Pinceau(surAccent));
        Poser(r, "AccentButtonForegroundPressed", Pinceau(surAccent, 180));

        // Accent utilisé comme couleur de texte.
        Poser(r, "AccentTextFillColorPrimaryBrush", Pinceau(accentTexte));
        Poser(r, "AccentTextFillColorSecondaryBrush", Pinceau(accentTexte));
        Poser(r, "AccentTextFillColorTertiaryBrush", Pinceau(accentTexte));

        _accentPose = true;
    }
}
