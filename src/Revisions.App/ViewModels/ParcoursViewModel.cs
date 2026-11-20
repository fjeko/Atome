using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Mascotte;
using Revisions.Core.Modeles;
using Revisions.Core.Parcours;
using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Séance à parcourir en « Lire puis contrôler ».</summary>
public sealed record ParametresParcours(string PackId);

/// <summary>Une section sur l'écran de résultats, avec ses liens « Relire » et « Réviser ces cartes ».</summary>
public sealed record LigneScoreSection(string SectionId, string Titre, string Score, bool Ratee);

/// <summary>
/// Parcours « Lire puis contrôler » (fiche A1) : le cours section par section avec 1 ou 2 questions flash,
/// puis un contrôle final cours masqué, puis les résultats par section.
/// Les règles (tirage, avancement, scores) sont dans <see cref="ParcoursLecture"/> ; la notation passe par
/// <see cref="ServiceNotation"/>, comme en session. L'état est enregistré après chaque action (reprise).
/// </summary>
public partial class ParcoursViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotRevisions _revisions;
    private readonly DepotParcours _depot;
    private readonly ServiceNotation _notation;
    private readonly SeanceALire _seance;
    private readonly Dictionary<string, CarteAReviser> _cartes;
    private readonly EtatParcours _parcours;
    private readonly long _sessionId;
    private readonly Stopwatch _chrono = new();
    private bool _sessionTerminee;

    // État de la question affichée (comme en session).
    private CarteAReviser? _question;
    private int _etapesRevelees;
    private int? _choixFait;
    private bool _reponseVisible;
    private int[] _ordreChoix = [];
    /// <summary>« Relire le passage » : la section est réaffichée par-dessus la question flash.</summary>
    private bool _relecture;

    public string Matiere => _seance.Matiere;
    public string Titre => _seance.Seance;

    [ObservableProperty] private string? _messageRendu;
    [ObservableProperty] private string _etape = "";
    [ObservableProperty] private double _avancement;
    [ObservableProperty] private string _consigne = "";
    [ObservableProperty] private string _messageMascotte = "";
    [ObservableProperty] private Humeur _humeurMascotte = Humeur.Encourageante;

    // Une seule rangée d'actions visible à la fois.
    [ObservableProperty] private bool _lectureVisible;
    [ObservableProperty] private string _texteSuivant = "";
    [ObservableProperty] private bool _revelerVisible;
    [ObservableProperty] private string _texteReveler = "";
    [ObservableProperty] private bool _notesVisibles;
    [ObservableProperty] private bool _rateVisible;
    [ObservableProperty] private bool _continuerVisible;
    [ObservableProperty] private bool _relireVisible;
    /// <summary>QCM pas encore répondu pendant la lecture : « Relire le passage » seul sur la rangée d'actions.</summary>
    [ObservableProperty] private bool _relireSeulVisible;
    [ObservableProperty] private bool _retourQuestionVisible;
    [ObservableProperty] private bool _resultatsVisibles;
    [ObservableProperty] private bool _saisieVisible;
    [ObservableProperty] private string _reponseSaisie = "";
    [ObservableProperty] private IReadOnlyList<LigneScoreSection> _scores = [];
    [ObservableProperty] private string _scoreGlobal = "";

    /// <summary>Levé quand il faut placer le curseur dans la zone de réponse écrite (touche Tab).</summary>
    public event Action? SaisieDemandee;

    public ParcoursViewModel(ParametresParcours parametres, MainViewModel main, EtatApplication etat, DepotBibliotheque bibliotheque,
        DepotRevisions revisions, DepotParcours depot, ServiceNotation notation)
    {
        _main = main;
        _etat = etat;
        _revisions = revisions;
        _depot = depot;
        _notation = notation;
        _seance = bibliotheque.ChargerSeance(parametres.PackId)
                  ?? throw new InvalidOperationException($"Séance introuvable : {parametres.PackId}");
        _cartes = revisions.CartesDuPack(parametres.PackId).ToDictionary(c => c.Carte.Id);

        var enCours = depot.Charger(parametres.PackId);
        bool reprise = enCours is not null && enCours.Phase != PhaseParcours.Resultats && enCours.IndexSection < _seance.Sections.Count;
        _parcours = reprise ? enCours! : ParcoursLecture.Composer(parametres.PackId, _seance.Sections, _cartes.Values.Select(c => c.Carte).ToList(), Random.Shared);
        MessageMascotte = reprise ? "On reprend là où tu t'étais arrêté. Tranquille." : "On lit une section, puis une petite question pour vérifier. C'est parti !";

        // Lire puis contrôler, c'est lire : la séance compte comme lue (ses nouvelles cartes sont débloquées).
        bibliotheque.MarquerLue(parametres.PackId, etat.Maintenant);
        // Une session à part entière : ses réponses comptent pour la série, l'objectif du jour et le planning.
        _sessionId = revisions.CreerSession("lire_controler", etat.Jour, etat.Maintenant);
        _depot.Enregistrer(_parcours);
        Afficher();
    }

    /// <summary>La calculatrice du panneau latéral : disponible en maths pendant les questions, jamais pendant la lecture.</summary>
    public CalculatriceViewModel Calculatrice => _main.Calculatrice;

    private Section SectionCourante => _seance.Sections[Math.Min(_parcours.IndexSection, _seance.Sections.Count - 1)];

    // ---- Affichage -------------------------------------------------------------------------

    /// <summary>Prépare l'écran selon l'état du parcours (section, question flash, contrôle ou résultats).</summary>
    private void Afficher()
    {
        // Une carte supprimée du pack depuis le début du parcours est simplement sautée.
        while (QuestionAttendue() is { } id && !_cartes.ContainsKey(id)) Sauter();

        LectureVisible = RevelerVisible = NotesVisibles = ContinuerVisible = RelireVisible = RelireSeulVisible = RetourQuestionVisible = ResultatsVisibles = SaisieVisible = false;
        int nbSections = Math.Max(1, _seance.Sections.Count);
        switch (_parcours.Phase)
        {
            case PhaseParcours.Lecture when _seance.Sections.Count == 0:
                _parcours.Phase = _parcours.Controle.Count > 0 ? PhaseParcours.Controle : PhaseParcours.Resultats;
                Afficher();
                return;

            case PhaseParcours.Lecture when _parcours.IndexFlash < 0 || _relecture:
                _question = _relecture ? _question : null;
                Etape = $"Lecture · section {_parcours.IndexSection + 1} / {nbSections}";
                Avancement = 0.7 * _parcours.IndexSection / nbSections;
                AfficherSection();
                if (_relecture) RetourQuestionVisible = true;
                else
                {
                    LectureVisible = true;
                    TexteSuivant = ParcoursLecture.FlashDe(_parcours, _seance.Sections).Count > 0 ? "J'ai lu : question flash"
                        : _parcours.IndexSection + 1 < _seance.Sections.Count ? "Section suivante" : "Passer au contrôle";
                }
                Consigne = _relecture ? "Relis tranquillement, puis reviens à la question." : "Lis la section, puis Entrée pour continuer.";
                Definir(EtatCalculatrice.Indisponible);
                break;

            case PhaseParcours.Lecture:
                var flash = ParcoursLecture.FlashDe(_parcours, _seance.Sections);
                Etape = $"Question flash · section {_parcours.IndexSection + 1} / {nbSections}";
                Avancement = 0.7 * (_parcours.IndexSection + 0.5) / nbSections;
                PreparerQuestion(flash[_parcours.IndexFlash]);
                break;

            case PhaseParcours.Controle:
                Etape = $"Contrôle final · question {_parcours.IndexControle + 1} / {_parcours.Controle.Count}";
                Avancement = 0.7 + 0.3 * _parcours.IndexControle / _parcours.Controle.Count;
                if (_parcours.IndexControle == 0 && _question is null)
                    MessageMascotte = "Le cours est masqué : à toi de jouer. Pas de stress, c'est pour ancrer.";
                PreparerQuestion(_parcours.Controle[_parcours.IndexControle]);
                break;

            case PhaseParcours.Resultats:
                AfficherResultats();
                break;
        }
    }

    /// <summary>Identifiant de la carte attendue à l'écran (question flash ou du contrôle), ou null.</summary>
    private string? QuestionAttendue() => _parcours.Phase switch
    {
        PhaseParcours.Lecture when _parcours.IndexFlash >= 0 => ParcoursLecture.FlashDe(_parcours, _seance.Sections) is { } l && _parcours.IndexFlash < l.Count ? l[_parcours.IndexFlash] : null,
        PhaseParcours.Controle => _parcours.IndexControle < _parcours.Controle.Count ? _parcours.Controle[_parcours.IndexControle] : null,
        _ => null,
    };

    private void Sauter()
    {
        if (_parcours.Phase == PhaseParcours.Lecture) ParcoursLecture.AvancerLecture(_parcours, _seance.Sections);
        else if (++_parcours.IndexControle >= _parcours.Controle.Count) _parcours.Phase = PhaseParcours.Resultats;
    }

    private void AfficherSection()
    {
        var section = SectionCourante;
        var elements = new List<object>();
        if (_parcours.IndexSection == 0 && !_relecture) elements.Add(new { genre = "resume", markdown = _seance.Resume });
        elements.Add(new { genre = "titre", markdown = section.Titre });
        elements.Add(new { genre = "texte", markdown = section.Contenu });
        MessageRendu = JsonSerializer.Serialize(new { elements });
    }

    /// <summary>Nouvelle question : réponse cachée, choix de QCM mélangés.</summary>
    private void PreparerQuestion(string carteId)
    {
        var carte = _cartes[carteId];
        if (_question?.Carte.Id != carteId)
        {
            _question = carte;
            _etapesRevelees = 0;
            _choixFait = null;
            _reponseVisible = false;
            ReponseSaisie = "";
            _ordreChoix = Enumerable.Range(0, carte.Carte.Contenu.Choix?.Count ?? 0).ToArray();
            Random.Shared.Shuffle(_ordreChoix);
            _chrono.Restart();
        }
        Definir(Revisions.Core.Calculatrice.RegleCalculatrice.EstMatiereMaths(Matiere) ? EtatCalculatrice.Disponible : EtatCalculatrice.Indisponible);
        MettreAJourQuestion(defilerEnBas: false);
    }

    private void Definir(EtatCalculatrice etat) => _main.Calculatrice.Definir(etat);

    private void MettreAJourQuestion(bool defilerEnBas)
    {
        var carte = _question!.Carte;
        bool qcm = carte.Type == TypeCarte.Qcm;
        var elements = RenduCarte.Elements(carte, _ordreChoix, _choixFait, _reponseVisible, _etapesRevelees, ReponseSaisie);
        MessageRendu = JsonSerializer.Serialize(new { elements, defiler = defilerEnBas ? "bas" : "haut", animation = "carte" });

        bool mauvaisChoix = qcm && _reponseVisible && _choixFait != carte.Contenu.BonneReponse;
        RevelerVisible = !_reponseVisible && !qcm;
        TexteReveler = RenduCarte.TexteReveler(carte, _etapesRevelees);
        SaisieVisible = carte.Type == TypeCarte.Question && !_reponseVisible;
        ContinuerVisible = mauvaisChoix;
        NotesVisibles = _reponseVisible && !mauvaisChoix;
        RateVisible = !qcm;
        // L'aide (relire le passage) n'existe que pendant la lecture : le contrôle final se fait sans le cours.
        RelireVisible = _parcours.Phase == PhaseParcours.Lecture;
        RelireSeulVisible = RelireVisible && qcm && !_reponseVisible;
        Consigne = !_reponseVisible
            ? (qcm ? "Choisis une réponse (clic ou touches A à E)"
                : SaisieVisible ? "Réponds de tête puis Espace, ou écris ta réponse puis Entrée"
                : "Réponds de tête, puis appuie sur Espace")
            : mauvaisChoix ? "Ce n'est pas grave : cette carte reviendra en révision."
            // QCM réussi : « Raté » n'est pas proposé, la consigne ne parle donc que des touches 2 à 4.
            : qcm ? "Bonne réponse ! C'était difficile, bien ou facile ? (touches 2 à 4)"
            : "Comment ça s'est passé ? (touches 1 à 4)";
    }

    private void AfficherResultats()
    {
        Definir(EtatCalculatrice.Indisponible);
        _question = null;
        int score = ParcoursLecture.ScoreGlobal(_parcours);
        Etape = "Résultats";
        Avancement = 1;
        ScoreGlobal = $"{score} %";
        Scores = ParcoursLecture.Scores(_parcours, _seance.Sections)
            .Select(s => new LigneScoreSection(s.SectionId, s.Titre, $"{s.Reussies} / {s.Total}", s.Ratee)).ToList();
        MessageMascotte = ParcoursLecture.Encouragement(score);
        HumeurMascotte = Humeur.Fiere;
        MessageRendu = null;
        ResultatsVisibles = true;
        Consigne = "";
        TerminerSession();
    }

    // ---- Actions ---------------------------------------------------------------------------

    /// <summary>Après la lecture d'une section : question flash, section suivante ou contrôle.</summary>
    [RelayCommand]
    private void Suivant()
    {
        if (!LectureVisible) return;
        ParcoursLecture.AvancerLecture(_parcours, _seance.Sections);
        if (_parcours.Phase == PhaseParcours.Resultats) _depot.Terminer(_parcours.PackId, _etat.Maintenant, ParcoursLecture.ScoreGlobal(_parcours));
        else _depot.Enregistrer(_parcours);
        Afficher();
    }

    /// <summary>« Relire le passage » : remonte à la section de la question flash.</summary>
    [RelayCommand]
    private void Relire()
    {
        if (!RelireVisible) return;
        _relecture = true;
        Afficher();
    }

    [RelayCommand]
    private void RetourQuestion()
    {
        _relecture = false;
        Afficher();
    }

    [RelayCommand]
    private void Reveler()
    {
        if (!RevelerVisible || _question is null) return;
        var carte = _question.Carte;
        if (carte.Type == TypeCarte.Exercice && _etapesRevelees < (carte.Contenu.Etapes?.Count ?? 0)) _etapesRevelees++;
        else _reponseVisible = true;
        MettreAJourQuestion(defilerEnBas: true);
    }

    /// <param name="position">Position du choix à l'écran (0 = A).</param>
    [RelayCommand]
    private void Choisir(int position)
    {
        if (_question is null || _relecture || _question.Carte.Type != TypeCarte.Qcm || _choixFait is not null
            || position < 0 || position >= _ordreChoix.Length) return;
        _choixFait = _ordreChoix[position];
        _reponseVisible = true;
        MettreAJourQuestion(defilerEnBas: true);
    }

    [RelayCommand]
    private void Continuer()
    {
        if (ContinuerVisible) Enregistrer(Note.Rate);
    }

    [RelayCommand]
    private void Noter(string note)
    {
        if (!NotesVisibles) return;
        var valeur = (Note)int.Parse(note);
        if (valeur == Note.Rate && !RateVisible) return;
        Enregistrer(valeur);
    }

    /// <summary>
    /// La réponse alimente FSRS par le même service que les sessions, puis le parcours avance et est enregistré.
    /// Une carte déjà posée pendant ce parcours ne rapporte pas d'XP une deuxième fois (comme un retour en session).
    /// </summary>
    private void Enregistrer(Note note)
    {
        var carte = _question!;
        bool dejaPosee = _parcours.Reponses.Any(r => r.CarteId == carte.Carte.Id);
        var (resultat, _) = _notation.Noter(_sessionId, carte, note, dejaPosee, _etat.Jour, _etat.Maintenant,
            (int)_chrono.ElapsedMilliseconds, _etat.Reglages.RetentionCible);
        _cartes[carte.Carte.Id] = carte with { Etat = resultat.NouvelEtat };

        ParcoursLecture.Repondre(_parcours, _seance.Sections, carte.Carte.Id, carte.Carte.SectionId, note);
        if (_parcours.Phase == PhaseParcours.Resultats) _depot.Terminer(_parcours.PackId, _etat.Maintenant, ParcoursLecture.ScoreGlobal(_parcours));
        else _depot.Enregistrer(_parcours);
        _question = null;
        Afficher();
    }

    /// <summary>Résultats : relire une section ratée (fenêtre à part, l'écran de résultats reste là).</summary>
    [RelayCommand]
    private void RelireSection(LigneScoreSection ligne) => _main.RevoirLaNotion(ligne.SectionId);

    /// <summary>Résultats : session sur les cartes d'une section ratée.</summary>
    [RelayCommand]
    private void ReviserSection(LigneScoreSection ligne) => _main.DemarrerSession(_revisions.CartesDeLaSection(ligne.SectionId), "seance");

    /// <summary>Quitter : l'avancement est déjà enregistré, on reprendra au même endroit.</summary>
    [RelayCommand]
    private void Quitter()
    {
        TerminerSession();
        Definir(EtatCalculatrice.Indisponible);
        _main.AllerBibliotheque();
    }

    private void TerminerSession()
    {
        if (_sessionTerminee) return;
        _sessionTerminee = true;
        _revisions.TerminerSession(_sessionId, _etat.Maintenant);
    }

    public void DemanderSaisie()
    {
        if (SaisieVisible) SaisieDemandee?.Invoke();
    }

    /// <summary>Touche dans la zone de réponse écrite : Entrée révèle, Maj+Entrée saute une ligne, Espace révèle tant que la zone est vide.</summary>
    public bool ToucheDansSaisie(string touche, bool maj, bool ctrl)
    {
        if (!SaisieVisible || !RevelerVisible) return false;
        bool revele = touche == "Enter" ? (ctrl || !maj) : touche == " " && string.IsNullOrEmpty(ReponseSaisie);
        if (revele) Reveler();
        return revele;
    }

    /// <summary>Raccourcis : Entrée/→ (suite), Espace (révéler), 1 à 4 (noter), A à E (QCM), R (relire), Échap (quitter).</summary>
    public void Touche(string touche)
    {
        switch (touche)
        {
            case "Escape":
                if (RetourQuestionVisible) RetourQuestion();
                else Quitter();
                break;
            case "Enter" or "ArrowRight" or " ":
                if (LectureVisible) Suivant();
                else if (RetourQuestionVisible) RetourQuestion();
                else if (RevelerVisible) Reveler();
                else if (ContinuerVisible) Continuer();
                else if (NotesVisibles && touche == "Enter") Noter("3");
                break;
            case "1" or "2" or "3" or "4":
                Noter(touche);
                break;
            case "r":
                Relire();
                break;
            case "Tab":
                DemanderSaisie();
                break;
            case "a" or "b" or "c" or "d" or "e":
                Choisir(touche[0] - 'a');
                break;
        }
    }
}
