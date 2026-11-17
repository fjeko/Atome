using Revisions.Core.Bibliotheque;
using Revisions.Core.Modeles;
using Revisions.Core.Planning;
using Revisions.Core.Sessions;

namespace Revisions.Tests;

/// <summary>
/// Choix des cartes d'une session : types de session, paquets de matière, motifs, verrouillage des cours non lus,
/// respect des collections masquées, du plafond et du mode reprise, durée estimée.
/// </summary>
public class ChoixSessionTests
{
    private static DateOnly J(int annee, int mois, int jour) => new(annee, mois, jour);
    private static readonly DateOnly Jour = J(2026, 11, 17); // un mardi

    // ---- Fabriques ---------------------------------------------------------------------------

    private static ResumeSeance S(string id, string matiere, string partie = "P1", int ordre = 1, bool lu = false) =>
        new() { PackId = id, Matiere = matiere, Partie = partie, Ordre = ordre, Seance = id, Lu = lu };

    /// <summary>Carte déjà vue : revue il y a <paramref name="joursDepuis"/> jours, due dans <paramref name="dueDans"/> jours (≤ 0 : due).</summary>
    private static CarteVue V(string id, string pack, int joursDepuis = 5, int dueDans = 0, double stabilite = 5, int oublis = 0, int ratees = 0) =>
        new(id, pack, stabilite, Jour.AddDays(-joursDepuis), Jour.AddDays(dueDans), oublis, ratees);

    private static CandidateNouvelle N(string id, string pack, int position = 1, bool prioritaire = false) => new(id, pack, position, prioritaire);

    private static List<CandidateNouvelle> Nouvelles(string pack, int nombre) =>
        Enumerable.Range(1, nombre).Select(i => N($"{pack}#n{i}", pack, i)).ToList();

    private static DonneesSession Donnees(
        IReadOnlyList<ResumeSeance> seances,
        IEnumerable<CarteVue>? vues = null,
        IEnumerable<CandidateNouvelle>? nouvelles = null,
        IEnumerable<string>? vus = null,
        bool nonLues = false,
        PlanningEffectif? planning = null,
        Dictionary<string, DateOnly>? lectures = null,
        Dictionary<string, ReussiteSeance>? reussites = null) => new()
    {
        Jour = Jour,
        Seances = seances,
        Vues = (vues ?? []).ToList(),
        Nouvelles = (nouvelles ?? []).ToList(),
        PacksVus = (vus ?? []).ToHashSet(),
        NouvellesNonLues = nonLues,
        Planning = planning,
        DernieresLectures = lectures ?? [],
        Reussites = reussites ?? [],
    };

    private static List<string> Ids(SessionComposee s) => s.Cartes.Select(c => c.CarteId).ToList();

    /// <summary>Suites de cartes de la même matière : [("Maths", 5), ("Anglais", 4)…].</summary>
    private static List<(string Matiere, int Nombre)> Suites(SessionComposee s, DonneesSession d)
    {
        var parPack = d.Seances.ToDictionary(x => x.PackId);
        var suites = new List<(string Matiere, int Nombre)>();
        foreach (var c in s.Cartes)
        {
            string m = parPack[c.PackId].Matiere;
            if (suites.Count > 0 && suites[^1].Matiere == m) suites[^1] = (m, suites[^1].Nombre + 1);
            else suites.Add((m, 1));
        }
        return suites;
    }

    private static Periode P(string matiere, string libelle, DateOnly debut, DateOnly fin, params string[] cibles) =>
        new($"periode:{matiere}|{libelle}", matiere, libelle, debut, fin, cibles);

    private static Devoir D(string matiere, string code, DateOnly semaine) =>
        new($"devoir:{matiere}|{code}", matiere, code, !code.StartsWith("QZ"), semaine);

    /// <summary>
    /// Planning d'essai : Maths SQ1 passée, SQ2 en cours (02/11 → 29/11), SQ3 future ; Bloc 1 B1.3 en cours.
    /// Devoir Maths DV1 la semaine du 23/11 (en préparation le 17/11) si <paramref name="avecDevoir"/>.
    /// </summary>
    private static PlanningEffectif Planning(bool avecDevoir = true, bool suivre = true) => new(new PlanningCned("essai", 1, "Essai",
        [
            P("Maths", "SQ1", J(2026, 9, 7), J(2026, 10, 4), "maths-sq1"),
            P("Maths", "SQ2", J(2026, 11, 2), J(2026, 11, 29), "maths-sq2"),
            P("Maths", "SQ3", J(2027, 1, 11), J(2027, 2, 7), "maths-sq3"),
            P("Bloc 1", "B1.3", J(2026, 11, 9), J(2026, 12, 20), "b1-b13"),
        ],
        avecDevoir ? [D("Maths", "DV1", J(2026, 11, 23))] : []), new ReglagesPlanning { Suivre = suivre });

