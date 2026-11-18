using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Revisions.App.Services;

namespace Revisions.App.Views;

/// <summary>CompteurXP : niveau, barre de progression et XP. <see cref="Gagner"/> fait monter un « +10 ».</summary>
public partial class CompteurXp : UserControl
{
    public static readonly DependencyProperty NiveauProperty = DependencyProperty.Register(nameof(Niveau), typeof(int), typeof(CompteurXp));
    public static readonly DependencyProperty ProgressionProperty = DependencyProperty.Register(nameof(Progression), typeof(double), typeof(CompteurXp));
    public static readonly DependencyProperty TexteProperty = DependencyProperty.Register(nameof(Texte), typeof(string), typeof(CompteurXp));

    public int Niveau { get => (int)GetValue(NiveauProperty); set => SetValue(NiveauProperty, value); }
    public double Progression { get => (double)GetValue(ProgressionProperty); set => SetValue(ProgressionProperty, value); }
    public string? Texte { get => (string?)GetValue(TexteProperty); set => SetValue(TexteProperty, value); }

    public CompteurXp() => InitializeComponent();

    /// <summary>Fait monter « +N » à côté du badge de niveau (600 ms), et fait rebondir le badge si un niveau est gagné.</summary>
    public void Gagner(int xp, bool niveauGagne = false)
    {
        var couleur = (Brush)FindResource("Pinceau.AccentTexte");
        Mouvement.FaireMonter(Scene, $"+{xp}", new Point(Badge.ActualWidth + 8, -4), couleur);
        if (niveauGagne) Mouvement.Celebrer(Badge);
    }
}
