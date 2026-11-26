using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Apparence;
using Revisions.Core.MisesAJour;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un thème dans la grille de choix, avec les couleurs de son aperçu.</summary>
public sealed partial class ChoixTheme(ThemeApp theme, string fond, string feuille, string accent, string texte) : ObservableObject
{
    public string Cle => theme.Cle;
    public string Nom => theme.Nom;
    /// <summary>Vrai : thème sombre ; faux : clair ; null : suit Windows (sert à ranger les tuiles en blocs).</summary>
    public bool? Sombre => theme.Sombre;
    public string Fond => fond;
    public string Feuille => feuille;
    public string Accent => accent;
    public string Texte => texte;

    /// <summary>Vrai pour le thème en cours (cadre mis en avant).</summary>
    [ObservableProperty] private bool _estChoisi;
}

/// <summary>Une pastille de couleur d'accent. <see cref="Hex"/> vaut null pour « celle du thème ».</summary>
public sealed partial class ChoixAccent(string nom, string? hex) : ObservableObject
{
    public string Nom => nom;
    public string? Hex => hex;
    public bool EstAutomatique => hex is null;

    [ObservableProperty] private bool _estChoisi;
}

/// <summary>
/// Écran Réglages. Chaque modification est enregistrée et appliquée tout de suite :
/// pas de bouton « Enregistrer » à oublier.
/// </summary>
public partial class ReglagesViewModel : ObservableObject
{
    private readonly EtatApplication _etat;
    private readonly MainViewModel _main;
    private readonly BaseDeDonnees _base;
    private readonly bool _initialise;
    private string _cleTheme;
    private string? _couleurAccent;

    [ObservableProperty] private double _objectifMinimal;
    [ObservableProperty] private double _plafondQuotidien;
    [ObservableProperty] private double _nouvellesParJour;
    [ObservableProperty] private double _tailleReprise;
    [ObservableProperty] private double _joursAvantReprise;
    /// <summary>Rétention visée, en pourcentage (80 à 95).</summary>
    [ObservableProperty] private double _retention;
    [ObservableProperty] private string _dossierPacks;
    [ObservableProperty] private string _dossierCours;

    /// <summary>Champ libre pour une couleur d'accent au format #RRGGBB.</summary>
    [ObservableProperty] private string _accentLibre;
    /// <summary>Message affiché si le champ libre ne contient pas une couleur valide.</summary>
    [ObservableProperty] private string? _erreurAccent;

    public IReadOnlyList<ChoixTheme> Themes { get; }
    // Les tuiles sont rangées en trois blocs : « Comme Windows », les sombres, les clairs (mêmes objets que Themes).
    public IReadOnlyList<ChoixTheme> ThemesAuto => Themes.Where(t => t.Sombre is null).ToList();
    public IReadOnlyList<ChoixTheme> ThemesSombres => Themes.Where(t => t.Sombre == true).ToList();
    public IReadOnlyList<ChoixTheme> ThemesClairs => Themes.Where(t => t.Sombre == false).ToList();
    public IReadOnlyList<ChoixAccent> Accents { get; }

    /// <summary>Navigation de l'écran (sections, recherche) : état d'interface, rien n'est enregistré.</summary>
    public NavigationReglagesViewModel Navigation { get; } = new();

    public string DossierDonnees => BaseDeDonnees.DossierDonnees;

    // ---- Apparence (cahier de design) ----
    /// <summary>Taille du texte en pourcentage (90 à 150), appliquée partout.</summary>
    [ObservableProperty] private double _tailleTexte;
    [ObservableProperty] private bool _reduireAnimations;
    [ObservableProperty] private bool _sons;
    /// <summary>Mascotte : 0 = normale, 1 = réduite, 2 = masquée.</summary>
    [ObservableProperty] private int _mascotteIndex;
    public IReadOnlyList<string> ChoixMascotte { get; } = ["Normale", "Réduite", "Masquée"];
    private static readonly string[] ClesMascotte = ["normale", "reduite", "masquee"];

