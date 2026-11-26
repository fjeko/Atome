using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Controles;

/// <summary>
/// Grille de tuiles qui occupe toute la largeur disponible : autant de colonnes que possible
/// (chaque tuile fait au moins <see cref="LargeurMin"/>), puis les tuiles s'élargissent pour remplir la ligne.
/// Ainsi la grille reste alignée sur les bandeaux au-dessus, quelle que soit la taille de la fenêtre
/// (un WrapPanel laissait un vide à droite).
/// </summary>
public sealed class GrilleTuiles : Panel
{
    public static readonly DependencyProperty LargeurMinProperty = DependencyProperty.Register(
        nameof(LargeurMin), typeof(double), typeof(GrilleTuiles),
        new FrameworkPropertyMetadata(228.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Largeur minimale d'une tuile, marges comprises.</summary>
    public double LargeurMin
    {
        get => (double)GetValue(LargeurMinProperty);
        set => SetValue(LargeurMinProperty, value);
    }

    private int Colonnes(double largeur) =>
        double.IsInfinity(largeur) ? Math.Max(1, InternalChildren.Count) : Math.Max(1, (int)(largeur / LargeurMin));

    protected override Size MeasureOverride(Size dispo)
    {
        int colonnes = Colonnes(dispo.Width);
        double largeurCase = double.IsInfinity(dispo.Width) ? LargeurMin : dispo.Width / colonnes;
        double total = 0, hauteurLigne = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement enfant = InternalChildren[i];
            enfant.Measure(new Size(largeurCase, double.PositiveInfinity));
            hauteurLigne = Math.Max(hauteurLigne, enfant.DesiredSize.Height);
            if (i % colonnes == colonnes - 1 || i == InternalChildren.Count - 1) { total += hauteurLigne; hauteurLigne = 0; }
        }
        double largeur = double.IsInfinity(dispo.Width) ? largeurCase * Math.Min(colonnes, InternalChildren.Count) : dispo.Width;
        return new Size(largeur, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int colonnes = Colonnes(final.Width);
        double largeurCase = final.Width / colonnes;
        double y = 0;
        for (int debut = 0; debut < InternalChildren.Count; debut += colonnes)
        {
            int fin = Math.Min(debut + colonnes, InternalChildren.Count);
            double hauteurLigne = 0;
            for (int i = debut; i < fin; i++) hauteurLigne = Math.Max(hauteurLigne, InternalChildren[i].DesiredSize.Height);
            for (int i = debut; i < fin; i++)
                InternalChildren[i].Arrange(new Rect((i - debut) * largeurCase, y, largeurCase, hauteurLigne));
            y += hauteurLigne;
        }
        return final;
    }
}
