using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Revisions.App.ViewModels;

namespace Revisions.App.Views;

/// <summary>
/// Panneau de la calculatrice. Seul code ici : le focus et les touches (Entrée, ↑ ↓, Échap),
/// qui dépendent des contrôles. Tout le reste est dans <see cref="CalculatriceViewModel"/>.
/// </summary>
public partial class VueCalculatrice : UserControl
{
    // Ce qui avait le focus avant l'ouverture : on le lui rend à la fermeture, pour reprendre l'exercice au clavier.
    private IInputElement? _precedent;

    public VueCalculatrice()
    {
        InitializeComponent();
        IsVisibleChanged += OnVisibiliteChangee;
    }

    private CalculatriceViewModel? Modele => DataContext as CalculatriceViewModel;

    /// <summary>Met le curseur dans la ligne de calcul.</summary>
    public void FocaliserSaisie()
    {
        SaisieBox.Focus();
        SaisieBox.CaretIndex = SaisieBox.Text.Length;
    }

    private void OnVisibiliteChangee(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _precedent = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(FocaliserSaisie, DispatcherPriority.Input);
        }
        else if (_precedent is UIElement precedent && precedent.IsVisible && IsKeyboardFocusWithin)
        {
            precedent.Focus();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Modele is not { } m) return;

        if (e.Key == Key.Escape)
        {
            // On rend le focus avant de fermer : sinon il se perdrait avec le panneau.
            if (_precedent is UIElement precedent && precedent.IsVisible) precedent.Focus();
            m.Fermer();
            e.Handled = true;
            return;
        }

        if (e.OriginalSource == SaisieBox)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    m.CalculerCommand.Execute(null);
                    SaisieBox.CaretIndex = SaisieBox.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Up:
                    m.NaviguerHistorique(+1);
                    SaisieBox.CaretIndex = SaisieBox.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Down:
                    m.NaviguerHistorique(-1);
                    SaisieBox.CaretIndex = SaisieBox.Text.Length;
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox { Tag: "cellule" })
        {
            m.EnregistrerCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Une touche d'aide : elle écrit son texte dans la ligne de calcul, là où est le curseur.</summary>
    private void Inserer(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string texte }) return;
        SaisieBox.SelectedText = texte;
        SaisieBox.CaretIndex = SaisieBox.SelectionStart + SaisieBox.SelectionLength;
        SaisieBox.SelectionLength = 0;
        SaisieBox.Focus();
    }

    /// <summary>Entrer dans une case sélectionne son contenu : on remplace la valeur en tapant directement.</summary>
    private void CaseFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox t) t.SelectAll();
    }
}
