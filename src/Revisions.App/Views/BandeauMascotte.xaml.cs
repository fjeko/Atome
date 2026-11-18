using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.Core.Mascotte;

namespace Revisions.App.Views;

/// <summary>
/// La mascotte avec sa bulle. Deux façons de l'utiliser :
/// - <see cref="Message"/> et <see cref="Humeur"/> donnés par l'écran (accueil, fin de session) ;
/// - ou seulement <see cref="Contexte"/> (« bibliotheque », « statistiques ») : une phrase est alors tirée
///   au hasard dans la banque de messages (Core/Mascotte/messages-mascotte.json).
/// Le réglage « Mascotte » la réduit (plus petite, bulle compacte) ou la masque.
/// </summary>
public partial class BandeauMascotte : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(BandeauMascotte),
        new PropertyMetadata(null, (o, e) => ((BandeauMascotte)o).Texte.Text = (string?)e.NewValue));

    public static readonly DependencyProperty HumeurProperty = DependencyProperty.Register(
        nameof(Humeur), typeof(Humeur), typeof(BandeauMascotte),
        new PropertyMetadata(Humeur.Contente, (o, e) => ((BandeauMascotte)o).Perso.Humeur = (Humeur)e.NewValue));

    public static readonly DependencyProperty TailleProperty = DependencyProperty.Register(
        nameof(Taille), typeof(double), typeof(BandeauMascotte),
        new PropertyMetadata(88.0, (o, _) => ((BandeauMascotte)o).AppliquerReglage()));

    public static readonly DependencyProperty ContexteProperty = DependencyProperty.Register(
        nameof(Contexte), typeof(string), typeof(BandeauMascotte),
        new PropertyMetadata(null, (o, e) =>
        {
            if (e.NewValue is not string contexte) return;
            var b = (BandeauMascotte)o;
            var message = BanqueMessages.Tirer(contexte);
            b.Message = message.Texte;
            b.Humeur = message.Humeur;
        }));

    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public Humeur Humeur { get => (Humeur)GetValue(HumeurProperty); set => SetValue(HumeurProperty, value); }
    public double Taille { get => (double)GetValue(TailleProperty); set => SetValue(TailleProperty, value); }
    public string? Contexte { get => (string?)GetValue(ContexteProperty); set => SetValue(ContexteProperty, value); }

    public BandeauMascotte()
    {
        InitializeComponent();
        AppliquerReglage();
    }

    /// <summary>Taille de la mascotte selon le réglage : normale, réduite (60 %) ou masquée.</summary>
    private void AppliquerReglage()
    {
        string reglage = App.Services?.GetService<EtatApplication>()?.Reglages.Mascotte ?? "normale";
        Visibility = reglage == "masquee" ? Visibility.Collapsed : Visibility.Visible;
        Perso.Width = Perso.Height = reglage == "reduite" ? Taille * 0.6 : Taille;
    }
}
