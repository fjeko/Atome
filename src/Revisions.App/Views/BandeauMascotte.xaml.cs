using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Views;

/// <summary>
/// La mascotte avec sa bulle. Deux façons de l'utiliser :
/// - <see cref="Message"/> et <see cref="Expression"/> donnés par l'écran (accueil) ;
/// - ou seulement <see cref="Contexte"/> (« bibliotheque », « statistiques ») : une phrase est alors tirée au hasard.
/// </summary>
public partial class BandeauMascotte : UserControl
{
    private static readonly Dictionary<string, (string Message, string Expression)[]> Phrases = new()
    {
        ["bibliotheque"] = new[]
        {
            ("On va bosser quoi aujourd'hui ?", "content"),
            ("Un cours à lire, ou direct des cartes ?", "clin"),
            ("Choisis ta séance, je m'occupe du reste !", "joie"),
            ("Tout ça, c'est du savoir qui n'attend que toi.", "content"),
        },
        ["statistiques"] = new[]
        {
            ("T'aimes bien les chiffres ?", "malin"),
            ("T'as envie de voir à quel point tu cartonnes ?", "joie"),
            ("Les chiffres ne mentent jamais. Moi non plus.", "clin"),
            ("Regarde-moi ça, ça avance !", "content"),
        },
    };

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(BandeauMascotte),
        new PropertyMetadata(null, (o, e) => ((BandeauMascotte)o).Texte.Text = (string?)e.NewValue));

    public static readonly DependencyProperty ExpressionProperty = DependencyProperty.Register(
        nameof(Expression), typeof(string), typeof(BandeauMascotte),
        new PropertyMetadata("content", (o, e) => ((BandeauMascotte)o).Perso.Expression = (string)e.NewValue));

    public static readonly DependencyProperty TailleProperty = DependencyProperty.Register(
        nameof(Taille), typeof(double), typeof(BandeauMascotte),
        new PropertyMetadata(88.0, (o, e) => { var b = (BandeauMascotte)o; b.Perso.Width = b.Perso.Height = (double)e.NewValue; }));

    public static readonly DependencyProperty ContexteProperty = DependencyProperty.Register(
        nameof(Contexte), typeof(string), typeof(BandeauMascotte),
        new PropertyMetadata(null, (o, e) =>
        {
            if (e.NewValue is string c && Phrases.TryGetValue(c, out var choix))
            {
                var (message, expression) = choix[Random.Shared.Next(choix.Length)];
                var b = (BandeauMascotte)o;
                b.Message = message;
                b.Expression = expression;
            }
        }));

    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string Expression { get => (string)GetValue(ExpressionProperty); set => SetValue(ExpressionProperty, value); }
    public double Taille { get => (double)GetValue(TailleProperty); set => SetValue(TailleProperty, value); }
    public string? Contexte { get => (string?)GetValue(ContexteProperty); set => SetValue(ContexteProperty, value); }

    public BandeauMascotte()
    {
        InitializeComponent();
        Perso.Width = Perso.Height = Taille;
    }
}
