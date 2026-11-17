using Dapper;
using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;
using Revisions.Data;
using Xunit;

namespace Revisions.Tests;

/// <summary>Acquis : héritage dans l'arbre, état de départ des cartes, cartes avec historique, étalement.</summary>
public class AcquisTests
{
    private static readonly DateOnly Jour = new(2026, 11, 17);

    private static Dictionary<string, ReglageAcquis> Reglages(params (string[] Cles, Avancement? Av, Comprehension? Co)[] lignes) =>
        lignes.ToDictionary(l => CalculAcquis.Chemin(l.Cles), l => new ReglageAcquis(l.Av, l.Co));

    [Fact]
    public void Un_reglage_s_applique_aux_enfants()
    {
        var r = Reglages((["Maths"], Avancement.Vu, Comprehension.Correct));
        var acquis = CalculAcquis.Resoudre(r, ["Maths", "Algèbre", "§m1"]);
        Assert.Equal(Avancement.Vu, acquis.Avancement);
        Assert.Equal(Comprehension.Correct, acquis.Comprehension);
    }

    [Fact]
    public void Un_enfant_regle_individuellement_garde_sa_valeur()
    {
        var r = Reglages(
            (["Maths"], Avancement.Revise, Comprehension.Solide),
            (["Maths", "Algèbre"], Avancement.PasVu, null),
            (["Maths", "Algèbre", "§m1"], null, Comprehension.Fragile));
        // La séance hérite de « pas vu » de sa partie, mais a sa propre compréhension.
        var acquis = CalculAcquis.Resoudre(r, ["Maths", "Algèbre", "§m1"]);
        Assert.Equal(Avancement.PasVu, acquis.Avancement);
        Assert.Equal(Comprehension.Fragile, acquis.Comprehension);
        // Une autre partie garde le réglage de la matière.
        Assert.Equal(Avancement.Revise, CalculAcquis.Resoudre(r, ["Maths", "Analyse", "§m2"]).Avancement);
        // Sans aucun réglage : pas vu.
        Assert.Equal(Avancement.PasVu, CalculAcquis.Resoudre(r, ["Algo", "§a1"]).Avancement);
    }

    [Fact]
    public void Pas_vu_donne_une_carte_nouvelle_et_decouverte_une_carte_nouvelle_prioritaire()
    {
        var pasVu = CalculAcquis.EtatInitial(new AcquisEffectif(Avancement.PasVu, Comprehension.Solide), "c1", Jour);
        Assert.Equal(EtatDepart.Nouvelle, pasVu);

        var decouverte = CalculAcquis.EtatInitial(new AcquisEffectif(Avancement.Vu, Comprehension.Decouverte), "c1", Jour);
        Assert.Null(decouverte.Stabilite);
        Assert.True(decouverte.Prioritaire);
    }

    [Theory]
    [InlineData(Comprehension.Fragile, 1, 2)]
    [InlineData(Comprehension.Correct, 5, 9)]
    [InlineData(Comprehension.Solide, 16, 26)]
    public void La_comprehension_donne_une_echeance_dans_la_bonne_plage(Comprehension niveau, int min, int max)
    {
        for (int i = 0; i < 200; i++)
        {
            var etat = CalculAcquis.EtatInitial(new AcquisEffectif(Avancement.Vu, niveau), $"carte-{i}", Jour);
            int jours = etat.Echeance!.Value.DayNumber - Jour.DayNumber;
            Assert.InRange(jours, min, max);
            Assert.Equal(jours, etat.Stabilite);
            Assert.Equal(Jour, etat.DerniereRevision);
            Assert.False(etat.Prioritaire);
        }
    }

    [Fact]
    public void Sans_comprehension_vu_vaut_decouverte_et_revise_vaut_fragile()
    {
        Assert.Equal(Comprehension.Decouverte, CalculAcquis.ComprehensionEffective(new AcquisEffectif(Avancement.Vu, null)));
        Assert.Equal(Comprehension.Fragile, CalculAcquis.ComprehensionEffective(new AcquisEffectif(Avancement.Revise, null)));
        Assert.Null(CalculAcquis.ComprehensionEffective(new AcquisEffectif(Avancement.PasVu, null)));
    }

    [Fact]
    public void Les_echeances_sont_etalees_et_stables()
    {
        var acquis = new AcquisEffectif(Avancement.Vu, Comprehension.Solide);
        var parJour = Enumerable.Range(0, 330)
            .Select(i => CalculAcquis.EtatInitial(acquis, $"carte-{i}", Jour).Echeance!.Value)
            .GroupBy(e => e).ToList();
        Assert.True(parJour.Count >= 8, "les échéances doivent couvrir plusieurs jours");
        Assert.True(parJour.Max(g => g.Count()) <= 330 / 4, "aucun jour ne doit concentrer plus d'un quart des cartes");
        // Même carte, même résultat : relancer le calcul ne fait pas bouger les échéances.
        Assert.Equal(CalculAcquis.EtatInitial(acquis, "x", Jour), CalculAcquis.EtatInitial(acquis, "x", Jour));
    }

