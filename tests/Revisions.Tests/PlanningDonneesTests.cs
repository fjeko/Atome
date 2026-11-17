using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Planning;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Planning en base : import, planning actif, choix de l'étudiant, dates personnelles, migration et journée.</summary>
public sealed class PlanningDonneesTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "planning-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _horloge = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Jour = new(2026, 11, 17);
    private readonly BaseDeDonnees _base;
    private readonly DepotPlanning _depot;

    public PlanningDonneesTests()
    {
        Directory.CreateDirectory(_dossier);
        _base = new BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        _depot = new DepotPlanning(_base, _horloge);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private static string Json(string id = "essai", string fin = "2026-11-29") => $$"""
        {
          "formatVersion": 1, "id": "{{id}}", "titre": "Essai {{id}}",
          "periodes": [
            { "matiere": "Maths", "libelle": "SQ1", "debut": "2026-09-07", "fin": "2026-10-04", "cible": ["p-sq1"] },
            { "matiere": "Maths", "libelle": "SQ2", "debut": "2026-11-02", "fin": "{{fin}}", "cible": ["p-sq2"] },
            { "matiere": "Maths", "libelle": "SQ3", "debut": "2027-01-11", "fin": "2027-02-07", "cible": ["p-sq3"] }
          ],
          "devoirs": [ { "matiere": "Maths", "code": "DV1", "note": true, "semaine": "2026-11-30" } ]
        }
        """;

    // ---- Import et planning actif --------------------------------------------------------------

    [Fact]
    public void Sans_planning_rien_n_est_actif()
    {
        Assert.Null(_depot.ChargerActif());
        Assert.Null(_depot.Effectif());
    }

    [Fact]
    public void Le_premier_planning_importe_devient_actif()
    {
        var r = _depot.Importer(Json(), "a.json", activer: false);
        Assert.True(r.Enregistre);
        Assert.Equal("essai", _depot.ChargerActif()!.Id);
        Assert.Equal("a.json", _depot.SourceActive());

        // Un deuxième planning trouvé dans le dossier ne remplace pas l'actif ; un import manuel, si.
        _depot.Importer(Json("autre"), "b.json", activer: false);
        Assert.Equal("essai", _depot.ChargerActif()!.Id);
        _depot.Importer(Json("autre"), "b.json", activer: true);
        Assert.Equal("autre", _depot.ChargerActif()!.Id);
    }

    [Fact]
    public void Un_planning_avec_des_erreurs_n_est_pas_enregistre()
    {
        var r = _depot.Importer(Json(fin: "2026-10-01"), "faux.json", activer: true);
        Assert.False(r.Enregistre);
        Assert.Contains(r.Lecture.Erreurs, e => e.Contains("avant le début"));
        Assert.Null(_depot.ChargerActif());
    }

    [Fact]
    public void Le_dossier_est_lu_et_un_fichier_identique_est_ignore()
    {
        string dossier = Path.Combine(_dossier, "planning");
        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "p.json"), Json());
        File.WriteAllText(Path.Combine(dossier, "faux.json"), "{");
        File.WriteAllText(Path.Combine(dossier, "lisez-moi.md"), "pas un planning");

        var premiers = _depot.ImporterDossier(dossier);
        Assert.Equal(2, premiers.Count);
        Assert.Single(premiers, r => r.Enregistre);
        Assert.Single(_depot.ImporterDossier(dossier));      // p.json identique : ignoré ; faux.json : toujours signalé
        Assert.Empty(_depot.ImporterDossier(Path.Combine(_dossier, "absent")));
        Assert.Empty(_depot.ImporterDossier(null));
    }

    [Fact]
    public void Un_fichier_modifie_remplace_le_contenu_et_garde_les_dates_personnelles()
    {
        _depot.Importer(Json(), "p.json", activer: false);
        _depot.DefinirSurcharge("essai", "periode:Maths|SQ1", new Surcharge(new DateOnly(2026, 9, 14), null));
        _depot.Importer(Json(fin: "2026-12-06"), "p.json", activer: false);

        var effectif = _depot.Effectif()!;
        Assert.Equal(new DateOnly(2026, 12, 6), effectif.Periodes.Single(p => p.Libelle == "SQ2").Fin);
        Assert.Equal(new DateOnly(2026, 9, 14), effectif.Periodes.Single(p => p.Libelle == "SQ1").Debut);
    }

    // ---- Choix de l'étudiant -------------------------------------------------------------------

    [Fact]
    public void Reglages_par_defaut_puis_enregistres()
    {
        _depot.Importer(Json(), "p.json", activer: false);
        var defaut = _depot.ChargerReglages("essai");
        Assert.True(defaut.Suivre);
        Assert.False(defaut.MasquerQuiz);
        Assert.Empty(defaut.MatieresIgnorees);
        Assert.Empty(defaut.Surcharges);

        _depot.DefinirSuivre(false);
        _depot.DefinirMasquerQuiz(true);
        _depot.DefinirMatiereSuivie("essai", "Maths", false);
        _depot.DefinirMatiereSuivie("essai", "Anglais", false);
        _depot.DefinirMatiereSuivie("essai", "Anglais", true);
        var r = _depot.ChargerReglages("essai");
        Assert.False(r.Suivre);
        Assert.True(r.MasquerQuiz);
        Assert.Equal(["Maths"], r.MatieresIgnorees);
    }

    [Fact]
    public void Dates_personnelles_enregistrees_puis_effacees_sans_toucher_au_fichier()
    {
        _depot.Importer(Json(), "p.json", activer: false);
        _depot.DefinirSurcharge("essai", "periode:Maths|SQ3", new Surcharge(new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 13)));
        _depot.DefinirSurcharge("essai", "devoir:Maths|DV1", new Surcharge(new DateOnly(2026, 12, 9), null));

        var effectif = _depot.Effectif()!;
        Assert.Equal(Moment.EnCours, PlanningEffectif.MomentDe(effectif.Periodes.Single(p => p.Libelle == "SQ3"), Jour));
        Assert.Equal(new DateOnly(2026, 12, 7), effectif.Devoirs.Single().Semaine);
        // Le planning d'origine n'a pas bougé.
        Assert.Equal(new DateOnly(2027, 1, 11), _depot.ChargerActif()!.Periodes.Single(p => p.Libelle == "SQ3").Debut);

        _depot.DefinirSurcharge("essai", "periode:Maths|SQ3", null);
        Assert.Single(_depot.ChargerReglages("essai").Surcharges);
        _depot.EffacerSurcharges("essai");
        Assert.Empty(_depot.ChargerReglages("essai").Surcharges);
    }

    [Fact]
    public void Les_reglages_du_planning_ne_genent_pas_les_reglages_generaux()
    {
        _depot.Importer(Json(), "p.json", activer: false);
        _depot.DefinirSuivre(false);
        _depot.DefinirMatiereSuivie("essai", "Maths", false);
        var reglages = new DepotProfil(_base).ChargerReglages();
        Assert.Equal(40, reglages.PlafondQuotidien);
    }

    // ---- Migration -----------------------------------------------------------------------------

    [Fact]
    public void Migration_depuis_la_version_2_sans_perte()
    {
        string chemin = Path.Combine(_dossier, "ancienne.db");
        new BaseDeDonnees(chemin);
        using (var cx = new SqliteConnection($"Data Source={chemin}"))
        {
            cx.Open();
            // On ramène la base à la version 2, puis on y met des données.
            cx.Execute("DROP TABLE planning; DROP TABLE planning_surcharge; ALTER TABLE pack DROP COLUMN fichier_taille; ALTER TABLE pack DROP COLUMN fichier_modifie; PRAGMA user_version = 2;");
            cx.Execute("INSERT INTO reglage (cle, valeur) VALUES ('plafond_quotidien', '25')");
            cx.Execute("INSERT INTO acquis (chemin, avancement, date_maj) VALUES ('x', 'vu', 'd')");
            cx.Execute("UPDATE profil SET xp_total = 1234");
        }
        SqliteConnection.ClearAllPools();

        var base3 = new BaseDeDonnees(chemin);
        using var connexion = base3.Ouvrir();
        Assert.Equal(4L, connexion.ExecuteScalar<long>("PRAGMA user_version;"));
        Assert.Equal(1234L, connexion.ExecuteScalar<long>("SELECT xp_total FROM profil"));
        Assert.Equal("25", connexion.ExecuteScalar<string>("SELECT valeur FROM reglage WHERE cle = 'plafond_quotidien'"));
        Assert.Equal(1L, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM acquis"));
        Assert.Equal(0L, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM planning"));
        Assert.Equal(0L, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM planning_surcharge"));
    }

    // ---- Journée complète ----------------------------------------------------------------------

    private void InsererPack(string pack, string partie, int nbCartes)
    {
        using var cx = _base.Ouvrir();
        cx.Execute("INSERT OR IGNORE INTO matiere (id, nom) VALUES (1, 'Maths')");
        cx.Execute("""
            INSERT INTO pack (id, matiere_id, partie, seance, ordre, source_fichier, resume, notions_json, format_version, empreinte, date_import, date_maj)
            VALUES (@pack, 1, @partie, @pack, 1, 'x.pdf', 'r', '[]', 1, 'e', 'd', 'd')
            """, new { pack, partie });
        cx.Execute("INSERT INTO section (id, pack_id, position, titre, contenu) VALUES (@s, @pack, 1, 't', 'c')", new { s = "s" + pack, pack });
        for (int i = 1; i <= nbCartes; i++)
        {
            string id = $"{pack}#{i}";
            cx.Execute("INSERT INTO carte (id, pack_id, section_id, position, type, difficulte, donnees_json, date_ajout) VALUES (@id, @pack, @s, @i, 'question', 1, '{}', 'd')",
                new { id, pack, s = "s" + pack, i });
            cx.Execute("INSERT INTO etat_carte (carte_id) VALUES (@id)", new { id });
        }
    }

    private Journee Journee(DateOnly jour)
    {
        var revisions = new DepotRevisions(_base);
        var biblio = new DepotBibliotheque(_base);
        return new SelectionDuJour(revisions, biblio, new DepotAcquis(_base, biblio), _depot).Charger(jour);
    }

    [Fact]
    public void Sans_planning_la_journee_propose_tout_dans_l_ordre_du_cours()
    {
        InsererPack("p-sq3", "SQ3", 2);
        InsererPack("p-sq1", "SQ1", 2);
        var journee = Journee(Jour);
        Assert.False(journee.PlanningSuivi);
        Assert.Equal(["p-sq1#1", "p-sq1#2", "p-sq3#1"], journee.Nouvelles(3).Select(c => c.Carte.Id));
        Assert.Empty(journee.Suggestions());
        Assert.Empty(journee.Echeances());
    }

    [Fact]
    public void Avec_planning_le_futur_n_est_pas_propose_mais_suggere_a_la_lecture()
    {
        InsererPack("p-sq1", "SQ1", 2);
        InsererPack("p-sq2", "SQ2", 2);
        InsererPack("p-sq3", "SQ3", 2);
        _depot.Importer(Json(), "p.json", activer: false);

        var journee = Journee(Jour);
        Assert.True(journee.PlanningSuivi);
        Assert.Equal(["p-sq2#1", "p-sq2#2", "p-sq1#1", "p-sq1#2"], journee.Nouvelles(10).Select(c => c.Carte.Id));
        Assert.Equal(4, journee.NombreDeNouvelles(10));
        Assert.Equal(["p-sq2", "p-sq1", "p-sq3"], journee.Suggestions().Select(s => s.Seance.PackId));
        Assert.Equal("DV1", Assert.Single(journee.Echeances()).Code);
    }

    [Fact]
    public void Une_seance_lue_passe_en_premier_meme_si_elle_est_future()
    {
        InsererPack("p-sq2", "SQ2", 1);
        InsererPack("p-sq3", "SQ3", 1);
        _depot.Importer(Json(), "p.json", activer: false);
        using (var cx = _base.Ouvrir()) cx.Execute("UPDATE pack SET date_lecture = 'd' WHERE id = 'p-sq3'");
        Assert.Equal(["p-sq3#1", "p-sq2#1"], Journee(Jour).Nouvelles(10).Select(c => c.Carte.Id));
    }

    [Fact]
    public void Les_cartes_avancees_avant_un_devoir_sont_presentees_comme_dues()
    {
        InsererPack("p-sq2", "SQ2", 2);
        _depot.Importer(Json(), "p.json", activer: false);
        using (var cx = _base.Ouvrir())
        {
            // Revue le 20/11 avec une stabilité faible, mais échéance placée après le devoir (06/12).
            cx.Execute("UPDATE etat_carte SET stabilite = 1, difficulte_fsrs = 5, derniere_revision = '2026-11-20', echeance = '2026-12-20', repetitions = 1 WHERE carte_id = 'p-sq2#1'");
        }
        var jour = new DateOnly(2026, 12, 6);  // dernier jour : tout ce qui reste est avancé
        var journee = Journee(jour);
        var avancee = Assert.Single(journee.Avancees());
        Assert.Equal("p-sq2#1", avancee.Carte.Id);
        Assert.Equal(jour, avancee.Etat.Echeance);                          // due aujourd'hui pour l'affichage et l'XP
        Assert.Equal(new DateOnly(2026, 11, 20), avancee.Etat.DerniereRevision);  // FSRS part de la vraie date
        Assert.Single(journee.Dues());

        // Le lendemain du devoir : plus rien n'est avancé.
        Assert.Empty(Journee(jour.AddDays(1)).Avancees());
    }

    [Fact]
    public void Planning_coupe_comme_sans_planning()
    {
        InsererPack("p-sq3", "SQ3", 1);
        _depot.Importer(Json(), "p.json", activer: false);
        Assert.Empty(Journee(Jour).Nouvelles(10));
        _depot.DefinirSuivre(false);
        Assert.Single(Journee(Jour).Nouvelles(10));
        Assert.Empty(Journee(Jour).Echeances());
    }
}
