using System.Windows.Controls;
using System.Windows.Input;
using Revisions.App.ViewModels;

namespace Revisions.App.Views;

/// <summary>
/// Écran « Lire puis contrôler ». Comme pour la session, seul le focus de la zone de saisie est géré ici
/// (affaire d'affichage) ; la logique est dans ParcoursViewModel.
/// </summary>
public partial class VueParcours : UserControl
{
    public VueParcours()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ParcoursViewModel ancien) ancien.SaisieDemandee -= EntrerDansLaZone;
            if (e.NewValue is ParcoursViewModel nouveau) nouveau.SaisieDemandee += EntrerDansLaZone;
        };
        // La zone disparaît (réponse révélée) : le focus revient à l'écran, les raccourcis remarchent.
        ZoneSaisie.IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) Dispatcher.BeginInvoke(SortirDeLaZone); };
    }

    private void EntrerDansLaZone()
    {
        ZoneSaisie.Focus();
        ZoneSaisie.CaretIndex = ZoneSaisie.Text.Length;
    }

    private void SortirDeLaZone()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Focus();
    }

    private void ZoneSaisie_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ParcoursViewModel parcours) return;
        if (e.Key == Key.Escape) { SortirDeLaZone(); e.Handled = true; return; }
        var mod = Keyboard.Modifiers;
        string? touche = e.Key switch { Key.Enter => "Enter", Key.Space => " ", _ => null };
        if (touche is not null && parcours.ToucheDansSaisie(touche, maj: (mod & ModifierKeys.Shift) != 0, ctrl: (mod & ModifierKeys.Control) != 0))
            e.Handled = true;
    }
}
