using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Revisions.App.ViewModels;

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

        // Quand le ViewModel le demande (touche Tab), le curseur va dans la zone de saisie.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SessionViewModel ancien) ancien.SaisieDemandee -= EntrerDansLaZone;
            if (e.NewValue is SessionViewModel nouveau) nouveau.SaisieDemandee += EntrerDansLaZone;
        };

        // Quand la zone disparaît (réponse révélée), le focus revient à la fenêtre :
        // Espace et 1-4 redeviennent des raccourcis.
        ZoneSaisie.IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) Dispatcher.BeginInvoke(SortirDeLaZone);
        };
    }

    private void EntrerDansLaZone()
    {
        ZoneSaisie.Focus();
        ZoneSaisie.CaretIndex = ZoneSaisie.Text.Length; // on continue d'écrire à la suite
    }

    /// <summary>
    /// Sort de la zone de saisie en donnant le focus à l'écran lui-même. Il ne suffit pas de le
    /// rendre à la fenêtre : WPF se souvient du dernier contrôle utilisé et y remettrait le curseur.
    /// </summary>
    private void SortirDeLaZone()
    {
        Focusable = true;
        FocusVisualStyle = null; // pas de cadre en pointillés autour de l'écran
        Focus();
    }

    /// <summary>Entrée (ou Ctrl+Entrée) révèle la réponse, Espace aussi tant que la zone est vide ; Maj+Entrée saute une ligne.</summary>
    private void ZoneSaisie_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel session) return;
        var mod = Keyboard.Modifiers;
        string? touche = e.Key switch { Key.Enter => "Enter", Key.Space => " ", _ => null };
        if (touche is not null && session.ToucheDansSaisie(touche, maj: (mod & ModifierKeys.Shift) != 0, ctrl: (mod & ModifierKeys.Control) != 0))
            e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Échap dans la zone : on la quitte (le texte déjà écrit est gardé).
        if (e.Key == Key.Escape && ZoneSaisie.IsKeyboardFocused && Keyboard.Modifiers == ModifierKeys.None)
        {
            SortirDeLaZone();
            e.Handled = true;
        }
    }
}
