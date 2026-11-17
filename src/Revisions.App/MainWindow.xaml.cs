using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Revisions.App.ViewModels;

namespace Revisions.App;

/// <summary>Fenêtre principale. Seul code ici : les raccourcis clavier de la session et de la lecture.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Services.ServiceTheme.Habiller(this); // fond du thème choisi
    }

    /// <summary>
    /// Traduit les touches en commandes pour l'écran affiché. On intercepte la touche avant les
    /// boutons (« Preview »), sinon Espace activerait le bouton qui a le focus.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.OriginalSource is TextBoxBase) return;
        // Ctrl, Alt et Windows annoncent un autre raccourci : on ne s'en mêle pas. Maj, elle, est permise :
        // sur un clavier AZERTY, les chiffres de la rangée du haut se tapent avec Maj.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;

        string? touche = e.Key switch
        {
            Key.Tab => "Tab",
            Key.Space => " ",
            Key.Enter => "Enter",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.S => "s",
            >= Key.A and <= Key.E => ((char)('a' + (e.Key - Key.A))).ToString(),
            _ => null,
        };
        if (touche is null) return;

        switch ((DataContext as MainViewModel)?.VueCourante)
        {
            case SessionViewModel session when touche == "Tab":
                // Tab entre dans la zone de réponse écrite quand il y en a une ; sinon Tab garde son rôle habituel.
                if (session.SaisieVisible)
                {
                    session.DemanderSaisie();
                    e.Handled = true;
                }
                break;
            case SessionViewModel session:
                session.Touche(touche);
                e.Handled = true;
                break;
            case LectureViewModel lecture when touche is "ArrowLeft" or "ArrowRight":
                lecture.ToucheCommand.Execute(touche);
                e.Handled = true;
                break;
        }
    }
}
