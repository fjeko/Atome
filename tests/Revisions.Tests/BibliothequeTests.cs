using Revisions.Core.Bibliotheque;

namespace Revisions.Tests;

/// <summary>Regroupement des séances en niveaux pour la bibliothèque en tuiles.</summary>
public class BibliothequeTests
{
    private static ResumeSeance Seance(string bloc, string matiere, string partie, string titre, int? ordre = null,
        string? sequence = null, int cartes = 10, int dues = 0, int maitrisees = 0, int nouvelles = 0) =>
        new()
        {
            PackId = titre, Bloc = bloc, Matiere = matiere, Partie = partie, Sequence = sequence, Seance = titre, Ordre = ordre,
            NbCartes = cartes, NbDues = dues, NbMaitrisees = maitrisees, NbNouvelles = nouvelles,
        };

    private const string Bloc1 = "Bloc 1 – Support et mise à disposition de services informatiques";

    // ---- Découpage du champ « partie » -----------------------------------------------------

    [Theory]
    [InlineData("B1.1 – SQ1 Explorer le patrimoine informatique", "B1.1", "SQ1 Explorer le patrimoine informatique")]
    [InlineData("B1.2 Partie I – SQ1 Corrections et évolutions", "B1.2 Partie I", "SQ1 Corrections et évolutions")]
    [InlineData("B1.2 Partie II – SQ3 Rechercher l'origine", "B1.2 Partie II", "SQ3 Rechercher l'origine")]
    [InlineData("B2.1 SQ1 – Modéliser et maquetter", "B2.1", "SQ1 – Modéliser et maquetter")]
    [InlineData("B3.1 – SQ1 – Cybermenaces", "B3.1", "SQ1 – Cybermenaces")]
    public void La_competence_est_separee_du_reste_de_la_partie(string partie, string competence, string reste) =>
        Assert.Equal((competence, reste), ArbreBibliotheque.DecouperPartie(partie));

    [Theory]
    [InlineData("SQ1 – Calcul matriciel")]
    [InlineData("Séquence 3 (Droit) – La formation des contrats")]
    [InlineData("Debating Education")]
    [InlineData("Boîte à outils")]
    public void Une_partie_sans_competence_reste_entiere(string partie) =>
        Assert.Equal((null, partie), ArbreBibliotheque.DecouperPartie(partie));

    // ---- Niveaux ---------------------------------------------------------------------------

