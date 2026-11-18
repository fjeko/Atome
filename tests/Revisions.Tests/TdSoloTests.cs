using Revisions.Core.TdGroupe;

namespace Revisions.Tests;

/// <summary>TD solo : réponses texte, sujets des générateurs.</summary>
public class TdSoloTests
{
    // ---- Réponses texte --------------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.0", " 192.168.1.0 ", true)]
    [InlineData("192.168.1.0", "192.168.1.1", false)]
    [InlineData("rwxr-x---", "RWXR-X---", true)]
    [InlineData("rwxr-x---", "rwxr-x--", false)]
    public void Texte_exact(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue), saisie).Juste);

    [Theory]
    [InlineData("A-C-F-FIN", "a c f fin", true)]
    [InlineData("A-C-F-FIN", "A → C → F → FIN", true)]
    [InlineData("A-C-F-FIN", "ACFFIN", true)]
    [InlineData("A-C-F-FIN", "A-F-C-FIN", false)]
    public void Texte_chemin(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue, FormeTexte.Chemin), saisie).Juste);

    [Theory]
    [InlineData("B, D", "D B", true)]
    [InlineData("B, D", "BD", true)]
    [InlineData("B, D", "B", false)]
    public void Texte_ensemble(string attendue, string saisie, bool juste) =>
        Assert.Equal(juste, VerificationSaisie.Texte(new ReponseTexte(attendue, FormeTexte.Ensemble), saisie).Juste);

    [Fact]
    public void Texte_variantes_et_saisie_vide()
    {
        var r = new ReponseTexte("rwxr-x---", Variantes: ["-rwxr-x---"]);
        Assert.True(VerificationSaisie.Texte(r, "-rwxr-x---").Juste);
        var vide = VerificationSaisie.Texte(r, "  ");
        Assert.False(vide.Juste);
        Assert.False(vide.Complete);
    }

    // ---- Calculs : exemples du cours --------------------------------------------------------

    [Fact]
    public void Mpm_exemple_du_cours()
    {
        // A..H : durées 2,2,2,4,1,2,2,1 ; C:A, D:A, E:B+D, F:D, G:E, H:C+F+G.
        List<int>[] ant = [[], [], [0], [0], [1, 3], [3], [4], [2, 5, 6]];
        var p = new ProjetMpm([2, 2, 2, 4, 1, 2, 2, 1], ant);
        Assert.Equal([0, 0, 2, 2, 6, 6, 7, 9, 10], p.Tot);
        Assert.Equal([0, 4, 7, 2, 6, 7, 7, 9, 10], p.Tard);
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 4], p.Niveaux);
        Assert.Equal(10, p.Duree);
        Assert.Equal(4, p.MargeTotale(1));            // B : 4 − 0
        Assert.Equal(5, p.MargeLibre(2));             // C : 9 − 2 − 2
        var chemin = Assert.Single(p.CheminsCritiques());
        Assert.Equal([0, 3, 4, 6, 7, 8], chemin);     // A-D-E-G-H-FIN
    }

    [Fact]
    public void Graphes_fermeture_et_niveaux()
    {
        // A → B → C → D : la fermeture ajoute A→C, A→D, B→D.
        var g = new bool[4, 4];
        g[0, 1] = g[1, 2] = g[2, 3] = true;
        var chapeau = GenerateurGraphes.FermetureTransitive(GenerateurGraphes.Adjacence(g));
        Assert.Equal(6, chapeau.Coefficients().Sum());
        Assert.Equal(1, chapeau[0, 3]);
        Assert.Equal(0, chapeau[3, 0]);
        Assert.Equal([0, 1, 2, 3], GenerateurGraphes.CalculerNiveaux(g).Niveaux);
        Assert.Single(GenerateurGraphes.TousLesChemins(g, 0, 3));
    }

    [Fact]
    public void Graphes_les_reponses_concordent_avec_les_calculs()
    {
        var gra = new GenerateurGraphes();
        for (ulong graine = 0; graine < 30; graine++)
        {
            var e = gra.CreerExercice("fermeture", new GenerateurAleatoire(graine), 2);
            var m2 = Assert.IsType<ReponseMatrice>(e.Questions[0].Attendu).Valeur;
            var chapeau = Assert.IsType<ReponseMatrice>(e.Questions[1].Attendu).Valeur;
            // M̂ contient M[2] (tout chemin de longueur 2 existe dans la fermeture).
            for (int i = 0; i < m2.Lignes; i++)
            for (int j = 0; j < m2.Colonnes; j++)
                Assert.True(m2[i, j] <= chapeau[i, j]);
        }
    }

    // ---- Sujets ----------------------------------------------------------------------------

    private static readonly IGenerateurASujets[] Tous = [new GenerateurMatrices(), new GenerateurGraphes(), new GenerateurOrdonnancement(), new GenerateurInverse(), new GenerateurIpv4(), new GenerateurDroitsLinux(), new GenerateurSql()];

    public static TheoryData<string> Generateurs() => [.. Tous.Select(g => g.Prefixe)];

    private static IGenerateurASujets Gen(string prefixe) => Tous.Single(g => g.Prefixe == prefixe);

    [Theory]
    [MemberData(nameof(Generateurs))]
    public void Chaque_sujet_genere_des_exercices_complets_a_chaque_niveau(string prefixe)
    {
        var g = Gen(prefixe);
        Assert.NotEmpty(g.Sujets);
        Assert.Equal(g.Sujets.Count, g.Sujets.Select(s => s.Cle).Distinct().Count());
        foreach (var sujet in g.Sujets)
        for (int niveau = sujet.NiveauMin; niveau <= 3; niveau++)
        for (ulong graine = 0; graine < 40; graine++)
        {
            var e = g.CreerExercice(sujet.Cle, new GenerateurAleatoire(graine * 7919 + (ulong)niveau), niveau);
            Assert.False(string.IsNullOrWhiteSpace(e.Enonce), $"{g.Prefixe}/{sujet.Cle}");
            Assert.NotEmpty(e.Questions);
            foreach (var q in e.Questions)
            {
                Assert.NotEmpty(q.Correction);
                Assert.False(string.IsNullOrWhiteSpace(q.Attendu.Texte), $"{g.Prefixe}/{sujet.Cle} {q.Lettre}");
                if (q.Attendu is ReponseTexte t) Assert.True(VerificationSaisie.Texte(t, t.Attendue).Juste, $"{g.Prefixe}/{sujet.Cle} : {t.Attendue}");
                if (q.Attendu is ReponseMatrice m)
                {
                    if (m.EnTetesColonnes is { } c) Assert.Equal(m.Valeur.Colonnes, c.Count);
                    if (m.EnTetesLignes is { } l) Assert.Equal(m.Valeur.Lignes, l.Count);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Generateurs))]
    public void Meme_graine_meme_td_en_groupe(string prefixe)
    {
        var g = Gen(prefixe);
        var p = new ParametresTd(g.Prefixe, g.VersionActuelle == 2 ? 2 : 1, 2, 8, 1234);
        Assert.Equal(g.Generer(p).Empreinte, g.Generer(p).Empreinte);
    }
}

