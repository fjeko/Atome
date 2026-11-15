using Revisions.Core.Modeles;
using Revisions.Core.Planification;

namespace Revisions.Tests;

/// <summary>Algorithme FSRS-5 : valeurs calculées à la main à partir des formules officielles, et propriétés.</summary>
public class PlanificationTests
{
    private readonly Fsrs _fsrs = new();
    private static readonly DateOnly Jour = new(2026, 11, 16);

    [Fact]
    public void Retrievabilite_vaut_90_pourcent_quand_le_temps_ecoule_egale_la_stabilite()
    {
        Assert.Equal(0.9, Fsrs.Retrievabilite(10, 10), 10);
        Assert.Equal(1.0, Fsrs.Retrievabilite(0, 10), 10);
    }

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(10.0, 10)]
    [InlineData(0.2, 1)]        // jamais moins d'un jour
    [InlineData(99999.0, 3650)] // jamais plus de 10 ans
    public void Intervalle_egale_la_stabilite_a_90_pourcent(double stabilite, int attendu) =>
        Assert.Equal(attendu, Fsrs.Intervalle(stabilite, 0.9));

    [Fact]
    public void Retention_plus_basse_donne_un_intervalle_plus_long() =>
        Assert.True(Fsrs.Intervalle(10, 0.8) > Fsrs.Intervalle(10, 0.9));

    [Theory]
    [InlineData(Note.Rate, 0.40255, 7.1949)]
    [InlineData(Note.Difficile, 1.18385, 6.48831)]  // 7.1949 − e^0.5345 + 1
    [InlineData(Note.Bien, 3.173, 5.28243)]         // 7.1949 − e^1.069 + 1
    [InlineData(Note.Facile, 15.69105, 3.22450)]    // 7.1949 − e^1.6035 + 1
    public void Premiere_reponse_donne_la_stabilite_et_la_difficulte_initiales(Note note, double stabilite, double difficulte)
    {
        var etat = _fsrs.PremiereReponse(note);
        Assert.Equal(stabilite, etat.Stabilite, 5);
        Assert.Equal(difficulte, etat.Difficulte, 2);
    }

    [Fact]
    public void Bien_a_l_echeance_donne_la_valeur_de_la_formule()
    {
        // S = 3,173 ; D = 5,28243 ; t = 3 j → R = (1 + 19/81 × 3/3,173)^−0,5
        var avant = _fsrs.PremiereReponse(Note.Bien);
        var apres = _fsrs.ReponseSuivante(avant, 3, Note.Bien);

        double r = Math.Pow(1 + 19.0 / 81.0 * 3 / 3.173, -0.5);
        double attendu = 3.173 * (Math.Exp(1.54575) * (11 - avant.Difficulte) * Math.Pow(3.173, -0.1192) * (Math.Exp(1.01925 * (1 - r)) - 1) + 1);
        Assert.Equal(attendu, apres.Stabilite, 6);
        Assert.InRange(apres.Stabilite, 10, 13); // ordre de grandeur attendu : une dizaine de jours
    }

    [Fact]
    public void Plus_la_note_est_bonne_plus_la_stabilite_grandit()
    {
        var avant = new EtatMemoire(10, 5);
        double rate = _fsrs.ReponseSuivante(avant, 10, Note.Rate).Stabilite;
        double difficile = _fsrs.ReponseSuivante(avant, 10, Note.Difficile).Stabilite;
        double bien = _fsrs.ReponseSuivante(avant, 10, Note.Bien).Stabilite;
        double facile = _fsrs.ReponseSuivante(avant, 10, Note.Facile).Stabilite;

        Assert.True(rate < 10);
        Assert.True(10 < difficile && difficile < bien && bien < facile);
    }

    [Fact]
    public void Bonne_reponse_apres_une_longue_pause_est_davantage_recompensee()
    {
        var avant = new EtatMemoire(10, 5);
        Assert.True(_fsrs.ReponseSuivante(avant, 60, Note.Bien).Stabilite > _fsrs.ReponseSuivante(avant, 10, Note.Bien).Stabilite);
    }

    [Fact]
    public void Difficulte_reste_entre_1_et_10()
    {
        var etat = _fsrs.PremiereReponse(Note.Rate);
        for (int i = 0; i < 50; i++) etat = _fsrs.ReponseSuivante(etat, 1, Note.Rate);
        Assert.InRange(etat.Difficulte, 1, 10);

        etat = _fsrs.PremiereReponse(Note.Facile);
        for (int i = 0; i < 50; i++) etat = _fsrs.ReponseSuivante(etat, 30, Note.Facile);
        Assert.InRange(etat.Difficulte, 1, 10);
    }

    [Fact]
    public void Revision_le_meme_jour_utilise_la_formule_court_terme()
    {
        var apres = _fsrs.ReponseSuivante(new EtatMemoire(2, 5), 0, Note.Bien);
        Assert.Equal(2 * Math.Exp(0.51655 * 0.6621), apres.Stabilite, 6);
    }

    [Fact]
    public void Planificateur_place_l_echeance_et_compte_les_oublis()
    {
        var planificateur = new Planificateur(_fsrs);
        var nouvelle = new EtatCarte { CarteId = "c1" };

        var premier = planificateur.Appliquer(nouvelle, Note.Bien, Jour, 0.9);
        Assert.Equal(Jour.AddDays(3), premier.NouvelEtat.Echeance);   // S0(Bien) = 3,173 → 3 jours
        Assert.Null(premier.Retrievabilite);
        Assert.Equal(0, premier.NouvelEtat.Oublis);

        var second = planificateur.Appliquer(premier.NouvelEtat, Note.Rate, Jour.AddDays(3), 0.9);
        Assert.Equal(1, second.NouvelEtat.Oublis);
        Assert.Equal(2, second.NouvelEtat.Repetitions);
        Assert.Equal(Jour.AddDays(4), second.NouvelEtat.Echeance);    // un oubli → revue dès le lendemain
        Assert.NotNull(second.Retrievabilite);
    }
}