    [Fact]
    public void Bloc_avec_competences_donne_matiere_puis_competence_puis_sequence_puis_seances()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 1", Bloc1, "B1.2 Partie I – SQ1 Corrections", "S1"),
            Seance("Bloc 1", Bloc1, "B1.2 Partie I – SQ1 Corrections", "S2"),
            Seance("Bloc 1", Bloc1, "B1.2 Partie I – SQ2 Créer une application", "S1 bis"),
            Seance("Bloc 1", Bloc1, "B1.1 – SQ1 Explorer", "S1 ter"),
        ]);

        var matiere = Assert.Single(racine.Enfants);
        Assert.Equal("Support et mise à disposition de services informatiques", matiere.Titre); // bloc non répété
        Assert.Equal("Bloc 1", matiere.Etiquette);
        Assert.Equal("Bloc 1", matiere.TitreCourt);
        Assert.Empty(matiere.Seances);

        Assert.Equal(["B1.1", "B1.2 Partie I"], matiere.Enfants.Select(e => e.Titre));
        var competence = matiere.Enfants[1];
        Assert.Equal(["SQ1 Corrections", "SQ2 Créer une application"], competence.Enfants.Select(e => e.Titre));
        Assert.Equal("SQ1", competence.Enfants[0].TitreCourt);

        var sequence = competence.Enfants[0];
        Assert.Empty(sequence.Enfants);
        Assert.Equal(["S1", "S2"], sequence.Seances.Select(s => s.Seance));
    }

    [Fact]
    public void Un_niveau_absent_est_saute()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 6", "Mathématiques pour l'informatique", "SQ1 – Calcul matriciel", "S1"),
            Seance("Bloc 5", "Anglais", "Debating Education", "L01"),
        ]);

        // Pas de compétence « Bx.y » : la matière mène directement aux séquences, puis aux séances.
        var maths = racine.Enfants.Single(e => e.Titre.StartsWith("Math"));
        Assert.Equal("Bloc 6", maths.Etiquette);
        Assert.Equal("Mathématiques pour l'informatique", maths.TitreCourt);
        var sequence = Assert.Single(maths.Enfants);
        Assert.Equal("SQ1 – Calcul matriciel", sequence.Titre);
        Assert.Equal("SQ1", sequence.TitreCourt);
        Assert.Empty(sequence.Enfants);
        Assert.Single(sequence.Seances);
    }

    [Fact]
    public void La_sequence_du_pack_ajoute_un_niveau_quand_elle_est_renseignee()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 9", "Matière", "Partie A", "S1", sequence: "Séquence 1"),
            Seance("Bloc 9", "Matière", "Partie A", "S2", sequence: "Séquence 2"),
            Seance("Bloc 9", "Matière", "Partie A", "Synthèse"),
        ]);

        var partie = racine.Enfants[0].Enfants[0];
        Assert.Equal(["Séquence 1", "Séquence 2"], partie.Enfants.Select(e => e.Titre));
        // Une séance sans séquence reste directement dans la partie : le niveau mélange tuiles et séances.
        Assert.Equal("Synthèse", Assert.Single(partie.Seances).Seance);
    }

    [Fact]
    public void Deux_matieres_d_un_meme_bloc_restent_separees_et_rangees_par_bloc()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 7", "CEJM", "Séquence 1", "S1"),
            Seance("Bloc 6", "Mathématiques pour l'informatique", "SQ1", "S1"),
            Seance("Bloc 6", "Algorithmique appliquée", "SQ1", "S1"),
            Seance("Bloc 1", Bloc1, "B1.1 – SQ1", "S1"),
        ]);

        Assert.Equal(["Bloc 1", "Bloc 6", "Bloc 6", "Bloc 7"], racine.Enfants.Select(e => e.Etiquette));
        Assert.Equal("Algorithmique appliquée", racine.Enfants[1].Titre);
    }

    // ---- Tri -------------------------------------------------------------------------------

    [Fact]
    public void Le_tri_est_naturel_sequence_2_avant_sequence_10()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 7", "CEJM", "Séquence 10 (Économie)", "S1"),
            Seance("Bloc 7", "CEJM", "Séquence 2 (Économie)", "S1"),
            Seance("Bloc 7", "CEJM", "Séquence 1 (Économie)", "S1"),
            Seance("Bloc 7", "CEJM", "Méthodologie", "Fiche"),
        ]);

        Assert.Equal(
            ["Méthodologie", "Séquence 1 (Économie)", "Séquence 2 (Économie)", "Séquence 10 (Économie)"],
            racine.Enfants[0].Enfants.Select(e => e.Titre));
    }

    [Fact]
    public void Les_seances_sont_triees_par_ordre_puis_par_titre()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("B", "M", "P", "S10 – Fin", ordre: 10),
            Seance("B", "M", "P", "Synthèse"),            // sans ordre : à la fin
            Seance("B", "M", "P", "S2 – Suite", ordre: 2),
            Seance("B", "M", "P", "S1 – Début", ordre: 1),
        ]);

        Assert.Equal(["S1 – Début", "S2 – Suite", "S10 – Fin", "Synthèse"],
            racine.Enfants[0].Enfants[0].Seances.Select(s => s.Seance));
    }

    // ---- Compteurs et chemins ----------------------------------------------------------------

    [Fact]
    public void Les_compteurs_d_une_tuile_additionnent_tout_ce_qu_elle_contient()
    {
        var racine = ArbreBibliotheque.Construire(
        [
            Seance("Bloc 1", Bloc1, "B1.1 – SQ1", "S1", cartes: 10, dues: 3, maitrisees: 5, nouvelles: 2),
            Seance("Bloc 1", Bloc1, "B1.1 – SQ2", "S1 bis", cartes: 20, dues: 1, maitrisees: 10, nouvelles: 4),
            Seance("Bloc 1", Bloc1, "B1.2 Partie I – SQ1", "S1 ter", cartes: 30, dues: 0, maitrisees: 0, nouvelles: 30),
        ]);

        var matiere = racine.Enfants[0];
        Assert.Equal((3, 60, 4, 15, 36), (matiere.NbSeances, matiere.NbCartes, matiere.NbDues, matiere.NbMaitrisees, matiere.NbNouvelles));
        Assert.Equal(0.25, matiere.Maitrise, 5);

        var b11 = matiere.Enfants[0];
        Assert.Equal((2, 30, 4), (b11.NbSeances, b11.NbCartes, b11.NbDues));
        Assert.Equal(60, racine.NbCartes);
    }

    [Fact]
    public void Une_bibliotheque_vide_donne_une_racine_sans_tuile()
    {
        var racine = ArbreBibliotheque.Construire([]);
        Assert.Empty(racine.Enfants);
        Assert.Equal(0, racine.Maitrise);
    }

    [Fact]
    public void Suivre_un_chemin_s_arrete_au_dernier_niveau_qui_existe()
    {
        var racine = ArbreBibliotheque.Construire([Seance("Bloc 1", Bloc1, "B1.1 – SQ1 Explorer", "S1")]);
        string cleMatiere = racine.Enfants[0].Cle;

        var complet = ArbreBibliotheque.Suivre(racine, [cleMatiere, "B1.1", "SQ1 Explorer"]);
        Assert.Equal(["Bloc 1", "B1.1", "SQ1"], complet.Select(n => n.TitreCourt));

        // Le dernier niveau a disparu (pack retiré) : on reste sur son parent.
        var partiel = ArbreBibliotheque.Suivre(racine, [cleMatiere, "B1.1", "n'existe plus"]);
        Assert.Equal(2, partiel.Count);
        Assert.Empty(ArbreBibliotheque.Suivre(racine, ["inconnu"]));
    }

    [Fact]
    public void Construire_un_arbre_de_plusieurs_centaines_de_seances_reste_instantane()
    {
        // 2 000 séances réparties sur 5 matières × 8 compétences × 10 séquences.
        var seances = Enumerable.Range(0, 2000).Select(i =>
            Seance($"Bloc {i % 5}", $"Matière {i % 5}", $"B{i % 5}.{i % 8} – SQ{i % 10} Titre", $"S{i}", ordre: i)).ToList();

        var chrono = System.Diagnostics.Stopwatch.StartNew();
        var racine = ArbreBibliotheque.Construire(seances);
        chrono.Stop();

        Assert.Equal(2000, racine.NbSeances);
        Assert.Equal(5, racine.Enfants.Count);
        Assert.True(chrono.ElapsedMilliseconds < 500, $"Construction en {chrono.ElapsedMilliseconds} ms");
    }
}