    // ---- Session du jour ---------------------------------------------------------------------

    [Fact]
    public void Session_du_jour_les_cartes_a_revoir_les_plus_fragiles_passent_avant_les_nouvelles()
    {
        var d = Donnees([S("m1", "Maths", lu: true)],
            vues: [V("solide", "m1", joursDepuis: 2, stabilite: 100), V("fragile", "m1", joursDepuis: 30, stabilite: 2), V("pas-due", "m1", dueDans: 3)],
            nouvelles: Nouvelles("m1", 3));

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(["fragile", "solide", "m1#n1", "m1#n2", "m1#n3"], Ids(s));
        Assert.Equal(3, s.NbNouvelles);
        Assert.Equal(TypeSession.DuJour, s.Type);
    }

    [Fact]
    public void Session_du_jour_respecte_le_plafond_et_la_limite_de_nouvelles()
    {
        var d = Donnees([S("m1", "Maths", lu: true)],
            vues: Enumerable.Range(0, 30).Select(i => V($"d{i}", "m1")),
            nouvelles: Nouvelles("m1", 30));

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, maxCartes: 35, maxNouvelles: 10);
        Assert.Equal(35, s.Cartes.Count);
        Assert.Equal(5, s.NbNouvelles);                  // 30 cartes à revoir + 5 places restantes