    /// <summary>En activant les sons, on en joue un tout de suite : on entend ce qu'on vient de choisir.</summary>
    partial void OnSonsChanged(bool value)
    {
        if (!_initialise || !value) return;
        ServiceSons.Actifs = true;
        ServiceSons.Jouer(ServiceSons.Son.BonneReponse);
    }

    /// <summary>Les réglages du planning sont dans l'écran Planning.</summary>
    [RelayCommand]
    private void AllerPlanning() => _main.AllerPlanning();

    /// <summary>Partage du contenu avec un camarade (export et import).</summary>
    public PartageViewModel Partage { get; }

    /// <summary>Ouvre l'écran « Mes signalements ».</summary>
    [RelayCommand]
    private void AllerSignalements() => _main.AllerSignalements();

    // ---- Voix (boutons 🔊 des cours d'anglais, test de positionnement) ----------------------

    private readonly ServiceVoix _voix;

    /// <summary>Téléchargement et état de la voix naturelle (affichés tels quels).</summary>
    public ServiceVoixNaturelle VoixNaturelle { get; }

    /// <summary>Volume de la voix, en pourcentage (0 à 100).</summary>
    [ObservableProperty] private double _volumeVoix;
    /// <summary>Vitesse de la voix, en pourcentage (50 à 150 ; 100 = normale).</summary>
    [ObservableProperty] private double _vitesseVoix;
    /// <summary>« Voix utilisée : … », recalculée quand la voix naturelle devient prête.</summary>
    [ObservableProperty] private string _voixUtilisee = "";

    private void MettreAJourVoixUtilisee(object? _ = null, System.ComponentModel.PropertyChangedEventArgs? __ = null)
    {
        string langue = Revisions.Core.Voix.ChoixVoix.Anglais;
        VoixUtilisee = _voix.NomVoix(langue) is { } nom
            ? $"Voix utilisée : {nom}" + (_voix.Moteur(langue) == Revisions.Core.Voix.MoteurVoix.Piper ? "" : " (voix de Windows)")
            : "Aucune voix anglaise pour l'instant : la voix naturelle se télécharge, ou ajoute une voix anglaise dans Windows (Paramètres → Heure et langue → Voix).";
    }

    /// <summary>Bouton « Tester » : une phrase courte, avec le volume et la vitesse choisis.</summary>
    [RelayCommand]
    private async Task TesterVoixAsync()
    {
        MettreAJourVoixUtilisee();
        await _voix.LireAsync("Hello! This is the English voice of Atome. Nice to meet you.", Revisions.Core.Voix.ChoixVoix.Anglais);
    }

    // ---- Mises à jour --------------------------------------------------------------------------

    private readonly ServiceMiseAJour _miseAJour;

    /// <summary>« Vérifier les mises à jour au démarrage » (activé par défaut).</summary>
    [ObservableProperty] private bool _verifierMisesAJour;
    /// <summary>Proposer des nouvelles cartes de cours non lus (désactivé par défaut).</summary>
    [ObservableProperty] private bool _nouvellesNonLues;

    /// <summary>« Mes collections » : une ligne par collection de packs, avec son interrupteur « Afficher et réviser ».</summary>
    [ObservableProperty] private IReadOnlyList<ChoixCollection> _mesCollections = [];
    /// <summary>Explication quand une collection ne peut pas être masquée (c'est la dernière).</summary>
    [ObservableProperty] private string? _messageCollections;

    private async Task ChangerCollectionAsync(ChoixCollection ligne)
    {
        MessageCollections = await _main.BasculerCollectionAsync(ligne.Cle, ligne.Visible);
        // On relit la liste : elle montre l'état réel (changement refusé, compteurs à jour).
        MesCollections = _main.ListerCollections().Select(i => new ChoixCollection(i, ChangerCollectionAsync)).ToList();
    }

    /// <summary>Résultat de « Vérifier maintenant ».</summary>
    [ObservableProperty] private string? _messageMiseAJour;

