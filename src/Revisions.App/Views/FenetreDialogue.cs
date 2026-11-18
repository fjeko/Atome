using System.Windows;
using System.Windows.Controls;
using Revisions.App.Controles;
using Revisions.App.Services;
using Revisions.Core.Mascotte;

namespace Revisions.App.Views;

/// <summary>Un bouton d'un dialogue : son texte, la valeur renvoyée, et son rôle (principal, annulation).</summary>
public sealed record ChoixDialogue(string Libelle, string Valeur, bool Principal = false, bool Annulation = false);

/// <summary>
/// Dialogue aux couleurs d'Atome (système de design) : remplace les boîtes de message de Windows.
/// La mascotte (facultative), un titre, un message, puis les boutons à droite : un seul bouton principal,
/// Entrée le choisit, Échap choisit l'annulation.
/// </summary>
public class FenetreDialogue : Window
{
    /// <summary>Valeur du bouton choisi, ou null si la fenêtre a été fermée autrement.</summary>
    public string? Reponse { get; private set; }

    public FenetreDialogue(string titre, string message, IReadOnlyList<ChoixDialogue> choix, Humeur? humeur = null)
    {
        Title = titre;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        if (Application.Current?.MainWindow is { IsLoaded: true } principale && principale != this) Owner = principale;
        ServiceTheme.Habiller(this);

        var pied = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pied.SetResourceReference(MarginProperty, "Marge.Haut.24");
        foreach (var c in choix)
        {
            var bouton = new Button { Content = c.Libelle, IsDefault = c.Principal, IsCancel = c.Annulation };
            bouton.SetResourceReference(StyleProperty, c.Principal ? "BoutonPrincipal" : "BoutonSecondaire");
            bouton.SetResourceReference(MarginProperty, "Marge.Gauche.8");
            bouton.Click += (_, _) =>
            {
                Reponse = c.Valeur;
                DialogResult = true;
            };
            pied.Children.Add(bouton);
        }

        var texte = new StackPanel();
        var titreBloc = new TextBlock { Text = titre };
        titreBloc.SetResourceReference(StyleProperty, "Texte.SousTitre");
        var messageBloc = new TextBlock { Text = message };
        messageBloc.SetResourceReference(StyleProperty, "Texte.Secondaire");
        messageBloc.SetResourceReference(MarginProperty, "Marge.Haut.8");
        texte.Children.Add(titreBloc);
        texte.Children.Add(messageBloc);

        var corps = new DockPanel();
        if (humeur is { } h)
        {
            var mascotte = new Mascotte { Humeur = h, Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
            mascotte.SetResourceReference(MarginProperty, "Marge.Droite.16");
            DockPanel.SetDock(mascotte, Dock.Left);
            corps.Children.Add(mascotte);
        }
        DockPanel.SetDock(pied, Dock.Bottom);
        corps.Children.Add(pied);
        corps.Children.Add(texte);

        var cadre = new Border { Child = corps };
        cadre.SetResourceReference(Border.PaddingProperty, "Remplissage.Carte");
        cadre.SetResourceReference(Border.BackgroundProperty, "Pinceau.Surelevee");
        Content = cadre;
    }

}

/// <summary>Raccourcis pour afficher un dialogue d'Atome depuis un ViewModel.</summary>
public static class ServiceDialogue
{
    /// <summary>Pose une question ; renvoie la valeur du bouton choisi (null si la fenêtre est fermée).</summary>
    public static string? Demander(string titre, string message, params ChoixDialogue[] choix)
    {
        var fenetre = new FenetreDialogue(titre, message, choix);
        return fenetre.ShowDialog() == true ? fenetre.Reponse : null;
    }

    /// <summary>Un simple message, avec « OK ».</summary>
    public static void Informer(string titre, string message) =>
        Demander(titre, message, new ChoixDialogue("OK", "ok", Principal: true, Annulation: true));

    /// <summary>Oui ou non : vrai seulement si l'étudiant choisit le bouton principal.</summary>
    public static bool Confirmer(string titre, string message, string oui = "Oui", string non = "Non") =>
        Demander(titre, message, new ChoixDialogue(non, "non", Annulation: true), new ChoixDialogue(oui, "oui", Principal: true)) == "oui";
}
