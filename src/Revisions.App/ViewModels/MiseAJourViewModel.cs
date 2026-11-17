using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.MisesAJour;

namespace Revisions.App.ViewModels;

/// <summary>
/// Bandeau « Une nouvelle version est disponible » : notes de version résumées et trois choix
/// (Mettre à jour, Plus tard, Ignorer cette version), puis le téléchargement avec sa barre de progression.
/// </summary>
/// <remarks>
/// En version installée, « Mettre à jour » télécharge et installe. En version portable ou de développement,
/// le bouton devient « Télécharger » et ouvre simplement la page de la Release dans le navigateur.
/// </remarks>
public partial class MiseAJourViewModel : ObservableObject
{
    private readonly ServiceMiseAJour _service;
    private readonly ServiceInstallation _installation;
    private readonly Func<bool> _peutQuitterMaintenant;
    private CancellationTokenSource? _annulation;
    private string? _setupVerifie;

    /// <summary>Demande au propriétaire (la fenêtre principale) de retirer le bandeau.</summary>
    public event Action? Ferme;

    public ReleaseGitHub Release { get; }

    public string Titre => $"Atome {Release.Version} est disponible";

    public string SousTitre => $"Tu as la version {_service.VersionActuelle}.";

    /// <summary>Premières lignes des notes de version, en texte simple.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Vrai si l'installation automatique est possible (Atome installé par le setup).</summary>
    public bool InstallationAutomatique { get; } = InfosApplication.Mode == ModeInstallation.Installee;

    public string TexteBoutonPrincipal => InstallationAutomatique ? "Mettre à jour" : "Télécharger";

    /// <summary>Choix affichés tant que rien n'est en cours.</summary>
    [ObservableProperty] private bool _choixVisibles = true;

    [ObservableProperty] private bool _enTelechargement;

    /// <summary>Avancement du téléchargement, de 0 à 1.</summary>
    [ObservableProperty] private double _progression;

    /// <summary>Message d'état ou d'erreur (vide si rien à dire).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>Téléchargé et vérifié, mais une session est en cours : on attend le retour pour installer.</summary>
    [ObservableProperty] private bool _pretAInstaller;

    public MiseAJourViewModel(ReleaseGitHub release, ServiceMiseAJour service, ServiceInstallation installation, Func<bool> peutQuitterMaintenant)
    {
        Release = release;
        _service = service;
        _installation = installation;
        _peutQuitterMaintenant = peutQuitterMaintenant;
        Notes = ResumeNotes.Resumer(release.Notes);
    }

    /// <summary>Mettre à jour (version installée) ou Télécharger (portable, développement).</summary>
    [RelayCommand]
    private async Task MettreAJourAsync()
    {
        if (!InstallationAutomatique)
        {
            OuvrirPageRelease();
            return;
        }

        ChoixVisibles = false;
        EnTelechargement = true;
        Progression = 0;
        Message = "Téléchargement de la mise à jour…";
        _annulation = new CancellationTokenSource();
        try
        {
            var progression = new Progress<double>(p => Progression = p); // revient sur le fil de l'interface
            _setupVerifie = await _service.TelechargerAsync(Release, InfosApplication.DossierTelechargement, progression, _annulation.Token);
            EnTelechargement = false;
            Journal.Info("Mise à jour", $"Installateur {Release.Version} téléchargé et vérifié (SHA-256).");
            Installer();
        }
        catch (OperationCanceledException)
        {
            Revenir("Téléchargement annulé.");
        }
        catch (ErreurMiseAJour e)
        {
            Journal.Info("Mise à jour", e.Message + (e.InnerException is { } cause ? $" ({cause.Message})" : ""));
            Revenir(e.Message);
        }
        finally
        {
            _annulation?.Dispose();
            _annulation = null;
        }
    }

    /// <summary>Lance l'installateur vérifié, sauf pendant une session (on attendra le retour à l'accueil).</summary>
    [RelayCommand]
    private void Installer()
    {
        if (_setupVerifie is null) return;
        if (!_peutQuitterMaintenant())
        {
            PretAInstaller = true;
            Message = "Mise à jour prête. Clique sur « Installer maintenant » quand tu veux : Atome se fermera puis se rouvrira.";
            return;
        }
        Message = "Installation… Atome va se fermer puis se rouvrir tout seul.";
        try
        {
            _installation.LancerEtQuitter(_setupVerifie, Release.Version);
        }
        catch (ErreurMiseAJour e)
        {
            PretAInstaller = false;
            Revenir(e.Message);
        }
        catch (Exception e) when (e is System.IO.IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            // La sauvegarde de la base a échoué : on n'installe pas sans copie de sécurité.
            Journal.Erreur("Mise à jour", e);
            PretAInstaller = false;
            Revenir("La sauvegarde de tes données a échoué, la mise à jour n'est pas lancée : " + e.Message);
        }
    }

    [RelayCommand]
    private void Annuler() => _annulation?.Cancel();

    /// <summary>Plus tard : le bandeau disparaît et reviendra au prochain lancement.</summary>
    [RelayCommand]
    private void PlusTard()
    {
        _annulation?.Cancel();
        Ferme?.Invoke();
    }

    /// <summary>Ignorer cette version : mémorisé, elle ne sera plus proposée (les suivantes, si).</summary>
    [RelayCommand]
    private void Ignorer()
    {
        _service.Ignorer(Release.Version);
        Journal.Info("Mise à jour", $"Version {Release.Version} ignorée.");
        Ferme?.Invoke();
    }

    /// <summary>Ouvre la page de la Release dans le navigateur (seulement une page du dépôt d'Atome).</summary>
    [RelayCommand]
    private void OuvrirPageRelease()
    {
        string adresse = ChoixFichiers.PageDeConfiance(Release.PageWeb) ? Release.PageWeb : DepotAtome.PageReleases;
        try { Process.Start(new ProcessStartInfo(adresse) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception e) { Message = "Impossible d'ouvrir le navigateur : " + e.Message; }
    }

    private void Revenir(string message)
    {
        EnTelechargement = false;
        ChoixVisibles = !PretAInstaller;
        Message = message;
    }
}
