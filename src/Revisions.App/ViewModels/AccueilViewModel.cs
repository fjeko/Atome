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
    private readonly EtatApplication _etat;
    private readonly Journee _journee;
    private readonly PlanDuJour _plan;

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

    /// <summary>De 1 à 3 séances à lire, choisies selon le planning (vide sans planning suivi).</summary>
    public IReadOnlyList<SeanceAuProgramme> AuProgramme { get; }
    public bool AfficherAuProgramme => AuProgramme.Count > 0;

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
        _etat = etat;
        _journee = selection.Charger(etat.Jour);

        var jour = etat.Jour;
        ChoisirMessage(profil, jour);
        var reglages = etat.Reglages;
        var joursActifs = profil.JoursActifs();
        int faites = profil.CartesFaites(jour);
        // Cartes dues, plus celles avancées pour préparer un devoir proche (planning).
        int dues = _journee.Dues().Count;

        // ---- Plan du jour (plafond, nouvelles cartes, mode reprise) ----
        _plan = CalculPlanDuJour.Calculer(new ContexteJour(
            jour, joursActifs.Count > 0 ? joursActifs[^1] : null, profil.DebutReprise,
            dues, faites, profil.NouvellesVues(jour), reglages));
        if (_plan.DebutReprise != profil.DebutReprise) profil.DebutReprise = _plan.DebutReprise;

        int nouvelles = _journee.NombreDeNouvelles(_plan.MaxNouvelles);
        int duesProposees = Math.Min(dues, _plan.MaxCartes);
        int aFaire = duesProposees + Math.Min(nouvelles, _plan.MaxCartes - duesProposees);
        int minutes = Math.Max(1, (int)Math.Round(aFaire * profil.SecondesParCarte() / 60));

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
            PeutContinuer = dues > 0 || _journee.NombreDeNouvelles(1) > 0;
        }
        else
        {
            Titre = faites > 0 ? "Tout est fait pour aujourd'hui" : "Rien à réviser aujourd'hui";
            Detail = faites > 0
                ? $"Tu as révisé {Texte.Pluriel(faites, "carte", "cartes")}. Tu peux lire une nouvelle séance si tu en as envie."
                : "Tu peux lire une séance dans la bibliothèque : ses cartes arriveront ensuite ici.";
            ProposerLecture = true;
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
        Echeances = _journee.Echeances(3).Select(Libelles.Echeance).ToList();
        AuProgramme = _journee.Suggestions(3)
            .Select(s => new SeanceAuProgramme(s.Seance.PackId, s.Seance.Seance, s.Seance.Matiere, s.Raison))
            .ToList();
    }

    /// <summary>Lance la session du jour.</summary>
    [RelayCommand]
    private void Commencer()
    {
        var jour = _etat.Jour;
        var cartes = ConstructeurSession.Construire(
            _journee.Dues(), _journee.Nouvelles(_plan.MaxNouvelles), jour, _plan.MaxCartes, _plan.MaxNouvelles);
        _main.DemarrerSession(cartes, _plan.ModeReprise ? "reprise" : "quotidienne");
    }

    /// <summary>Après le plafond : 5 cartes de plus, seulement si l'étudiant le demande.</summary>
    [RelayCommand]
    private void EncoreCinq()
    {
        var jour = _etat.Jour;
        int nouvelles = _plan.ModeReprise ? 0 : 5;
        var cartes = ConstructeurSession.Construire(
            _journee.Dues(), _journee.Nouvelles(nouvelles), jour, 5, nouvelles);
        _main.DemarrerSession(cartes, "bonus");
    }

    [RelayCommand]
    private void AllerBibliotheque() => _main.AllerBibliotheque();

    [RelayCommand]
    private void AllerPlanning() => _main.AllerPlanning();

    /// <summary>Ouvre une séance du bloc « Au programme ».</summary>
    [RelayCommand]
    private void Lire(SeanceAuProgramme seance) => _main.OuvrirLecture(seance.PackId);
}
