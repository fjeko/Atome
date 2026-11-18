using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Revisions.App.Controles;

/// <summary>
/// AnneauProgression : un cercle qui se remplit de 0 à 1 (maîtrise d'une matière, réussite d'une session).
/// Dessiné directement (OnRender), sans éléments enfants : léger même dans une longue liste de tuiles.
/// Le contenu (un pourcentage, une icône) est centré dedans.
/// </summary>
public class Anneau : ContentControl
{
    public static readonly DependencyProperty ValeurProperty = DependencyProperty.Register(
        nameof(Valeur), typeof(double), typeof(Anneau),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, null,
            (_, v) => Math.Clamp((double)v, 0, 1)));

    public static readonly DependencyProperty EpaisseurProperty = DependencyProperty.Register(
        nameof(Epaisseur), typeof(double), typeof(Anneau),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TraitProperty = DependencyProperty.Register(
        nameof(Trait), typeof(Brush), typeof(Anneau),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PisteProperty = DependencyProperty.Register(
        nameof(Piste), typeof(Brush), typeof(Anneau),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Part remplie, de 0 à 1.</summary>
    public double Valeur { get => (double)GetValue(ValeurProperty); set => SetValue(ValeurProperty, value); }
    public double Epaisseur { get => (double)GetValue(EpaisseurProperty); set => SetValue(EpaisseurProperty, value); }
    /// <summary>Couleur de la partie remplie.</summary>
    public Brush Trait { get => (Brush)GetValue(TraitProperty); set => SetValue(TraitProperty, value); }
    /// <summary>Couleur du reste du cercle.</summary>
    public Brush Piste { get => (Brush)GetValue(PisteProperty); set => SetValue(PisteProperty, value); }

    static Anneau()
    {
        HorizontalContentAlignmentProperty.OverrideMetadata(typeof(Anneau), new FrameworkPropertyMetadata(HorizontalAlignment.Center));
        VerticalContentAlignmentProperty.OverrideMetadata(typeof(Anneau), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        FocusableProperty.OverrideMetadata(typeof(Anneau), new FrameworkPropertyMetadata(false));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double taille = Math.Min(ActualWidth, ActualHeight);
        if (taille <= Epaisseur) return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        double rayon = (taille - Epaisseur) / 2;

        dc.DrawEllipse(null, new Pen(Piste, Epaisseur), centre, rayon, rayon);
        if (Valeur <= 0) return;

        var stylo = new Pen(Trait, Epaisseur) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (Valeur >= 0.999)
        {
            dc.DrawEllipse(null, stylo, centre, rayon, rayon);
            return;
        }
        // Arc partant du haut, dans le sens des aiguilles d'une montre.
        double angle = Valeur * 2 * Math.PI;
        var depart = new Point(centre.X, centre.Y - rayon);
        var arrivee = new Point(centre.X + rayon * Math.Sin(angle), centre.Y - rayon * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = depart, IsClosed = false };
        figure.Segments.Add(new ArcSegment(arrivee, new Size(rayon, rayon), 0, Valeur > 0.5, SweepDirection.Clockwise, true));
        var geometrie = new PathGeometry([figure]);
        geometrie.Freeze();
        dc.DrawGeometry(null, stylo, geometrie);
    }
}
