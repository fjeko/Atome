using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.TdGroupe;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Étapes de l'écran « TD en groupe ».</summary>
public enum PhaseTd
{
    /// <summary>Créer un TD ou saisir le code d'un camarade.</summary>
    Choix,
    /// <summary>« Prêts ? » : on attend que quelqu'un dise « go » sur Discord.</summary>
    Pret,
    /// <summary>Compte à rebours de 3 secondes (facultatif).</summary>
    CompteARebours,
    /// <summary>« On cherche » : chacun répond de son côté.</summary>
    Recherche,
    /// <summary>« Correction » : vérification case par case, puis correction pas à pas.</summary>
    Correction,
    /// <summary>Récap personnel.</summary>
    Fin,
}

/// <summary>Un thème proposé à la création (un générateur et ses niveaux).</summary>
public sealed record ThemeTd(string Nom, IGenerateurTd Generateur, IReadOnlyList<string> Niveaux, bool DemandeUneSeance);

/// <summary>Une séance de la bibliothèque, proposée pour un TD « séance ».</summary>
public sealed record SeanceTd(string PackId, string Libelle);

/// <summary>Une ligne du récap de fin.</summary>
public sealed record LigneRecapTd(string Numero, string Titre, string Issue, IssueExercice Valeur);

/// <summary>
/// Écran « TD en groupe » : chacun sur son PC, tout le monde fait le même TD au même rythme,
/// sans réseau. Le seul lien entre les camarades est le code, dicté à l'oral.
/// Déroulé de chaque exercice : Prêts ? → On cherche → Correction → Exercice suivant.
/// </summary>
public partial class TdGroupeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CatalogueTd _catalogue;
    private readonly DepotBibliotheque _bibliotheque;
    private readonly DepotProfil _profil;
    private readonly EtatApplication _etat;
    private readonly DepotTd _depotTd;
    private readonly DepotRevisions _revisions;

    // Réglages gardés le temps que l'application est ouverte (on refait souvent plusieurs TD d'affilée).
    private static int _dernierThemeIndex, _dernierNiveauIndex, _dernierNombre = 6;
    private static bool _dernierCompte, _dernierMinuteur, _dernierHorsCours;

    private Td? _td;
    private int _index;
    private bool _indiceVu;
    private int _etapesVues;
    /// <summary>Étapes de la correction : sous-question (lettre et intitulé), numéro dans cette sous-question, texte.</summary>
    private List<(string Lettre, string Intitule, int Numero, string Texte)> _etapes = [];
    private readonly List<IssueExercice> _issues = [];
    private DispatcherTimer? _timerCompte, _timerMinuteur;
    private TimeSpan _tempsRestant;

    public TdGroupeViewModel(MainViewModel main, CatalogueTd catalogue, DepotBibliotheque bibliotheque, DepotProfil profil, EtatApplication etat,
        DepotTd depotTd, DepotRevisions revisions)
    {
        _main = main;
        _catalogue = catalogue;
        _bibliotheque = bibliotheque;
        _profil = profil;
        _etat = etat;
        _depotTd = depotTd;
        _revisions = revisions;

        Themes =
        [
            new ThemeTd("Matrices (SQ1 Calcul matriciel)", catalogue.Trouver("MAT")!, GenerateurMatrices.NomsNiveaux, false),
            .. catalogue.Generateurs.OfType<GenerateurASujets>().Select(g => new ThemeTd(g.Nom, g, GenerateurASujets.NomsNiveaux, false)),
            new ThemeTd("Une séance de ma bibliothèque", catalogue.Trouver("SEA")!, GenerateurSeance.NomsNiveaux, true),
        ];
        Seances = bibliotheque.ListerSeances(etat.Jour)
            .Where(s => s.NbCartes > 0)
            .Select(s => new SeanceTd(s.PackId, $"{s.Matiere} · {s.Seance}"))
            .ToList();
        _themeChoisi = Themes[Math.Min(_dernierThemeIndex, Themes.Count - 1)];
        _niveauIndex = _dernierNiveauIndex;
        _nombre = _dernierNombre;
        _compteARebours = _dernierCompte;
        _inclureHorsCours = _dernierHorsCours;
        _minuteurActif = _dernierMinuteur;
        _seanceChoisie = Seances.FirstOrDefault();
        InitialiserSolo();
    }

    // ==== Écran de choix ========================================================================

    public IReadOnlyList<ThemeTd> Themes { get; }
    public IReadOnlyList<SeanceTd> Seances { get; }
    public IReadOnlyList<int> NombresPossibles { get; } = Enumerable.Range(3, 10).ToList(); // 3 à 12

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NiveauxDuTheme), nameof(ChoixSeanceVisible), nameof(ChoixHorsCoursVisible))]
    private ThemeTd _themeChoisi;

    public IReadOnlyList<string> NiveauxDuTheme => ThemeChoisi.Niveaux;
    public bool ChoixHorsCoursVisible => ThemeChoisi.Generateur.ProposeHorsCours;
    public bool ChoixSeanceVisible => ThemeChoisi.DemandeUneSeance;

    [ObservableProperty] private int _niveauIndex;
    [ObservableProperty] private int _nombre;
    [ObservableProperty] private SeanceTd? _seanceChoisie;

    /// <summary>Code saisi pour rejoindre un camarade.</summary>
    [ObservableProperty] private string _codeSaisi = "";

    [ObservableProperty] private string? _messageErreur;

    /// <summary>Le code vient d'une version plus récente : on propose d'aller vérifier les mises à jour.</summary>
    [ObservableProperty] private bool _miseAJourConseillee;

    /// <summary>Ajoute les exercices « en plus du cours » (décoché par défaut) ; porté par la version du code.</summary>
    [ObservableProperty] private bool _inclureHorsCours;

    /// <summary>Compte à rebours de 3 secondes avant chaque exercice (désactivé par défaut).</summary>
    [ObservableProperty] private bool _compteARebours;

    /// <summary>Minuteur indicatif par exercice (désactivé par défaut, jamais punitif).</summary>
    [ObservableProperty] private bool _minuteurActif;

    public IReadOnlyList<int> MinutesPossibles { get; } = [3, 5, 8, 10, 15];
    [ObservableProperty] private int _minutesParExercice = 5;

    partial void OnThemeChoisiChanged(ThemeTd value) => _dernierThemeIndex = Themes.ToList().IndexOf(value);
    partial void OnNiveauIndexChanged(int value) { if (value >= 0) _dernierNiveauIndex = value; }
    partial void OnNombreChanged(int value) => _dernierNombre = value;
    partial void OnInclureHorsCoursChanged(bool value) => _dernierHorsCours = value;
    partial void OnCompteAReboursChanged(bool value) => _dernierCompte = value;
    partial void OnMinuteurActifChanged(bool value) => _dernierMinuteur = value;

    /// <summary>Crée un nouveau TD : nouvelle graine, code à dicter aux autres.</summary>
    [RelayCommand]
    private void Creer()
    {
        MessageErreur = null;
        MiseAJourConseillee = false;
        string? packId = null;
        if (ThemeChoisi.DemandeUneSeance)
        {
            if (SeanceChoisie is null) { MessageErreur = "Choisis d'abord une séance."; return; }
            packId = SeanceChoisie.PackId;
        }
        try
        {
            Commencer(_catalogue.Creer(ThemeChoisi.Generateur, Math.Clamp(NiveauIndex + 1, 1, 3), Nombre, CatalogueTd.NouvelleGraine(), packId, InclureHorsCours));
        }
        catch (TdImpossibleException exception)
        {
            MessageErreur = exception.Message;
        }
    }

    /// <summary>Rejoint le TD d'un camarade à partir de son code.</summary>
    [RelayCommand]
    public void Rejoindre()
    {
        var ouverture = _catalogue.Ouvrir(CodeSaisi);
        MessageErreur = ouverture.Erreur;
        MiseAJourConseillee = ouverture.MiseAJourNecessaire;
        if (ouverture.Td is { } td) Commencer(td);
    }

    [RelayCommand]
    private void VerifierMisesAJour() => _main.AllerReglages();

    // ==== TD en cours ===========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstChoix), nameof(EstPret), nameof(EstCompte), nameof(EstRecherche),
        nameof(EstCorrection), nameof(EnExercice), nameof(EstFin), nameof(Consigne))]
    private PhaseTd _phase = PhaseTd.Choix;

    public bool EstChoix => Phase == PhaseTd.Choix;
    public bool EstPret => Phase == PhaseTd.Pret;
    public bool EstCompte => Phase == PhaseTd.CompteARebours;
    public bool EstRecherche => Phase == PhaseTd.Recherche;
    public bool EstCorrection => Phase == PhaseTd.Correction;
    public bool EnExercice => Phase is PhaseTd.Recherche or PhaseTd.Correction;
    public bool EstFin => Phase == PhaseTd.Fin;

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _empreinte = "";
    [ObservableProperty] private string _titreTd = "";
    [ObservableProperty] private string _avancement = "";
    [ObservableProperty] private double _progression;
    [ObservableProperty] private string _titreExercice = "";
    [ObservableProperty] private string? _messageRendu;
    [ObservableProperty] private int _compteRestant;

    /// <summary>Temps indicatif restant (null si le minuteur est désactivé).</summary>
    [ObservableProperty] private string? _minuteur;

    /// <summary>Petit message après la copie du code ou de la phrase du récap.</summary>
    [ObservableProperty] private string? _confirmation;

    public ObservableCollection<QuestionTdViewModel> Questions { get; } = [];

    /// <summary>Il reste des étapes de correction à dévoiler.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Consigne))]
    private bool _resteDesEtapes;

    /// <summary>« Exercice suivant » : possible quand chaque réponse rédigée a été évaluée par l'étudiant.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Consigne))]
    private bool _peutContinuer;

    [ObservableProperty] private bool _indiceDisponible;
    [ObservableProperty] private bool _notionDisponible;

    /// <summary>Texte du bouton « Exercice suivant » (ou « Voir mon récap » au dernier).</summary>
    public string TexteSuivant => _td is not null && _index >= (_passeCorrection ? _reponses.Count : _td.Exercices.Count) - 1 ? "Voir mon récap" : "Exercice suivant";

    /// <summary>Rappel des touches, en bas de l'écran.</summary>
    public string Consigne => Phase switch
    {
        PhaseTd.Pret => "Quand tout le monde est prêt, quelqu'un dit « go » : Espace ou Entrée pour démarrer.",
        PhaseTd.Recherche => "Tab ou flèches pour passer d'une case à l'autre · Ctrl + Entrée pour vérifier",
        PhaseTd.Correction when ResteDesEtapes => "Espace : étape suivante de la correction",
        PhaseTd.Correction when !PeutContinuer => "Dis si tu avais juste, puis passe à la suite.",
        PhaseTd.Correction => "Entrée : exercice suivant",
        _ => "",
    };

    /// <summary>Levé quand le curseur doit aller dans la première case de saisie.</summary>
    public event Action? SaisieDemandee;

    /// <summary>Levé quand le curseur doit quitter les cases (correction : Espace redevient un raccourci).</summary>
    public event Action? SaisieTerminee;

    private void Commencer(Td td)
    {
        _td = td;
        _index = 0;
        _issues.Clear();
        Code = td.Code;
        Empreinte = td.Empreinte;
        TitreTd = td.Titre;
        _main.NavigationVisible = false;
        Journal.Info("TD en groupe", $"TD {td.Code} ({td.Exercices.Count} exercices, empreinte {td.Empreinte}).");
        AllerAuPret();
    }

    /// <summary>Écran « Prêts ? » avant chaque exercice.</summary>
    private void AllerAuPret()
    {
        ArreterMinuteur();
        Avancement = $"Exercice {_index + 1} / {_td!.Exercices.Count}";
        Progression = (double)_index / _td.Exercices.Count;
        TitreExercice = _td.Exercices[_index].Titre;
        Confirmation = null;
        if (EstSolo) { AfficherExercice(); return; } // seul : pas de « Prêts ? », on enchaîne
        Phase = PhaseTd.Pret;
    }

    /// <summary>« Go » : compte à rebours éventuel, puis l'exercice.</summary>
    [RelayCommand]
    private void Partir()
    {
        if (Phase != PhaseTd.Pret) return;
        if (!CompteARebours) { AfficherExercice(); return; }

        CompteRestant = 3;
        Phase = PhaseTd.CompteARebours;
        _timerCompte = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerCompte.Tick += (_, _) =>
        {
            if (--CompteRestant > 0) return;
            _timerCompte!.Stop();
            AfficherExercice();
        };
        _timerCompte.Start();
    }

    private ExerciceTd Exercice => _td!.Exercices[_index];

    private void AfficherExercice()
    {
        var exercice = Exercice;
        _indiceVu = false;
        _etapesVues = 0;
        Questions.Clear();
        foreach (var q in exercice.Questions)
        {
            var vm = new QuestionTdViewModel(q);
            vm.AutoEvaluee += () => PeutContinuer = Questions.All(x => x.Juste is not null);
            Questions.Add(vm);
        }
        IndiceDisponible = true;
        NotionDisponible = exercice.SectionId is { } s && _bibliotheque.PackDeLaSection(s) is not null;
        ResteDesEtapes = false;
        PeutContinuer = false;
        OnPropertyChanged(nameof(TexteSuivant));
        Phase = PhaseTd.Recherche;
        Rendre(defilerEnBas: false);
        DemarrerMinuteur();
        Application.Current.Dispatcher.BeginInvoke(() => SaisieDemandee?.Invoke(), DispatcherPriority.Input);
    }

    /// <summary>Affiche un rappel de méthode (compté dans le récap : « avec indice »).</summary>
    [RelayCommand]
    private void Indice()
    {
        if (!EnExercice || _indiceVu) return;
        _indiceVu = true;
        IndiceDisponible = false;
        Rendre(defilerEnBas: true);
    }

    /// <summary>Vérification automatique, puis passage à la correction pas à pas.</summary>
    [RelayCommand]
    public void Verifier()
    {
        if (Phase != PhaseTd.Recherche) return;
        if (EnExamen && !_passeCorrection) { ValiderSansCorriger(); return; }
        foreach (var q in Questions) q.Verifier();
        ArreterMinuteur();

        // Toutes les étapes de correction, sous-question après sous-question ; la numérotation repart à 1 pour chacune.
        _etapes = [];
        foreach (var q in Exercice.Questions)
            for (int k = 0; k < q.Correction.Count; k++)
                _etapes.Add((q.Lettre, q.Intitule, k + 1, q.Correction[k]));
        _etapesVues = 0;
        ResteDesEtapes = _etapes.Count > 0;
        PeutContinuer = Questions.All(q => q.Juste is not null);
        Phase = PhaseTd.Correction;
        Rendre(defilerEnBas: false);
        SaisieTerminee?.Invoke();
    }

    /// <summary>Dévoile l'étape suivante de la correction, comme un prof au tableau.</summary>
    [RelayCommand]
    private void EtapeSuivante()
    {
        if (Phase != PhaseTd.Correction || !ResteDesEtapes) return;
        _etapesVues++;
        ResteDesEtapes = _etapesVues < _etapes.Count;
        Rendre(defilerEnBas: true);
    }

    /// <summary>Dévoile toute la correction d'un coup.</summary>
    [RelayCommand]
    private void ToutVoir()
    {
        if (Phase != PhaseTd.Correction) return;
        _etapesVues = _etapes.Count;
        ResteDesEtapes = false;
        Rendre(defilerEnBas: true);
    }

    [RelayCommand]
    private void RevoirNotion()
    {
        if (_td is not null && Exercice.SectionId is { } section) _main.RevoirLaNotion(section);
    }

    /// <summary>Exercice suivant (ou récap après le dernier).</summary>
    [RelayCommand]
    private void ExerciceSuivant()
    {
        if (Phase != PhaseTd.Correction || !PeutContinuer) return;
        _issues.Add(IssueDeLExercice());
        _index++;
        if (_passeCorrection) { if (_index < _reponses.Count) AfficherCorrectionSauvee(); else Terminer(); }
        else if (_index < _td!.Exercices.Count) AllerAuPret();
        else Terminer();
    }

    private IssueExercice IssueDeLExercice() =>
        Questions.All(q => q.Juste == true)
            ? (_indiceVu ? IssueExercice.AvecIndice : IssueExercice.PremierCoup)
            : IssueExercice.PasEncore;

    /// <summary>Quitter en cours de route : les exercices faits comptent quand même (jamais de punition).</summary>
    [RelayCommand]
    private void Quitter()
    {
        ArreterMinuteur();
        _timerCompte?.Stop();
        // Examen en cours : on corrige ce qui a déjà été validé.
        if (EnExamen && !_passeCorrection && _reponses.Count > 0) { CommencerPasseCorrection(); return; }
        if (Phase == PhaseTd.Correction && PeutContinuer) _issues.Add(IssueDeLExercice());
        if (_issues.Count > 0) Terminer();
        else RevenirAuChoix();
    }

    // ==== Fin ===================================================================================

    public ObservableCollection<LigneRecapTd> Recap { get; } = [];
    [ObservableProperty] private string _phraseRecap = "";
    [ObservableProperty] private string _texteXp = "";

    private void Terminer()
    {
        ArreterMinuteur();
        Recap.Clear();
        for (int i = 0; i < _issues.Count; i++)
        {
            string issue = _issues[i] switch
            {
                IssueExercice.PremierCoup => "Réussi du premier coup",
                IssueExercice.AvecIndice => "Réussi avec l'indice",
                _ => "À retravailler",
            };
            Recap.Add(new LigneRecapTd($"{i + 1}.", _td!.Exercices[i].Titre, issue, _issues[i]));
        }
        PhraseRecap = RecapTd.Phrase(_issues);
        int xp = RecapTd.Xp(_issues);
        int niveauAvant = Niveaux.NiveauPour(_profil.XpTotal());
        _profil.AjouterXp(xp); // l'XP habituelle ; le planning FSRS des cartes n'est pas touché
        int niveauApres = Niveaux.NiveauPour(_profil.XpTotal());
        TexteXp = $"+{xp} XP" + (niveauApres > niveauAvant ? $" · niveau {niveauApres} atteint !" : "");
        if (EstSolo) TerminerSolo();
        Progression = 1;
        Confirmation = null;
        _main.NavigationVisible = true;
        Phase = PhaseTd.Fin;
    }

    /// <summary>Retour à l'écran de choix (refaire un TD).</summary>
    [RelayCommand]
    private void RevenirAuChoix()
    {
        OublierSolo();
        _td = null;
        Questions.Clear();
        CodeSaisi = "";
        MessageErreur = null;
        _main.NavigationVisible = true;
        Phase = PhaseTd.Choix;
    }

    [RelayCommand]
    private void CopierCode() => Copier(Code, "Code copié : colle-le sur Discord.");

    [RelayCommand]
    private void CopierPhrase() => Copier(PhraseRecap, "Phrase copiée.");

    private void Copier(string texte, string message)
    {
        try
        {
            Clipboard.SetText(texte);
            Confirmation = message;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Confirmation = "Le presse-papiers est occupé, réessaie dans un instant.";
        }
    }

