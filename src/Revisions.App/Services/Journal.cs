using System.IO;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>Petit journal d'erreurs dans %LOCALAPPDATA%\RevisionsBTS\journal.log (utile pour comprendre un plantage).</summary>
public static class Journal
{
    private static readonly object Verrou = new();

    public static string Chemin => Path.Combine(BaseDeDonnees.DossierDonnees, "journal.log");

    /// <summary>
    /// Heure de lancement du processus (pour mesurer le démarrage). On part du lancement réel et non du premier
    /// appel au journal : le chargement de .NET avant notre code compte aussi pour l'utilisateur.
    /// </summary>
    private static readonly DateTime Debut = HeureDeLancement();

    private static DateTime HeureDeLancement()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime; }
        catch (Exception) { return DateTime.Now; }
    }

    /// <summary>Millisecondes écoulées depuis le lancement du processus.</summary>
    public static long Ecoule => (long)(DateTime.Now - Debut).TotalMilliseconds;

    /// <summary>Note une étape du démarrage avec le temps écoulé depuis le lancement.</summary>
    public static void Etape(string nom)
    {
        try
        {
            lock (Verrou)
            {
                Directory.CreateDirectory(BaseDeDonnees.DossierDonnees);
                File.AppendAllText(Chemin, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [Chrono] {nom} : {Ecoule} ms\n");
            }
        }
        catch (IOException) { }
    }

    /// <summary>Note une information sur une seule ligne (par exemple : « GitHub injoignable »).</summary>
    public static void Info(string contexte, string message)
    {
        try
        {
            lock (Verrou)
            {
                Directory.CreateDirectory(BaseDeDonnees.DossierDonnees);
                File.AppendAllText(Chemin, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{contexte}] {message.ReplaceLineEndings(" ")}\n");
            }
        }
        catch (IOException) { }
    }

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
