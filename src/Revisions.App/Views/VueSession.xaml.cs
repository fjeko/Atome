using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Revisions.App.Views;

/// <summary>
/// Écran de session. Le seul code ici concerne le focus clavier de la zone de saisie,
/// ce qui est une affaire d'affichage : la logique reste dans SessionViewModel.
/// </summary>
public partial class VueSession : UserControl
{
    public VueSession()
    {
        InitializeComponent();

        // L'écran lui-même peut recevoir le focus : c'est là qu'on « sort » de la zone de saisie,
        // pour que Espace et 1-4 redeviennent des raccourcis.
        Focusable = true;
        FocusVisualStyle = null;
        Loaded += (_, _) => Focus();

        // Quand la zone disparaît (réponse révélée), le focus ne doit pas rester dans le vide.
        ZoneSaisie.IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) Dispatcher.BeginInvoke(() => Focus());
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        if (e.Key == Key.Escape && ZoneSaisie.IsKeyboardFocused)
        {
            // Échap : on quitte la zone (le texte déjà écrit est gardé).
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && ZoneSaisie.IsVisible && !ZoneSaisie.IsKeyboardFocused)
        {
            // Tab : on entre dans la zone pour écrire.
            ZoneSaisie.Focus();
            e.Handled = true;
        }
    }
}
