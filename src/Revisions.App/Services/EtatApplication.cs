using System.IO;
using Revisions.Core.Apparence;
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

    /// <summary>Couleurs en cours, calculées à partir du thème et de la couleur d'accent choisis.</summary>
    public Palette Palette { get; private set; } =
        Themes.Resoudre(Themes.Trouver(null), null, systemeSombre: false, Couleur.Parse("#0067C0"));

    /// <summary>Vrai si l'application est affichée en sombre.</summary>
    public bool EstSombre => Palette.Sombre;

    /// <summary>
    /// Dossier des cours libres livrés avec Atome (« contenu-libre\packs » à côté de l'exécutable), ou null s'il n'existe pas.
    /// Lu en lecture seule : on ne copie rien, les packs sont importés d'où ils sont (une mise à jour d'Atome les remplace).
    /// En développement, on remonte les dossiers parents jusqu'à « app\contenu-libre ».
    /// </summary>
    public string? DossierCoursLibres { get; private set; }

    /// <summary>Anglais général (A1 → C1) livré avec Atome : « contenu-libre/anglais », chargé à chaque démarrage (collection « Anglais »).</summary>
    public string? DossierAnglais { get; private set; }

    private static string? TrouverCoursLibres()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            string candidat = Path.Combine(dossier.FullName, "contenu-libre", "packs");
            if (Directory.Exists(candidat)) return candidat;
        }
        return null;
    }

    public void Initialiser()
    {
        DossierCoursLibres = TrouverCoursLibres();
        DossierAnglais = DossierCoursLibres is { } libres && Path.GetDirectoryName(libres) is { } racine && Directory.Exists(Path.Combine(racine, "anglais"))
            ? Path.Combine(racine, "anglais") : null;
        Reglages = profil.ChargerReglages();
        DetecterDossiers();
        AppliquerTheme();
    }

    /// <summary>Enregistre les réglages sans réappliquer le thème (choix sans effet sur les couleurs, comme la barre repliée).</summary>
    public void EnregistrerSansAppliquer() => profil.EnregistrerReglages(Reglages);

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
        var theme = Themes.Trouver(Reglages.Theme);
        Couleur? accentPerso = Couleur.TryParse(Reglages.CouleurAccent, out var accent) ? accent : null;
        Palette = Themes.Resoudre(theme, accentPerso, SystemeEnSombre(), ServiceTheme.AccentWindows());
        ServiceTheme.Appliquer(Palette, theme.Sombre, Reglages.TailleTexte / 100.0);
        Mouvement.ReductionDemandee = Reglages.ReduireAnimations;
        ServiceSons.Actifs = Reglages.Sons;
        ThemeChange?.Invoke();
    }

    /// <summary>Lit dans le registre si Windows est réglé sur le mode sombre pour les applications.</summary>
    private static bool SystemeEnSombre()
    {
        using var cle = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return cle?.GetValue("AppsUseLightTheme") is int valeur && valeur == 0;
    }
}
