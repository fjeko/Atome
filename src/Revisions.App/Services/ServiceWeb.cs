using System.IO;
using Microsoft.Web.WebView2.Core;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>
/// Environnement WebView2 partagé par toutes les zones de contenu web (cours, cartes, PDF).
/// Le créer une seule fois évite de relancer un navigateur par écran.
/// </summary>
public static class ServiceWeb
{
    // Noms de domaine fictifs : en « .example » (réservé, jamais résolu sur le réseau), et surtout pas en « .local » :
    // Windows tente alors une résolution mDNS sur le réseau local, et chaque page attendait ~2 s avant de charger
    // ses scripts (mesuré : HTML reçu à 6 ms, markdown-it et KaTeX demandés à 2 010 ms).

    /// <summary>Nom de domaine fictif qui pointe vers le dossier Web local.</summary>
    public const string HoteApplication = "app.atome.example";

    /// <summary>Nom de domaine fictif qui pointe vers le dossier « Cours » (pour les PDF).</summary>
    public const string HoteCours = "cours.atome.example";

    private static Task<CoreWebView2Environment>? _environnement;

    /// <summary>Dossier des pages locales, à côté de l'exécutable.</summary>
    public static string DossierWeb => Path.Combine(AppContext.BaseDirectory, "Web");

    public static Task<CoreWebView2Environment> Environnement =>
        _environnement ??= CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(BaseDeDonnees.DossierDonnees, "WebView2"));

    /// <summary>Zone web invisible gardée en vie : le navigateur intégré reste démarré entre deux écrans.</summary>
    private static CoreWebView2Controller? _veilleuse;

    /// <summary>
    /// Préchauffage, une fois l'accueil affiché : crée l'environnement et une zone web invisible qui charge la page
    /// de rendu. Le premier cours ou la première carte s'ouvrent ensuite sans attendre le démarrage du navigateur
    /// intégré, et les scripts (markdown-it, KaTeX, highlight.js) sont déjà en cache. Sans effet en cas d'échec.
    /// </summary>
    public static async Task PrechaufferAsync(IntPtr fenetre)
    {
        if (_veilleuse is not null || fenetre == IntPtr.Zero) return;
        try
        {
            var environnement = await Environnement;
            _veilleuse = await environnement.CreateCoreWebView2ControllerAsync(fenetre);
            _veilleuse.IsVisible = false;
            Securiser(_veilleuse.CoreWebView2);
            _veilleuse.CoreWebView2.SetVirtualHostNameToFolderMapping(HoteApplication, DossierWeb, CoreWebView2HostResourceAccessKind.Deny);
            _veilleuse.CoreWebView2.Navigate($"https://{HoteApplication}/rendu.html");
            Journal.Etape("Navigateur intégré préchauffé");
        }
        catch (Exception exception)
        {
            Journal.Erreur("Préchauffage WebView2", exception);
        }
    }

    /// <summary>Réglages communs : pas de menu contextuel, pas d'outils de développement, pas de nouvelle fenêtre.</summary>
    public static void Securiser(CoreWebView2 web)
    {
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        web.Settings.AreDevToolsEnabled = false;
#endif
        web.NewWindowRequested += (_, e) => e.Handled = true;
    }

    public static void AppliquerTheme(CoreWebView2 web, bool sombre) =>
        web.Profile.PreferredColorScheme = sombre ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
}
