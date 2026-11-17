using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Revisions.App.ViewModels;

namespace Revisions.App.Views;

/// <summary>
/// Écran « TD en groupe ». Le code ici ne concerne que le focus clavier (première case, sortie des cases)
/// et Ctrl + Entrée : toute la logique est dans TdGroupeViewModel.
/// </summary>
public partial class VueTdGroupe : UserControl
{
    public VueTdGroupe()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TdGroupeViewModel ancien)
            {
                ancien.SaisieDemandee -= AllerALaPremiereCase;
                ancien.SaisieTerminee -= SortirDesCases;
            }
            if (e.NewValue is TdGroupeViewModel nouveau)
            {
                nouveau.SaisieDemandee += AllerALaPremiereCase;
                nouveau.SaisieTerminee += SortirDesCases;
            }
        };
    }

    /// <summary>Début d'un exercice : le curseur va dans la première case de saisie (s'il y en a une).</summary>
    private void AllerALaPremiereCase()
    {
        UpdateLayout(); // les cases viennent d'être créées : on attend qu'elles existent à l'écran
        if (Premiere<TextBox>(this, t => t.IsVisible && !t.IsReadOnly && t.Name != nameof(ZoneCode)) is { } zone) zone.Focus();
        else SortirDesCases();
    }

    /// <summary>
    /// Correction : le focus quitte les cases et revient à l'écran, pour qu'Espace et Entrée redeviennent
    /// des raccourcis (la fenêtre ne relaie pas les touches tapées dans une zone de texte).
    /// </summary>
    private void SortirDesCases()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Ctrl + Entrée : vérifier, même depuis une case (comme « voir la réponse » en session).
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && DataContext is TdGroupeViewModel td && td.EstRecherche)
        {
            td.Verifier();
            e.Handled = true;
        }
    }

    /// <summary>Entrée dans la zone du code : rejoindre.</summary>
    private void ZoneCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TdGroupeViewModel td)
        {
            td.Rejoindre();
            e.Handled = true;
        }
    }

    private static T? Premiere<T>(DependencyObject parent, Func<T, bool> condition) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var enfant = VisualTreeHelper.GetChild(parent, i);
            if (enfant is T trouve && condition(trouve)) return trouve;
            if (Premiere(enfant, condition) is { } plusBas) return plusBas;
        }
        return null;
    }
}
