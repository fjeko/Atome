using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Collections;
using Revisions.Core.Gamification;
using Revisions.Core.Mascotte;
using Revisions.Core.Planning;
using Revisions.Core.Sessions;
using Revisions.Core.Temps;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un jour de la semaine sur l'accueil (pastille pleine s'il y a eu une révision).</summary>
public sealed record JourDeSemaine(string Lettre, bool Actif, bool EstAujourdhui, string Nom = "")
{
    /// <summary>Lu par le lecteur d'écran : « mercredi, aujourd'hui, révisé ».</summary>
    public string NomAccessible => $"{Nom}{(EstAujourdhui ? ", aujourd'hui" : "")}, {(Actif ? "révisé" : "pas de révision")}";
}

/// <summary>Une séance proposée à la lecture dans le bloc « Au programme ».</summary>
public sealed record SeanceAuProgramme(string PackId, string Seance, string Matiere, string Raison);

/// <summary>
/// Écran d'accueil : dit quoi faire aujourd'hui, sans jamais parler de « retard ».
/// </summary>
public partial class AccueilViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly JourneeDeRevision _journee;
    private readonly PlanDuJour _plan;
    private readonly SessionComposee _duJour;
    private readonly SessionComposee _bonus;

    public string Titre { get; }
    public string Detail { get; }
    public bool PeutCommencer { get; }
    /// <summary>Le quota du jour est rempli : on propose seulement un petit supplément.</summary>
    public bool PeutContinuer { get; }
    /// <summary>Rien à réviser : on propose d'aller lire un cours.</summary>
    public bool ProposerLecture { get; }

    public string Objectif { get; }
    public bool ObjectifAtteint { get; }

    public int Niveau { get; }
    public string Xp { get; }
    public double ProgressionNiveau { get; }

    public string SerieTexte { get; }
    public string SerieDetail { get; }
    public IReadOnlyList<JourDeSemaine> Semaine { get; }

    /// <summary>Prochaines échéances du planning, sur un ton neutre (vide sans planning suivi).</summary>
    public IReadOnlyList<string> Echeances { get; }
    public bool AfficherEcheances => Echeances.Count > 0;

    /// <summary>
    /// De 1 à 3 séances à lire : celles qui débloquent de nouvelles cartes s'il en manque,
    /// sinon celles du planning (vide sans planning suivi).
    /// </summary>
    public IReadOnlyList<SeanceAuProgramme> AuProgramme { get; }
    public bool AfficherAuProgramme => AuProgramme.Count > 0;
    /// <summary>« À lire pour débloquer de nouvelles cartes » ou « Au programme ».</summary>
    public string TitreAuProgramme { get; }
    public string DetailAuProgramme { get; }

    // ---- Données d'affichage du relooking (docs/design-atome.md) ----

    /// <summary>Mode reprise après une pause : un bandeau doux l'explique.</summary>
    public bool ModeReprise { get; }
    /// <summary>Objectif du jour : part accomplie (0 à 1) pour l'anneau, et « 3 / 5 » au centre.</summary>
    public double ObjectifProgression { get; }
    public string ObjectifCourt { get; }
    /// <summary>Matières de la session du jour, avec leur nombre de cartes (tuiles « Au menu »).</summary>
    public IReadOnlyList<PartMatiere> MatieresDuJour { get; }
    public bool AfficherMatieresDuJour => MatieresDuJour.Count > 0;
    /// <summary>Des cours existent, mais toutes leurs collections sont masquées (Réglages → Mes collections).</summary>
    public bool CollectionsToutesMasquees { get; }

    // ---- Mascotte (banque de messages : Core/Mascotte/messages-mascotte.json) ----
    // Le message est tiré une fois par contexte et par lancement : revenir sur l'accueil ne le change pas.
    private static readonly Dictionary<string, MessageMascotte> MessagesDuLancement = [];
    /// <summary>L'application avait-elle déjà été ouverte aujourd'hui avant ce lancement ? (lu une seule fois)</summary>
    private static bool? _dejaOuvertAuLancement;

    /// <summary>Phrase dite par la mascotte.</summary>
    public string MessageMascotte { get; private set; } = "";
    /// <summary>Humeur de la mascotte, qui va avec la phrase.</summary>
    public Humeur HumeurMascotte { get; private set; }

    /// <summary>Choisit ce que dit la mascotte, d'après la journée (voir Core/Mascotte/ContexteAccueil).</summary>
    private void ChoisirMessage(DepotProfil profil, DateOnly jour, SituationAccueil situation)
    {
        var (contexte, valeurs) = ContexteAccueil.Choisir(situation);
        if (!MessagesDuLancement.TryGetValue(contexte, out var message))
            MessagesDuLancement[contexte] = message = BanqueMessages.Tirer(contexte, valeurs: valeurs);
        MessageMascotte = message.Texte;
        HumeurMascotte = message.Humeur;
    }

    /// <summary>« le DV1 (Mathématiques) » : code du devoir et début du nom de la matière.</summary>
    private static string NomCourtDevoir(Devoir d)
    {
        string matiere = d.Matiere;
        foreach (string coupure in new[] { " – ", " - ", " pour ", " : " })
            if (matiere.IndexOf(coupure, StringComparison.Ordinal) is > 0 and var i) matiere = matiere[..i];
        return $"le {d.Code} ({matiere})";
    }

    public AccueilViewModel(MainViewModel main, EtatApplication etat, SelectionDuJour selection, DepotProfil profil, DepotCollections collections)
    {
        _main = main;
        var jour = etat.Jour;
        var reglages = etat.Reglages;
        var joursActifs = profil.JoursActifs();

        // ---- Plan du jour (plafond, nouvelles cartes, mode reprise) et session du jour ----
        _journee = JourneeDeRevision.Preparer(etat, selection, profil);
        _plan = _journee.Plan;
        int faites = _journee.Faites;
        _duJour = _journee.Composer(DemandeSession.DuJour);
        // « Encore 5 cartes » : seulement si l'étudiant le demande, une fois le quota du jour rempli.
        _bonus = _journee.Journee.Composer(DemandeSession.DuJour, 5, _plan.ModeReprise ? 0 : 5);

        int aFaire = _duJour.Cartes.Count;
        int minutes = EstimationDuree.Minutes(aFaire, _journee.SecondesParCarte);

        if (aFaire > 0)
        {
            PeutCommencer = true;
            string cartes = aFaire == 1 ? "1 carte" : $"{aFaire} cartes";
            if (_plan.ModeReprise)
            {
                Titre = _plan.RetourApresPause ? "Content de te revoir !" : "On continue en douceur";
                Detail = $"Aujourd'hui : {cartes}, environ {minutes} min. On reprend tranquillement, sans nouvelle carte.";
            }
            else
            {
                Titre = _plan.RetourApresPause ? "Content de te revoir !" : $"Aujourd'hui : {cartes}";
                Detail = (_plan.RetourApresPause ? $"Aujourd'hui : {cartes}, environ {minutes} min." : $"Environ {minutes} min.")
                         + (faites > 0 ? $" Déjà {Texte.Pluriel(faites, "carte faite", "cartes faites")} aujourd'hui." : "");
            }
        }
        else if (_plan.PlafondAtteint)
        {
            Titre = "C'est bien pour aujourd'hui !";
            Detail = $"Tu as révisé {faites} cartes. Le reste attendra demain.";
            PeutContinuer = _bonus.Cartes.Count > 0;
        }
        else
        {
            Titre = faites > 0 ? "Tout est fait pour aujourd'hui" : "Rien à réviser aujourd'hui";
            Detail = faites > 0
                ? $"Tu as révisé {Texte.Pluriel(faites, "carte", "cartes")}. Tu peux lire une nouvelle séance si tu en as envie."
                : "Tu peux lire une séance : ses nouvelles cartes arriveront ensuite ici.";
            ProposerLecture = true;
            // Limite de nouvelles cartes du jour atteinte : on peut quand même continuer, par paquets de 5, si on en a envie.
            PeutContinuer = !_plan.ModeReprise && _bonus.Cartes.Count > 0;
        }

        // ---- Objectif minimal ----
        ObjectifAtteint = faites >= reglages.ObjectifMinimal;
        Objectif = ObjectifAtteint
            ? $"Objectif du jour atteint ({faites} / {reglages.ObjectifMinimal})"
            : $"Objectif du jour : {faites} / {reglages.ObjectifMinimal} cartes";

        ObjectifProgression = Math.Min(1, (double)faites / Math.Max(1, reglages.ObjectifMinimal));
        ObjectifCourt = $"{Math.Min(faites, reglages.ObjectifMinimal)} / {reglages.ObjectifMinimal}";
        ModeReprise = _plan.ModeReprise;
        var matiereDuPack = _journee.Journee.Seances.GroupBy(s => s.PackId).ToDictionary(g => g.Key, g => g.First().Matiere);
        MatieresDuJour = Repartition.ParMatiere(_duJour.Cartes.Select(c => matiereDuPack.GetValueOrDefault(c.PackId, "")));
        // Rien à proposer alors que des cours existent : peut-être que toutes les collections sont masquées.
        CollectionsToutesMasquees = aFaire == 0 && !_plan.PlafondAtteint && Collections.ToutesMasquees(collections.Lister());
        if (CollectionsToutesMasquees)
        {
            Titre = "Tes cours sont masqués";
            Detail = "Toutes tes collections sont masquées : rien ne peut être proposé. Rouvre-en une dans les Réglages (ta progression est gardée).";
            ProposerLecture = false;
            PeutContinuer = false;
        }
        // ---- XP et niveau ----
        int xp = profil.XpTotal();
        Niveau = Niveaux.NiveauPour(xp);
        ProgressionNiveau = Niveaux.Progression(xp);
        Xp = $"{xp} XP  ·  niveau suivant à {Niveaux.XpPourNiveau(Niveau + 1)}";

        // ---- Série et semaine ----
        var serie = SerieHebdomadaire.Calculer(joursActifs, jour);
        SerieTexte = serie.Actuelle switch
        {
            0 => "Pas de série en cours",
            1 => "Série : 1 semaine",
            _ => $"Série : {serie.Actuelle} semaines",
        };
        SerieDetail = Texte.Pluriel(serie.ActivesSurQuatre, "semaine active", "semaines actives") + " sur les 4 dernières"
                      + (serie.Meilleure > 1 ? $"  ·  meilleure série : {serie.Meilleure}" : "");

        var lundi = JourDeRevision.LundiDe(jour);
        var actifs = joursActifs.ToHashSet();
        Semaine = Enumerable.Range(0, 7)
            .Select(i => lundi.AddDays(i))
            .Select(j => new JourDeSemaine("LMMJVSD"[j.DayNumber - lundi.DayNumber].ToString(), actifs.Contains(j), j == jour,
                j.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))))
            .ToList();

        // ---- Planning du CNED : prochaines échéances et séances à lire (seulement s'il est suivi) ----
        var devoirs = _journee.Journee.Echeances(3);
        Echeances = devoirs.Select(Libelles.Echeance).ToList();

        // Pas assez de nouvelles cartes parce que leurs cours n'ont pas été lus : on propose 1 à 3 séances à lire.
        bool debloquer = _duJour.DebloquerUtile || (aFaire == 0 && !_plan.PlafondAtteint && _bonus.DebloquerUtile);
        var suggestions = debloquer ? _journee.Journee.SeancesADebloquer(3) : _journee.Journee.Suggestions(3);
        AuProgramme = suggestions
            .Select(s => new SeanceAuProgramme(s.Seance.PackId, s.Seance.Seance, s.Seance.Matiere, s.Raison))
            .ToList();
        TitreAuProgramme = debloquer ? "À lire pour débloquer de nouvelles cartes" : "Au programme";
        DetailAuProgramme = debloquer
            ? "Les nouvelles cartes arrivent après la lecture de leur cours. Ouvre une de ces séances quand tu veux."
            : "Des séances à lire quand tu veux : leurs cartes arriveront ensuite.";

        // ---- Mascotte : ce qu'elle dit dépend de la journée ----
        _dejaOuvertAuLancement ??= profil.DernierJourOuverture == jour;
        profil.DernierJourOuverture = jour;
        var avant = joursActifs.Where(j => j < jour).ToList();
        var prochain = devoirs.FirstOrDefault();
        ChoisirMessage(profil, jour, new SituationAccueil(
            RetourApresPause: _plan.RetourApresPause,
            JoursAbsence: avant.Count > 0 ? jour.DayNumber - avant.Max().DayNumber : 0,
            PlafondAtteint: aFaire == 0 && _plan.PlafondAtteint,
            RienAFaire: aFaire == 0 && !_plan.PlafondAtteint,
            DejaOuvertAujourdhui: _dejaOuvertAuLancement.Value,
            PremiereOuvertureDeLaSemaine: !avant.Any(j => j >= lundi),
            SerieSemaines: serie.Actuelle,
            ProchainDevoir: prochain is null ? null : NomCourtDevoir(prochain),
            JoursAvantDevoir: prochain is null ? -1 : prochain.DateLimite.DayNumber - jour.DayNumber));
    }

    /// <summary>Lance la session du jour.</summary>
    [RelayCommand]
    private void Commencer() => _main.DemarrerSession(_journee.Journee.Charger(_duJour), _journee.Mode(TypeSession.DuJour));

    /// <summary>Après le plafond : 5 cartes de plus, seulement si l'étudiant le demande.</summary>
    [RelayCommand]
    private void EncoreCinq() => _main.DemarrerSession(_journee.Journee.Charger(_bonus), "bonus");

    /// <summary>Ouvre l'écran « Autre révision » (une matière, mes points faibles, ce que je viens de lire, un devoir).</summary>
    [RelayCommand]
    private void AutreRevision() => _main.AllerChoixRevision();

    [RelayCommand]
    private void AllerBibliotheque() => _main.AllerBibliotheque();

    [RelayCommand]
    private void AllerPlanning() => _main.AllerPlanning();
/// <summary>Collections toutes masquées : ouvre les Réglages, où chaque collection a son interrupteur.</summary>    [RelayCommand]    private void AllerReglages() => _main.AllerReglages();

    /// <summary>Ouvre une séance du bloc « Au programme ».</summary>
    [RelayCommand]
    private void Lire(SeanceAuProgramme seance) => _main.OuvrirLecture(seance.PackId);
}