    public string VersionInstallee => $"Version {InfosApplication.Version}" + InfosApplication.Mode switch
    {
        ModeInstallation.Installee => " (installée avec le setup : mise à jour automatique possible)",
        ModeInstallation.Portable => " (portable : les nouvelles versions se téléchargent à la main)",
        _ => " (version de développement : pas de mise à jour automatique)",
    };

    /// <summary>Contacte GitHub tout de suite, même si une version a été ignorée.</summary>
    [RelayCommand]
    private async Task VerifierMaintenantAsync()
    {
        MessageMiseAJour = "Vérification…";
        var resultat = await _miseAJour.VerifierAsync(manuelle: true);
        Journal.Info("Mise à jour", $"Vérification manuelle : {resultat.Issue}" + (resultat.Detail is { } d ? $". {d}" : ""));
        MessageMiseAJour = resultat.Issue switch
        {
            IssueVerification.NouvelleVersion => $"La version {resultat.Release!.Version} est disponible : regarde le bandeau en bas de la fenêtre.",
            IssueVerification.AJour => $"Atome est à jour (version {_miseAJour.VersionActuelle}).",
            IssueVerification.AucuneRelease => "Aucune version n'est encore publiée sur GitHub.",
            IssueVerification.QuotaDepasse => "GitHub limite le nombre de vérifications depuis ta connexion. Réessaie dans une heure.",
            _ => "Impossible de joindre GitHub. Vérifie ta connexion à Internet, puis réessaie.",
        };
        if (resultat.AProposer) _main.Proposer(resultat.Release!, immediatement: true);
    }

    public ReglagesViewModel(EtatApplication etat, PartageViewModel partage, MainViewModel main, ServiceMiseAJour miseAJour, BaseDeDonnees baseDeDonnees, ServiceVoix voix, ServiceVoixNaturelle voixNaturelle)
    {
        _voix = voix;
        VoixNaturelle = voixNaturelle;
        // Abonnement « faible » : l'écran Réglages peut disparaître sans rester accroché au service.
        System.ComponentModel.PropertyChangedEventManager.AddHandler(voixNaturelle, MettreAJourVoixUtilisee, nameof(ServiceVoixNaturelle.Etat));
        _main = main;
        _base = baseDeDonnees;
        _etat = etat;
        _miseAJour = miseAJour;
        Partage = partage;
        _ = partage.EstimerAsync(); // taille des documents, calculée en arrière-plan
        var r = etat.Reglages;
#pragma warning disable MVVMTK0034 // on remplit les champs directement : rien à enregistrer pendant la construction
        _objectifMinimal = r.ObjectifMinimal;
        _plafondQuotidien = r.PlafondQuotidien;
        _nouvellesParJour = r.NouvellesParJour;
        _tailleReprise = r.TailleReprise;
        _joursAvantReprise = r.JoursAvantReprise;
        _retention = Math.Round(r.RetentionCible * 100);
        _dossierPacks = r.DossierPacks ?? "";
        _dossierCours = r.DossierCours ?? "";
        _accentLibre = r.CouleurAccent ?? "";
        _verifierMisesAJour = r.VerifierMisesAJour;
        _nouvellesNonLues = r.NouvellesNonLues;
        _dossierSauvegardeAuto = r.DossierSauvegardeAuto ?? "";
        _tailleTexte = r.TailleTexte;
        _reduireAnimations = r.ReduireAnimations;
        _sons = r.Sons;
        _volumeVoix = r.VolumeVoix;
        _vitesseVoix = r.VitesseVoix;
        MettreAJourVoixUtilisee();
        _mascotteIndex = Math.Max(0, Array.IndexOf(ClesMascotte, r.Mascotte));
        MesCollections = main.ListerCollections().Select(i => new ChoixCollection(i, ChangerCollectionAsync)).ToList();
#pragma warning restore MVVMTK0034
        _cleTheme = r.Theme;
        _couleurAccent = r.CouleurAccent;

        // Aperçu de chaque thème : on calcule sa palette comme si on l'appliquait.
        bool systemeSombre = etat.Palette.Sombre;
        Couleur accentWindows = ServiceTheme.AccentWindows();
        Themes = Core.Apparence.Themes.Tous.Select(t =>
        {
            var p = Core.Apparence.Themes.Resoudre(t, null, systemeSombre, accentWindows);
            var charte = Charte.Calculer(p);
            var fond = charte.Fond;
            var feuille = charte.Surface;
            return new ChoixTheme(t, fond.Hex, feuille.Hex, p.Accent.Hex, p.Texte.Hex) { EstChoisi = t.Cle == _cleTheme };
        }).ToList();

        Accents = Core.Apparence.Themes.Accents
            .Select(a => new ChoixAccent(a.Nom, a.Couleur.Hex))
            .Prepend(new ChoixAccent("Celle du thème", null))
            .ToList();
        MarquerAccent();
        _initialise = true;
    }

