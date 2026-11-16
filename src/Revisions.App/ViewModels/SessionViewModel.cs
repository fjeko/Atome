using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Ce qu'il faut pour lancer une session.</summary>
public sealed record ParametresSession(IReadOnlyList<CarteAReviser> Cartes, string Mode);

/// <summary>Une carte ratée, rappelée sur l'écran de fin.</summary>
public sealed record CarteRatee(string Texte, string Seance, string SectionId);

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
    [ObservableProperty] private string _contexte = "";
    [ObservableProperty] private string? _messageRendu;

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

    /// <summary>
    /// Réponse écrite par l'étudiant sur une carte « question » (facultatif). Elle n'est pas enregistrée :
    /// elle sert seulement à comparer avec la bonne réponse avant de se noter.
    /// </summary>
    [ObservableProperty] private string _reponseSaisie = "";
    /// <summary>La zone de saisie n'existe que sur une carte « question », avant la réponse.</summary>
    [ObservableProperty] private bool _saisieVisible;

    public SessionViewModel(ParametresSession parametres, MainViewModel main, EtatApplication etat,
        DepotRevisions revisions, DepotProfil profil, Planificateur planificateur)
    {
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

    // ---- Affichage -------------------------------------------------------------------------

    /// <summary>Prépare l'écran pour la carte courante (réponse cachée).</summary>
    private void AfficherCarte()
    {
        _etapesRevelees = 0;
        _choixFait = null;
        _reponseVisible = false;
        ReponseSaisie = "";
        _ordreChoix = Enumerable.Range(0, Courante.Carte.Contenu.Choix?.Count ?? 0).ToArray();
        Random.Shared.Shuffle(_ordreChoix);
        _chrono.Restart();

        Compteur = $"{_session.Faites + 1} / {_session.Total}";
        Avancement = (double)_session.Faites / _session.Total;
        Contexte = $"{Courante.Matiere}  ·  {Courante.Seance}" + (_session.CouranteEstUnRetour ? "  ·  on la revoit" : "");
        MettreAJourEcran(defilerEnBas: false);
    }

    /// <summary>Recalcule le contenu affiché et les boutons selon l'état de la carte.</summary>
    private void MettreAJourEcran(bool defilerEnBas)
    {
        var carte = Courante.Carte;
        var c = carte.Contenu;
        var elements = new List<object>();
        bool qcm = carte.Type == TypeCarte.Qcm;

        switch (carte.Type)
        {
            case TypeCarte.Question:
                elements.Add(new { genre = "question", markdown = c.Recto });
                if (_reponseVisible && !string.IsNullOrWhiteSpace(ReponseSaisie))
                {
                    // Une réponse a été écrite : on l'affiche à côté de la bonne pour comparer.
                    elements.Add(new { genre = "comparaison", saisie = ReponseSaisie.Trim(), markdown = c.Verso });
                }
                else if (_reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = "Réponse" });
                    elements.Add(new { genre = "reponse", markdown = c.Verso });
                }
                break;

            case TypeCarte.Qcm:
                elements.Add(new { genre = "question", markdown = c.Question });
                for (int position = 0; position < _ordreChoix.Length; position++)
                {
                    int i = _ordreChoix[position]; // index du choix dans le pack
                    // Avant le choix : cliquable. Après : le bon en vert, le mauvais choisi en rouge.
                    string etat = _choixFait is null ? "actif"
                        : i == c.BonneReponse ? "bon"
                        : i == _choixFait ? "mauvais"
                        : "neutre";
                    elements.Add(new { genre = "choix", index = position, markdown = c.Choix![i], etat });
                }
                if (_reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = _choixFait == c.BonneReponse ? "Bonne réponse" : "Explication" });
                    elements.Add(new { genre = "explication", markdown = c.Explication });
                }
                break;

            case TypeCarte.Exercice:
                elements.Add(new { genre = "question", markdown = c.Enonce });
                for (int i = 0; i < _etapesRevelees; i++)
                    elements.Add(new { genre = "etape", index = i + 1, markdown = c.Etapes![i] });
                if (_reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = "Réponse" });
                    elements.Add(new { genre = "reponse", markdown = c.Reponse });
                }
                break;
        }
        MessageRendu = JsonSerializer.Serialize(new { elements, defiler = defilerEnBas ? "bas" : "haut" });

        bool mauvaisChoix = qcm && _reponseVisible && _choixFait != c.BonneReponse;
        RevelerVisible = !_reponseVisible && !qcm;
        TexteReveler = carte.Type == TypeCarte.Exercice && _etapesRevelees < c.Etapes!.Count
            ? (_etapesRevelees == 0 ? "Voir la première étape" : "Étape suivante")
            : "Voir la réponse";
        SaisieVisible = carte.Type == TypeCarte.Question && !_reponseVisible;
        ContinuerVisible = mauvaisChoix;
        NotesVisibles = _reponseVisible && !mauvaisChoix;
        RateVisible = !qcm;
        Consigne = !_reponseVisible
            ? (qcm ? "Choisis une réponse (clic ou touches A à E)"
                : SaisieVisible ? "Réponds de tête ou par écrit, puis appuie sur Espace"
                : "Réponds de tête, puis appuie sur Espace")
            : mauvaisChoix ? "Ce n'est pas grave : la carte reviendra bientôt."
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
    private string Delai(Note note)
    {
        int jours = _planificateur.Appliquer(Courante.Etat, note, _jour, _etat.Reglages.RetentionCible).IntervalleJours;
        return jours switch
        {
            1 => "demain",
            < 30 => $"{jours} j",
            < 365 => $"{Math.Round(jours / 30.0)} mois",
            _ => $"{jours / 365.0:0.#} an(s)",
        };
    }

    // ---- Actions ---------------------------------------------------------------------------

    /// <summary>Révèle la réponse (ou l'étape suivante d'un exercice).</summary>
    [RelayCommand]
    private void Reveler()
    {
        if (!RevelerVisible) return;
        var carte = Courante.Carte;
        if (carte.Type == TypeCarte.Exercice && _etapesRevelees < carte.Contenu.Etapes!.Count)
            _etapesRevelees++;
        else
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

    [RelayCommand]
    private void RevoirNotion() => _main.RevoirLaNotion(Courante.Carte.SectionId);

    /// <summary>Arrête la session : ce qui est fait est enregistré, le reste reviendra plus tard.</summary>
    [RelayCommand]
    private void Arreter() => Terminer();

    /// <summary>Raccourcis clavier : Espace (révéler), 1 à 4 (noter), A à E (QCM), Entrée (suite logique).</summary>
    [RelayCommand]
    public void Touche(string touche)
    {
        switch (touche)
        {
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
            case "a" or "b" or "c" or "d" or "e":
                Choisir(touche[0] - 'a');
                break;
        }
    }

    // ---- Enregistrement --------------------------------------------------------------------

    private void Enregistrer(Note note)
    {
        var carte = Courante;
        bool retour = _session.CouranteEstUnRetour;
        bool nouvelle = carte.Etat.EstNouvelle;
        bool due = !nouvelle && carte.Etat.Echeance <= _jour;

        var resultat = _planificateur.Appliquer(carte.Etat, note, _jour, _etat.Reglages.RetentionCible);
        int xp = CalculXp.PourReponse(note, due, nouvelle, retour);
        _revisions.EnregistrerReponse(_sessionId, carte.Etat, resultat, note, _jour, _etat.Maintenant,
            (int)_chrono.ElapsedMilliseconds, due || nouvelle, xp);
        _xpGagnee += xp;

        // Bilan : on ne compte que le premier passage de chaque carte.
        if (_cartesVues.Add(carte.Carte.Id))
        {
            if (note != Note.Rate) _reussies++;
            else _ratees.Add(new CarteRatee(Extrait(carte.Carte), carte.Seance, carte.Carte.SectionId));
        }

        VerifierObjectif();

        _session.Avancer(note == Note.Rate, carte with { Etat = resultat.NouvelEtat });
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
    private static string Extrait(Carte carte)
    {
        string texte = (carte.Contenu.Recto ?? carte.Contenu.Question ?? carte.Contenu.Enonce ?? "").ReplaceLineEndings(" ");
        return texte.Length <= 110 ? texte : texte[..110] + "…";
    }
}
