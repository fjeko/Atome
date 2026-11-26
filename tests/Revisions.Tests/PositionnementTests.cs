using System.Text;
using System.Text.Json;
using Dapper;
using Revisions.Core.Acquis;
using Revisions.Core.Positionnement;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Test de positionnement : lecture des banques, algorithme adaptatif, application aux acquis.</summary>
public class PositionnementTests
{
    private static readonly string[] Codes = ["A1", "A2", "B1", "B2", "C1"];
    private static readonly string[] Competences = ["grammaire", "vocabulaire", "lecture", "ecoute"];

    /// <summary>Une banque complète : 5 niveaux, <paramref name="parCompetence"/> questions par compétence et par niveau.</summary>
    private static string JsonBanque(int parCompetence = 4, string depart = "B1")
    {
        var questions = new List<object>();
        foreach (string niveau in Codes)
            foreach (string comp in Competences)
                for (int i = 0; i < parCompetence; i++)
                    questions.Add(new
                    {
                        id = $"q-{niveau.ToLowerInvariant()}-{comp}-{i}",
                        niveau,
                        competence = comp,
                        question = $"Question {niveau} {comp} {i}",
                        choix = new[] { "bonne", "piège 1", "piège 2", "piège 3" },
                        texte = comp == "lecture" ? "Un petit texte." : null,
                        ecoute = comp == "ecoute" ? "Something to listen to." : null,
                    });
        return JsonSerializer.Serialize(new
        {
            formatPositionnement = 1,
            id = "en-positionnement",
            matiere = "Anglais général (A1 → C1)",
            bloc = "Anglais",
            titre = "Test de positionnement",
            langueEcoute = "en-GB",
            depart,
            niveaux = Codes.Select(c => new { code = c, nom = "Niveau " + c, partie = $"{c} – Niveau", description = "…" }),
            questions,
        });
    }

    private static BanquePositionnement Banque(int parCompetence = 4, string depart = "B1") =>
        LecteurBanque.Lire(JsonBanque(parCompetence, depart)).Banque ?? throw new InvalidOperationException("banque invalide");

    /// <summary>Fait passer le test à un élève qui répond juste à tout ce qui est de son niveau ou en dessous.</summary>
    private static (ResultatPositionnement Resultat, int Questions) Passer(BanquePositionnement banque, int niveauReel,
        bool ecoute = true, int seed = 1, Func<QuestionPositionnement, bool>? repondre = null)
    {
        var test = new TestAdaptatif(banque, new Random(seed), ecoute);
        int garde = 0;
        while (!test.Termine)
        {
            var q = test.Courante!;
            test.Repondre(repondre?.Invoke(q) ?? q.Niveau <= niveauReel);
            Assert.True(++garde < 200, "Le test ne se termine pas.");
        }
        return (test.Resultat(), test.Repondues);
    }

    [Fact]
    public void Une_banque_valide_se_lit()
    {
        var lecture = LecteurBanque.Lire(JsonBanque());
        Assert.Empty(lecture.Erreurs);
        var b = lecture.Banque!;
        Assert.Equal(5, b.Niveaux.Count);
        Assert.Equal(2, b.Depart);
        Assert.Equal("Anglais Anglais général (A1 → C1)", b.CleMatiere);
        Assert.Equal(80, b.Questions.Count);
        Assert.True(b.AEcoute);
    }

    [Theory]
    [InlineData("""{ "formatPositionnement": 1 }""", "id")]
    [InlineData("pas du json", "JSON invalide")]
    [InlineData("""[1, 2]""", "objet JSON")]
    public void Une_banque_invalide_est_refusee_avec_un_message(string json, string attendu)
    {
        var lecture = LecteurBanque.Lire(json);
        Assert.Null(lecture.Banque);
        Assert.Contains(lecture.Erreurs, e => e.Contains(attendu));
    }

    [Fact]
    public void Les_erreurs_de_questions_sont_precises()
    {
        string json = JsonBanque().Replace("\"competence\":\"ecoute\",\"question\":\"Question A1 ecoute 0\"", "\"competence\":\"chant\",\"question\":\"Question A1 ecoute 0\"");
        var lecture = LecteurBanque.Lire(json);
        Assert.Null(lecture.Banque);
        Assert.Contains(lecture.Erreurs, e => e.Contains("q-a1-ecoute-0") && e.Contains("chant"));
    }

    [Fact]
    public void Une_question_d_ecoute_sans_texte_a_lire_est_refusee()
    {
        string json = JsonBanque().Replace("\"ecoute\":\"Something to listen to.\"", "\"ecoute\":null");
        Assert.Contains(LecteurBanque.Lire(json).Erreurs, e => e.Contains("ecoute"));
    }

