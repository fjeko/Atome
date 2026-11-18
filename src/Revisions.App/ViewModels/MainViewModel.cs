using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.App.Views;
using Revisions.Core.Collections;
using Revisions.Core.MisesAJour;
using Revisions.Core.Packs;
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

    /// <summary>Bandeau « nouvelle version disponible » (null s'il n'y a rien à proposer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MiseAJourVisible))]
    private MiseAJourViewModel? _miseAJour;

    /// <summary>Le bandeau de mise à jour n'apparaît jamais pendant une session, une lecture ou un questionnaire.</summary>
    public bool MiseAJourVisible => MiseAJour is not null && NavigationVisible;

    partial void OnNavigationVisibleChanged(bool value) => OnPropertyChanged(nameof(MiseAJourVisible));

    // ---- Barre latérale : repliable en icônes seules, repliée d'office sous 1100 px de large ----

    /// <summary>Choix de l'utilisateur (bouton ☰), gardé d'un lancement à l'autre.</summary>
    public bool NavigationRepliee
    {
        get => etat.Reglages.NavigationRepliee;
        set
        {
            if (etat.Reglages.NavigationRepliee == value) return;
            etat.Reglages.NavigationRepliee = value;
            etat.EnregistrerSansAppliquer();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NavigationCompacte));
        }
    }

    /// <summary>La fenêtre fait moins de 1100 px de large (mis à jour par la fenêtre).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationCompacte))]
    private bool _fenetreEtroite;

    /// <summary>Barre latérale en icônes seules.</summary>
    public bool NavigationCompacte => NavigationRepliee || FenetreEtroite;

    [RelayCommand]
    private void BasculerNavigation()
    {
        // Fenêtre étroite : le bouton déplie quand même la barre, le temps d'y lire les libellés.
        if (FenetreEtroite && NavigationCompacte) { FenetreEtroite = false; NavigationRepliee = false; return; }
        NavigationRepliee = !NavigationRepliee;
    }

    /// <summary>
    /// La Galerie des composants n'existe que dans la version de développement
    /// (et « --sans-galerie » la cache, pour les captures d'écran du README).
    /// </summary>
    [ObservableProperty]
#if DEBUG
    private bool _galerieVisible = true;
#else
    private bool _galerieVisible;