#if DEBUG
    /// <summary>
    /// Outil de développement (« --ecran=td:CODE » ou « td:CODE:demo ») : rejoint le TD et va jusqu'au
    /// premier exercice avec une matrice. En mode « demo », remplit la grille (une case fausse) et vérifie.
    /// </summary>
    public void DemonstrationDev(string argument)
    {
        // « td:solo:ORD,GRA » : TD solo de 15 min sur ces thèmes ; « td:solo:ORD:examen » corrige à la fin.
        if (argument.StartsWith("solo"))
        {
            string[] options = argument.Split(':');
            var prefixes = options.Length > 1 ? options[1].Split(',') : ["GRA"];
            foreach (var t in ThemesSolo) t.Coche = t.Generateur is { } g && prefixes.Contains(g.Prefixe);
            SeulementLu = false;
            DureeChoisie = Durees[0];
            CorrectionALaFin = options.Contains("examen");
            CreerSolo();
            return;
        }
        string[] morceaux = argument.Split(':');
        CodeSaisi = morceaux[0];
        Rejoindre();
        if (_td is null) return;
        _index = Math.Max(0, _td.Exercices.ToList().FindIndex(e => e.Questions.Any(q => q.Attendu is ReponseMatrice)));
        AllerAuPret();
        AfficherExercice();
        if (morceaux.Length < 2 || morceaux[1] != "demo") return;
        foreach (var q in Questions)
        {
            if (q.Question.Attendu is ReponseMatrice m)
            {
                var valeurs = m.Valeur.Coefficients().ToList();
                for (int i = 0; i < q.Cases.Count; i++) q.Cases[i].Valeur = (i == 1 ? valeurs[i] + 1 : valeurs[i]).ToString();
            }
            q.Choisir(0);
        }
        Verifier();
        EtapeSuivante();
        EtapeSuivante();
    }
