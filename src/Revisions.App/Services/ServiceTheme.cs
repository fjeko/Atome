using System.Windows;
using System.Windows.Media;
using Revisions.Core.Apparence;

namespace Revisions.App.Services;

/// <summary>
/// Applique une palette (voir Core/Apparence) à l'interface WPF : mode clair ou sombre,
/// couleur d'accent et fond des fenêtres. Le contenu web reçoit la même palette par VueMarkdown.
/// </summary>
/// <remarks>
/// Le thème Fluent de WPF range la couleur d'accent de Windows dans les ressources de l'application,
/// sous des noms fixes. Pour imposer notre couleur, on écrit nos valeurs sous ces mêmes noms.
/// Comme WPF peut les réécrire (nouvelle fenêtre, changement dans Windows), on les repose aussi
/// à chaque fenêtre ouverte.
/// </remarks>
public static class ServiceTheme
{
    /// <summary>Palette en cours (utile aux fenêtres ouvertes plus tard).</summary>
    public static Palette? PaletteCourante { get; private set; }

    /// <summary>Vrai si on a déjà écrit notre couleur d'accent (il faudra alors la retirer proprement).</summary>
    private static bool _accentPose;

    /// <summary>Noms des ressources que nous avons écrites, pour pouvoir toutes les retirer.</summary>
    private static readonly HashSet<string> ClesPosees = [];

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
    public static void Appliquer(Palette palette, bool? modeImpose)
    {
        PaletteCourante = palette;
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
        foreach (Window fenetre in application.Windows) PoserFond(fenetre);
    }

    /// <summary>
    /// Donne à une fenêtre le fond et l'accent du thème. À appeler dans le constructeur de chaque fenêtre.
    /// </summary>
    public static void Habiller(Window fenetre)
    {
        PoserFond(fenetre);
        // WPF réapplique le thème Fluent quand une fenêtre s'ouvre : on repose notre accent juste après.
        fenetre.Loaded += (_, _) => PoserAccent();
        fenetre.ContentRendered += (_, _) => PoserAccent();
    }

    /// <summary>Sans fond imposé, on laisse celui du thème Fluent (avec son effet de transparence).</summary>
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

        // Utilisé comme texte sur le fond des fenêtres, l'accent doit rester lisible.
        Couleur fondFenetre = palette.Fond ?? (palette.Sombre ? Couleur.Parse("#202020") : Couleur.Parse("#F3F3F3"));
        Couleur accentTexte = accent.AjusterPourContraste(fondFenetre, 4.5);

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