#endif

    [RelayCommand]
    public void AllerGalerie() => Afficher(new GalerieViewModel(), "galerie");

    /// <summary>Proposition trouvée au démarrage, qui attend que l'accueil soit affiché.</summary>
    private MiseAJourViewModel? _propositionEnAttente;

    private bool _verificationLancee;

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
        if (vue is AccueilViewModel && _propositionEnAttente is not null)
        {
            MiseAJour = _propositionEnAttente;
            _propositionEnAttente = null;
        }
    }

    // ---- Mises à jour ------------------------------------------------------------------------

    /// <summary>
    /// Au lancement, en tâche de fond : bilan de la dernière installation, puis vérification (une fois par heure
    /// au plus, si le réglage est activé). Les problèmes réseau ne s'affichent pas : une ligne dans le journal suffit.
    /// </summary>
    public async Task VerifierMisesAJourAuDemarrageAsync()
    {
        if (_verificationLancee) return; // une seule fois par lancement
        _verificationLancee = true;
        try
        {
            var service = services.GetRequiredService<ServiceMiseAJour>();
            // Installateur d'une mise à jour précédente (installée ou abandonnée) : plus utile.
            await Task.Run(ServiceInstallation.NettoyerTelechargements);
            if (service.BilanInstallationPrecedente() is { } bilan)
            {
                Journal.Info("Mise à jour", $"Installation de la version {bilan.Version} : {bilan.Bilan}.");
                string texte = bilan.Bilan == BilanInstallation.Reussie
                    ? $"Atome a été mis à jour en version {InfosApplication.Version}. Tes données sont intactes."
                    : $"La mise à jour vers la version {bilan.Version} n'a pas pu s'installer. Tu peux la télécharger à la main : voir l'Aide, rubrique « Mises à jour ».";
                // On garde le message de l'import des packs s'il y en a un.
                Bandeau = Bandeau is { Length: > 0 } import ? import + " " + texte : texte;
            }

            if (!etat.Reglages.VerifierMisesAJour) return;
            // On laisse d'abord l'interface respirer : cette vérification n'est jamais urgente.
            await Task.Delay(TimeSpan.FromSeconds(2));
            var resultat = await service.VerifierAsync(manuelle: false);
            if (!resultat.DepuisMemoire)
                Journal.Info("Mise à jour", $"Vérification : {resultat.Issue}" +
                                            (resultat.Release is { } r ? $" (dernière version : {r.Version})" : "") +
                                            (resultat.Detail is { } d ? $". {d}" : ""));
            if (resultat.AProposer) Proposer(resultat.Release!, immediatement: false);
        }
        catch (Exception exception)
        {
            // Jamais d'erreur visible pour une vérification de fond.
            Journal.Erreur("Mise à jour", exception);
        }
    }

    /// <summary>
    /// Affiche le bandeau de mise à jour. Au démarrage, il attend l'accueil (jamais au milieu d'une révision) ;
    /// depuis « Vérifier maintenant », il s'affiche tout de suite.
    /// </summary>
    public void Proposer(ReleaseGitHub release, bool immediatement)
    {
        // Un téléchargement déjà en cours pour la même version : on garde le bandeau tel quel.
        if (MiseAJour?.Release.Version == release.Version) return;
        var proposition = new MiseAJourViewModel(release,
            services.GetRequiredService<ServiceMiseAJour>(), services.GetRequiredService<ServiceInstallation>(),
            peutQuitterMaintenant: () => NavigationVisible);
        proposition.Ferme += () =>
        {
            if (MiseAJour == proposition) MiseAJour = null;
        };

        if (immediatement || VueCourante is AccueilViewModel) MiseAJour = proposition;
        else _propositionEnAttente = proposition;
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
            case "acquis": AllerAcquis(); break;
            case "planning": AllerPlanning(); break;
            case "td": AllerTdGroupe(); break;
            case "aide": AllerAide(); break;
            case "galerie": AllerGalerie(); break;
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

    /// <summary>« Mes signalements » : rangé sous la rubrique Réglages de la barre de gauche.</summary>
    [RelayCommand]
    public void AllerSignalements() => Afficher(Creer<MesSignalementsViewModel>(), "reglages");

    [RelayCommand]
    public void AllerAcquis() => Afficher(Creer<AcquisViewModel>(), "acquis");

    [RelayCommand]
    public void AllerPlanning() => Afficher(Creer<PlanningViewModel>(), "planning");

    /// <summary>TD en groupe : faire le même TD à plusieurs, chacun sur son PC (un code suffit).</summary>
    [RelayCommand]
    public void AllerTdGroupe() => Afficher(Creer<TdGroupeViewModel>(), "td");

    /// <summary>Questionnaire des acquis : plein écran, sans la barre de gauche.</summary>
    public void AllerQuestionnaire() => Afficher(Creer<QuestionnaireViewModel>(), null);

    [RelayCommand]
    public void AllerAide() => Afficher(Creer<AideViewModel>(), "aide");

    /// <summary>Écran « Autre révision » : choisir ce qu'on révise (rangé sous la rubrique Accueil).</summary>
    public void AllerChoixRevision() => Afficher(Creer<ChoixRevisionViewModel>(), "accueil");

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
        // La fenêtre est déjà visible : on affiche « Chargement… » et on laisse WPF la dessiner avant de calculer l'accueil.
        Bandeau = "Chargement…";
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        Journal.Etape("Fenêtre dessinée");
        AllerAccueil();
        Bandeau = null;
        Journal.Etape("Accueil calculé");
        try
        {
            var jour = etat.Jour;
            bool planningArrive = false;
            bool proposerCoursLibres = false;
            var resultats = await Task.Run(() =>
            {
                baseDeDonnees.SauvegarderSiNecessaire(jour);
                Journal.Etape("Sauvegarde quotidienne finie");
                var resultatsImport = ImporterDossier();
                Journal.Etape($"Scan des packs fini ({resultatsImport.Count} fichiers)");
                // Cours libres livrés avec Atome : mise à jour si activés, proposition au premier lancement.
                resultatsImport.AddRange(SynchroniserCoursLibres(out proposerCoursLibres));
                Journal.Etape("Cours libres synchronisés");
                // Planning du CNED : dossier « planning » à côté du dossier « packs » (facultatif).
                planningArrive = ImporterPlanning();
                Journal.Etape("Planning chargé");
                // Les acquis déjà déclarés s'appliquent aussi aux cartes qui viennent d'arriver.
                services.GetRequiredService<DepotAcquis>().Recalculer(jour);
                return resultatsImport;
            });
            Journal.Etape("Import terminé");
            // Navigateur intégré : démarré maintenant, en tâche de fond, pour que le premier cours s'ouvre sans attendre.
            _ = ServiceWeb.PrechaufferAsync(new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle);
            // Mises à jour : en tâche de fond, sans attendre (ni le rapport d'import qui peut s'ouvrir juste après).
            _ = VerifierMisesAJourAuDemarrageAsync();
            AnnoncerImport(resultats, afficherRapportSiRien: false);
            // Premier lancement sans aucun pack : proposition des cours libres (Oui / Plus tard).
            if (proposerCoursLibres && await ProposerCoursLibresAsync())
                resultats = [.. resultats, .. await ChargerCoursLibresAsync(jour)];
            // L'accueil a été calculé avant l'import : on le rafraîchit si des packs sont arrivés.
            if (VueCourante is AccueilViewModel && (planningArrive || resultats.Any(r => r.Statut is StatutImport.Importe or StatutImport.MisAJour)))
                AllerAccueil();
            // Premier lancement : la mascotte propose le questionnaire des acquis (une fois les cours importés).
            if (VueCourante is AccueilViewModel && services.GetRequiredService<DepotAcquis>().QuestionnaireAProposer()
                && services.GetRequiredService<DepotBibliotheque>().ListerSeances(jour).Count > 0)
                AllerQuestionnaire();
        }
        catch (Exception exception)
        {
            Journal.Erreur("Démarrage", exception);
            Bandeau = "L'import automatique des packs a échoué : " + exception.Message;
            _ = VerifierMisesAJourAuDemarrageAsync();
        }
    }

    /// <summary>
    /// Cours libres au démarrage (à appeler hors du fil de l'interface). Cours libres activés : on réimporte le dossier
    /// livré avec Atome (lecture seule ; seuls les fichiers nouveaux ou modifiés sont lus). Aucun choix encore :
    /// avec des packs privés déjà là, on désactive sans rien importer ; sans aucun pack, on demande de proposer.
    /// </summary>
    private List<ResultatImport> SynchroniserCoursLibres(out bool proposer)
    {
        proposer = false;
        var depot = services.GetRequiredService<DepotCoursLibres>();
        string? dossier = etat.DossierCoursLibres;
        // Source de vérité : la collection « Cours libres » en base. L'ancien réglage « cours_libres » n'est lu que
        // tant que cette collection n'existe pas (base jamais passée par les collections).
        bool? choix = depot.EstVisible() ?? etat.Reglages.CoursLibres;
        if (ContenuLibre.Evaluer(choix, depot.NombrePacksPrives(), dossier is not null) == ActionCoursLibres.Proposer)
            proposer = true;
        return choix == true && dossier is not null
            ? importateur.ImporterDossier(dossier, null)
            : [];
    }

    /// <summary>Affiche la proposition ; si l'étudiant accepte, le choix est enregistré. « Plus tard » ne fige rien.</summary>
    private Task<bool> ProposerCoursLibresAsync()
    {
        int nombre = etat.DossierCoursLibres is { } d ? Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories).Count() : 0;
        var fenetre = new FenetreCoursLibres(nombre) { Owner = Application.Current.MainWindow };
        bool oui = fenetre.ShowDialog() == true && fenetre.Reponse == "oui";
        return Task.FromResult(oui);   // l'activation crée et affiche la collection « Cours libres » : rien d'autre à mémoriser
    }

    private async Task<List<ResultatImport>> ChargerCoursLibresAsync(DateOnly jour)
    {
        Bandeau = "Chargement des cours libres…";
        int parJour = CartesParJourAuReaffichage();
        return await Task.Run(() =>
        {
            var r = etat.DossierCoursLibres is { } d ? services.GetRequiredService<DepotCoursLibres>().Activer(d, jour, parJour) : [];
            services.GetRequiredService<DepotAcquis>().Recalculer(jour);
            return r;
        });
    }

    /// <summary>Au réaffichage d'une collection, nombre de cartes en retard ramenées par jour (la moitié du plafond quotidien).</summary>
    private int CartesParJourAuReaffichage() => Math.Max(5, etat.Reglages.PlafondQuotidien / 2);

    /// <summary>
    /// Les collections à proposer (Réglages, filtres de la Bibliothèque). Les cours libres livrés avec Atome
    /// y figurent même s'ils n'ont encore jamais été chargés.
    /// </summary>
    public List<InfoCollection> ListerCollections()
    {
        int livres = etat.DossierCoursLibres is { } d && Directory.Exists(d)
            ? Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories).Count() : 0;
        var liste = services.GetRequiredService<DepotCollections>().Lister();
        return Collections.AvecLibresAbsents(liste, livres);
    }

    /// <summary>
    /// Affiche ou masque une collection (Réglages et filtres de la Bibliothèque). Rien n'est effacé : la progression
    /// est conservée, et au réaffichage les cartes devenues dues entre-temps sont étalées sur plusieurs jours.
    /// </summary>
    /// <returns>Un message à montrer à l'étudiant si le changement a été refusé, sinon null.</returns>
    public async Task<string?> BasculerCollectionAsync(string cle, bool visible)
    {
        if (!visible && !Collections.PeutMasquer(ListerCollections(), cle)) return Collections.MessageDerniere;
        var jour = etat.Jour;
        int parJour = CartesParJourAuReaffichage();
        List<ResultatImport> resultats = [];
        try
        {
            if (cle == Collections.Libres)
            {
                if (visible) resultats = await ChargerCoursLibresAsync(jour);
                else await Task.Run(() => { services.GetRequiredService<DepotCoursLibres>().Masquer(jour); services.GetRequiredService<DepotAcquis>().Recalculer(jour); });
            }
            else
            {
                await Task.Run(() =>
                {
                    services.GetRequiredService<DepotCollections>().DefinirVisible(cle, visible, jour, parJour, imposerUneVisible: false);
                    services.GetRequiredService<DepotAcquis>().Recalculer(jour);
                });
            }
        }
        catch (Exception exception)
        {
            Journal.Erreur("Collections", exception);
            Bandeau = "La collection n'a pas pu être mise à jour : " + exception.Message;
            return null;
        }
        AnnoncerImport(resultats, afficherRapportSiRien: false);
        if (!visible) Bandeau = "Collection masquée. Ta progression est conservée.";
        return null;
    }

    /// <summary>
    /// Lit le dossier « planning » (à côté de « packs », comme dans le dossier Cours) :
    /// un fichier nouveau ou modifié est validé puis enregistré. Renvoie vrai si un planning a été enregistré.
    /// </summary>
    public bool ImporterPlanning()
    {
        if (etat.Reglages.DossierPacks is not { Length: > 0 } packs) return false;
        string? cours = Path.GetDirectoryName(packs.TrimEnd('\\', '/'));
        if (cours is null) return false;
        bool enregistre = false;
        foreach (var r in services.GetRequiredService<DepotPlanning>().ImporterDossier(Path.Combine(cours, "planning")))
        {
            enregistre |= r.Enregistre;
            if (!r.Enregistre)
                Journal.Erreur("Planning", new InvalidDataException($"{r.Fichier} : {string.Join(" ; ", r.Lecture.Erreurs)}"));
        }
        return enregistre;
    }

    /// <summary>
    /// Après l'arrivée d'un contenu partagé : import habituel des packs et du planning (la progression est
    /// conservée), recalcul des acquis, puis on rouvre l'écran en cours pour qu'il montre le nouveau contenu.
    /// </summary>
    /// <param name="fichiersPacks">Fichiers de pack qui viennent d'être extraits d'un .atome : leurs nouveaux packs vont dans la collection <paramref name="cle"/>.</param>
    public async Task ApresImportContenuAsync(IReadOnlyList<string>? fichiersPacks = null, string? cle = null, string? nom = null)
    {
        var jour = etat.Jour;
        var resultats = await Task.Run(() =>
        {
            // Les packs reçus sont importés d'abord, avec leur collection ; le scan du dossier les trouve ensuite « inchangés ».
            var r = (fichiersPacks ?? []).Select(f => importateur.ImporterFichier(f, etat.Reglages.DossierCours, null, cle, nom, reafficherSiMasquee: true)).ToList();
            r.AddRange(ImporterDossier());
            ImporterPlanning();
            services.GetRequiredService<DepotAcquis>().Recalculer(jour);
            return r;
        });
        Bandeau = null;
        if (resultats.Any(r => r.Statut == StatutImport.Refuse)) AnnoncerImport(resultats, afficherRapportSiRien: false);
        else AnnoncerReaffichage(resultats);
        // On rouvre l'écran : il se recalcule avec les nouveaux packs.
        if (Rubrique == "bibliotheque") AllerBibliotheque(); else AllerAccueil();
    }

    /// <summary>Parcourt le dossier des packs (réglage) et importe ce qui est nouveau ou modifié.</summary>
    public List<ResultatImport> ImporterDossier() =>
        etat.Reglages.DossierPacks is { Length: > 0 } dossier
            ? importateur.ImporterDossier(dossier, etat.Reglages.DossierCours)
            : [];

    /// <summary>Prévient l'étudiant quand un import a réaffiché une collection qu'il avait masquée.</summary>
    private void AnnoncerReaffichage(IReadOnlyList<ResultatImport> resultats)
    {
        var noms = resultats.Select(r => r.CollectionReaffichee).Where(n => n is not null).Distinct().ToList();
        if (noms.Count == 0) return;
        string liste = string.Join(", ", noms.Select(n => $"« {n} »"));
        Bandeau = (Bandeau is { Length: > 0 } b ? b + " " : "")
            + $"La collection {liste} était masquée : je l'ai réaffichée pour que tu voies ce que tu viens d'importer (tu peux la masquer à nouveau dans Réglages → Mes collections).";
    }

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
        AnnoncerReaffichage(resultats);

        if (refuses > 0 || afficherRapportSiRien)
            new FenetreRapport { DataContext = new RapportImportViewModel(resultats), Owner = Application.Current.MainWindow }.ShowDialog();
    }
}
