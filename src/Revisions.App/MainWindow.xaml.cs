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
        Services.ServiceTheme.Habiller(this); // fond, police et couleurs du thème choisi
        AdapterALEcran();
        // Sous 1100 px de large, la barre latérale se replie d'elle-même en icônes seules.
        SizeChanged += (_, e) =>
        {
            if (DataContext is MainViewModel principal) principal.FenetreEtroite = e.NewSize.Width < 1100;
        };
    }

    /// <summary>
    /// Petit écran (portable 14 pouces avec mise à l'échelle) : la taille par défaut dépasserait de l'écran
    /// et cacherait la barre de titre. On réduit alors la fenêtre à la zone utile et on l'ouvre agrandie.
    /// </summary>
    private void AdapterALEcran()
    {
        Rect zone = SystemParameters.WorkArea; // écran principal, sans la barre des tâches
        if (zone.Width >= Width + 40 && zone.Height >= Height + 40) return;
        Width = Math.Max(MinWidth, Math.Min(Width, zone.Width - 40));
        Height = Math.Max(MinHeight, Math.Min(Height, zone.Height - 40));
        WindowState = WindowState.Maximized;
    }

    /// <summary>Nouvel écran : il apparaît en fondu, avec un léger glissement (ou un fondu très court, animations réduites).</summary>
    private void EcranChange(object? sender, System.Windows.Data.DataTransferEventArgs e) => Services.Mouvement.Apparaitre(Ecran);

    // ---- Plein écran (F11) : pratique en session, pour se concentrer ----
    private WindowState _etatAvantPleinEcran;
    private bool _pleinEcran;

    private void BasculerPleinEcran()
    {
        _pleinEcran = !_pleinEcran;
        if (_pleinEcran)
        {
            _etatAvantPleinEcran = WindowState;
            WindowState = WindowState.Normal; // passer par « Normal » : sinon Windows garde la barre des tâches
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _etatAvantPleinEcran;
        }
    }

    /// <summary>
    /// Traduit les touches en commandes pour l'écran affiché. On intercepte la touche avant les
    /// boutons (« Preview »), sinon Espace activerait le bouton qui a le focus.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _pleinEcran && e.OriginalSource is not TextBoxBase))
        {
            BasculerPleinEcran();
            e.Handled = true;
            return;
        }
        // Ctrl + Z en session : annule la dernière note (hors zone de texte, où Ctrl + Z annule la frappe).
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && e.OriginalSource is not TextBoxBase
            && DataContext is MainViewModel { VueCourante: SessionViewModel enSession })
        {
            enSession.Annuler();
            e.Handled = true;
            return;
        }
        // Ctrl + K : ouvre la calculatrice (exercices de maths), la ferme si on est dedans, ou y ramène le curseur.
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control && DataContext is MainViewModel principal)
        {
            var calculatrice = principal.Calculatrice;
            e.Handled = true;
            if (!calculatrice.Visible) return;
            if (calculatrice.EstOuverte && PanneauCalculatrice.IsKeyboardFocusWithin) calculatrice.Fermer();
            else if (calculatrice.EstOuverte) PanneauCalculatrice.FocaliserSaisie();
            else calculatrice.EstOuverte = true; // le panneau prend lui-même le focus en s'affichant
            return;
        }
        // Dans la calculatrice, les touches sont pour elle (Entrée, Échap, Espace…) et pas pour la session.
        if (PanneauCalculatrice.IsKeyboardFocusWithin) return;
        if (e.OriginalSource is TextBoxBase) return;
        // Ctrl, Alt et Windows annoncent un autre raccourci : on ne s'en mêle pas. Maj, elle, est permise :
        // sur un clavier AZERTY, les chiffres de la rangée du haut se tapent avec Maj.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;

        string? touche = e.Key switch
        {
            Key.Tab => "Tab",
            Key.Space => " ",
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.S => "s",
            Key.I => "i",
            Key.R => "r",
            >= Key.A and <= Key.E => ((char)('a' + (e.Key - Key.A))).ToString(),
            _ => null,
        };
        if (touche is null) return;

        switch ((DataContext as MainViewModel)?.VueCourante)
        {
            case SessionViewModel session when touche == "Tab":
                // Tab entre dans la zone de réponse écrite quand il y en a une ; sinon Tab garde son rôle habituel.
                if (session.SaisieVisible || session.MatriceVisible)
                {
                    session.DemanderSaisie();
                    e.Handled = true;
                }
                break;
            case SessionViewModel session:
                session.Touche(touche);
                e.Handled = true;
                break;
            case ParcoursViewModel parcours when touche == "Tab":
                if (parcours.SaisieVisible || parcours.MatriceVisible)
                {
                    parcours.DemanderSaisie();
                    e.Handled = true;
                }
                break;
            case ParcoursViewModel parcours:
                // Comme en session ; sur l'écran de résultats, Entrée et Espace gardent leur rôle sur le bouton qui a le focus.
                if (!parcours.ResultatsVisibles || touche == "Escape") { parcours.Touche(touche); e.Handled = true; }
                break;
            case TdGroupeViewModel td when touche != "Tab":
                // Hors des cases de saisie : Espace, Entrée et I (indice). Les autres touches gardent leur rôle.
                e.Handled = td.Touche(touche);
                break;
            case PositionnementViewModel positionnement when touche is "Escape" || (positionnement.EstQuestion && touche is "a" or "b" or "c" or "d" or "e" or " "):
                // Pendant les questions : A à E et Espace (réécouter). Ailleurs, les touches gardent leur rôle sur les boutons.
                positionnement.Touche(touche);
                e.Handled = true;
                break;
            case LectureViewModel lecture when touche is "ArrowLeft" or "ArrowRight":
                lecture.ToucheCommand.Execute(touche);
                e.Handled = true;
                break;
        }
    }
}
