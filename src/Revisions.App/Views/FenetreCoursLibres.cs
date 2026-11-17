using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Views;

/// <summary>
/// Proposition du premier lancement (aucun pack) : charger les cours libres livrés avec Atome ?
/// ShowDialog() renvoie true pour « Oui », false pour « Plus tard ».
/// </summary>
public sealed class FenetreCoursLibres : Window
{
    public FenetreCoursLibres(int nombreDeCours)
    {
        Title = "Bienvenue dans Atome";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var oui = new Button { Content = "Oui", IsDefault = true, Padding = new Thickness(28, 8, 28, 8), Margin = new Thickness(0, 0, 10, 0) };
        oui.Click += (_, _) => DialogResult = true;
        var plusTard = new Button { Content = "Plus tard", IsCancel = true, Padding = new Thickness(20, 8, 20, 8) };
        plusTard.Click += (_, _) => DialogResult = false;

        var pied = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        pied.Children.Add(oui);
        pied.Children.Add(plusTard);

        var contenu = new StackPanel { Margin = new Thickness(24) };
        contenu.Children.Add(new TextBlock
        {
            Text = $"Charger les cours libres BTS SIO SLAM – 1re année ? ({nombreDeCours} cours, licence libre)",
            FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        contenu.Children.Add(new TextBlock
        {
            Text = "Ce sont des cours originaux livrés avec Atome, avec leurs cartes de révision. Tu pourras les masquer ou les remettre à tout moment dans Réglages → Cours libres intégrés.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.75,
        });
        contenu.Children.Add(pied);
        Content = contenu;
    }
}