    // ---- Avec une vraie base : les cartes qui ont un historique ne sont pas touchées ----

    private static void InsererPack(System.Data.IDbConnection cx, string pack, int ordre, params string[] cartes)
    {
        cx.Execute("""
            INSERT INTO pack (id, matiere_id, partie, seance, ordre, source_fichier, resume, notions_json, format_version, empreinte, date_import, date_maj)
            VALUES (@pack, 1, 'Algèbre', @pack, @ordre, 'x.pdf', 'r', '[]', 1, 'e', 'd', 'd')
            """, new { pack, ordre });
        cx.Execute("INSERT INTO section (id, pack_id, position, titre, contenu) VALUES (@s, @pack, 1, 't', 'c')", new { s = "s" + pack, pack });
        foreach (string id in cartes)
        {
            cx.Execute("INSERT INTO carte (id, pack_id, section_id, position, type, difficulte, donnees_json, date_ajout) VALUES (@id, @pack, @s, 1, 'question', 1, '{}', 'd')",
                new { id, pack, s = "s" + pack });
            cx.Execute("INSERT INTO etat_carte (carte_id) VALUES (@id)", new { id });
        }
    }

    [Fact]
    public void Les_cartes_avec_historique_gardent_leur_etat_et_les_autres_suivent_les_acquis()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "acquis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var bd = new BaseDeDonnees(Path.Combine(dossier, "test.db"));
            using (var cx = bd.Ouvrir())
            {
                cx.Execute("INSERT INTO matiere (id, nom, bloc) VALUES (1, 'Maths', 'Bloc 1')");
                InsererPack(cx, "p1", 1, "vieille", "neuve1", "neuve2");
                // « vieille » a un vrai historique et un état FSRS.
                cx.Execute("UPDATE etat_carte SET stabilite = 3, difficulte_fsrs = 5, echeance = '2026-11-20', derniere_revision = '2026-11-17', repetitions = 1 WHERE carte_id = 'vieille'");
                cx.Execute("INSERT INTO revision (carte_id, date_heure, jour, note, etait_due) VALUES ('vieille', 'd', '2026-11-17', 3, 0)");
            }

            var biblio = new DepotBibliotheque(bd);
            var depot = new DepotAcquis(bd, biblio);
            var seance = biblio.ListerSeances(Jour).Single();
            string matiere = CalculAcquis.Chemin(ArbreBibliotheque.Chemin(seance).Take(1));
            depot.Definir(matiere, Avancement.Revise, Comprehension.Solide);
            Assert.Equal(2, depot.Recalculer(Jour));

            using var lecture = bd.Ouvrir();
            Assert.Equal(3, lecture.ExecuteScalar<double>("SELECT stabilite FROM etat_carte WHERE carte_id = 'vieille'"));
            Assert.Equal("2026-11-20", lecture.ExecuteScalar<string>("SELECT echeance FROM etat_carte WHERE carte_id = 'vieille'"));
            Assert.Equal(2, lecture.ExecuteScalar<int>("SELECT COUNT(*) FROM etat_carte WHERE stabilite >= 16 AND carte_id <> 'vieille'"));

            // On corrige : « pas vu ». Seules les cartes sans historique repartent à zéro.
            depot.Definir(matiere, Avancement.PasVu, null);
            Assert.Equal(2, depot.Recalculer(Jour));
            Assert.Equal(2, lecture.ExecuteScalar<int>("SELECT COUNT(*) FROM etat_carte WHERE stabilite IS NULL"));
            Assert.Equal(3, lecture.ExecuteScalar<double>("SELECT stabilite FROM etat_carte WHERE carte_id = 'vieille'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void Les_cartes_decouverte_passent_en_premier_parmi_les_nouvelles()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "acquis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var bd = new BaseDeDonnees(Path.Combine(dossier, "test.db"));
            using (var cx = bd.Ouvrir())
            {
                cx.Execute("INSERT INTO matiere (id, nom, bloc) VALUES (1, 'Maths', 'Bloc 1')");
                InsererPack(cx, "pa", 1, "cpa");
                InsererPack(cx, "pb", 2, "cpb");
            }
            var biblio = new DepotBibliotheque(bd);
            var depot = new DepotAcquis(bd, biblio);
            var seanceB = biblio.ListerSeances(Jour).Single(s => s.PackId == "pb");
            depot.Definir(CalculAcquis.Chemin(ArbreBibliotheque.Chemin(seanceB)), Avancement.Vu, Comprehension.Decouverte);
            depot.Recalculer(Jour);

            var nouvelles = new SelectionDuJour(new DepotRevisions(bd), biblio, depot, new DepotPlanning(bd, TimeProvider.System)).Charger(Jour).Nouvelles(10);
            Assert.Equal("cpb", nouvelles[0].Etat.CarteId); // la séance « je découvre » passe avant la séance 1
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }
    }
}