#endif

    // ==== Clavier ===============================================================================

    /// <summary>
    /// Raccourcis (hors des cases de saisie) : Espace = go / étape suivante ; Entrée = suite logique ; I = indice.
    /// Renvoie vrai si la touche a servi.
    /// </summary>
    public bool Touche(string touche)
    {
        switch (Phase, touche)
        {
            case (PhaseTd.Pret, " " or "Enter"):
                Partir();
                return true;
            case (PhaseTd.Recherche, "Enter"):
                Verifier();
                return true;
            case (PhaseTd.Recherche or PhaseTd.Correction, "i"):
                Indice();
                return true;
            case (PhaseTd.Correction, " "):
                EtapeSuivante();
                return true;
            case (PhaseTd.Correction, "Enter"):
                if (ResteDesEtapes) EtapeSuivante();
                else ExerciceSuivant();
                return true;
            default:
                return false;
        }
    }

    // ==== Minuteur indicatif ====================================================================

    private void DemarrerMinuteur()
    {
        ArreterMinuteur();
        if (!MinuteurActif || EstSolo) { Minuteur = null; return; }
        _tempsRestant = TimeSpan.FromMinutes(MinutesParExercice);
        Minuteur = Format(_tempsRestant);
        _timerMinuteur = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerMinuteur.Tick += (_, _) =>
        {
            _tempsRestant -= TimeSpan.FromSeconds(1);
            if (_tempsRestant > TimeSpan.Zero) { Minuteur = Format(_tempsRestant); return; }
            // Rien ne se passe à la fin : c'est seulement un repère.
            Minuteur = "Temps indicatif écoulé : prenez le temps qu'il faut.";
            _timerMinuteur!.Stop();
        };
        _timerMinuteur.Start();
    }

    private static string Format(TimeSpan t) => $"Temps indicatif : {(int)t.TotalMinutes}:{t.Seconds:00}";

    private void ArreterMinuteur()
    {
        _timerMinuteur?.Stop();
        _timerMinuteur = null;
        if (Phase != PhaseTd.Correction) Minuteur = null;
    }

    // ==== Rendu (WebView2 : Markdown + LaTeX) ===================================================

    private void Rendre(bool defilerEnBas)
    {
        var exercice = Exercice;
        var elements = new List<object> { new { genre = "question", markdown = exercice.Enonce } };
        foreach (var q in exercice.Questions)
        {
            string texte = $"**{q.Lettre})** {q.Intitule}";
            // Propositions avec formules (QCM d'un pack) : affichées ici, bien rendues, en plus des boutons.
            if (q.Attendu is ReponseChoix c && c.Choix.Any(x => x.Contains('$') || x.Contains('`')))
                texte += "\n\n" + string.Join("\n", c.Choix.Select((x, i) => $"- **{(char)('A' + i)}.** {x}"));
            elements.Add(new { genre = "sousquestion", markdown = texte });
        }
        if (_indiceVu) elements.Add(new { genre = "indice", markdown = "**Rappel de méthode.** " + exercice.Indice });
        if (Phase == PhaseTd.Correction)
        {
            elements.Add(new { genre = "etiquette", markdown = "Correction" });
            for (int i = 0; i < _etapesVues; i++)
            {
                var etape = _etapes[i];
                // Début d'une sous-question : un intertitre « a) … » sépare nettement sa correction de la précédente.
                if (etape.Numero == 1)
                    elements.Add(new { genre = "partie", markdown = $"**{etape.Lettre})** {etape.Intitule}" });
                elements.Add(new { genre = "etape", index = etape.Numero, markdown = etape.Texte });
            }
        }
        MessageRendu = JsonSerializer.Serialize(new { elements, defiler = defilerEnBas ? "bas" : "haut" });
    }
}
