using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Revisions.App.Services;

namespace Revisions.App.Views;

/// <summary>
/// Fenêtre qui affiche le PDF d'origine d'une séance, directement à la bonne page (US8).
/// Elle utilise la visionneuse PDF intégrée à WebView2 : rien à installer, et ça marche hors ligne.
/// </summary>
public sealed class FenetrePdf : Window
{
    private readonly WebView2 _web = new();

    /// <param name="dossierCours">Dossier « Cours » sur le disque.</param>
    /// <param name="fichierRelatif">Chemin du PDF, relatif à ce dossier.</param>
    /// <param name="page">Page à afficher (null = début du document).</param>
    /// <param name="sombre">Thème en cours.</param>
    public FenetrePdf(string dossierCours, string fichierRelatif, int? page, bool sombre)
    {
        Title = Path.GetFileName(fichierRelatif);
        Width = 1000;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _web;
        ServiceTheme.Habiller(this);
        Loaded += async (_, _) => await OuvrirAsync(dossierCours, fichierRelatif, page, sombre);
        Closed += (_, _) => _web.Dispose();
    }

    private async Task OuvrirAsync(string dossierCours, string fichierRelatif, int? page, bool sombre)
    {
        try
        {
            await _web.EnsureCoreWebView2Async(await ServiceWeb.Environnement);
            var web = _web.CoreWebView2;
            ServiceWeb.Securiser(web);
            ServiceWeb.AppliquerTheme(web, sombre);
            // Le dossier « Cours » devient lisible à l'adresse https://cours.atome.example/…
            web.SetVirtualHostNameToFolderMapping(ServiceWeb.HoteCours, dossierCours, CoreWebView2HostResourceAccessKind.Deny);

            // Chaque morceau du chemin est encodé (espaces, accents, apostrophes…).
            string chemin = string.Join("/", fichierRelatif.Split('\\', '/').Select(Uri.EscapeDataString));
            string ancre = page is > 0 ? $"#page={page}" : "";
            web.Navigate($"https://{ServiceWeb.HoteCours}/{chemin}{ancre}");
        }
        catch (Exception exception)
        {
            // Solution de repli : le lecteur PDF habituel de Windows (sans garantie sur la page).
            Journal.Erreur("PDF", exception);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(dossierCours, fichierRelatif)) { UseShellExecute = true });
            Close();
        }
    }
}
