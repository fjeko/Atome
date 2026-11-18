using System.Windows;
using System.Windows.Controls;
using Revisions.App.Services;
using Revisions.Core.Apparence;

namespace Revisions.App.Views;

/// <summary>
/// Galerie : la même planche en Sombre (à gauche) et en Clair (à droite). Chaque moitié reçoit ses propres
/// ressources de couleur ; comme tous les composants lisent leurs couleurs en DynamicResource, ils prennent
/// celles de leur moitié. L'accent reste celui choisi dans les réglages.
/// </summary>
public partial class VueGalerie : UserControl
{
    public VueGalerie()
    {
        InitializeComponent();
        Habiller(CadreSombre, "sombre");
        Habiller(CadreClair, "clair");
    }

    private static void Habiller(FrameworkElement cadre, string cleTheme)
    {
        var actuelle = ServiceTheme.PaletteCourante;
        Couleur? accent = actuelle is { AccentImpose: true } ? actuelle.Accent : null;
        var palette = Themes.Resoudre(Themes.Trouver(cleTheme), accent, false, ServiceTheme.AccentWindows());
        foreach (var (cle, valeur) in ServiceTheme.Ressources(Charte.Calculer(palette)))
            cadre.Resources[cle] = valeur;
    }

    /// <summary>Bouton « +10 XP » : fait monter un « +10 » sur le compteur.</summary>
    private void GagnerXp(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CompteurXp compteur) compteur.Gagner(10, niveauGagne: true);
    }
}
