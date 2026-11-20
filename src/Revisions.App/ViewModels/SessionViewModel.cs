using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Sessions;
using Revisions.Core.Signalements;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Ce qu'il faut pour lancer une session.</summary>
public sealed record ParametresSession(IReadOnlyList<CarteAReviser> Cartes, string Mode);

/// <summary>Une carte ratée, rappelée sur l'écran de fin.</summary>
/// <remarks>Le recto et le verso sont gardés pour pouvoir signaler la carte depuis l'écran de fin.</remarks>
public sealed record CarteRatee(string Texte, string Seance, string SectionId, string CarteId, string PackId, string Recto, string Verso);

/// <summary>Résumé d'une session, affiché sur l'écran de fin.</summary>
public sealed record BilanSession(int CartesRevues, int Reussies, int XpGagnee, int NiveauAvant, int NiveauApres,
    bool ObjectifAtteintPendantLaSession, IReadOnlyList<CarteRatee> Ratees);

/// <summary>
/// Écran Session : une carte à la fois (US4 à US7).
/// Déroulé : la question s'affiche → l'étudiant répond de tête → il révèle la réponse (Espace)
/// → il se note de 1 à 4 → la carte est replanifiée et on passe à la suivante.
/// </summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotRevisions _revisions;
    private readonly DepotProfil _profil;
    private readonly Planificateur _planificateur;
    private readonly ServiceSignalement _signalements;
    private readonly ServiceNotation _notation;
    private readonly SessionEnCours _session;
    private readonly long _sessionId;
    private readonly DateOnly _jour;
    private readonly Stopwatch _chrono = new();

    // Bilan accumulé au fil de la session.
    private readonly HashSet<string> _cartesVues = [];
    private readonly List<CarteRatee> _ratees = [];
    private readonly int _niveauAvant;
    private int _reussies;
    private int _xpGagnee;
    private bool _objectifAtteintIci;

    // État de la carte affichée.
    private int _etapesRevelees;
    private int? _choixFait;          // index du choix dans le pack (pas sa position à l'écran)
    private bool _reponseVisible;
    // Ordre d'affichage des choix d'un QCM, tiré au sort à chaque passage : dans les packs,
    // la bonne réponse est souvent la première, il ne faut pas qu'on puisse la deviner à sa place.
    private int[] _ordreChoix = [];

    [ObservableProperty] private string _compteur = "";
    [ObservableProperty] private double _avancement;
    /// <summary>Fil « Matière › Partie › Séance » au-dessus de la carte (un clic ouvre la section du cours).</summary>
    [ObservableProperty] private string _fil = "";
    /// <summary>Pourquoi cette carte est là (« À revoir · vue il y a 6 jours »…), ou null.</summary>
    [ObservableProperty] private string? _motif;
    [ObservableProperty] private string? _messageRendu;
    /// <summary>Matière et partie de la carte : pour sa pastille de couleur (système de design).</summary>
    [ObservableProperty] private string _matiere = "";
    [ObservableProperty] private string _partie = "";
    /// <summary>« Partie › Séance » : le fil sans la matière, déjà dans la pastille.</summary>
    [ObservableProperty] private string _filCourt = "";
    /// <summary>La carte revient après un « Raté » dans la même session.</summary>
    [ObservableProperty] private bool _estUnRetour;
    /// <summary>« Quitter la session ? » : confirmation douce avant d'arrêter.</summary>
    [ObservableProperty] private bool _confirmationQuitter;

    /// <summary>Levé après chaque note : l'écran fait monter « +10 » et joue le son de la bonne réponse (si activé).</summary>
    public event Action<int, bool>? ReponseEnregistree;


    /// <summary>Bouton principal avant la réponse : « Voir la réponse » ou « Étape suivante ».</summary>
    [ObservableProperty] private string _texteReveler = "";
    [ObservableProperty] private bool _revelerVisible;
    /// <summary>Les boutons de note (après la réponse).</summary>
    [ObservableProperty] private bool _notesVisibles;
    /// <summary>« Raté » est masqué après un bon choix de QCM.</summary>
    [ObservableProperty] private bool _rateVisible;
    /// <summary>Après un mauvais choix de QCM : un seul bouton « Continuer ».</summary>
    [ObservableProperty] private bool _continuerVisible;
    [ObservableProperty] private string _consigne = "";

    // Délai avant la prochaine révision, affiché sous chaque bouton de note.
    [ObservableProperty] private string _delaiRate = "";
    [ObservableProperty] private string _delaiDifficile = "";
    [ObservableProperty] private string _delaiBien = "";
    [ObservableProperty] private string _delaiFacile = "";

    /// <summary>Message d'encouragement quand l'objectif du jour est atteint.</summary>
    [ObservableProperty] private string? _bandeau;

    /// <summary>Remerciement affiché à côté du drapeau après un signalement (effacé à la carte suivante).</summary>
    [ObservableProperty] private string? _messageSignalement;

    /// <summary>
    /// Réponse écrite par l'étudiant sur une carte « question » (facultatif). Elle n'est pas enregistrée :
    /// elle sert seulement à comparer avec la bonne réponse avant de se noter.
    /// </summary>
    [ObservableProperty] private string _reponseSaisie = "";
    /// <summary>La zone de saisie n'existe que sur une carte « question », avant la réponse.</summary>
    [ObservableProperty] private bool _saisieVisible;
    /// <summary>Exercice dont la réponse est une matrice : une grille à remplir remplace la zone de texte.</summary>
    [ObservableProperty] private SaisieMatrice? _matrice;
    [ObservableProperty] private bool _matriceVisible;

    public SessionViewModel(ParametresSession parametres, MainViewModel main, EtatApplication etat,
        DepotRevisions revisions, DepotProfil profil, Planificateur planificateur, ServiceSignalement signalements, ServiceNotation notation)
    {
        _notation = notation;
        _signalements = signalements;
        _main = main;
        _etat = etat;
        _revisions = revisions;
        _profil = profil;
        _planificateur = planificateur;
        _jour = etat.Jour;
        _session = new SessionEnCours(parametres.Cartes);
        _sessionId = revisions.CreerSession(parametres.Mode, _jour, etat.Maintenant);
        _niveauAvant = Niveaux.NiveauPour(profil.XpTotal());
        AfficherCarte();
    }

    private CarteAReviser Courante => _session.Courante!;

    /// <summary>La calculatrice du panneau latéral (bouton dans la barre du haut).</summary>
    public CalculatriceViewModel Calculatrice => _main.Calculatrice;

    /// <summary>Calculatrice permise sur les cartes de la matière Mathématiques, invisible ailleurs.</summary>
    internal void MettreAJourCalculatrice() =>
        _main.Calculatrice.Definir(Revisions.Core.Calculatrice.RegleCalculatrice.EstMatiereMaths(Matiere) ? EtatCalculatrice.Disponible : EtatCalculatrice.Indisponible);

    // ---- Affichage -------------------------------------------------------------------------

    /// <summary>Prépare l'écran pour la carte courante (réponse cachée).</summary>
    private void AfficherCarte()
    {
        _etapesRevelees = 0;
        _choixFait = null;
        _reponseVisible = false;
        ReponseSaisie = "";
        Matrice = SaisieMatrice.Pour(Courante.Carte);
        if (Matrice is not null) Matrice.Valider = () => RevelerCommand.Execute(null);
        MessageSignalement = null;
        _ordreChoix = Enumerable.Range(0, Courante.Carte.Contenu.Choix?.Count ?? 0).ToArray();
        Random.Shared.Shuffle(_ordreChoix);
        _chrono.Restart();

        Compteur = $"{_session.Faites + 1} / {_session.Total}";
        Avancement = (double)_session.Faites / _session.Total;
        Fil = Courante.Fil;
        Motif = _session.CouranteEstUnRetour ? "On la revoit · pour bien l'ancrer" : Courante.Motif?.Texte;
        EstUnRetour = _session.CouranteEstUnRetour;
        Matiere = Courante.Matiere;
        MettreAJourCalculatrice();
        Partie = Courante.Partie;
        FilCourt = string.Join(" › ", new[] { Courante.Partie, Courante.Seance }.Where(t => !string.IsNullOrWhiteSpace(t)));
        MettreAJourEcran(defilerEnBas: false);
    }

    /// <summary>Recalcule le contenu affiché et les boutons selon l'état de la carte.</summary>
    private void MettreAJourEcran(bool defilerEnBas)
    {
        var carte = Courante.Carte;
        var c = carte.Contenu;
        bool qcm = carte.Type == TypeCarte.Qcm;
        var elements = RenduCarte.Elements(carte, _ordreChoix, _choixFait, _reponseVisible, _etapesRevelees, ReponseSaisie, Matrice?.Latex);
        MessageRendu = JsonSerializer.Serialize(new { elements, defiler = defilerEnBas ? "bas" : "haut", animation = "carte" });

        bool mauvaisChoix = qcm && _reponseVisible && _choixFait != c.BonneReponse;
        RevelerVisible = !_reponseVisible && !qcm;
        TexteReveler = "Voir la réponse";
        // Question et exercice : on peut écrire sa réponse (le résultat d'un exercice) pour la comparer.
        SaisieVisible = carte.Type is TypeCarte.Question or TypeCarte.Exercice && !_reponseVisible && Matrice is null;
        MatriceVisible = Matrice is not null && !_reponseVisible;
        ContinuerVisible = mauvaisChoix;
        NotesVisibles = _reponseVisible && !mauvaisChoix;
        RateVisible = !qcm;
        Consigne = !_reponseVisible
            ? (qcm ? "Choisis une réponse (clic ou touches A à E)"
                : SaisieVisible ? "Réponds de tête puis Espace, ou écris ta réponse puis Entrée"
                : MatriceVisible ? "Réponds de tête puis Espace, ou remplis la matrice (Tab) puis Entrée"
                : "Réponds de tête, puis appuie sur Espace")
            : mauvaisChoix ? "Ce n'est pas grave : la carte reviendra bientôt."
            // QCM réussi : « Raté » n'est pas proposé, la consigne ne parle donc que des touches 2 à 4.
            : qcm ? "Bonne réponse ! C'était difficile, bien ou facile ? (touches 2 à 4)"
            : "Comment ça s'est passé ? (touches 1 à 4)";

        if (NotesVisibles)
        {
            DelaiRate = Delai(Note.Rate);
            DelaiDifficile = Delai(Note.Difficile);
            DelaiBien = Delai(Note.Bien);
            DelaiFacile = Delai(Note.Facile);
        }
    }

    /// <summary>Délai avant la prochaine révision si l'on donnait cette note.</summary>
    private string Delai(Note note) =>
        RenduCarte.Delai(_planificateur.Appliquer(Courante.Etat, note, _jour, _etat.Reglages.RetentionCible).IntervalleJours);

    // ---- Actions ---------------------------------------------------------------------------

    /// <summary>Révèle la réponse. Exercice : la réponse s'affiche directement, ses étapes servent de correction si le résultat diffère.</summary>
    [RelayCommand]
    private void Reveler()
    {
        if (!RevelerVisible) return;
        if (Matrice is not null) ReponseSaisie = Matrice.Texte;
        _reponseVisible = true;
        MettreAJourEcran(defilerEnBas: true);
    }

    /// <summary>Choix d'une réponse de QCM (clic dans le contenu ou touche A à E).</summary>
    [RelayCommand]
    /// <param name="position">Position du choix à l'écran (0 = A).</param>
    private void Choisir(int position)
    {
        var carte = Courante.Carte;
        if (carte.Type != TypeCarte.Qcm || _choixFait is not null || position < 0 || position >= _ordreChoix.Length) return;
        _choixFait = _ordreChoix[position];
        _reponseVisible = true;
        MettreAJourEcran(defilerEnBas: true);
    }

    /// <summary>Après un mauvais choix de QCM : la carte est notée « Raté ».</summary>
    [RelayCommand]
    private void Continuer()
    {
        if (ContinuerVisible) Enregistrer(Note.Rate);
    }

    /// <summary>Note la carte (1 = Raté … 4 = Facile).</summary>
    [RelayCommand]
    private void Noter(string note)
    {
        if (!NotesVisibles) return;
        var valeur = (Note)int.Parse(note);
        if (valeur == Note.Rate && !RateVisible) return;
        Enregistrer(valeur);
    }

    /// <summary>Levé quand il faut placer le curseur dans la zone de réponse écrite (touche Tab).</summary>
    public event Action? SaisieDemandee;

    /// <summary>
    /// Demande à l'écran de placer le curseur dans la zone de saisie. Le ViewModel ne connaît pas
    /// les contrôles : il prévient seulement, et c'est la vue qui déplace le focus.
    /// </summary>
    public void DemanderSaisie()
    {
        if (SaisieVisible) SaisieDemandee?.Invoke();
        else if (MatriceVisible) Matrice!.DemanderFocus();
    }

    /// <summary>
    /// Touche pressée dans la zone de réponse écrite. Renvoie vrai si elle est consommée (la zone ne doit pas la traiter).
    /// Entrée ou Ctrl+Entrée révèle ; Maj+Entrée laisse la zone faire un saut de ligne ;
    /// Espace révèle seulement tant que rien n'est écrit (ensuite, c'est un espace dans le texte).
    /// </summary>
    public bool ToucheDansSaisie(string touche, bool maj, bool ctrl)
    {
        if (!SaisieVisible || !RevelerVisible) return false;
        bool revele = touche == "Enter" ? (ctrl || !maj) : touche == " " && string.IsNullOrEmpty(ReponseSaisie);
        if (revele) Reveler();
        return revele;
    }

    /// <summary>
    /// Signale une erreur dans la carte affichée (bouton drapeau ou touche S). Le recto et le verso sont
    /// toujours enregistrés, même si la réponse n'est pas encore visible à l'écran.
    /// </summary>
    [RelayCommand]
    private void Signaler()
    {
        var carte = Courante;
        if (_signalements.SignalerCarte(carte.Carte.Id, carte.PackId, $"{carte.Seance}", TexteCarte.Recto(carte.Carte), TexteCarte.Verso(carte.Carte)))
            MessageSignalement = ServiceSignalement.Merci;
    }

    [RelayCommand]
    private void RevoirNotion() => _main.RevoirLaNotion(Courante.Carte.SectionId);

    /// <summary>
    /// « Arrêter » : si des cartes ont déjà été notées, on demande doucement confirmation
    /// (ce qui est fait est enregistré, le reste reviendra plus tard) ; sinon on quitte tout de suite.
    /// </summary>
    [RelayCommand]
    private void Arreter()
    {
        if (_cartesVues.Count == 0) Terminer();
        else ConfirmationQuitter = true;
    }

    [RelayCommand]
    private void ConfirmerQuitter()
    {
        ConfirmationQuitter = false;
        Terminer();
    }

    [RelayCommand]
    private void Rester() => ConfirmationQuitter = false;

    /// <summary>Raccourcis clavier : Espace (révéler), 1 à 4 (noter), A à E (QCM), Entrée (suite logique), Échap (quitter).</summary>
    [RelayCommand]
    public void Touche(string touche)
    {
        // Pendant la confirmation « Quitter la session ? » : Échap ou Entrée font rester, rien d'autre ne compte.
        if (ConfirmationQuitter)
        {
            if (touche is "Escape" or "Enter" or " ") Rester();
            return;
        }
        switch (touche)
        {
            case "Escape":
                Arreter();
                break;
            case " ":
                if (RevelerVisible) Reveler();
                else if (ContinuerVisible) Continuer();
                break;
            case "Enter":
                if (RevelerVisible) Reveler();
                else if (ContinuerVisible) Continuer();
                else if (NotesVisibles) Noter("3");
                break;
            case "1" or "2" or "3" or "4":
                Noter(touche);
                break;
            case "Tab":
                DemanderSaisie();
                break;
            case "s":
                // Jamais pendant la saisie d'une réponse : MainWindow ne relaie pas les touches d'une zone de texte.
                Signaler();
                break;
            case "a" or "b" or "c" or "d" or "e":
                Choisir(touche[0] - 'a');
                break;
        }
    }

    // ---- Cartes coriaces : une proposition douce, jamais une remontrance ---------------------

    private readonly HashSet<string> _coriacesProposees = [];
    private CarteAReviser? _coriace;
    /// <summary>Message d'aide affiché quand une carte résiste (3 « Raté » sur ses 4 dernières notes).</summary>
    [ObservableProperty] private bool _coriaceVisible;
    [ObservableProperty] private string _coriaceTexte = "";

    /// <summary>Après un « Raté » : si la carte résiste depuis un moment, on propose de l'aide (une seule fois par session).</summary>
    private void ProposerSiCoriace(CarteAReviser carte, Note note)
    {
        if (note != Note.Rate || _coriacesProposees.Contains(carte.Carte.Id)) return;
        if (!CartesCoriaces.EstCoriace(_revisions.DernieresNotes(carte.Carte.Id, CartesCoriaces.Fenetre))) return;
        _coriacesProposees.Add(carte.Carte.Id);
        _coriace = carte;
        CoriaceTexte = $"« {Extrait(carte.Carte)} » résiste un peu : ça arrive à tout le monde, et elle est peut-être simplement mal formulée. "
                       + "Tu veux la relire dans le cours, la signaler, ou la mettre de côté pour l'instant ?";
        CoriaceVisible = true;
    }

    [RelayCommand]
    private void CoriaceRelire()
    {
        if (_coriace is { } c) _main.RevoirLaNotion(c.Carte.SectionId);
        CoriaceVisible = false;
    }

    [RelayCommand]
    private void CoriaceSignaler()
    {
        if (_coriace is { } c)
        {
            _signalements.SignalerCarte(c.Carte.Id, c.PackId, c.Seance, TexteCarte.Recto(c.Carte), TexteCarte.Verso(c.Carte));
            MessageSignalement = ServiceSignalement.Merci;
        }
        CoriaceVisible = false;
    }

    /// <summary>La carte est suspendue (retrouvable et réactivable dans les Statistiques) et retirée de la suite de la session.</summary>
    [RelayCommand]
    private void CoriaceMettreDeCote()
    {
        CoriaceVisible = false;
        if (_coriace is not { } c) return;
        _revisions.DefinirSuspendue(c.Carte.Id, true);
        bool courante = _session.Retirer(c.Carte.Id);
        if (_session.EstTerminee) Terminer();
        else if (courante) AfficherCarte();
    }

    [RelayCommand]
    private void CoriaceFermer() => CoriaceVisible = false;

    // ---- Annuler la dernière note (Ctrl+Z) : un seul niveau, pour une erreur de bouton ------------

    /// <summary>Tout ce qu'il faut pour revenir exactement avant la dernière note.</summary>
    private sealed record Annulable(CarteAReviser Carte, SessionEnCours.Instantane File, HashSet<string> Vues, List<CarteRatee> Ratees,
        int Reussies, int Xp, bool ObjectifIci, int XpTotalAvant, DateOnly? JourBonusAvant, HashSet<string> Coriaces);

    private Annulable? _annulable;
    /// <summary>Le petit bouton « Annuler » est visible tant que la dernière note peut être annulée.</summary>
    [ObservableProperty] private bool _annulerVisible;

    /// <summary>
    /// Revient sur la carte précédente : son historique est marqué « annulé », son état FSRS et l'XP sont ceux d'avant,
    /// la file de la session aussi. La carte est de nouveau à répondre.
    /// </summary>
    [RelayCommand]
    public void Annuler()
    {
        if (_annulable is not { } a) return;
        _annulable = null;
        AnnulerVisible = false;
        if (!_revisions.AnnulerDerniereNotation(_sessionId, a.Carte.Etat)) return;
        _profil.RestaurerXp(a.XpTotalAvant);
        _profil.JourBonusObjectif = a.JourBonusAvant;

        _session.Restaurer(a.File);
        _cartesVues.Clear(); _cartesVues.UnionWith(a.Vues);
        _ratees.Clear(); _ratees.AddRange(a.Ratees);
        _coriacesProposees.Clear(); _coriacesProposees.UnionWith(a.Coriaces);
        _reussies = a.Reussies;
        _xpGagnee = a.Xp;
        _objectifAtteintIci = a.ObjectifIci;
        Bandeau = null;
        CoriaceVisible = false;
        MessageSignalement = null;
        AfficherCarte();
    }

    // ---- Enregistrement --------------------------------------------------------------------

    private void Enregistrer(Note note)
    {
        CoriaceVisible = false;
        var carte = Courante;
        var avant = new Annulable(carte, _session.Capturer(), [.. _cartesVues], [.. _ratees], _reussies, _xpGagnee, _objectifAtteintIci,
            _profil.XpTotal(), _profil.JourBonusObjectif, [.. _coriacesProposees]);
        // Même service de notation que le parcours « Lire puis contrôler » : FSRS, XP et historique.
        var (resultat, xp) = _notation.Noter(_sessionId, carte, note, _session.CouranteEstUnRetour, _jour, _etat.Maintenant,
            (int)_chrono.ElapsedMilliseconds, _etat.Reglages.RetentionCible);
        _xpGagnee += xp;
        ReponseEnregistree?.Invoke(xp, note != Note.Rate);

        // Bilan : on ne compte que le premier passage de chaque carte.
        if (_cartesVues.Add(carte.Carte.Id))
        {
            if (note != Note.Rate) _reussies++;
            else _ratees.Add(new CarteRatee(Extrait(carte.Carte), carte.Seance, carte.Carte.SectionId, carte.Carte.Id, carte.PackId,
                TexteCarte.Recto(carte.Carte), TexteCarte.Verso(carte.Carte)));
        }

        VerifierObjectif();

        _session.Avancer(note == Note.Rate, carte with { Etat = resultat.NouvelEtat });
        ProposerSiCoriace(carte, note);
        _annulable = _session.EstTerminee ? null : avant;
        AnnulerVisible = _annulable is not null;
        if (_session.EstTerminee) Terminer();
        else AfficherCarte();
    }

    /// <summary>Bonus d'XP et petit message, une fois par jour, quand l'objectif minimal est atteint.</summary>
    private void VerifierObjectif()
    {
        if (_profil.JourBonusObjectif == _jour) return;
        if (_profil.CartesFaites(_jour) < _etat.Reglages.ObjectifMinimal) return;

        _profil.JourBonusObjectif = _jour;
        _profil.AjouterXp(CalculXp.BonusObjectif);
        _xpGagnee += CalculXp.BonusObjectif;
        _objectifAtteintIci = true;
        Bandeau = $"Objectif du jour atteint ! +{CalculXp.BonusObjectif} XP. Tu peux t'arrêter là ou continuer.";
    }

    private void Terminer()
    {
        _revisions.TerminerSession(_sessionId, _etat.Maintenant);
        if (_cartesVues.Count == 0)
        {
            _main.AllerAccueil();
            return;
        }
        _main.AfficherFinDeSession(new BilanSession(
            _cartesVues.Count, _reussies, _xpGagnee, _niveauAvant, Niveaux.NiveauPour(_profil.XpTotal()),
            _objectifAtteintIci, _ratees));
    }

    /// <summary>Début du texte d'une carte, pour la liste des cartes ratées.</summary>
    private static string Extrait(Carte carte) =>
        // La liste est en texte simple : les formules LaTeX sont converties en caractères ordinaires.
        Core.Packs.TexteSimple.Extrait(carte.Contenu.Recto ?? carte.Contenu.Question ?? carte.Contenu.Enonce, 110);
}
