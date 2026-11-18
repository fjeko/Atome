using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Revisions.App.Controles;

/// <summary>
/// Courbe d'une seule série (écran Statistiques : cartes révisées par jour sur 30 jours).
/// Trait de 2 px, aire très légère dessous, grille discrète ; au survol, un repère vertical, un point
/// et une infobulle donnent la valeur exacte du jour. Dessinée directement (OnRender) : rien à mettre en page.
/// </summary>
public class Courbe : FrameworkElement
{
    public static readonly DependencyProperty ValeursProperty = DependencyProperty.Register(nameof(Valeurs), typeof(IList), typeof(Courbe),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EtiquettesProperty = DependencyProperty.Register(nameof(Etiquettes), typeof(IList), typeof(Courbe));
    public static readonly DependencyProperty TraitProperty = DependencyProperty.Register(nameof(Trait), typeof(Brush), typeof(Courbe),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GrilleProperty = DependencyProperty.Register(nameof(Grille), typeof(Brush), typeof(Courbe),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Valeurs (nombres), de la plus ancienne à la plus récente.</summary>
    public IList? Valeurs { get => (IList?)GetValue(ValeursProperty); set => SetValue(ValeursProperty, value); }
    /// <summary>Texte de l'infobulle pour chaque valeur (« mardi 18/11 : 24 cartes »).</summary>
    public IList? Etiquettes { get => (IList?)GetValue(EtiquettesProperty); set => SetValue(EtiquettesProperty, value); }
    public Brush Trait { get => (Brush)GetValue(TraitProperty); set => SetValue(TraitProperty, value); }
    public Brush Grille { get => (Brush)GetValue(GrilleProperty); set => SetValue(GrilleProperty, value); }

    private int _survol = -1;

    public Courbe()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Mouse);
    }

    private double[] Nombres() => Valeurs?.Cast<object>().Select(Convert.ToDouble).ToArray() ?? [];

    private Point PointDe(int i, double[] v, double max)
    {
        double largeur = ActualWidth, hauteur = ActualHeight - 6;
        double x = v.Length == 1 ? largeur / 2 : i * largeur / (v.Length - 1);
        double y = 3 + hauteur - v[i] / max * hauteur;
        return new Point(x, y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var v = Nombres();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // pour recevoir la souris partout
        if (v.Length == 0 || ActualWidth <= 0) return;
        double max = Math.Max(1, v.Max());

        // Grille discrète : trois lignes horizontales.
        var stylo = new Pen(Grille, 1);
        for (int k = 0; k <= 2; k++)
        {
            double y = Math.Round(3 + (ActualHeight - 6) * k / 2) + 0.5;
            dc.DrawLine(stylo, new Point(0, y), new Point(ActualWidth, y));
        }

        var ligne = new StreamGeometry();
        using (var g = ligne.Open())
        {
            g.BeginFigure(PointDe(0, v, max), false, false);
            for (int i = 1; i < v.Length; i++) g.LineTo(PointDe(i, v, max), true, true);
        }
        ligne.Freeze();

        // Aire très légère sous la courbe, puis le trait de 2 px.
        var aire = new StreamGeometry();
        using (var g = aire.Open())
        {
            g.BeginFigure(new Point(PointDe(0, v, max).X, ActualHeight - 3), true, true);
            for (int i = 0; i < v.Length; i++) g.LineTo(PointDe(i, v, max), true, true);
            g.LineTo(new Point(PointDe(v.Length - 1, v, max).X, ActualHeight - 3), true, true);
        }
        aire.Freeze();
        var voile = Trait.CloneCurrentValue();
        voile.Opacity = 0.12;
        dc.DrawGeometry(voile, null, aire);
        dc.DrawGeometry(null, new Pen(Trait, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, ligne);

        // Survol : repère vertical et point (8 px, entouré de la couleur de fond).
        if (_survol >= 0 && _survol < v.Length)
        {
            var p = PointDe(_survol, v, max);
            dc.DrawLine(new Pen(Grille, 1), new Point(p.X, 0), new Point(p.X, ActualHeight));
            dc.DrawEllipse(Trait, new Pen((Brush)(TryFindResource("Pinceau.Surface") ?? Brushes.White), 2), p, 5, 5);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var v = Nombres();
        if (v.Length == 0) return;
        double x = e.GetPosition(this).X;
        int i = v.Length == 1 ? 0 : (int)Math.Round(Math.Clamp(x / ActualWidth, 0, 1) * (v.Length - 1));
        if (i == _survol) return;
        _survol = i;
        ToolTip = Etiquettes is { } e2 && i < e2.Count ? e2[i] : v[i].ToString();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        InvalidateVisual();
    }
}
