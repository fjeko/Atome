using Revisions.Core.Gamification;
using Revisions.Core.Modeles;

namespace Revisions.Tests;

/// <summary>XP, niveaux et série hebdomadaire (G1, G2, R1).</summary>
public class GamificationTests
{
    [Theory]
    [InlineData(Note.Rate, 2)]
    [InlineData(Note.Difficile, 12)] // bonus pour la bonne réponse obtenue avec effort
    [InlineData(Note.Bien, 10)]
    [InlineData(Note.Facile, 8)]
    public void Xp_des_cartes_dues(Note note, int attendu) =>
        Assert.Equal(attendu, CalculXp.PourReponse(note, etaitDue: true, etaitNouvelle: false, retourDansLaSession: false));

    [Fact]
    public void Xp_des_autres_cas()
    {
        Assert.Equal(5, CalculXp.PourReponse(Note.Bien, false, etaitNouvelle: true, false));
        Assert.Equal(0, CalculXp.PourReponse(Note.Bien, etaitDue: false, false, false));       // révision en avance
        Assert.Equal(0, CalculXp.PourReponse(Note.Bien, true, false, retourDansLaSession: true));
    }

    [Fact]
    public void L_xp_n_est_jamais_negative()
    {
        foreach (Note note in Enum.GetValues<Note>())
        foreach (bool due in new[] { true, false })
        foreach (bool nouvelle in new[] { true, false })
        foreach (bool retour in new[] { true, false })
            Assert.True(CalculXp.PourReponse(note, due, nouvelle, retour) >= 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(5500, 10)]
    public void Niveaux(int xp, int niveau) => Assert.Equal(niveau, Revisions.Core.Gamification.Niveaux.NiveauPour(xp));

    [Fact]
    public void Progression_vers_le_niveau_suivant()
    {
        Assert.Equal(0.5, Revisions.Core.Gamification.Niveaux.Progression(200), 5); // entre 100 et 300
        Assert.Equal(0.0, Revisions.Core.Gamification.Niveaux.Progression(300), 5);
    }

    // Lundi 16 novembre 2026.
    private static readonly DateOnly Lundi = new(2026, 11, 16);

    [Fact]
    public void Serie_de_semaines_consecutives()
    {
        var jours = new[] { Lundi.AddDays(2), Lundi.AddDays(-3), Lundi.AddDays(-10) }; // 3 semaines de suite
        var serie = SerieHebdomadaire.Calculer(jours, Lundi.AddDays(4));

        Assert.Equal(3, serie.Actuelle);
        Assert.Equal(3, serie.Meilleure);
        Assert.Equal(3, serie.ActivesSurQuatre);
    }

    [Fact]
    public void La_semaine_en_cours_encore_vide_ne_casse_pas_la_serie()
    {
        var jours = new[] { Lundi.AddDays(-3), Lundi.AddDays(-10) };
        Assert.Equal(2, SerieHebdomadaire.Calculer(jours, Lundi).Actuelle);
    }

    [Fact]
    public void Un_trou_d_une_semaine_remet_la_serie_a_zero_mais_garde_la_meilleure()
    {
        var jours = new[] { Lundi.AddDays(-14), Lundi.AddDays(-21), Lundi.AddDays(-28) };
        var serie = SerieHebdomadaire.Calculer(jours, Lundi);

        Assert.Equal(0, serie.Actuelle);
        Assert.Equal(3, serie.Meilleure);
        Assert.Equal(2, serie.ActivesSurQuatre); // les semaines −2 et −3 comptent parmi les 4 dernières
    }

    [Fact]
    public void La_serie_traverse_le_changement_d_annee()
    {
        // Mercredi 30 décembre 2026 et lundi 4 janvier 2027 : deux semaines qui se suivent.
        var jours = new[] { new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 4) };
        Assert.Equal(2, SerieHebdomadaire.Calculer(jours, new DateOnly(2027, 1, 5)).Actuelle);
    }

    [Fact]
    public void Sans_activite_tout_vaut_zero() =>
        Assert.Equal(new Serie(0, 0, 0), SerieHebdomadaire.Calculer([], Lundi));
}
