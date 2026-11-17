namespace Revisions.Core.MisesAJour;

/// <summary>Comment cette copie d'Atome a été obtenue : cela décide de ce qu'on peut proposer.</summary>
public enum ModeInstallation
{
    /// <summary>Installée par Atome-Setup : la mise à jour automatique est possible.</summary>
    Installee,
    /// <summary>Version portable (ZIP décompressé) : on propose seulement le lien de téléchargement.</summary>
    Portable,
    /// <summary>Lancée depuis le code source (bin\Debug, publish…) : jamais de mise à jour automatique.</summary>
    Developpement,
}

/// <summary>
/// Détecte le <see cref="ModeInstallation"/> d'après le dossier de l'exécutable.
/// Les accès au disque sont passés en paramètres pour pouvoir tester sans vrais fichiers.
/// </summary>
public static class DetectionInstallation
{
    /// <summary>
    /// Règles, dans l'ordre :
    /// <list type="number">
    /// <item>un dossier parent contient la solution « Revisions.slnx » : c'est le dossier du code source → Développement ;</item>
    /// <item>Inno Setup a laissé son désinstalleur (unins000.exe et unins000.dat) à côté de l'exécutable → Installée ;</item>
    /// <item>sinon → Portable.</item>
    /// </list>
    /// </summary>
    /// <param name="dossierApplication">Dossier qui contient Atome.exe.</param>
    /// <param name="fichierExiste">Fonction qui dit si un fichier existe (File.Exists en vrai).</param>
    public static ModeInstallation Detecter(string dossierApplication, Func<string, bool> fichierExiste)
    {
        // On remonte jusqu'à 6 niveaux : app\src\Revisions.App\bin\Release\net10.0-windows\ en compte 5.
        string? dossier = Path.GetFullPath(dossierApplication);
        for (int niveau = 0; niveau <= 6 && !string.IsNullOrEmpty(dossier); niveau++)
        {
            if (fichierExiste(Path.Combine(dossier, "Revisions.slnx"))) return ModeInstallation.Developpement;
            dossier = Path.GetDirectoryName(dossier);
        }

        bool desinstalleur = fichierExiste(Path.Combine(dossierApplication, "unins000.exe"))
                             && fichierExiste(Path.Combine(dossierApplication, "unins000.dat"));
        return desinstalleur ? ModeInstallation.Installee : ModeInstallation.Portable;
    }
}