    /// <summary>Appelée à chaque changement d'une propriété : on recopie tout dans les réglages et on enregistre.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Le champ libre et son message d'erreur ne s'enregistrent pas tels quels (voir OnAccentLibreChanged).
        if (!_initialise || e.PropertyName is nameof(AccentLibre) or nameof(ErreurAccent) or nameof(MessageMiseAJour)
            or nameof(MesCollections) or nameof(MessageCollections) or nameof(MessageSauvegarde) or nameof(VoixUtilisee)) return;
        Enregistrer();
    }

    private void Enregistrer()
    {
        var r = _etat.Reglages;
        r.ObjectifMinimal = (int)ObjectifMinimal;
        r.PlafondQuotidien = (int)PlafondQuotidien;
        r.NouvellesParJour = (int)NouvellesParJour;
        r.TailleReprise = (int)TailleReprise;
        r.JoursAvantReprise = (int)JoursAvantReprise;
        r.RetentionCible = Retention / 100;
        r.DossierPacks = DossierPacks.Length == 0 ? null : DossierPacks;
        r.DossierCours = DossierCours.Length == 0 ? null : DossierCours;
        r.Theme = _cleTheme;
        r.CouleurAccent = _couleurAccent;
        r.VerifierMisesAJour = VerifierMisesAJour;
        r.NouvellesNonLues = NouvellesNonLues;
        r.DossierSauvegardeAuto = DossierSauvegardeAuto.Length == 0 ? null : DossierSauvegardeAuto;
        r.TailleTexte = (int)Math.Round(TailleTexte);
        r.ReduireAnimations = ReduireAnimations;
        r.Sons = Sons;
        r.VolumeVoix = (int)Math.Round(VolumeVoix);
        r.VitesseVoix = (int)Math.Round(VitesseVoix);
        r.Mascotte = ClesMascotte[Math.Clamp(MascotteIndex, 0, 2)];
        _etat.Enregistrer(); // enregistre et applique le thème immédiatement
    }

    // ---- Personnalisation ------------------------------------------------------------------

    [RelayCommand]
    private void ChoisirTheme(ChoixTheme choix)
    {
        _cleTheme = choix.Cle;
        foreach (var theme in Themes) theme.EstChoisi = theme == choix;
        Enregistrer();
    }

    [RelayCommand]
    private void ChoisirAccent(ChoixAccent choix)
    {
        _couleurAccent = choix.Hex;
        AccentLibre = choix.Hex ?? "";
        ErreurAccent = null;
        MarquerAccent();
        Enregistrer();
    }

    /// <summary>Champ libre : dès que le texte est une couleur valide, elle est appliquée.</summary>
    partial void OnAccentLibreChanged(string value)
    {
        if (!_initialise) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            ErreurAccent = null;
            return; // champ vidé : on garde la couleur en cours
        }
        if (!Couleur.TryParse(value, out var couleur))
        {
            ErreurAccent = "Écris une couleur comme #2F7DE1 (un # puis 6 chiffres ou lettres de A à F).";
            return;
        }
        ErreurAccent = null;
        if (couleur.Hex == _couleurAccent) return;
        _couleurAccent = couleur.Hex;
        MarquerAccent();
        Enregistrer();
    }

    private void MarquerAccent()
    {
        foreach (var accent in Accents)
            accent.EstChoisi = string.Equals(accent.Hex, _couleurAccent, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Dossiers --------------------------------------------------------------------------

    [RelayCommand]
    private void ChoisirDossierPacks()
    {
        if (ChoisirDossier("Dossier des packs", DossierPacks) is { } dossier) DossierPacks = dossier;
    }

    [RelayCommand]
    private void ChoisirDossierCours()
    {
        if (ChoisirDossier("Dossier « Cours » (celui qui contient le dossier cours/ avec les PDF)", DossierCours) is { } dossier) DossierCours = dossier;
    }

    private static string? ChoisirDossier(string titre, string actuel)
    {
        var dialogue = new OpenFolderDialog { Title = titre, InitialDirectory = actuel };
        return dialogue.ShowDialog() == true ? dialogue.FolderName : null;
    }

    // ---- Sauvegarde de la progression ------------------------------------------------------

    /// <summary>Résultat de la dernière sauvegarde ou restauration, affiché sous les boutons.</summary>
    [ObservableProperty] private string? _messageSauvegarde;
    /// <summary>Dossier de la sauvegarde automatique (vide = désactivée).</summary>
    [ObservableProperty] private string _dossierSauvegardeAuto = "";

    [RelayCommand]
    private void SauvegarderProgression()
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Sauvegarder ma progression",
            Filter = $"Sauvegarde Atome (*{BaseDeDonnees.ExtensionSauvegarde})|*{BaseDeDonnees.ExtensionSauvegarde}",
            FileName = $"atome-progression-{_etat.Jour:yyyy-MM-dd}{BaseDeDonnees.ExtensionSauvegarde}",
        };
        if (dialogue.ShowDialog() != true) return;
        try
        {
            _base.SauvegarderProgression(dialogue.FileName);
            MessageSauvegarde = "Ta progression est sauvegardée. Garde ce fichier en lieu sûr ou copie-le sur ton autre PC.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Journal.Info("Sauvegarde", "Échec : " + e.Message);
            MessageSauvegarde = "La sauvegarde n'a pas pu être créée : " + e.Message;
        }
    }

    /// <summary>Choisit un fichier, le vérifie, garde une copie de la base actuelle puis redémarre Atome avec la progression restaurée.</summary>
    [RelayCommand]
    private void Restaurer()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Restaurer ma progression",
            Filter = $"Sauvegarde Atome (*{BaseDeDonnees.ExtensionSauvegarde};*.db)|*{BaseDeDonnees.ExtensionSauvegarde};*.db",
        };
        if (dialogue.ShowDialog() != true) return;
        if (BaseDeDonnees.VerifierSauvegarde(dialogue.FileName) is { } probleme)
        {
            MessageSauvegarde = probleme;
            return;
        }
        var reponse = System.Windows.MessageBox.Show(
            "Ta progression actuelle sera remplacée par celle de cette sauvegarde. Une copie de sécurité de ta progression actuelle est faite avant, " +
            "et Atome redémarre.\n\nContinuer ?",
            "Restaurer ma progression", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (reponse != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            string copie = _base.PreparerRestauration(dialogue.FileName);
            Journal.Info("Sauvegarde", $"Restauration depuis {dialogue.FileName} ; copie de sécurité : {copie}");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageSauvegarde = "La restauration n'a pas pu être préparée, rien n'a été changé : " + e.Message;
            return;
        }
        ((App)System.Windows.Application.Current).Redemarrer();
    }

    [RelayCommand]
    private void ChoisirDossierSauvegardeAuto()
    {
        if (ChoisirDossier("Dossier de la sauvegarde automatique (clé USB, OneDrive…)", DossierSauvegardeAuto) is { } dossier)
            DossierSauvegardeAuto = dossier;
    }

    [RelayCommand]
    private void DesactiverSauvegardeAuto() => DossierSauvegardeAuto = "";

    [RelayCommand]
    private void OuvrirDossierDonnees() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DossierDonnees) { UseShellExecute = true });
}
