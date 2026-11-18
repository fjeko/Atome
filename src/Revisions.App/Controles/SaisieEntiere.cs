using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Revisions.App.ViewModels;
using Revisions.Core.TdGroupe;

namespace Revisions.App.Controles;

/// <summary>
/// Comportement des cases de saisie du TD en groupe (propriété attachée, à poser sur une TextBox) :
/// <list type="bullet">
/// <item>seuls les chiffres, les signes « - » et « + », la virgule et le point passent ; le texte n'est jamais reformaté
/// pendant la frappe (« - », « -0, » restent tels quels) : il n'est lu qu'à la vérification ;</item>
/// <item>← et → passent à la case voisine quand le curseur est au bord du texte ; ↑ et ↓ changent de ligne ;</item>
/// <item>Entrée passe à la case suivante, comme Tab ;</item>
/// <item>en arrivant dans une case, son contenu est sélectionné : on tape directement par-dessus.</item>
/// </list>
/// C'est de l'affichage pur : la vérification des valeurs reste dans le Core (VerificationSaisie).
/// </summary>
public static class SaisieEntiere
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(SaisieEntiere), new PropertyMetadata(false, QuandActiveChange));

    public static bool GetActive(DependencyObject d) => (bool)d.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject d, bool valeur) => d.SetValue(ActiveProperty, valeur);

    private static void QuandActiveChange(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox zone || e.NewValue is not true) return;
        zone.PreviewTextInput += (_, args) =>
        {
            if (!args.Text.All(VerificationSaisie.CaractereAutorise)) args.Handled = true;
        };
        // Coller du texte : seulement des chiffres, signes et séparateurs (la conversion attend la vérification).
        DataObject.AddPastingHandler(zone, (_, args) =>
        {
            if (args.DataObject.GetData(DataFormats.UnicodeText) is not string texte || !VerificationSaisie.SaisieAutorisee(texte))
                args.CancelCommand();
        });
        zone.GotKeyboardFocus += (_, _) => zone.SelectAll();
        zone.PreviewKeyDown += ToucheDansLaCase;
    }

    private static void ToucheDansLaCase(object sender, KeyEventArgs e)
    {
        var zone = (TextBox)sender;
        if (Keyboard.Modifiers != ModifierKeys.None) return; // Ctrl + Entrée : géré par l'écran (vérifier)
        var direction = e.Key switch
        {
            Key.Enter => FocusNavigationDirection.Next,
            Key.Left when zone.CaretIndex == 0 && zone.SelectionLength == 0 => FocusNavigationDirection.Previous,
            Key.Right when zone.CaretIndex == zone.Text.Length && zone.SelectionLength == 0 => FocusNavigationDirection.Next,
            _ => (FocusNavigationDirection?)null,
        };
        if (direction is { } d)
        {
            zone.MoveFocus(new TraversalRequest(d));
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Up or Key.Down && zone.DataContext is CaseTdViewModel cellule)
        {
            int cible = cellule.Index + (e.Key == Key.Down ? cellule.Colonnes : -cellule.Colonnes);
            if (ItemsControl.ItemsControlFromItemContainer(ConteneurDe(zone)) is { } grille
                && cible >= 0 && cible < grille.Items.Count
                && grille.ItemContainerGenerator.ContainerFromIndex(cible) is DependencyObject conteneur
                && Descendant<TextBox>(conteneur) is { } voisine)
                voisine.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Remonte jusqu'au conteneur d'élément (ContentPresenter) de la case dans la grille.</summary>
    private static DependencyObject ConteneurDe(DependencyObject element)
    {
        var courant = element;
        while (VisualTreeHelper.GetParent(courant) is { } parent)
        {
            if (parent is ItemsPresenter or Panel && courant is ContentPresenter) return courant;
            courant = parent;
        }
        return element;
    }

    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var enfant = VisualTreeHelper.GetChild(parent, i);
            if (enfant is T trouve) return trouve;
            if (Descendant<T>(enfant) is { } plusBas) return plusBas;
        }
        return null;
    }
}
