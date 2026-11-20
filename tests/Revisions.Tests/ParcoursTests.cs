using Dapper;
using Revisions.Core.Modeles;
using Revisions.Core.Parcours;
using Revisions.Core.Planification;
using Revisions.Data;
using Xunit;

namespace Revisions.Tests;

/// <summary>Parcours « Lire puis contrôler » : composition des questions, avancement, reprise, marquage et notation FSRS.</summary>
public class ParcoursTests
{
    private static readonly DateOnly Jour = new(2026, 11, 20);

    private static Section Section(string id) => new(id, "Titre " + id, "Contenu", null, []);

    private static Carte Carte(string id, string section) =>
        new(id, section, TypeCarte.Question, 1, new ContenuCarte { Recto = "q", Verso = "r" });

    /// <summary>s1 : 5 cartes, s2 : 2 cartes, s3 : aucune, s4 : 6 cartes (13 cartes en tout), plus une carte orpheline.</summary>
    private static (List<Section> Sections, List<Carte> Cartes) Seance()
    {
        var sections = new[] { "s1", "s2", "s3", "s4" }.Select(Section).ToList();
        var cartes = new List<Carte>();
        foreach (var (s, n) in new[] { ("s1", 5), ("s2", 2), ("s4", 6) })
            for (int i = 0; i < n; i++) cartes.Add(Carte($"{s}-c{i}", s));
        cartes.Add(Carte("orpheline", "ailleurs"));
        return (sections, cartes);
    }

    // ---- Composition ---------------------------------------------------------------------------

    [Fact]
    public void Les_questions_flash_viennent_de_leur_section_et_une_section_sans_carte_n_en_a_pas()
    {
        var (sections, cartes) = Seance();
        var etat = ParcoursLecture.Composer("p", sections, cartes, new Random(1));

        Assert.Equal(2, etat.Flash["s1"].Count);   // au moins 4 cartes : 2 questions
        Assert.Single(etat.Flash["s2"]);            // moins de 4 cartes : 1 question
        Assert.False(etat.Flash.ContainsKey("s3")); // pas de carte : on passe sans question
        Assert.Equal(2, etat.Flash["s4"].Count);
        foreach (var (section, ids) in etat.Flash)
            Assert.All(ids, id => Assert.StartsWith(section + "-", id));
    }

    [Fact]
    public void Le_controle_a_8_a_12_questions_sans_doublon_et_privilegie_les_cartes_non_vues()
    {
        var (sections, cartes) = Seance();
        var etat = ParcoursLecture.Composer("p", sections, cartes, new Random(2));
        var flash = etat.Flash.Values.SelectMany(v => v).ToHashSet();

        Assert.InRange(etat.Controle.Count, ParcoursLecture.ControleMin, ParcoursLecture.ControleMax);
        Assert.Equal(etat.Controle.Count, etat.Controle.Distinct().Count());
        Assert.DoesNotContain("orpheline", etat.Controle);
        // 13 cartes, 5 en flash : les 8 non vues sont toutes au contrôle, complétées par des cartes flash.
        Assert.Equal(12, etat.Controle.Count);
        Assert.Equal(8, etat.Controle.Count(id => !flash.Contains(id)));
    }

    [Fact]
    public void Une_petite_seance_a_un_controle_reduit_a_ses_cartes()
    {
        var sections = new List<Section> { Section("s1") };
        var cartes = new List<Carte> { Carte("a", "s1"), Carte("b", "s1"), Carte("c", "s1") };
        var etat = ParcoursLecture.Composer("p", sections, cartes, new Random(3));
        Assert.Equal(3, etat.Controle.Count);
    }

    [Fact]
    public void Le_controle_est_melange()
    {
        var (sections, cartes) = Seance();
        var ordres = Enumerable.Range(0, 10).Select(i => string.Join(",", ParcoursLecture.Composer("p", sections, cartes, new Random(i)).Controle)).Distinct();
        Assert.True(ordres.Count() > 1);
    }

    // ---- Avancement et scores ------------------------------------------------------------------

    /// <summary>Joue tout le parcours : chaque carte dont l'identifiant est dans « ratees » est notée Raté.</summary>
    private static EtatParcours Jouer(List<Section> sections, EtatParcours etat, Func<string, bool> ratee)
    {
        for (int garde = 0; etat.Phase != PhaseParcours.Resultats && garde < 200; garde++)
        {
            string? id = etat.Phase == PhaseParcours.Controle ? etat.Controle[etat.IndexControle]
                : etat.IndexFlash >= 0 ? ParcoursLecture.FlashDe(etat, sections)[etat.IndexFlash] : null;
            if (id is null) ParcoursLecture.AvancerLecture(etat, sections); // on a lu la section
            else ParcoursLecture.Repondre(etat, sections, id, id.Split('-')[0], ratee(id) ? Note.Rate : Note.Bien);
        }
        return etat;
    }

    [Fact]
    public void Le_parcours_enchaine_lecture_flash_controle_puis_resultats_avec_un_score_par_section()
    {
        var (sections, cartes) = Seance();
        var etat = Jouer(sections, ParcoursLecture.Composer("p", sections, cartes, new Random(4)), id => id.StartsWith("s2"));

        Assert.Equal(PhaseParcours.Resultats, etat.Phase);
        Assert.Equal(5, etat.Reponses.Count(r => !r.Controle));
        Assert.Equal(12, etat.Reponses.Count(r => r.Controle));

        var scores = ParcoursLecture.Scores(etat, sections);
        Assert.DoesNotContain(scores, s => s.SectionId == "s3");     // aucune question : pas de score
        Assert.True(scores.Single(s => s.SectionId == "s2").Ratee);
        Assert.False(scores.Single(s => s.SectionId == "s1").Ratee);
        int ratees = etat.Reponses.Count(r => r.Controle && r.SectionId == "s2");
        Assert.Equal((int)Math.Round(100.0 * (12 - ratees) / 12), ParcoursLecture.ScoreGlobal(etat));
    }

