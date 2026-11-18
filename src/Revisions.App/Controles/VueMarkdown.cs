using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Wpf;
using Revisions.App.Services;

namespace Revisions.App.Controles;

/// <summary>
/// Zone qui affiche du Markdown + LaTeX grâce à WebView2 et à la page locale Web/rendu.html.
/// Le ViewModel fournit un message JSON (propriété <see cref="Message"/>) ; la page le rend sans se recharger.
/// Les clics sur un choix de QCM et les touches utiles reviennent par des commandes.
/// </summary>
public sealed class VueMarkdown : ContentControl
{
    private readonly WebView2 _web = new();
    private bool _pagePrete;

    /// <summary>Message JSON à afficher : { "elements": [ { "genre", "markdown", … } ] }.</summary>
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(VueMarkdown),
        new PropertyMetadata(null, (d, _) => ((VueMarkdown)d).Envoyer()));

    /// <summary>Commande appelée avec l'index (int) du choix de QCM cliqué.</summary>
    public static readonly DependencyProperty CommandeChoixProperty = DependencyProperty.Register(
        nameof(CommandeChoix), typeof(ICommand), typeof(VueMarkdown));

    /// <summary>Commande appelée avec la touche (string) pressée dans le contenu.</summary>
    public static readonly DependencyProperty CommandeToucheProperty = DependencyProperty.Register(
        nameof(CommandeTouche), typeof(ICommand), typeof(VueMarkdown));

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? CommandeChoix
    {
        get => (ICommand?)GetValue(CommandeChoixProperty);
        set => SetValue(CommandeChoixProperty, value);
    }

    public ICommand? CommandeTouche
    {
        get => (ICommand?)GetValue(CommandeToucheProperty);
        set => SetValue(CommandeToucheProperty, value);
    }

    public VueMarkdown()
    {
        Content = _web;
        Focusable = false;
        Loaded += async (_, _) => await InitialiserAsync();
        Unloaded += (_, _) =>
        {
            Etat.ThemeChange -= AppliquerTheme;
            // Si la zone a vraiment quitté l'écran (changement d'écran, fenêtre fermée), on libère
            // le navigateur intégré. On vérifie un peu plus tard, car un changement de thème
            // déclenche aussi « Unloaded » puis « Loaded » sans que la zone disparaisse.
            Dispatcher.BeginInvoke(() =>
            {
                if (PresentationSource.FromVisual(this) is null) _web.Dispose();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private static EtatApplication Etat => App.Services.GetRequiredService<EtatApplication>();

    private async Task InitialiserAsync()
    {
        Etat.ThemeChange -= AppliquerTheme;
        Etat.ThemeChange += AppliquerTheme;
        if (_web.CoreWebView2 is not null) { AppliquerTheme(); return; }

        try
        {
            // Couleur de fond avant le premier affichage : évite un éclair blanc en thème sombre.
            var feuille = Etat.Palette.Feuille;
            _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(feuille.R, feuille.G, feuille.B);
            await _web.EnsureCoreWebView2Async(await ServiceWeb.Environnement);
            var web = _web.CoreWebView2!;
            ServiceWeb.Securiser(web);
            ServiceWeb.AppliquerTheme(web, Etat.EstSombre);
            web.SetVirtualHostNameToFolderMapping(ServiceWeb.HoteApplication, ServiceWeb.DossierWeb,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Deny);

            // La page ne doit jamais naviguer ailleurs que sur elle-même.
            web.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith($"https://{ServiceWeb.HoteApplication}/", StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            web.WebMessageReceived += (_, e) => RecevoirMessage(e.WebMessageAsJson);
            web.Navigate($"https://{ServiceWeb.HoteApplication}/rendu.html");
        }
        catch (Exception exception)
        {
            Journal.Erreur("WebView2", exception);
            Content = new TextBlock
            {
                Text = "Impossible d'afficher le contenu : le composant WebView2 de Windows n'a pas démarré.\n" + exception.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
            };
        }
    }

    /// <summary>Transmet le thème à la page : mode clair/sombre, puis les couleurs exactes de la palette.</summary>
    private void AppliquerTheme()
    {
        if (_web.CoreWebView2 is not { } web) return;
        var palette = Etat.Palette;
        ServiceWeb.AppliquerTheme(web, palette.Sombre);
        if (!_pagePrete) return;

        // Les noms sont ceux des variables CSS de Web/app.css : la charte du système de design (Core/Apparence/Charte).
        var c = ServiceTheme.CharteCourante ?? Revisions.Core.Apparence.Charte.Calculer(palette);
        var variables = new Dictionary<string, string>
        {
            ["--fond"] = palette.Feuille.Hex,
            ["--texte"] = c.Texte.Hex,
            ["--discret"] = c.TexteSecondaire.Hex,
            ["--bord"] = c.Bordure.Hex,
            ["--fond-doux"] = c.Surface.Melanger(c.Texte, c.Sombre ? 0.06 : 0.035).Hex,
            ["--accent"] = c.AccentTexte.Hex,
            ["--accent-trait"] = c.AccentTrait.Hex,
            ["--accent-pale"] = c.AccentDoux.Pale.Hex,
            ["--bon"] = c.Succes.Texte.Hex,
            ["--bon-trait"] = c.Succes.Trait.Hex,
            ["--bon-fond"] = c.Succes.Pale.Hex,
            ["--mauvais"] = c.Erreur.Texte.Hex,
            ["--mauvais-trait"] = c.Erreur.Trait.Hex,
            ["--mauvais-fond"] = c.Erreur.Pale.Hex,
            // Taille du texte choisie dans les Réglages (90 à 150 %).
            ["--taille"] = $"{16 * Etat.Reglages.TailleTexte / 100.0:0.#}px".Replace(',', '.'),
            ["--animations"] = Mouvement.Reduit ? "0" : "1",
        };
        web.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "theme", variables }));
    }

    /// <summary>Envoie le message courant à la page (dès qu'elle est prête).</summary>
    private void Envoyer()
    {
        if (_pagePrete && Message is { } message)
            _web.CoreWebView2.PostWebMessageAsJson(message);
    }

    /// <summary>Messages venant de la page : « pret », « choix » (QCM), « touche », « lien ».</summary>
    private void RecevoirMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var racine = document.RootElement;
        switch (racine.GetProperty("type").GetString())
        {
            case "pret":
                _pagePrete = true;
                AppliquerTheme();
                Envoyer();
                break;
            case "choix":
                int index = racine.GetProperty("index").GetInt32();
                if (CommandeChoix?.CanExecute(index) == true) CommandeChoix.Execute(index);
                break;
            case "touche":
                string touche = racine.GetProperty("touche").GetString() ?? "";
                if (CommandeTouche?.CanExecute(touche) == true) CommandeTouche.Execute(touche);
                break;
            // « lien » : ignoré volontairement, l'application fonctionne hors ligne.
        }
    }
}
