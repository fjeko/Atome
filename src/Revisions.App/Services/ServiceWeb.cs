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
    /// <summary>Nom de domaine fictif qui pointe vers le dossier Web local.</summary>
    public const string HoteApplication = "app.local";

    /// <summary>Nom de domaine fictif qui pointe vers le dossier « Cours » (pour les PDF).</summary>
    public const string HoteCours = "cours.local";

    private static Task<CoreWebView2Environment>? _environnement;

    /// <summary>Dossier des pages locales, à côté de l'exécutable.</summary>
    public static string DossierWeb => Path.Combine(AppContext.BaseDirectory, "Web");

    public static Task<CoreWebView2Environment> Environnement =>
        _environnement ??= CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(BaseDeDonnees.DossierDonnees, "WebView2"));

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
