using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Revisions.Core.MisesAJour;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>Version et mode d'installation de cette copie d'Atome (calculés une fois au lancement).</summary>
public static class InfosApplication
{
    /// <summary>
    /// Version de l'application, lue dans l'assembly (balise &lt;Version&gt; du .csproj).
    /// La version « informationnelle » peut finir par « +commit » : ignoré par la comparaison SemVer.
    /// </summary>
    public static VersionSemantique Version { get; } = LireVersion();

    /// <summary>Installée par le setup, portable (ZIP) ou développement.</summary>
    public static ModeInstallation Mode { get; } = DetectionInstallation.Detecter(AppContext.BaseDirectory, File.Exists);

    /// <summary>Dossier où l'installateur est téléchargé : %TEMP%\Atome-mises-a-jour.</summary>
    public static string DossierTelechargement => Path.Combine(Path.GetTempPath(), "Atome-mises-a-jour");

    private static VersionSemantique LireVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(InfosApplication).Assembly;
        string? texte = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? assembly.GetName().Version?.ToString(3);
        return VersionSemantique.TryParse(texte, out var version) ? version : new VersionSemantique(0, 0, 0);
    }
}

/// <summary>
/// Dernière étape d'une mise à jour : sauvegarde, fermeture de la base, lancement de l'installateur, puis on quitte.
/// </summary>
public sealed class ServiceInstallation(BaseDeDonnees baseDeDonnees, ServiceMiseAJour miseAJour)
{
    /// <summary>
    /// Arguments du setup Inno :
    /// /SILENT : pas d'assistant, seulement une fenêtre de progression ;
    /// /SP- : pas de question « Voulez-vous installer… ? » ;
    /// /CLOSEAPPLICATIONS : ferme ce qui utiliserait encore les fichiers d'Atome ;
    /// /NORESTART : jamais de redémarrage de Windows.
    /// Atome est relancé à la fin par l'entrée [Run] de installateur\atome.iss.
    /// </summary>
    public const string ArgumentsSetup = "/SILENT /SP- /CLOSEAPPLICATIONS /NORESTART";

    /// <summary>
    /// Termine proprement et lance l'installateur déjà vérifié (empreinte SHA-256 contrôlée).
    /// Si Windows refuse de le lancer (SmartScreen, Contrôle intelligent des applications), lève
    /// <see cref="ErreurMiseAJour"/> et Atome reste ouvert, comme avant.
    /// </summary>
    public void LancerEtQuitter(string cheminSetup, VersionSemantique version)
    {
        // 1) Copie de la base avec le mécanisme de sauvegarde habituel (VACUUM INTO dans « sauvegardes »).
        string copie = baseDeDonnees.SauvegarderAvantMiseAJour(version.ToString());
        Journal.Info("Mise à jour", $"Sauvegarde avant mise à jour : {copie}");

        // 2) On note la tentative : au prochain lancement, Atome dira si l'installation a abouti.
        miseAJour.NoterInstallationLancee(version);

        // 3) Plus aucune connexion ouverte sur la base, et on rend la place d'« instance unique »
        //    pour que l'Atome relancé par l'installateur puisse démarrer.
        BaseDeDonnees.FermerConnexions();
        var application = (App)Application.Current;
        application.LibererInstanceUnique();

        try
        {
            // UseShellExecute : lancement comme un double-clic, donc avec les contrôles de Windows (SmartScreen…).
            Process.Start(new ProcessStartInfo(cheminSetup, ArgumentsSetup) { UseShellExecute = true });
        }
        catch (Win32Exception e)
        {
            // Lancement refusé (bloqué par Windows, ou « Ne pas exécuter ») : Atome reste ouvert.
            application.ReprendreInstanceUnique();
            miseAJour.AnnulerInstallationLancee();
            Journal.Info("Mise à jour", $"Lancement du setup refusé (code {e.NativeErrorCode}) : {e.Message}");
            throw new ErreurMiseAJour(
                "Windows a empêché le lancement de l'installateur. Tu peux télécharger la mise à jour à la main " +
                "depuis la page de la Release (voir l'Aide, rubrique « Mises à jour »).", e);
        }

        Journal.Info("Mise à jour", $"Installateur de la version {version} lancé, Atome se ferme.");
        application.Shutdown();
    }

    /// <summary>Supprime les installateurs téléchargés lors d'une mise à jour précédente (déjà installée ou abandonnée).</summary>
    public static void NettoyerTelechargements()
    {
        try
        {
            if (Directory.Exists(InfosApplication.DossierTelechargement))
                Directory.Delete(InfosApplication.DossierTelechargement, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
