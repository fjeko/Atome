using System.Diagnostics;
using Revisions.Core.MisesAJour;

namespace Revisions.App.Services;

/// <summary>Ouverture de liens web dans le navigateur par défaut.</summary>
public static class Liens
{
    /// <summary>Ouvre l'invitation au Discord de la communauté. Renvoie false si aucun navigateur n'a pu être lancé.</summary>
    public static bool OuvrirDiscord()
    {
        try { Process.Start(new ProcessStartInfo(DepotAtome.LienDiscord) { UseShellExecute = true }); return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
