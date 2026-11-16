using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.App.Views;
using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale : il décide quel écran est affiché.
/// Chaque écran est un ViewModel ; WPF choisit la vue correspondante (DataTemplate dans App.xaml).
/// </summary>
public partial class MainViewModel(
    IServiceProvider services,
    EtatApplication etat,
    ImportateurPacks importateur,
    BaseDeDonnees baseDeDonnees) : ObservableObject
{
    /// <summary>ViewModel de l'écran affiché.</summary>
    [ObservableProperty]
    private object? _vueCourante;

    /// <summary>Rubrique sélectionnée dans la barre de gauche : accueil, bibliotheque, statistiques, reglages.</summary>
    [ObservableProperty]
    private string _rubrique = "accueil";

    /// <summary>La barre de navigation est masquée pendant une session ou une lecture.</summary>
    [ObservableProperty]
    private bool _navigationVisible = true;

    /// <summary>Petit message d'information en bas de la fenêtre (résultat de l'import au démarrage).</summary>
    [ObservableProperty]
    private string? _bandeau;

    /// <summary>Crée un ViewModel en lui fournissant ses dépendances (et d'éventuels paramètres).</summary>
    private T Creer<T>(params object[] parametres) => ActivatorUtilities.CreateInstance<T>(services, parametres);

    // Vrai pendant qu'on change nous-mêmes la rubrique (pour ne pas naviguer deux fois).
    private bool _changementInterne;

    private void Afficher(object vue, string? rubrique)
    {
        if (rubrique is not null)
        {
            _changementInterne = true;
            Rubrique = rubrique;
            _changementInterne = false;
        }
        NavigationVisible = rubrique is not null;
        VueCourante = vue;
    }

    /// <summary>Appelée quand l'utilisateur clique sur une rubrique de la barre de gauche.</summary>
    partial void OnRubriqueChanged(string value)
    {
        if (_changementInterne) return;
        switch (value)
        {
            case "accueil": AllerAccueil(); break;
            case "bibliotheque": AllerBibliotheque(); break;
            case "statistiques": AllerStatistiques(); break;
            case "reglages": AllerReglages(); break;
            case "aide": AllerAide(); break;
        }
    }

    [RelayCommand]
    public void AllerAccueil() => Afficher(Creer<AccueilViewModel>(), "accueil");

    [RelayCommand]
    public void AllerBibliotheque() => Afficher(Creer<BibliothequeViewModel>(), "bibliotheque");

    [RelayCommand]
    public void AllerStatistiques() => Afficher(Creer<StatistiquesViewModel>(), "statistiques");

    [RelayCommand]
    public void AllerReglages() => Afficher(Creer<ReglagesViewModel>(), "reglages");

    [RelayCommand]
    public void AllerAide() => Afficher(Creer<AideViewModel>(), "aide");

    [RelayCommand]
    private void FermerBandeau() => Bandeau = null;

    /// <summary>Ouvre une séance en lecture, en plein écran.</summary>
    public void OuvrirLecture(string packId) =>
        Afficher(Creer<LectureViewModel>(new ParametresLecture(packId, null, DansUneFenetre: false)), null);

    /// <summary>
    /// Ouvre une section du cours dans une fenêtre à part (« Revoir la notion »),
    /// pour ne pas perdre la session en cours.
    /// </summary>
    public void RevoirLaNotion(string sectionId)
    {
        string? packId = services.GetRequiredService<DepotBibliotheque>().PackDeLaSection(sectionId);
        if (packId is null) return;
        var lecture = Creer<LectureViewModel>(new ParametresLecture(packId, sectionId, DansUneFenetre: true));
        new FenetreLecture { DataContext = lecture, Owner = Application.Current.MainWindow }.ShowDialog();
    }

    /// <summary>Lance une session avec les cartes données.</summary>
    /// <param name="cartes">Cartes, déjà dans l'ordre.</param>
    /// <param name="mode">quotidienne, reprise, seance, point_faible ou bonus.</param>
    public void DemarrerSession(IReadOnlyList<CarteAReviser> cartes, string mode)
    {
        if (cartes.Count == 0) return;
        Afficher(Creer<SessionViewModel>(new ParametresSession(cartes, mode)), null);
    }

    public void AfficherFinDeSession(BilanSession bilan) => Afficher(Creer<FinSessionViewModel>(bilan), null);

    /// <summary>Au démarrage : accueil tout de suite, puis import des packs et sauvegarde en arrière-plan.</summary>
    public async Task DemarrerAsync()
    {
        AllerAccueil();
        try
        {
            var jour = etat.Jour;
            var resultats = await Task.Run(() =>
            {
                baseDeDonnees.SauvegarderSiNecessaire(jour);
                return ImporterDossier();
            });
            AnnoncerImport(resultats, afficherRapportSiRien: false);
            // L'accueil a été calculé avant l'import : on le rafraîchit si des packs sont arrivés.
            if (VueCourante is AccueilViewModel && resultats.Any(r => r.Statut is StatutImport.Importe or StatutImport.MisAJour))
                AllerAccueil();
        }
        catch (Exception exception)
        {
            Journal.Erreur("Démarrage", exception);
            Bandeau = "L'import automatique des packs a échoué : " + exception.Message;
        }
    }

    /// <summary>Parcourt le dossier des packs (réglage) et importe ce qui est nouveau ou modifié.</summary>
    public List<ResultatImport> ImporterDossier() =>
        etat.Reglages.DossierPacks is { Length: > 0 } dossier
            ? importateur.ImporterDossier(dossier, etat.Reglages.DossierCours)
            : [];

    /// <summary>Résume un import dans le bandeau ; ouvre le rapport détaillé s'il y a des refus.</summary>
    public void AnnoncerImport(IReadOnlyList<ResultatImport> resultats, bool afficherRapportSiRien)
    {
        int importes = resultats.Count(r => r.Statut == StatutImport.Importe);
        int misAJour = resultats.Count(r => r.Statut == StatutImport.MisAJour);
        int refuses = resultats.Count(r => r.Statut == StatutImport.Refuse);

        var morceaux = new List<string>();
        if (importes > 0) morceaux.Add(importes == 1 ? "1 pack importé" : $"{importes} packs importés");
        if (misAJour > 0) morceaux.Add(misAJour == 1 ? "1 pack mis à jour" : $"{misAJour} packs mis à jour");
        if (refuses > 0) morceaux.Add(refuses == 1 ? "1 pack refusé" : $"{refuses} packs refusés");
        Bandeau = morceaux.Count > 0 ? string.Join(", ", morceaux) + "." : null;

        if (refuses > 0 || afficherRapportSiRien)
            new FenetreRapport { DataContext = new RapportImportViewModel(resultats), Owner = Application.Current.MainWindow }.ShowDialog();
    }
}
