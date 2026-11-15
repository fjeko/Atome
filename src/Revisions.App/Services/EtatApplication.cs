using System.IO;
using System.Windows;
using Revisions.Core.Modeles;
using Revisions.Core.Temps;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>
/// Ce qui est partagé par tous les écrans : l'horloge, les réglages et le thème.
/// </summary>
public sealed class EtatApplication(TimeProvider horloge, DepotProfil profil)
{
    /// <summary>Réglages en cours (rechargés au démarrage, enregistrés par l'écran Réglages).</summary>
    public Reglages Reglages { get; private set; } = new();

    /// <summary>Jour de révision actuel (il commence à 4 h du matin).</summary>
    public DateOnly Jour => JourDeRevision.Aujourdhui(horloge);

    public DateTimeOffset Maintenant => horloge.GetUtcNow();

    /// <summary>Prévenu quand le thème change, pour que les zones de contenu web suivent.</summary>
    public event Action? ThemeChange;

    /// <summary>Vrai si l'application est affichée en sombre.</summary>
    public bool EstSombre => Reglages.Theme switch
    {
        "sombre" => true,
        "clair" => false,
        _ => SystemeEnSombre(),
    };

    public void Initialiser()
    {
        Reglages = profil.ChargerReglages();
        DetecterDossiers();
        AppliquerTheme();
    }

    public void Enregistrer()
    {
        profil.EnregistrerReglages(Reglages);
        AppliquerTheme();
    }

    /// <summary>
    /// Au premier lancement, cherche le dossier « packs » : on remonte depuis l'exécutable jusqu'à
    /// trouver packs/FORMAT.md, sinon on prend Documents\Cours\packs.
    /// Le dossier « Cours » (pour ouvrir les PDF) est le parent du dossier des packs.
    /// </summary>
    private void DetecterDossiers()
    {
        if (string.IsNullOrEmpty(Reglages.DossierPacks) || !Directory.Exists(Reglages.DossierPacks))
        {
            string? trouve = null;
            for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
            {
                if (File.Exists(Path.Combine(dossier.FullName, "packs", "FORMAT.md")))
                {
                    trouve = Path.Combine(dossier.FullName, "packs");
                    break;
                }
            }
            trouve ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Cours", "packs");
            if (Directory.Exists(trouve))
            {
                Reglages.DossierPacks = trouve;
                profil.EcrireValeur("dossier_packs", trouve);
            }
        }

        if (string.IsNullOrEmpty(Reglages.DossierCours) && !string.IsNullOrEmpty(Reglages.DossierPacks))
        {
            Reglages.DossierCours = Path.GetDirectoryName(Reglages.DossierPacks.TrimEnd('\\', '/'));
            profil.EcrireValeur("dossier_cours", Reglages.DossierCours);
        }
    }

    private void AppliquerTheme()
    {
        Application.Current.ThemeMode = Reglages.Theme switch
        {
            "sombre" => ThemeMode.Dark,
            "clair" => ThemeMode.Light,
            _ => ThemeMode.System,
        };
        ThemeChange?.Invoke();
    }

    /// <summary>Lit dans le registre si Windows est réglé sur le mode sombre pour les applications.</summary>
    private static bool SystemeEnSombre()
    {
        using var cle = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return cle?.GetValue("AppsUseLightTheme") is int valeur && valeur == 0;
    }
}