/// <summary>TD solo : composition d'une feuille selon la durée et l'historique.</summary>
public class CompositeurTdTests
{
    private static readonly IGenerateurASujets[] Themes = [new GenerateurGraphes(), new GenerateurOrdonnancement()];

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void La_duree_est_respectee_a_10_pourcent(int minutes)
    {
        for (ulong graine = 0; graine < 20; graine++)
        {
            var td = CompositeurTd.Composer(new DemandeTdSolo(Themes, minutes, 2), new GenerateurAleatoire(graine));
            int total = td.Sum(e => e.Minutes);
            Assert.InRange(total, minutes * 0.9 - 8, minutes * 1.1); // 8 min : le plus long exercice peut ne pas rentrer
            Assert.NotEmpty(td);
        }
    }

    [Fact]
    public void Rangement_par_theme_puis_ordre_du_cours_et_jamais_deux_fois_de_suite_au_tirage()
    {
        var td = CompositeurTd.Composer(new DemandeTdSolo(Themes, 120, 2), new GenerateurAleatoire(7));
        var rangs = td.Select(e => (Array.IndexOf(Themes.Select(t => t.Prefixe).ToArray(), e.Prefixe),
            Themes.First(t => t.Prefixe == e.Prefixe).Sujets.ToList().IndexOf(e.Sujet))).ToList();
        Assert.Equal(rangs.OrderBy(r => r.Item1).ThenBy(r => r.Item2), rangs);
    }

    [Fact]
    public void Un_sujet_rate_revient_plus_souvent()
    {
        Dictionary<string, BilanSujet> Historique(bool rate) => Themes.SelectMany(t => t.Sujets.Select(s => (Cle: CompositeurTd.Cle(t.Prefixe, s.Cle), Sujet: s.Cle)))
            .ToDictionary(x => x.Cle, x => new BilanSujet(4, rate && x.Sujet == "fermeture"));
        int Compter(bool rate) => Enumerable.Range(0, 200).Sum(graine => CompositeurTd.Composer(
            new DemandeTdSolo(Themes, 30, 2, Historique: Historique(rate)), new GenerateurAleatoire((ulong)graine)).Count(e => e.Sujet.Cle == "fermeture"));
        int avecEchec = Compter(true), sansEchec = Compter(false);
        Assert.True(avecEchec > sansEchec * 1.5, $"raté : {avecEchec}, réussi : {sansEchec}");
    }

