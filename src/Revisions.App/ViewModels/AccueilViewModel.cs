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

/// <summary>
/// Écran d'accueil : dit quoi faire aujourd'hui, sans jamais parler de « retard ».
/// </summary>
public partial class AccueilViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotRevisions _revisions;
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

    public AccueilViewModel(MainViewModel main, EtatApplication etat, DepotRevisions revisions, DepotProfil profil)
    {
        _main = main;
        _etat = etat;
        _revisions = revisions;

        var jour = etat.Jour;
        var reglages = etat.Reglages;
        var joursActifs = profil.JoursActifs();
        int faites = profil.CartesFaites(jour);
        int dues = revisions.NombreDeDues(jour);

        // ---- Plan du jour (plafond, nouvelles cartes, mode reprise) ----
        _plan = CalculPlanDuJour.Calculer(new ContexteJour(
            jour, joursActifs.Count > 0 ? joursActifs[^1] : null, profil.DebutReprise,
            dues, faites, profil.NouvellesVues(jour), reglages));
        if (_plan.DebutReprise != profil.DebutReprise) profil.DebutReprise = _plan.DebutReprise;

        int nouvelles = revisions.NouvellesCartes(_plan.MaxNouvelles).Count;
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
            PeutContinuer = dues > 0 || revisions.NouvellesCartes(1).Count > 0;
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
    }

    /// <summary>Lance la session du jour.</summary>
    [RelayCommand]
    private void Commencer()
    {
        var jour = _etat.Jour;
        var cartes = ConstructeurSession.Construire(
            _revisions.CartesDues(jour), _revisions.NouvellesCartes(_plan.MaxNouvelles), jour, _plan.MaxCartes, _plan.MaxNouvelles);
        _main.DemarrerSession(cartes, _plan.ModeReprise ? "reprise" : "quotidienne");
    }

    /// <summary>Après le plafond : 5 cartes de plus, seulement si l'étudiant le demande.</summary>
    [RelayCommand]
    private void EncoreCinq()
    {
        var jour = _etat.Jour;
        int nouvelles = _plan.ModeReprise ? 0 : 5;
        var cartes = ConstructeurSession.Construire(
            _revisions.CartesDues(jour), _revisions.NouvellesCartes(nouvelles), jour, 5, nouvelles);
        _main.DemarrerSession(cartes, "bonus");
    }

    [RelayCommand]
    private void AllerBibliotheque() => _main.AllerBibliotheque();
}