    [Fact]
    public void Une_seance_sans_carte_va_directement_aux_resultats()
    {
        var sections = new List<Section> { Section("s1"), Section("s2") };
        var etat = ParcoursLecture.Composer("p", sections, [], new Random(5));
        ParcoursLecture.AvancerLecture(etat, sections);
        ParcoursLecture.AvancerLecture(etat, sections);
        Assert.Equal(PhaseParcours.Resultats, etat.Phase);
        Assert.Equal(0, ParcoursLecture.ScoreGlobal(etat));
    }

    [Fact]
    public void L_encouragement_ne_culpabilise_jamais()
    {
        foreach (int score in new[] { 0, 30, 50, 80, 100 })
        {
            string texte = ParcoursLecture.Encouragement(score);
            Assert.DoesNotContain("échec", texte, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nul", texte, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Base : reprise, marquage, notation ------------------------------------------------------

    private static void AvecBase(Action<BaseDeDonnees> test)
    {
        string dossier = Path.Combine(Path.GetTempPath(), "parcours-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var bd = new BaseDeDonnees(Path.Combine(dossier, "test.db"));
            using (var cx = bd.Ouvrir())
            {
                cx.Execute("INSERT INTO matiere (id, nom, bloc) VALUES (1, 'Maths', 'Bloc 1')");
                cx.Execute("""
                    INSERT INTO pack (id, matiere_id, partie, seance, ordre, source_fichier, resume, notions_json, format_version, empreinte, date_import, date_maj)
                    VALUES ('p', 1, 'Algèbre', 'S1', 1, '', 'r', '[]', 1, 'e', 'd', 'd')
                    """);
                cx.Execute("INSERT INTO section (id, pack_id, position, titre, contenu) VALUES ('s1', 'p', 1, 't', 'c')");
                cx.Execute("INSERT INTO carte (id, pack_id, section_id, position, type, difficulte, donnees_json, date_ajout) VALUES ('c1', 'p', 's1', 1, 'question', 1, '{}', 'd')");
                cx.Execute("INSERT INTO etat_carte (carte_id) VALUES ('c1')");
            }
            test(bd);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void On_reprend_le_parcours_exactement_ou_on_l_avait_laisse() => AvecBase(bd =>
    {
        var (sections, cartes) = Seance();
        var depot = new DepotParcours(bd);
        var etat = ParcoursLecture.Composer("p", sections, cartes, new Random(6));
        ParcoursLecture.AvancerLecture(etat, sections); // section 1 lue → première question flash
        ParcoursLecture.Repondre(etat, sections, etat.Flash["s1"][0], "s1", Note.Bien);
        depot.Enregistrer(etat);

        var repris = depot.Charger("p")!;
        Assert.Equal(etat.Phase, repris.Phase);
        Assert.Equal(etat.IndexSection, repris.IndexSection);
        Assert.Equal(1, repris.IndexFlash);
        Assert.Equal(etat.Controle, repris.Controle);
        Assert.Equal(etat.Flash["s1"], repris.Flash["s1"]);
        Assert.Equal(etat.Reponses, repris.Reponses);

        depot.Abandonner("p");
        Assert.Null(depot.Charger("p"));
        Assert.Null(depot.Charger("inconnu"));
    });

    [Fact]
    public void Terminer_marque_la_seance_lue_et_controlee_avec_date_et_score() => AvecBase(bd =>
    {
        var depot = new DepotParcours(bd);
        var biblio = new DepotBibliotheque(bd);
        Assert.Null(biblio.ListerSeances(Jour).Single().TexteControle);

        depot.Enregistrer(new EtatParcours { PackId = "p" });
        depot.Terminer("p", new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(1)), 83);

        var seance = biblio.ListerSeances(Jour).Single();
        Assert.Equal(83, seance.ScoreControle);
        Assert.Equal("Lue et contrôlée le 20/11 · 83 %", seance.TexteControle);
        Assert.Null(depot.Charger("p")); // le parcours terminé n'est plus « en cours »
    });

    [Fact]
    public void Une_reponse_du_parcours_passe_par_FSRS_comme_en_session() => AvecBase(bd =>
    {
        var revisions = new DepotRevisions(bd);
        var notation = new ServiceNotation(revisions, new Planificateur(new Fsrs()));
        long session = revisions.CreerSession("lire_controler", Jour, DateTimeOffset.Now);
        var carte = revisions.CartesDuPack("p").Single();

        var premiere = notation.Noter(session, carte, Note.Bien, retour: false, Jour, DateTimeOffset.Now, 1000, 0.9);
        Assert.Equal(5, premiere.Xp); // nouvelle carte, comme en session
        // La même carte reposée au contrôle : FSRS en tient compte, sans XP en double.
        var seconde = notation.Noter(session, carte with { Etat = premiere.Planification.NouvelEtat }, Note.Bien, retour: true, Jour, DateTimeOffset.Now, 1000, 0.9);
        Assert.Equal(0, seconde.Xp);

        using var cx = bd.Ouvrir();
        Assert.Equal(2, cx.ExecuteScalar<int>("SELECT COUNT(*) FROM revision WHERE carte_id = 'c1' AND session_id = @session", new { session }));
        Assert.NotNull(cx.ExecuteScalar<double?>("SELECT stabilite FROM etat_carte WHERE carte_id = 'c1'"));
        Assert.Equal("lire_controler", cx.ExecuteScalar<string>("SELECT mode FROM session_revision WHERE id = @session", new { session }));
    });
}
