using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>
/// La journée de révision prête à l'emploi : données de composition, limites du jour (plafond, nouvelles cartes,
/// mode reprise) et vitesse moyenne de réponse. Partagée par l'accueil et l'écran « Autre révision ».
/// </summary>
public sealed class JourneeDeRevision
{
    public Journee Journee { get; }
    public PlanDuJour Plan { get; }
    /// <summary>Secondes par carte (médiane de l'historique, sinon environ 20 s).</summary>
    public double SecondesParCarte { get; }
    /// <summary>Cartes déjà faites aujourd'hui.</summary>
    public int Faites { get; }

    private JourneeDeRevision(Journee journee, PlanDuJour plan, double secondes, int faites)
    {
        Journee = journee;
        Plan = plan;
        SecondesParCarte = secondes;
        Faites = faites;
    }

    /// <summary>Lit la journée et calcule le plan du jour (et mémorise le début d'un mode reprise).</summary>
    public static JourneeDeRevision Preparer(EtatApplication etat, SelectionDuJour selection, DepotProfil profil)
    {
        var jour = etat.Jour;
        var journee = selection.Charger(jour, etat.Reglages.NouvellesNonLues);
        var joursActifs = profil.JoursActifs();
        int faites = profil.CartesFaites(jour);
        var plan = CalculPlanDuJour.Calculer(new ContexteJour(
            jour, joursActifs.Count > 0 ? joursActifs[^1] : null, profil.DebutReprise,
            journee.NombreDeDues(), faites, profil.NouvellesVues(jour), etat.Reglages));
        if (plan.DebutReprise != profil.DebutReprise) profil.DebutReprise = plan.DebutReprise;
        return new JourneeDeRevision(journee, plan, profil.SecondesParCarte(), faites);
    }

    /// <summary>Compose une session dans les limites du jour.</summary>
    public SessionComposee Composer(DemandeSession demande) => Journee.Composer(demande, Plan.MaxCartes, Plan.MaxNouvelles);

    /// <summary>« 12 cartes · environ 4 min ».</summary>
    public string Resume(SessionComposee session) => EstimationDuree.Texte(session.Cartes.Count, SecondesParCarte);

    /// <summary>Nom du mode enregistré avec la session (statistiques).</summary>
    public string Mode(TypeSession type) => type switch
    {
        TypeSession.Perimetre => "matiere",
        TypeSession.PointsFaibles => "points_faibles",
        TypeSession.LectureRecente => "lecture_recente",
        TypeSession.Devoir => "devoir",
        _ => Plan.ModeReprise ? "reprise" : "quotidienne",
    };
}
