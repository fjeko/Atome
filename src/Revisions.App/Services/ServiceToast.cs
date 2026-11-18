using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Revisions.App.Services;

/// <summary>Une notification brève (« Prompt copié », « Signalement enregistré »).</summary>
public sealed record Toast(string Texte, string Icone);

/// <summary>
/// Toasts : de petites notifications qui apparaissent en bas de la fenêtre et s'effacent seules
/// au bout de quelques secondes. La fenêtre principale les affiche (voir MainWindow.xaml).
/// </summary>
public static class ServiceToast
{
    /// <summary>Toasts visibles en ce moment (trois au plus).</summary>
    public static ObservableCollection<Toast> Visibles { get; } = [];

    public static void Afficher(string texte, string icone = Controles.Icones.Coche)
    {
        var toast = new Toast(texte, icone);
        Visibles.Add(toast);
        while (Visibles.Count > 3) Visibles.RemoveAt(0);
        var minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        minuterie.Tick += (_, _) =>
        {
            minuterie.Stop();
            Visibles.Remove(toast);
        };
        minuterie.Start();
    }
}
