using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Revisions.App.ViewModels;

namespace Revisions.App.Views;

/// <summary>Grille de saisie d'une matrice : seul le clavier (Entrée, Échap, focus de la première case) est géré ici.</summary>
public partial class VueSaisieMatrice : UserControl
{
    public VueSaisieMatrice()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SaisieMatrice ancienne) ancienne.FocusDemande -= FocaliserPremiereCase;
            if (e.NewValue is SaisieMatrice nouvelle) nouvelle.FocusDemande += FocaliserPremiereCase;
        };
    }

    private void FocaliserPremiereCase()
    {
        if (Premiere(Grille) is { } caseTexte) { caseTexte.Focus(); caseTexte.SelectAll(); }
    }

    private static TextBox? Premiere(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var enfant = VisualTreeHelper.GetChild(parent, i);
            if (enfant is TextBox t) return t;
            if (Premiere(enfant) is { } trouve) return trouve;
        }
        return null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Enter && DataContext is SaisieMatrice saisie)
        {
            saisie.Valider?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // On rend le focus à l'écran : les raccourcis (Espace, Échap…) remarchent.
            if (Window.GetWindow(this) is { } fenetre) { Keyboard.ClearFocus(); fenetre.Focus(); }
            e.Handled = true;
        }
    }
}
