using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Revisions.App.Services;
using Revisions.Core.Mascotte;

namespace Revisions.App.Views;

/// <summary>
/// La mascotte : un petit atome souriant. Son <see cref="Humeur"/> choisit les yeux et la bouche ;
/// ses orbites tournent lentement, sauf si elle dort ou si les animations sont réduites.
/// </summary>
public partial class Mascotte : UserControl
{
    public static readonly DependencyProperty HumeurProperty = DependencyProperty.Register(
        nameof(Humeur), typeof(Humeur), typeof(Mascotte),
        new PropertyMetadata(Humeur.Contente, (o, _) => ((Mascotte)o).Appliquer()));

    public Humeur Humeur
    {
        get => (Humeur)GetValue(HumeurProperty);
        set => SetValue(HumeurProperty, value);
    }

    public Mascotte()
    {
        InitializeComponent();
        Appliquer();
        Loaded += (_, _) => Animer();
        Unloaded += (_, _) => Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>Les orbites font un tour en 40 secondes : un mouvement de fond, à peine perceptible.</summary>
    private void Animer()
    {
        if (Mouvement.Reduit || Humeur == Humeur.Endormie)
        {
            Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            return;
        }
        Rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(40)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>Montre les yeux, la bouche et les petits signes de l'humeur choisie.</summary>
    private void Appliquer()
    {
        if (OeilG is null) return; // pendant InitializeComponent
        var h = Humeur;
        static Visibility Si(bool vrai) => vrai ? Visibility.Visible : Visibility.Collapsed;

        bool yeuxOuverts = h is Humeur.Contente or Humeur.Curieuse or Humeur.Encourageante;
        OeilG.Visibility = Si(yeuxOuverts);
        OeilD.Visibility = Si(yeuxOuverts && h != Humeur.Encourageante);
        ClinD.Visibility = Si(h == Humeur.Encourageante);
        JoieG.Visibility = JoieD.Visibility = Si(h == Humeur.Fiere);
        SommeilG.Visibility = SommeilD.Visibility = Si(h == Humeur.Endormie);
        Sourcil.Visibility = Si(h == Humeur.Curieuse);

        Sourire.Visibility = Si(h == Humeur.Contente);
        GrandSourire.Visibility = Si(h == Humeur.Encourageante);
        Rire.Visibility = Si(h == Humeur.Fiere);
        BoucheO.Visibility = Si(h is Humeur.Curieuse or Humeur.Endormie);
        JoueG.Visibility = JoueD.Visibility = Si(h is Humeur.Fiere or Humeur.Contente or Humeur.Encourageante);

        Zzz.Visibility = Zzz2.Visibility = Si(h == Humeur.Endormie);
        Question.Visibility = Si(h == Humeur.Curieuse);

        // Curieuse : le regard part sur le côté.
        double decalage = h == Humeur.Curieuse ? 2 : 0;
        Canvas.SetLeft(OeilG, 39 + decalage);
        Canvas.SetLeft(OeilD, 55 + decalage);
        Orbites.Opacity = h == Humeur.Endormie ? 0.55 : 1;
        if (IsLoaded) Animer();
    }
}
