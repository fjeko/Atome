using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Modeles;
using Revisions.Core.Sessions;
using Revisions.Core.Temps;

namespace Revisions.Tests;

/// <summary>Construction des sessions et règles du rythme irrégulier (R2, R3, R4).</summary>
public class SessionsTests
{
    private static readonly DateOnly Jour = new(2026, 11, 16);

    private static CarteAReviser Carte(string id, string matiere, double? stabilite = null, int joursDepuis = 0) =>
        new(new Carte(id, "s1", TypeCarte.Question, 1, new ContenuCarte { Recto = "?", Verso = "!" }),
            new EtatCarte
            {
                CarteId = id,
                Stabilite = stabilite,
                DifficulteFsrs = stabilite is null ? null : 5,
                DerniereRevision = stabilite is null ? null : Jour.AddDays(-joursDepuis),
                Echeance = stabilite is null ? null : Jour,
            },
            "pack", matiere, "Séance");

    private static ContexteJour Contexte(int dues, DateOnly? dernier, DateOnly? debutReprise = null, int faites = 0, int nouvellesVues = 0) =>
        new(Jour, dernier, debutReprise, dues, faites, nouvellesVues, new Reglages());

    // ---- Construction ----------------------------------------------------------------------

    [Fact]
    public void Les_cartes_les_plus_fragiles_passent_en_premier()
    {
        var dues = new[]
        {
            Carte("solide", "Maths", stabilite: 100, joursDepuis: 10),
            Carte("fragile", "Maths", stabilite: 2, joursDepuis: 30),
            Carte("moyenne", "Maths", stabilite: 10, joursDepuis: 10),
        };

        var session = ConstructeurSession.Construire(dues, [], Jour, 10, 10);

        Assert.Equal(["fragile", "moyenne", "solide"], session.Select(c => c.Carte.Id));
    }

    [Fact]
    public void Le_plafond_et_la_limite_de_nouvelles_sont_respectes()
    {
        var dues = Enumerable.Range(0, 30).Select(i => Carte($"d{i}", "Maths", 5, 5));
        var nouvelles = Enumerable.Range(0, 30).Select(i => Carte($"n{i}", "Maths")).ToList();

        var session = ConstructeurSession.Construire(dues, nouvelles, Jour, maxCartes: 35, maxNouvelles: 10);
        Assert.Equal(35, session.Count);
        Assert.Equal(5, session.Count(c => c.Etat.EstNouvelle)); // 30 dues + 5 places restantes

        session = ConstructeurSession.Construire([], nouvelles, Jour, maxCartes: 35, maxNouvelles: 10);
        Assert.Equal(10, session.Count);

        Assert.Empty(ConstructeurSession.Construire(dues, nouvelles, Jour, 0, 10));
    }

    [Fact]
    public void Les_matieres_sont_melangees()
    {
        var dues = new[]
        {
            Carte("m1", "Maths", 1, 9), Carte("m2", "Maths", 1, 8), Carte("m3", "Maths", 1, 7),
            Carte("c1", "CEJM", 1, 3), Carte("c2", "CEJM", 1, 2),
        };

        var session = ConstructeurSession.Construire(dues, [], Jour, 10, 0);

        Assert.Equal(["m1", "c1", "m2", "c2", "m3"], session.Select(c => c.Carte.Id));
    }

    [Fact]
    public void Les_nouvelles_cartes_sont_reparties_dans_la_session()
    {
        var dues = Enumerable.Range(0, 6).Select(i => Carte($"d{i}", "Maths", 5, 5));
        var nouvelles = new[] { Carte("n0", "Maths"), Carte("n1", "Maths") };

        var session = ConstructeurSession.Construire(dues, nouvelles, Jour, 20, 5);

        Assert.Equal(8, session.Count);
        Assert.False(session[0].Etat.EstNouvelle);
        Assert.False(session[^1].Etat.EstNouvelle);
        Assert.Equal(2, session.Count(c => c.Etat.EstNouvelle));
    }

    // ---- File de session -----------------------------------------------------------------

    [Fact]
    public void Une_carte_ratee_revient_une_seule_fois()
    {
        var session = new SessionEnCours(Enumerable.Range(0, 3).Select(i => Carte($"c{i}", "Maths")));

        session.Avancer(ratee: true);                 // c0 ratée → revient à la fin
        Assert.Equal(4, session.Total);
        session.Avancer(ratee: false);
        session.Avancer(ratee: false);
        Assert.Equal("c0", session.Courante!.Carte.Id);
        Assert.True(session.CouranteEstUnRetour);
        session.Avancer(ratee: true);                 // ratée de nouveau : ne revient plus

        Assert.True(session.EstTerminee);
        Assert.Equal(4, session.Faites);
    }

