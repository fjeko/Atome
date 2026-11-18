using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Revisions.App.Services;

/// <summary>
/// Les animations d'Atome, au même endroit (cahier de design, section 1) : transitions d'écran, retournement
/// de carte, « +10 » qui monte, petite célébration. Elles n'animent que des propriétés légères (opacité,
/// transformations), jamais la mise en page.
/// </summary>
/// <remarks>
/// Si Windows a désactivé les « Effets d'animation », ou si l'interrupteur « Réduire les animations » est actif,
/// tout devient un fondu de moins de 100 ms.
/// </remarks>
public static class Mouvement
{
    /// <summary>Réglage d'Atome « Réduire les animations ».</summary>
    public static bool ReductionDemandee { get; set; }

    /// <summary>Vrai si les animations doivent rester minimales (réglage d'Atome ou de Windows).</summary>
    public static bool Reduit => ReductionDemandee || !SystemParameters.ClientAreaAnimation;

    public static readonly Duration Fondu = TimeSpan.FromMilliseconds(90);
    public static readonly Duration Transition = TimeSpan.FromMilliseconds(250);
    public static readonly Duration DemiRetournement = TimeSpan.FromMilliseconds(150);
    public static readonly Duration Monte = TimeSpan.FromMilliseconds(600);

    private static readonly IEasingFunction Douceur = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static TTransform Transformation<TTransform>(UIElement element) where TTransform : Transform, new()
    {
        if (element.RenderTransform is TTransform existante && !existante.IsFrozen) return existante;
        var t = new TTransform();
        element.RenderTransform = t;
        return t;
    }

    private static DoubleAnimation Animation(double? de, double vers, Duration duree) =>
        new() { From = de, To = vers, Duration = duree, EasingFunction = Douceur };

    /// <summary>Apparition d'un écran : fondu et léger glissement vers le haut (250 ms).</summary>
    public static void Apparaitre(UIElement element)
    {
        if (Reduit)
        {
            element.BeginAnimation(UIElement.OpacityProperty, Animation(0, 1, Fondu));
            return;
        }
        var t = Transformation<TranslateTransform>(element);
        element.BeginAnimation(UIElement.OpacityProperty, Animation(0, 1, Transition));
        t.BeginAnimation(TranslateTransform.YProperty, Animation(12, 0, Transition));
    }

    /// <summary>
    /// Retournement de carte (300 ms) : la carte se referme, <paramref name="auMilieu"/> change son contenu,
    /// puis elle se rouvre. En animations réduites : un simple fondu.
    /// </summary>
    public static void Retourner(UIElement element, Action auMilieu)
    {
        if (Reduit)
        {
            auMilieu();
            element.BeginAnimation(UIElement.OpacityProperty, Animation(0.3, 1, Fondu));
            return;
        }
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var t = Transformation<ScaleTransform>(element);
        var fermeture = new DoubleAnimation(1, 0, DemiRetournement) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        fermeture.Completed += (_, _) =>
        {
            auMilieu();
            t.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, DemiRetournement) { EasingFunction = Douceur });
        };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, fermeture);
    }

    /// <summary>
    /// Petit texte qui monte et s'efface (« +10 », 600 ms), posé sur un Canvas au-dessus de l'écran.
    /// </summary>
    public static void FaireMonter(Canvas scene, string texte, Point position, Brush couleur)
    {
        var etiquette = new TextBlock
        {
            Text = texte,
            Foreground = couleur,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
        };
        etiquette.SetResourceReference(TextBlock.FontSizeProperty, "Taille.SousTitre");
        Canvas.SetLeft(etiquette, position.X);
        Canvas.SetTop(etiquette, position.Y);
        scene.Children.Add(etiquette);

        var duree = Reduit ? Fondu : Monte;
        var disparition = new DoubleAnimation(1, 0, duree) { BeginTime = Reduit ? null : TimeSpan.FromMilliseconds(200) };
        disparition.Completed += (_, _) => scene.Children.Remove(etiquette);
        if (!Reduit)
            Transformation<TranslateTransform>(etiquette).BeginAnimation(TranslateTransform.YProperty, Animation(0, -32, duree));
        etiquette.BeginAnimation(UIElement.OpacityProperty, disparition);
    }

    /// <summary>Petite célébration discrète (niveau gagné, tuile qui se coche) : un léger rebond, jamais bloquant.</summary>
    public static void Celebrer(UIElement element)
    {
        if (Reduit)
        {
            element.BeginAnimation(UIElement.OpacityProperty, Animation(0.4, 1, Fondu));
            return;
        }
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var t = Transformation<ScaleTransform>(element);
        var rebond = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(0)));
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromPercent(0.55), Douceur));
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new SineEase()));
        t.BeginAnimation(ScaleTransform.ScaleXProperty, rebond);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, rebond);
    }
}