    [Fact]
    public void Filtre_des_sujets_et_aucun_sujet_permis()
    {
        var td = CompositeurTd.Composer(new DemandeTdSolo(Themes, 30, 1, (g, _) => g.Prefixe == "ORD"), new GenerateurAleatoire(1));
        Assert.All(td, e => Assert.Equal("ORD", e.Prefixe));
        Assert.DoesNotContain(td, e => e.Sujet.Cle == "projet"); // niveau 3 seulement
        Assert.Empty(CompositeurTd.Composer(new DemandeTdSolo(Themes, 30, 1, (_, _) => false), new GenerateurAleatoire(1)));
    }

    [Fact]
    public void Exercices_rediges_ne_gardent_que_les_cartes_exercice()
    {
        var cartes = new List<Revisions.Core.Modeles.Carte>
        {
            new("c1", "s1", Revisions.Core.Modeles.TypeCarte.Exercice, 2, new() { Enonce = "Calculer…", Reponse = "42", Etapes = ["Étape"] }),
            new("c2", "s1", Revisions.Core.Modeles.TypeCarte.Question, 1, new() { Recto = "?", Verso = "!" }),
        };
        var red = new GenerateurRediges("CEJM", cartes, new Dictionary<string, string> { ["s1"] = "Le contrat" });
        var sujet = Assert.Single(red.Sujets);
        Assert.Equal("Le contrat", sujet.Nom);
        Assert.IsType<ReponseLibre>(red.CreerExercice("c1", new GenerateurAleatoire(0), 1).Questions[0].Attendu);
    }
}

/// <summary>TD solo : historique et remise en révision (base SQLite).</summary>
public sealed class DepotTdTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "atome-td-" + Guid.NewGuid().ToString("N"));
    private readonly Revisions.Data.BaseDeDonnees _base;
    private readonly Revisions.Data.DepotTd _depot;
    private static readonly DateOnly Jour = new(2026, 11, 18);

    public DepotTdTests()
    {
        Directory.CreateDirectory(_dossier);
        _base = new Revisions.Data.BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        File.WriteAllText(Path.Combine(_dossier, "pack.json"), PackExemple.Json);
        new Revisions.Data.ImportateurPacks(_base, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)))
            .ImporterFichier(Path.Combine(_dossier, "pack.json"), null);
        _depot = new Revisions.Data.DepotTd(_base);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dossier, true); } catch (IOException) { }
    }

    [Fact]
    public void Historique_compte_et_repere_les_echecs_recents()
    {
        _depot.EnregistrerExercice(Jour, "GRA", "fermeture", "maths-sq2-s04-s05", IssueExercice.PasEncore);
        _depot.EnregistrerExercice(Jour, "GRA", "fermeture", "maths-sq2-s04-s05", IssueExercice.PremierCoup);
        _depot.EnregistrerExercice(Jour, "IPV", "masque", null, IssueExercice.PremierCoup);
        var h = _depot.Historique();
        Assert.Equal(new BilanSujet(2, true), h["GRA/fermeture"]);
        Assert.Equal(new BilanSujet(1, false), h["IPV/masque"]);
    }

    [Fact]
    public void Remise_en_revision_des_cartes_deja_vues_seulement()
    {
        using (var c = _base.Ouvrir())
        {
            Dapper.SqlMapper.Execute(c, "INSERT OR REPLACE INTO etat_carte (carte_id, stabilite, difficulte_fsrs, echeance, repetitions, oublis) VALUES ('maths-sq1-s03-c001', 12, 5, '2026-12-20', 2, 0)");
            Dapper.SqlMapper.Execute(c, "INSERT OR REPLACE INTO etat_carte (carte_id, stabilite, difficulte_fsrs, echeance, repetitions, oublis) VALUES ('maths-sq1-s03-c002', NULL, NULL, NULL, 0, 0)");
        }
        Assert.Equal(1, _depot.RemettreEnRevision("maths-sq1-s03-s01", Jour));
        using var cx = _base.Ouvrir();
        Assert.Equal("2026-11-18", Dapper.SqlMapper.ExecuteScalar<string>(cx, "SELECT echeance FROM etat_carte WHERE carte_id = 'maths-sq1-s03-c001'"));
        Assert.Equal(12.0, Dapper.SqlMapper.ExecuteScalar<double>(cx, "SELECT stabilite FROM etat_carte WHERE carte_id = 'maths-sq1-s03-c001'"));
        Assert.Equal(0, _depot.RemettreEnRevision("maths-sq1-s03-s01", Jour)); // déjà due aujourd'hui : rien à faire
    }
}