        Assert.Equal(10, CompositeurSession.Composer(Donnees([S("m1", "Maths", lu: true)], nouvelles: Nouvelles("m1", 30)),
            DemandeSession.DuJour, 35, 10).Cartes.Count);
        Assert.Empty(CompositeurSession.Composer(d, DemandeSession.DuJour, 0, 10).Cartes);
    }

    // ---- Pas de nouvelle carte d'un cours non lu ---------------------------------------------

    [Fact]
    public void Une_nouvelle_carte_d_un_cours_non_lu_n_est_pas_proposee()
    {
        var d = Donnees([S("lu", "Maths", lu: true), S("pas-lu", "Maths", ordre: 2)],
            nouvelles: [.. Nouvelles("lu", 2), .. Nouvelles("pas-lu", 4)]);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(["lu#n1", "lu#n2"], Ids(s));
        Assert.Equal(4, s.NouvellesBloquees);
        Assert.True(s.DebloquerUtile);                   // 2 nouvelles sur 10 possibles : il manque de quoi lire
    }

    [Fact]
    public void Vu_ou_Revise_dans_Mes_acquis_et_les_cartes_prioritaires_debloquent_aussi()
    {
        var d = Donnees([S("acquis", "Maths"), S("rien", "Maths", ordre: 2), S("prio", "Maths", ordre: 3)],
            nouvelles: [N("acquis#1", "acquis"), N("rien#1", "rien"), N("prio#1", "prio", prioritaire: true)],
            vus: ["acquis"]);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(["acquis#1", "prio#1"], Ids(s).Order());
        Assert.Equal(1, s.NouvellesBloquees);
    }

    [Fact]
    public void Les_cartes_deja_vues_d_un_cours_non_lu_ne_sont_jamais_bloquees()
    {
        var d = Donnees([S("pas-lu", "Maths")], vues: [V("revue", "pas-lu")], nouvelles: Nouvelles("pas-lu", 2));

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(["revue"], Ids(s));
        Assert.Equal(2, s.NouvellesBloquees);
    }

    [Fact]
    public void Le_reglage_inverse_propose_les_nouvelles_cartes_des_cours_non_lus()
    {
        var d = Donnees([S("pas-lu", "Maths")], nouvelles: Nouvelles("pas-lu", 3), nonLues: true);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(3, s.NbNouvelles);
        Assert.Equal(0, s.NouvellesBloquees);
        Assert.False(s.DebloquerUtile);
        Assert.All(s.Cartes, c => Assert.Equal("Nouvelle · à découvrir", c.Motif.Texte));
        Assert.Empty(CompositeurSession.SeancesADebloquer(d));
    }

    [Fact]
    public void Debloquer_n_est_pas_utile_quand_la_limite_de_nouvelles_est_deja_remplie()
    {
        var d = Donnees([S("lu", "Maths", lu: true), S("pas-lu", "Maths", ordre: 2)],
            nouvelles: [.. Nouvelles("lu", 10), .. Nouvelles("pas-lu", 4)]);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(10, s.NbNouvelles);
        Assert.False(s.DebloquerUtile);
    }

    [Fact]
    public void Seances_a_lire_pour_debloquer_sans_planning_matieres_commencees_d_abord_une_par_matiere()
    {
        var d = Donnees(
            [
                S("algo-1", "Algo"), S("algo-2", "Algo", ordre: 2),
                S("maths-1", "Maths", lu: true), S("maths-2", "Maths", ordre: 2), S("maths-3", "Maths", ordre: 3),
                S("ang-1", "Anglais"), S("cejm-1", "CEJM"),
            ],
            nouvelles: [.. Nouvelles("algo-1", 1), .. Nouvelles("algo-2", 1), .. Nouvelles("maths-2", 1), .. Nouvelles("maths-3", 1),
                        .. Nouvelles("ang-1", 1), .. Nouvelles("cejm-1", 1)]);

        var suggestions = CompositeurSession.SeancesADebloquer(d, 3);

        Assert.Equal(["maths-2", "algo-1", "ang-1"], suggestions.Select(x => x.Seance.PackId));
        Assert.Equal("La suite de ce que tu as lu", suggestions[0].Raison);
        Assert.Equal("Pour commencer cette matière", suggestions[1].Raison);
    }

    [Fact]
    public void Seances_a_lire_pour_debloquer_avec_planning_suivent_la_logique_au_programme()
    {
        var d = Donnees([S("maths-sq2-s01", "Maths", "SQ2"), S("maths-sq3-s01", "Maths", "SQ3"), S("b1-b13-s01", "Bloc 1", "B1.3")],
            nouvelles: [.. Nouvelles("maths-sq2-s01", 1), .. Nouvelles("maths-sq3-s01", 1), .. Nouvelles("b1-b13-s01", 1)],
            planning: Planning(avecDevoir: false));

        var suggestions = CompositeurSession.SeancesADebloquer(d, 3);

        // Périodes en cours d'abord (une matière à la fois), puis la prochaine période.
        Assert.Equal(["b1-b13-s01", "maths-sq2-s01", "maths-sq3-s01"], suggestions.Select(x => x.Seance.PackId));
        Assert.Equal("Au programme en ce moment", suggestions[1].Raison);
        Assert.StartsWith("Bientôt", suggestions[2].Raison);
    }

    // ---- Collections, plafond, mode reprise --------------------------------------------------

    [Fact]
    public void Les_cartes_des_collections_masquees_ne_sont_proposees_dans_aucun_type_de_session()
    {
        // « cache » n'est pas dans les séances : c'est ce qui arrive quand sa collection est masquée.
        var d = Donnees([S("visible", "Maths", lu: true)],
            vues: [V("v1", "visible", oublis: 2), V("c1", "cache", oublis: 5)],
            nouvelles: [.. Nouvelles("visible", 1), .. Nouvelles("cache", 3)],
            vus: ["cache"],
            lectures: new() { ["visible"] = Jour, ["cache"] = Jour });

        foreach (var demande in new[] { DemandeSession.DuJour, DemandeSession.PointsFaibles, DemandeSession.LectureRecente,
                                         DemandeSession.Perimetre(["visible", "cache"]) })
        {
            var s = CompositeurSession.Composer(d, demande, 40, 10);
            Assert.All(s.Cartes, c => Assert.Equal("visible", c.PackId));
            Assert.NotEmpty(s.Cartes);
        }
        Assert.Empty(CompositeurSession.SeancesADebloquer(d));
    }

    [Fact]
    public void Plafond_atteint_aucun_type_de_session_ne_propose_de_carte()
    {
        var d = Donnees([S("maths-sq2-s01", "Maths", "SQ2", lu: true)],
            vues: [V("v1", "maths-sq2-s01", oublis: 2)], nouvelles: Nouvelles("maths-sq2-s01", 2),
            planning: Planning(), lectures: new() { ["maths-sq2-s01"] = Jour });

        foreach (var type in Enum.GetValues<TypeSession>())
            Assert.Empty(CompositeurSession.Composer(d, new DemandeSession(type, new HashSet<string> { "maths-sq2-s01" }), 0, 10).Cartes);
    }

    [Fact]
    public void Mode_reprise_pas_de_nouvelle_carte_et_taille_reduite_pour_tous_les_types()
    {
        // 12 jours d'absence : mode reprise, session de 10 cartes, pas de nouvelle carte.
        var plan = CalculPlanDuJour.Calculer(new ContexteJour(Jour, Jour.AddDays(-12), null, 30, 0, 0, new Reglages()));
        Assert.True(plan.ModeReprise);

        var seances = new[] { S("maths-sq2-s01", "Maths", "SQ2", lu: true) };
        var d = Donnees(seances,
            vues: Enumerable.Range(0, 30).Select(i => V($"v{i}", "maths-sq2-s01", oublis: 1)),
            nouvelles: Nouvelles("maths-sq2-s01", 10),
            planning: Planning(), lectures: new() { ["maths-sq2-s01"] = Jour.AddDays(-1) });

        foreach (var type in Enum.GetValues<TypeSession>())
        {
            var s = CompositeurSession.Composer(d, new DemandeSession(type, new HashSet<string> { "maths-sq2-s01" }), plan.MaxCartes, plan.MaxNouvelles);
            Assert.Equal(plan.MaxCartes, s.Cartes.Count);
            Assert.Equal(0, s.NbNouvelles);
            Assert.False(s.DebloquerUtile);
        }
    }

    // ---- Paquets de matière ------------------------------------------------------------------

    [Theory]
    [InlineData(0, new int[0])]
    [InlineData(3, new[] { 3 })]
    [InlineData(6, new[] { 6 })]
    [InlineData(7, new[] { 4, 3 })]
    [InlineData(10, new[] { 5, 5 })]
    [InlineData(13, new[] { 5, 4, 4 })]
    [InlineData(25, new[] { 5, 5, 5, 5, 5 })]
    public void Decouper_fait_des_paquets_de_6_au_plus_presque_egaux(int nombre, int[] tailles)
    {
        Assert.Equal(tailles, Paquets.Decouper(Enumerable.Range(0, nombre).ToList()).Select(p => p.Count));
    }

    [Fact]
    public void Les_paquets_de_4_a_6_cartes_alternent_entre_les_matieres()
    {
        var seances = new[] { S("m", "Maths"), S("a", "Anglais"), S("g", "Algo") };
        // Les plus fragiles sont en maths, puis en anglais, puis en algo.
        var vues = Enumerable.Range(0, 10).Select(i => V($"m{i}", "m", joursDepuis: 40))
            .Concat(Enumerable.Range(0, 7).Select(i => V($"a{i}", "a", joursDepuis: 20)))
            .Concat(Enumerable.Range(0, 3).Select(i => V($"g{i}", "g", joursDepuis: 10)));
        var d = Donnees(seances, vues: vues);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal([("Maths", 5), ("Anglais", 4), ("Algo", 3), ("Maths", 5), ("Anglais", 3)], Suites(s, d));
    }

    [Fact]
    public void Dans_un_paquet_les_cartes_d_une_meme_partie_sont_rapprochees()
    {
        var seances = new[] { S("p1", "Maths", "Partie 1"), S("p2", "Maths", "Partie 2") };
        // Ordre de fragilité : p1, p2, p1, p2 : on regroupe par partie (la partie la plus fragile d'abord).
        var vues = new[] { V("a", "p1", 40), V("b", "p2", 30), V("c", "p1", 20), V("e", "p2", 10) };
        var s = CompositeurSession.Composer(Donnees(seances, vues: vues), DemandeSession.DuJour, 40, 10);

        Assert.Equal(["a", "c", "b", "e"], Ids(s));
    }

    [Fact]
    public void Les_nouvelles_cartes_suivent_les_cartes_a_revoir_de_leur_matiere()
    {
        var seances = new[] { S("m", "Maths", lu: true), S("a", "Anglais", lu: true) };
        var d = Donnees(seances, vues: [V("m-rev", "m", 30), V("a-rev", "a", 20)], nouvelles: [.. Nouvelles("m", 2), .. Nouvelles("a", 2)]);

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        Assert.Equal(["m-rev", "m#n1", "m#n2", "a-rev", "a#n1", "a#n2"], Ids(s));
    }

    [Fact]
    public void Une_carte_ratee_revient_si_possible_dans_un_paquet_de_sa_matiere()
    {
        static CarteAReviser C(string id, string matiere) =>
            new(new Carte(id, "s", TypeCarte.Question, 1, new ContenuCarte()), new EtatCarte { CarteId = id }, "p", matiere, "S");

        // M0 A1 A2 A3 A4 A5 M6 A7 A8 : ratée, M0 revient après M6 (et non au milieu des cartes d'anglais).
        var cartes = new List<CarteAReviser> { C("M0", "Maths") };
        cartes.AddRange(Enumerable.Range(1, 5).Select(i => C($"A{i}", "Anglais")));
        cartes.Add(C("M6", "Maths"));
        cartes.AddRange(Enumerable.Range(7, 2).Select(i => C($"A{i}", "Anglais")));
        var session = new SessionEnCours(cartes);

        var ordre = new List<string>();
        session.Avancer(ratee: true);
        ordre.Add("M0");
        while (!session.EstTerminee)
        {
            ordre.Add(session.Courante!.Carte.Id);
            session.Avancer(ratee: false);
        }
        Assert.Equal(["M0", "A1", "A2", "A3", "A4", "A5", "M6", "M0", "A7", "A8"], ordre);
    }

    [Fact]
    public void Une_carte_ratee_sans_autre_carte_de_sa_matiere_revient_5_cartes_plus_loin()
    {
        static CarteAReviser C(string id, string matiere) =>
            new(new Carte(id, "s", TypeCarte.Question, 1, new ContenuCarte()), new EtatCarte { CarteId = id }, "p", matiere, "S");

        var cartes = new List<CarteAReviser> { C("M0", "Maths") };
        cartes.AddRange(Enumerable.Range(1, 8).Select(i => C($"A{i}", "Anglais")));
        var session = new SessionEnCours(cartes);
        session.Avancer(ratee: true);
        for (int i = 0; i < SessionEnCours.EcartRetour; i++) session.Avancer(ratee: false);

        Assert.Equal("M0", session.Courante!.Carte.Id);
        Assert.True(session.CouranteEstUnRetour);
    }

    // ---- Motifs ------------------------------------------------------------------------------

    [Fact]
    public void Motif_a_revoir_dit_quand_la_carte_a_ete_vue()
    {
        var d = Donnees([S("m", "Maths")], vues: [V("six", "m", joursDepuis: 6), V("hier", "m", joursDepuis: 1, stabilite: 1)]);
        var motifs = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10).Cartes.ToDictionary(c => c.CarteId, c => c.Motif);

        Assert.Equal("À revoir · vue il y a 6 jours", motifs["six"].Texte);
        Assert.Equal("À revoir · vue hier", motifs["hier"].Texte);
        Assert.Equal(GenreMotif.ARevoir, motifs["six"].Genre);
        Assert.Equal("aujourd'hui", Motifs.IlYa(Jour, Jour));
    }

    [Fact]
    public void Motif_d_une_nouvelle_carte_lue_vue_ou_au_programme()
    {
        var d = Donnees(
            [S("lu", "Algo", lu: true), S("vu", "CEJM"), S("maths-sq2-s01", "Maths", "SQ2"), S("ailleurs", "Anglais")],
            nouvelles: [N("lu#1", "lu"), N("vu#1", "vu"), N("prog#1", "maths-sq2-s01"), N("autre#1", "ailleurs")],
            vus: ["vu"], nonLues: true, planning: Planning(avecDevoir: false));

        var motifs = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10).Cartes.ToDictionary(c => c.CarteId, c => c.Motif.Texte);

        Assert.Equal("Nouvelle · tu as lu ce cours", motifs["lu#1"]);
        Assert.Equal("Nouvelle · tu as indiqué avoir vu ce cours", motifs["vu#1"]);
        Assert.Equal("Nouvelle · au programme cette semaine", motifs["prog#1"]);
        Assert.Equal("Nouvelle · à découvrir", motifs["autre#1"]);
    }

    [Fact]
    public void Motif_point_faible_et_preparation_de_devoir()
    {
        Assert.Equal("Point faible · ratée 3 fois", Motifs.PointFaible(3, 0.5).Texte);
        Assert.Equal("Point faible · séance réussie à 55 %", Motifs.PointFaible(0, 0.55).Texte);
        Assert.Equal("Préparation du devoir DV1 (semaine du 30/11)", Motifs.Devoir(D("Maths", "DV1", J(2026, 11, 30))).Texte);
        Assert.Equal("Préparation du quiz QZ2 (semaine du 30/11)", Motifs.Devoir(D("Bloc 1", "QZ2", J(2026, 11, 30))).Texte);
    }

    [Fact]
    public void Session_du_jour_une_carte_avancee_avant_un_devoir_porte_le_motif_du_devoir()
    {
        // Revue il y a 2 jours, stabilité 3 jours, prévue dans 30 jours : elle risque d'être oubliée le jour du DV1.
        var d = Donnees([S("maths-sq2-s01", "Maths", "SQ2", lu: true)],
            vues: [V("fragile", "maths-sq2-s01", joursDepuis: 2, dueDans: 30, stabilite: 3)],
            nouvelles: Nouvelles("maths-sq2-s01", 1), planning: Planning());

        var s = CompositeurSession.Composer(d, DemandeSession.DuJour, 40, 10);

        var avancee = Assert.Single(s.Cartes, c => c.CarteId == "fragile");
        Assert.True(avancee.Avancee);
        Assert.Equal("Préparation du devoir DV1 (semaine du 23/11)", avancee.Motif.Texte);
        // Une nouvelle carte du contenu du devoir dit aussi pourquoi elle arrive maintenant.
        Assert.Equal("Préparation du devoir DV1 (semaine du 23/11)", s.Cartes.Single(c => c.Nouvelle).Motif.Texte);
    }

    // ---- Une matière ou une partie -----------------------------------------------------------

    [Fact]
    public void Perimetre_seulement_les_seances_choisies_cartes_dues_puis_nouvelles_dans_la_limite()
    {
        var seances = new[] { S("m1", "Maths", lu: true), S("m2", "Maths", ordre: 2, lu: true), S("a1", "Anglais", lu: true) };
        var d = Donnees(seances,
            vues: [V("m1-due", "m1"), V("m2-due", "m2", joursDepuis: 9), V("m1-pas-due", "m1", dueDans: 4), V("a1-due", "a1")],
            nouvelles: [.. Nouvelles("m1", 5), .. Nouvelles("m2", 5), .. Nouvelles("a1", 5)]);

        var s = CompositeurSession.Composer(d, DemandeSession.Perimetre(["m1", "m2"]), 40, maxNouvelles: 3);

        Assert.Equal(["m2-due", "m1-due", "m1#n1", "m1#n2", "m1#n3"], Ids(s));
        Assert.Equal(TypeSession.Perimetre, s.Type);
    }

    [Fact]
    public void PacksDe_rassemble_toutes_les_seances_d_un_niveau_de_la_bibliotheque()
    {
        var racine = ArbreBibliotheque.Construire([S("m1", "Maths", "SQ1"), S("m2", "Maths", "SQ2"), S("a1", "Anglais")]);
        var maths = racine.Enfants.Single(n => n.Titre == "Maths");

        Assert.Equal(["m1", "m2"], CompositeurSession.PacksDe(maths).Order());
        Assert.Equal(3, CompositeurSession.PacksDe(racine).Count);
    }

    // ---- Mes points faibles ------------------------------------------------------------------

    [Fact]
    public void Points_faibles_les_plus_ratees_d_abord_et_les_seances_au_taux_bas()
    {
        var seances = new[] { S("m1", "Maths"), S("m2", "Maths", ordre: 2), S("a1", "Anglais"), S("solide", "CEJM") };
        var d = Donnees(seances,
            vues:
            [
                V("trois", "m1", dueDans: 10, oublis: 1, ratees: 3),
                V("une", "a1", dueDans: 10, ratees: 1),
                V("deux", "m1", dueDans: 10, oublis: 2),
                V("jamais-ratee-seance-faible", "m2", dueDans: 10),
                V("jamais-ratee", "solide", dueDans: 10),
            ],
            nouvelles: Nouvelles("m1", 3),
            reussites: new()
            {
                ["m2"] = new ReussiteSeance(10, 5),      // 50 % : séance faible
                ["solide"] = new ReussiteSeance(10, 10),
                ["a1"] = new ReussiteSeance(3, 1),       // trop peu de réponses pour juger la séance
            });

        var s = CompositeurSession.Composer(d, DemandeSession.PointsFaibles, 40, 10);

        // Les plus ratées d'abord (3, 2, 1, 0), puis rangées en paquets de matière : les maths ensemble, puis l'anglais.
        Assert.Equal(["trois", "deux", "jamais-ratee-seance-faible", "une"], Ids(s));
        Assert.Equal(0, s.NbNouvelles);
        Assert.Equal("Point faible · ratée 3 fois", s.Cartes.Single(c => c.CarteId == "trois").Motif.Texte);
        Assert.Equal("Point faible · séance réussie à 50 %", s.Cartes.Single(c => c.CarteId == "jamais-ratee-seance-faible").Motif.Texte);

        // Séances concernées, les plus ratées d'abord.
        Assert.Equal(["m1", "a1", "m2"], s.SeancesFaibles.Select(x => x.Seance.PackId));
        Assert.Equal(5, s.SeancesFaibles[0].FoisRatees);
        Assert.Equal(2, s.SeancesFaibles[0].NbCartes);
        Assert.Equal(0.5, s.SeancesFaibles[2].Taux);
        Assert.Null(s.SeancesFaibles[1].Taux);          // 3 réponses seulement : taux non affiché
    }

    [Fact]
    public void Points_faibles_dans_la_limite_du_jour_les_plus_ratees_en_premier()
    {
        var d = Donnees([S("m1", "Maths")], vues: Enumerable.Range(1, 10).Select(i => V($"r{i}", "m1", ratees: i)));

        var s = CompositeurSession.Composer(d, DemandeSession.PointsFaibles, maxCartes: 3, maxNouvelles: 10);

        Assert.Equal(["r10", "r9", "r8"], Ids(s));
    }

    // ---- Ce que je viens de lire -------------------------------------------------------------

    [Fact]
    public void Lecture_recente_les_seances_lues_ces_7_derniers_jours()
    {
        var seances = new[] { S("recent", "Maths", lu: true), S("limite", "Maths", ordre: 2, lu: true), S("ancien", "Maths", ordre: 3, lu: true) };
        var d = Donnees(seances,
            vues:
            [
                V("recent-due", "recent"),
                V("recent-pas-due", "recent", joursDepuis: 3, dueDans: 5),
                V("recent-vue-aujourdhui", "recent", joursDepuis: 0, dueDans: 2),
                V("ancien-due", "ancien"),
            ],
            nouvelles: [.. Nouvelles("limite", 1), .. Nouvelles("ancien", 1)],
            lectures: new() { ["recent"] = Jour, ["limite"] = Jour.AddDays(-6), ["ancien"] = Jour.AddDays(-7) });

        var s = CompositeurSession.Composer(d, DemandeSession.LectureRecente, 40, 10);

        Assert.Equal(["recent-due", "limite#n1", "recent-pas-due"], Ids(s));
        Assert.Equal("Tu viens de lire ce cours · vue il y a 3 jours", s.Cartes[2].Motif.Texte);
        Assert.Equal(["limite", "recent"], CompositeurSession.PacksLusRecemment(d).Order());
    }

    // ---- Préparer un devoir ------------------------------------------------------------------

    [Fact]
    public void Devoir_sans_planning_aucune_carte()
    {
        var d = Donnees([S("m1", "Maths", lu: true)], vues: [V("v", "m1")], nouvelles: Nouvelles("m1", 2));

        var s = CompositeurSession.Composer(d, DemandeSession.Devoir, 40, 10);

        Assert.Empty(s.Cartes);
        Assert.Null(s.Devoir);
        Assert.Null(CompositeurSession.ProchainDevoir(d));
    }

    [Fact]
    public void Devoir_le_contenu_couvert_cartes_dues_cartes_fragiles_le_jour_J_et_nouvelles_des_cours_lus()
    {
        var seances = new[]
        {
            S("maths-sq2-s01", "Maths", "SQ2", lu: true), S("maths-sq2-s02", "Maths", "SQ2", ordre: 2),
            S("maths-sq3-s01", "Maths", "SQ3", lu: true), S("b1-b13-s01", "Bloc 1", "B1.3", lu: true),
        };
        var d = Donnees(seances,
            vues:
            [
                V("due", "maths-sq2-s01"),
                V("fragile-jour-j", "maths-sq2-s01", joursDepuis: 2, dueDans: 30, stabilite: 3),
                V("solide", "maths-sq2-s01", joursDepuis: 2, dueDans: 300, stabilite: 400),
                V("hors-devoir", "b1-b13-s01"),
            ],
            nouvelles: [.. Nouvelles("maths-sq2-s01", 1), .. Nouvelles("maths-sq2-s02", 1), .. Nouvelles("maths-sq3-s01", 1)],
            planning: Planning());

        var s = CompositeurSession.Composer(d, DemandeSession.Devoir, 40, 10);

        Assert.Equal("DV1", s.Devoir!.Code);
        Assert.Equal(["due", "fragile-jour-j", "maths-sq2-s01#n1"], Ids(s));
        Assert.All(s.Cartes, c => Assert.Equal("Préparation du devoir DV1 (semaine du 23/11)", c.Motif.Texte));
        Assert.Equal(1, s.NouvellesBloquees);           // maths-sq2-s02 pas encore lue
    }

    [Fact]
    public void Prochain_devoir_le_plus_proche_qui_porte_sur_la_bibliotheque()
    {
        var planning = new PlanningEffectif(new PlanningCned("essai", 1, "Essai",
            [P("Maths", "SQ2", J(2026, 11, 2), J(2026, 11, 29), "maths-sq2"), P("CEJM", "T1", J(2026, 11, 2), J(2026, 11, 29), "cejm-t1")],
            [D("Maths", "DV0", J(2026, 10, 5)), D("CEJM", "DV1", J(2026, 11, 23)), D("Maths", "DV1", J(2026, 12, 7))]),
            new ReglagesPlanning());

        // Aucune séance de CEJM dans la bibliothèque : on passe au devoir de maths.
        var d = Donnees([S("maths-sq2-s01", "Maths", "SQ2")], planning: planning);
        Assert.Equal("Maths", CompositeurSession.ProchainDevoir(d)!.Matiere);
        Assert.Equal("DV1", CompositeurSession.ProchainDevoir(d)!.Code);
    }

    // ---- Durée estimée -----------------------------------------------------------------------

    [Fact]
    public void Duree_estimee_20_secondes_par_carte_sans_historique_sinon_la_mediane()
    {
        Assert.Equal(20, EstimationDuree.SecondesParCarte([]));
        Assert.Equal(20, EstimationDuree.SecondesParCarte(Enumerable.Repeat(8000, 9)));
        Assert.Equal(8, EstimationDuree.SecondesParCarte(Enumerable.Repeat(8000, 10)));
        // Médiane (une carte laissée ouverte une heure ne fausse rien) et bornes.
        Assert.Equal(12, EstimationDuree.SecondesParCarte([.. Enumerable.Repeat(12000, 10), 3_600_000]));
        Assert.Equal(5, EstimationDuree.SecondesParCarte(Enumerable.Repeat(1000, 10)));
        Assert.Equal(120, EstimationDuree.SecondesParCarte(Enumerable.Repeat(600_000, 10)));
    }

    [Fact]
    public void Duree_estimee_en_minutes_et_en_texte()
    {
        Assert.Equal(0, EstimationDuree.Minutes(0, 20));
        Assert.Equal(1, EstimationDuree.Minutes(1, 20));
        Assert.Equal(4, EstimationDuree.Minutes(12, 20));
        Assert.Equal("12 cartes · environ 4 min", EstimationDuree.Texte(12, 20));
        Assert.Equal("1 carte · environ 1 min", EstimationDuree.Texte(1, 20));
        Assert.Equal("Aucune carte pour l'instant", EstimationDuree.Texte(0, 20));
    }

    [Fact]
    public void Le_fil_d_une_carte_donne_matiere_partie_et_seance()
    {
        var carte = new CarteAReviser(new Carte("c", "s", TypeCarte.Question, 1, new ContenuCarte()), new EtatCarte { CarteId = "c" },
            "p", "Maths", "Séance 3", "SQ1 – Suites");
        Assert.Equal("Maths › SQ1 – Suites › Séance 3", carte.Fil);
        Assert.Equal("Maths › Séance 3", (carte with { Partie = "" }).Fil);
    }
}