    // ---- Plan du jour --------------------------------------------------------------------

    [Fact]
    public void Jour_normal_applique_le_plafond_et_la_limite_de_nouvelles()
    {
        var plan = CalculPlanDuJour.Calculer(Contexte(dues: 100, dernier: Jour.AddDays(-1), faites: 15, nouvellesVues: 4));

        Assert.False(plan.ModeReprise);
        Assert.Equal(25, plan.MaxCartes);   // 40 − 15
        Assert.Equal(6, plan.MaxNouvelles); // 10 − 4
        Assert.False(plan.PlafondAtteint);
    }

    [Fact]
    public void Plafond_atteint_quand_tout_le_quota_est_fait() =>
        Assert.True(CalculPlanDuJour.Calculer(Contexte(100, Jour.AddDays(-1), faites: 40)).PlafondAtteint);

    [Fact]
    public void Premiere_utilisation_n_est_pas_une_reprise()
    {
        var plan = CalculPlanDuJour.Calculer(Contexte(dues: 0, dernier: null));
        Assert.False(plan.ModeReprise);
        Assert.False(plan.RetourApresPause);
    }

    [Fact]
    public void La_reprise_se_declenche_a_7_jours_pas_a_6()
    {
        Assert.False(CalculPlanDuJour.Calculer(Contexte(100, Jour.AddDays(-6))).ModeReprise);

        var plan = CalculPlanDuJour.Calculer(Contexte(100, Jour.AddDays(-7)));
        Assert.True(plan.ModeReprise);
        Assert.True(plan.RetourApresPause);
        Assert.Equal(Jour, plan.DebutReprise);
        Assert.Equal(10, plan.MaxCartes);
        Assert.Equal(0, plan.MaxNouvelles); // aucune nouvelle carte en reprise
    }

    [Fact]
    public void La_session_de_reprise_grandit_de_jour_en_jour()
    {
        // Reprise commencée il y a 1 puis 2 jours ; l'étudiant a révisé la veille.
        Assert.Equal(15, CalculPlanDuJour.Calculer(Contexte(100, Jour.AddDays(-1), debutReprise: Jour.AddDays(-1))).MaxCartes);
        Assert.Equal(20, CalculPlanDuJour.Calculer(Contexte(100, Jour.AddDays(-1), debutReprise: Jour.AddDays(-2))).MaxCartes);
    }

    [Fact]
    public void La_reprise_s_arrete_quand_le_retard_est_absorbe_ou_le_plafond_rejoint()
    {
        var absorbe = CalculPlanDuJour.Calculer(Contexte(dues: 8, dernier: Jour.AddDays(-1), debutReprise: Jour.AddDays(-1)));
        Assert.False(absorbe.ModeReprise);
        Assert.Null(absorbe.DebutReprise);

        var plafond = CalculPlanDuJour.Calculer(Contexte(dues: 500, dernier: Jour.AddDays(-1), debutReprise: Jour.AddDays(-6)));
        Assert.False(plafond.ModeReprise); // 10 + 5 × 6 = 40 = plafond
        Assert.Equal(40, plafond.MaxCartes);
    }

    [Fact]
    public void Retour_avec_peu_de_cartes_dues_reste_une_journee_normale()
    {
        var plan = CalculPlanDuJour.Calculer(Contexte(dues: 4, dernier: Jour.AddDays(-30)));
        Assert.False(plan.ModeReprise);
        Assert.True(plan.RetourApresPause);
        Assert.Equal(10, plan.MaxNouvelles);
    }

    // ---- Jour de révision ------------------------------------------------------------------

    [Theory]
    [InlineData(3, 59, 15)] // 3 h 59 : encore la veille
    [InlineData(4, 0, 16)]  // 4 h 00 : nouveau jour
    public void Le_jour_de_revision_commence_a_4_heures(int heure, int minute, int jourAttendu)
    {
        var horloge = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, heure, minute, 0, TimeSpan.Zero));
        horloge.SetLocalTimeZone(TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 11, jourAttendu), JourDeRevision.Aujourdhui(horloge));
    }
}
