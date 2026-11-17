using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.Sessions;
using Revisions.Core.Temps;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un jour de la semaine sur l'accueil (pastille pleine s'il y a eu une révision).</summary>
public sealed record JourDeSemaine(string Lettre, bool Actif, bool EstAujourdhui);

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

    // ---- Mascotte : un message par ouverture de l'application (pas à chaque retour sur l'accueil) ----
    private static string? _messageOuverture;
    private static string _expressionOuverture = "content";

    private static readonly (string Message, string Expression)[] PremieresOuvertures =
    {
        ("Bonjour ! Prêt à apprendre ?", "content"),
        ("Coucou ! Content que t'aies ouvert l'application !", "joie"),
        ("Salut ! On s'y met tranquillement ?", "clin"),
        ("Hello ! Un petit coup de cerveau aujourd'hui ?", "content"),
        ("Te revoilà ! On fait pousser l'arbre ?", "clin"),
    };

    private static readonly (string Message, string Expression)[] OuverturesDuMemeJour =
    {
        ("Ça faisait longtemps ! Non, j'déconne. On s'y remet ?", "malin"),
        ("Déjà de retour ? J'adore. On s'y remet ?", "joie"),
        ("Tu m'as manqué ! Bon, depuis tout à l'heure. On s'y remet ?", "malin"),
    };

    /// <summary>Phrases pour rire (on se moque gentiment des applis qui harcèlent leurs utilisateurs). Une ouverture sur cinq.</summary>
    private static readonly (string Message, string Expression)[] Blagues =
    {
        ("Si tu veux que j'arrête de te montrer des pubs, souscris à mon abonnement à seulement 199,99 € hebdomadaires !", "malin"),
        ("Ton arbre dépérit si tu ne révises pas… Je rigole, il est en plastique. Mais révise quand même.", "malin"),
        ("Pas de notification menaçante ici. Juste moi, qui te regarde fixement. Sans clignoter.", "clin"),
        ("Passe en version Premium+ pour débloquer… exactement les mêmes cartes, mais plus chères !", "malin"),
        ("Une chouette vengeresse m'a proposé un partenariat. J'ai refusé. Tu as de la chance.", "clin"),
    };

    /// <summary>Phrase dite par la mascotte (la même tant que l'application reste ouverte).</summary>
    public string MessageMascotte => _messageOuverture!;
    /// <summary>Expression de la mascotte : content, clin, joie ou malin.</summary>
    public string ExpressionMascotte => _expressionOuverture;

    /// <summary>Choisit le message à la première visite de l'accueil après le lancement.</summary>
    private static void ChoisirMessage(DepotProfil profil, DateOnly jour)
    {
        if (_messageOuverture is not null) return;
        bool dejaOuvert = profil.DernierJourOuverture == jour;
        profil.DernierJourOuverture = jour;
        var choix = Random.Shared.Next(5) == 0 ? Blagues : dejaOuvert ? OuverturesDuMemeJour : PremieresOuvertures;
        (_messageOuverture, _expressionOuverture) = choix[Random.Shared.Next(choix.Length)];
    }

    public AccueilViewModel(MainViewModel main, EtatApplication etat, SelectionDuJour selection, DepotProfil profil)
    {
        _main = main;
        var jour = etat.Jour;
        ChoisirMessage(profil, jour);
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
            .Select(j => new JourDeSemaine("LMMJVSD"[j.DayNumber - lundi.DayNumber].ToString(), actifs.Contains(j), j == jour))
            .ToList();

        // ---- Planning du CNED : prochaines échéances et séances à lire (seulement s'il est suivi) ----
        Echeances = _journee.Journee.Echeances(3).Select(Libelles.Echeance).ToList();

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

    /// <summary>Ouvre une séance du bloc « Au programme ».</summary>
    [RelayCommand]
    private void Lire(SeanceAuProgramme seance) => _main.OuvrirLecture(seance.PackId);
}
