using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.Core.Mascotte;

namespace Revisions.App.Views;

/// <summary>
/// État vide : la mascotte (curieuse par défaut), une phrase et une action. Si la mascotte est masquée
/// dans les réglages, seuls le texte et l'action restent.
/// </summary>
public partial class EtatVide : UserControl
{
    public static readonly DependencyProperty TitreProperty = DependencyProperty.Register(nameof(Titre), typeof(string), typeof(EtatVide));
    public static readonly DependencyProperty TexteProperty = DependencyProperty.Register(nameof(Texte), typeof(string), typeof(EtatVide));
    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(object), typeof(EtatVide));
    public static readonly DependencyProperty HumeurProperty = DependencyProperty.Register(nameof(Humeur), typeof(Humeur), typeof(EtatVide), new PropertyMetadata(Humeur.Curieuse));

    public string? Titre { get => (string?)GetValue(TitreProperty); set => SetValue(TitreProperty, value); }
    public string? Texte { get => (string?)GetValue(TexteProperty); set => SetValue(TexteProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public Humeur Humeur { get => (Humeur)GetValue(HumeurProperty); set => SetValue(HumeurProperty, value); }

    public EtatVide()
    {
        InitializeComponent();
        string reglage = App.Services?.GetService<EtatApplication>()?.Reglages.Mascotte ?? "normale";
        if (reglage == "masquee") Perso.Visibility = Visibility.Collapsed;
        else if (reglage == "reduite") Perso.Width = Perso.Height = 60;
    }
}