    [Fact]
    public void Un_niveau_avec_trop_peu_de_questions_est_refuse()
    {
        // Une seule question par compétence : 4 questions par niveau, moins qu'un bloc.
        Assert.Contains(LecteurBanque.Lire(JsonBanque(parCompetence: 1)).Erreurs, e => e.Contains("au moins"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Un_eleve_regulier_est_positionne_exactement(int niveauReel)
    {
        var banque = Banque();
        for (int seed = 1; seed <= 20; seed++)
        {
            var (r, nb) = Passer(banque, niveauReel, seed: seed);
            Assert.Equal(niveauReel, r.NiveauAtteint);
            Assert.Equal(niveauReel == 4, r.ToutAcquis);
            Assert.Equal(Math.Min(niveauReel + 1, 4), r.NiveauDepart);
            Assert.InRange(nb, TestAdaptatif.TailleBloc, 5 * TestAdaptatif.TailleBloc);
        }
    }

    [Fact]
    public void Le_test_reste_court()
    {
        // B1 réel, départ B1 : B1 validé, B2 raté → 2 blocs.
        Assert.Equal(12, Passer(Banque(), 2).Questions);
        // Débutant complet : B1, A2, A1 ratés → 3 blocs.
        Assert.Equal(18, Passer(Banque(), -1).Questions);
    }

    [Fact]
    public void Un_resultat_moyen_demande_un_second_bloc()
    {
        var banque = Banque();
        // 4 / 6 sur chaque bloc au niveau B1 (grammaire et vocabulaire justes) : 8 / 12 → validé.
        var (r, _) = Passer(banque, 0, repondre: q => q.Niveau <= 1
            || (q.Niveau == 2 && q.Competence is Competence.Grammaire or Competence.Vocabulaire));
        Assert.Equal(2, r.ParNiveau[2].Total / TestAdaptatif.TailleBloc);
        Assert.True(r.ParNiveau[2].Acquis);
        Assert.Equal(2, r.NiveauAtteint);
    }

    [Fact]
    public void Une_question_n_est_jamais_posee_deux_fois()
    {
        var banque = Banque();
        var test = new TestAdaptatif(banque, new Random(3), true);
        var vues = new HashSet<string>();
        bool alterne = false;
        while (!test.Termine)
        {
            Assert.True(vues.Add(test.Courante!.Id));
            test.Repondre(alterne = !alterne);   // une sur deux : tout reste incertain, on enchaîne les seconds blocs
        }
    }

    [Fact]
    public void Chaque_bloc_melange_les_competences()
    {
        var test = new TestAdaptatif(Banque(), new Random(5), true);
        var bloc = new List<QuestionPositionnement>();
        for (int i = 0; i < TestAdaptatif.TailleBloc; i++) { bloc.Add(test.Courante!); test.Repondre(true); }
        Assert.Equal(2, bloc.Count(q => q.Competence == Competence.Grammaire));
        Assert.Equal(2, bloc.Count(q => q.Competence == Competence.Vocabulaire));
        Assert.Single(bloc, q => q.Competence == Competence.Lecture);
        Assert.Single(bloc, q => q.Competence == Competence.Ecoute);
        Assert.All(bloc, q => Assert.Equal(2, q.Niveau));
    }

    [Fact]
    public void Sans_voix_il_n_y_a_aucune_question_d_ecoute()
    {
        var banque = Banque();
        var test = new TestAdaptatif(banque, new Random(2), ecoutePossible: false);
        while (!test.Termine)
        {
            Assert.NotEqual(Competence.Ecoute, test.Courante!.Competence);
            test.Repondre(test.Courante!.Niveau <= 1);
        }
        Assert.Equal(1, test.Resultat().NiveauAtteint);
    }

    [Fact]
    public void Le_point_faible_ressort_quand_une_competence_est_nettement_en_dessous()
    {
        // Bon partout sauf à l'écoute, ratée à tous les niveaux.
        var (r, _) = Passer(Banque(), 4, repondre: q => q.Competence != Competence.Ecoute);
        Assert.Equal(Competence.Ecoute, r.PointFaible);
    }

    [Fact]
    public void Le_plan_d_acquis_marque_les_niveaux_du_dessous()
    {
        var plan = DepotPositionnement.PlanAcquis(Banque(), depart: 2);
        Assert.Equal(5, plan.Count);
        Assert.Equal(CalculAcquis.Chemin(["Anglais Anglais général (A1 → C1)", "A1 – Niveau"]), plan[0].Chemin);
        Assert.Equal((Avancement.Revise, Comprehension.Solide), (plan[0].Avancement!.Value, plan[0].Comprehension!.Value));
        Assert.Equal((Avancement.Revise, Comprehension.Correct), (plan[1].Avancement!.Value, plan[1].Comprehension!.Value));
        Assert.All(plan.Skip(2), p => Assert.Null(p.Avancement));
    }

    [Fact]
    public void Le_niveau_s_enregistre_et_les_fichiers_de_test_ne_sont_pas_importes_comme_packs()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "positionnement-" + Guid.NewGuid().ToString("N"));
        string packs = Path.Combine(dossier, "packs", "Anglais");
        Directory.CreateDirectory(packs);
        try
        {
            File.WriteAllText(Path.Combine(packs, "Test." + LecteurBanque.Extension.TrimStart('.')), JsonBanque(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(packs, "pack.json"), PackExemple.Json, new UTF8Encoding(false));

            var bd = new BaseDeDonnees(Path.Combine(dossier, "test.db"));
            var resultats = new ImportateurPacks(bd, TimeProvider.System).ImporterDossier(Path.Combine(dossier, "packs"), null);
            Assert.Single(resultats);   // seul le pack, pas le test

            var erreurs = new List<(string, IReadOnlyList<string>)>();
            var banques = DepotPositionnement.ChargerBanques([Path.Combine(dossier, "packs"), null], erreurs);
            Assert.Single(banques);
            Assert.Empty(erreurs);

            var depot = new DepotPositionnement(bd, new DepotAcquis(bd, new DepotBibliotheque(bd)));
            Assert.Null(depot.Lire("en-positionnement"));
            depot.Enregistrer(banques[0], depart: 3, atteint: 2, parTest: true, new DateOnly(2026, 11, 26));
            var niveau = depot.Lire("en-positionnement");
            Assert.Equal(new NiveauEnregistre("B2", "B1", true, "2026-11-26"), niveau);

            using var cx = bd.Ouvrir();
            Assert.Equal(3, cx.ExecuteScalar<int>("SELECT COUNT(*) FROM acquis WHERE avancement = 'revise'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }
    }
}
