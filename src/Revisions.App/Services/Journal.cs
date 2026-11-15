using System.IO;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>Petit journal d'erreurs dans %LOCALAPPDATA%\RevisionsBTS\journal.log (utile pour comprendre un plantage).</summary>
public static class Journal
{
    private static readonly object Verrou = new();

    public static string Chemin => Path.Combine(BaseDeDonnees.DossierDonnees, "journal.log");

    public static void Erreur(string contexte, Exception exception)
    {
        try
        {
            lock (Verrou)
            {
                Directory.CreateDirectory(BaseDeDonnees.DossierDonnees);
                File.AppendAllText(Chemin, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{contexte}] {exception}\n\n");
            }
        }
        catch (IOException)
        {
            // Si même le journal est inaccessible, on ne peut rien faire de plus.
        }
    }
}
