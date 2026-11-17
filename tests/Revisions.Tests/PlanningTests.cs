using Revisions.Core.Bibliotheque;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Planning;
using Revisions.Core.Sessions;

namespace Revisions.Tests;

/// <summary>
/// Planning du CNED : lecture du fichier, priorité des nouvelles cartes, couverture des devoirs,
/// avancement avant une échéance, surcharges et absence de planning.
/// </summary>
public class PlanningTests
{
    private static DateOnly J(int annee, int mois, int jour) => new(annee, mois, jour);
    private static readonly DateOnly Aujourdhui = J(2026, 11, 17); // un mardi

    // ---- Fabriques ---------------------------------------------------------------------------

    private static Periode P(string matiere, string libelle, DateOnly debut, DateOnly fin, params string[] cibles) =>
        new($"periode:{matiere}|{libelle}", matiere, libelle, debut, fin, cibles);

    private static Devoir D(string matiere, string code, DateOnly semaine, bool? note = null) =>
        new($"devoir:{matiere}|{code}", matiere, code, note ?? !code.StartsWith("QZ"), semaine);

    private static ResumeSeance S(string id, string matiere, string partie, int ordre = 1) =>
        new() { PackId = id, Matiere = matiere, Partie = partie, Ordre = ordre, Seance = id };

    /// <summary>Deux cartes par séance (positions 1 et 2) : « id#1 », « id#2 ».</summary>
    private static List<CandidateNouvelle> Cartes(IEnumerable<ResumeSeance> seances, int parSeance = 2) =>
        seances.SelectMany(s => Enumerable.Range(1, parSeance).Select(i => new CandidateNouvelle($"{s.PackId}#{i}", s.PackId, i))).ToList();

    /// <summary>
    /// Planning d'essai (au 17/11/2026) :
    /// Maths SQ1 passée, SQ2 en cours, SQ3 future ; Bloc 1 B1.1 passée, B1.3 en cours ;
    /// Anglais en continu ; devoirs Maths DV0 (05/10) et DV1 (30/11), Bloc 1 QZ1 (02/11) et QZ2 (30/11).
    /// </summary>
    private static PlanningCned Planning() => new("essai", 1, "Essai",
        [
            P("Maths", "SQ1", J(2026, 9, 7), J(2026, 10, 4), "maths-sq1"),
            P("Maths", "SQ2", J(2026, 11, 2), J(2026, 11, 29), "maths-sq2"),
            P("Maths", "SQ3", J(2027, 1, 11), J(2027, 2, 7), "maths-sq3"),
            P("Bloc 1", "B1.1", J(2026, 9, 21), J(2026, 10, 4), "b1-b11"),
            P("Bloc 1", "B1.3", J(2026, 11, 9), J(2026, 12, 20), "b1-b13"),
            new Periode("periode:Anglais|B2", "Anglais", "B2", J(2026, 9, 7), J(2027, 5, 16), ["ang"], Continu: true),
        ],
        [
            D("Maths", "DV0", J(2026, 10, 5)),
            D("Maths", "DV1", J(2026, 11, 30)),
            D("Bloc 1", "QZ1", J(2026, 11, 2)),
            D("Bloc 1", "QZ2", J(2026, 11, 30)),
            D("Anglais", "DV1", J(2026, 11, 2)),
            D("Anglais", "DV2", J(2027, 5, 10)),
        ]);

    private static PlanningEffectif Effectif(ReglagesPlanning? reglages = null) => new(Planning(), reglages ?? new ReglagesPlanning());

    private static readonly ResumeSeance[] Seances =
    [
        S("maths-sq1-s01", "Maths", "SQ1"),
        S("maths-sq2-s01", "Maths", "SQ2"),
        S("maths-sq3-s01", "Maths", "SQ3"),
        S("b1-b11-sq1-s01", "Bloc 1", "B1.1 – SQ1"),
        S("b1-b13-sq1-s01", "Bloc 1", "B1.3 – SQ1"),
        S("algo-ref-memento", "Algo", "Référence"),
    ];

    private static List<string> Choisir(PlanningEffectif? planning, DateOnly jour, int maximum, ISet<string>? lus = null,
        IEnumerable<ResumeSeance>? seances = null, IEnumerable<CandidateNouvelle>? cartes = null)
    {
        var liste = (seances ?? Seances).ToList();
        return ChoixNouvelles.Choisir(cartes ?? Cartes(liste), liste, (lus ?? new HashSet<string>()).ToHashSet(), planning, jour, maximum)
            .Select(c => c.CarteId).ToList();
    }

    // ---- Correspondance des préfixes ---------------------------------------------------------

    [Theory]
    [InlineData("b1-b16", "b1-b16", true)]
    [InlineData("b1-b16-sq1-s01", "b1-b16", true)]
    [InlineData("b1-b161", "b1-b16", false)]
    [InlineData("b1-b1", "b1-b16", false)]
    [InlineData("ang-academic-skills-l01", "ang", true)]
    [InlineData("anglais", "ang", false)]
    [InlineData("x", "", false)]
    public void Un_prefixe_concerne_l_id_egal_ou_suivi_d_un_tiret(string packId, string prefixe, bool attendu) =>
        Assert.Equal(attendu, Periode.CorrespondPrefixe(packId, prefixe));

    [Fact]
    public void Le_prefixe_le_plus_precis_l_emporte()
    {
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [
                P("Bloc 1", "Large", J(2026, 9, 7), J(2026, 9, 20), "b1-b11"),
                P("Bloc 1", "Précis", J(2026, 12, 21), J(2027, 1, 17), "b1-b11-sq3"),
            ], []), new ReglagesPlanning());
        Assert.Equal("Précis", planning.PeriodeDuPack("b1-b11-sq3-s02")!.Libelle);
        Assert.Equal("Large", planning.PeriodeDuPack("b1-b11-sq1-s01")!.Libelle);
        Assert.Null(planning.PeriodeDuPack("b2-b21-sq1"));
    }

    [Fact]
    public void A_precision_egale_la_periode_la_plus_ancienne_l_emporte()
    {
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [
                P("Bloc 1", "Tard", J(2027, 1, 4), J(2027, 1, 17), "b1-b14"),
                P("Bloc 1", "Tôt", J(2026, 9, 7), J(2026, 9, 20), "b1-b14"),
            ], []), new ReglagesPlanning());
        Assert.Equal("Tôt", planning.PeriodeDuPack("b1-b14-sq1")!.Libelle);
    }

    [Theory]
    [InlineData(2026, 11, 1, Moment.Future)]
    [InlineData(2026, 11, 2, Moment.EnCours)]   // premier jour
    [InlineData(2026, 11, 29, Moment.EnCours)]  // dernier jour (dimanche) compris
    [InlineData(2026, 11, 30, Moment.Passee)]
    public void Moment_d_une_periode(int a, int m, int j, Moment attendu) =>
        Assert.Equal(attendu, PlanningEffectif.MomentDe(Planning().Periodes[1], J(a, m, j)));

    // ---- Couverture d'un devoir --------------------------------------------------------------

    private static List<string> Couverture(PlanningEffectif p, string matiere, string code) =>
        p.Couverture(p.Complet.Devoirs.Single(d => d.Matiere == matiere && d.Code == code)).Select(x => x.Libelle).ToList();

    [Fact]
    public void Le_premier_devoir_porte_sur_tout_ce_qui_a_commence_avant_sa_date_limite()
    {
        // Bloc 1 QZ1 : date limite dimanche 08/11. B1.1 (21/09) oui ; B1.3 (09/11) non.
        Assert.Equal(["B1.1"], Couverture(Effectif(), "Bloc 1", "QZ1"));
        // Maths DV0 : date limite 11/10. Seule la SQ1 a commencé.
        Assert.Equal(["SQ1"], Couverture(Effectif(), "Maths", "DV0"));
    }

    [Fact]
    public void Un_devoir_porte_sur_ce_qui_a_commence_apres_le_devoir_precedent()
    {
        // Maths DV1 (limite 06/12) : SQ2 a commencé après DV0 (11/10) ; SQ1 avant ; SQ3 après la date limite.
        Assert.Equal(["SQ2"], Couverture(Effectif(), "Maths", "DV1"));
        Assert.Equal(["B1.3"], Couverture(Effectif(), "Bloc 1", "QZ2"));
    }

    [Fact]
    public void Couverture_aux_limites_exactes()
    {
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [
                P("M", "Le jour du devoir précédent", J(2026, 10, 11), J(2026, 10, 25), "a"),   // dimanche limite de QZ1 : exclu
                P("M", "Le lendemain", J(2026, 10, 12), J(2026, 10, 25), "b"),                  // inclus
                P("M", "Le jour de la date limite", J(2026, 11, 8), J(2026, 11, 22), "c"),      // inclus
                P("M", "Le lendemain de la date limite", J(2026, 11, 9), J(2026, 11, 22), "d"),  // exclu
            ],
            [D("M", "DV0", J(2026, 10, 5)), D("M", "DV1", J(2026, 11, 2))]), new ReglagesPlanning());
        Assert.Equal(["Le lendemain", "Le jour de la date limite"], Couverture(planning, "M", "DV1"));
    }

    [Fact]
    public void Un_devoir_note_compte_depuis_le_devoir_note_precedent_un_quiz_depuis_le_devoir_precedent()
    {
        // Comme en CEJM : QZ1 puis DV1 la semaine suivante sur le même thème, puis QZ2 et DV2 sur le thème suivant.
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [
                P("CEJM", "Thème 1", J(2026, 9, 14), J(2026, 11, 15), "cejm-sq01"),
                P("CEJM", "Thème 4", J(2026, 11, 30), J(2027, 1, 31), "cejm-sq07"),
            ],
            [D("CEJM", "QZ1", J(2026, 11, 9)), D("CEJM", "DV1", J(2026, 11, 16)), D("CEJM", "QZ2", J(2027, 1, 25)), D("CEJM", "DV2", J(2027, 2, 1))]),
            new ReglagesPlanning());
        Assert.Equal(["Thème 1"], Couverture(planning, "CEJM", "QZ1"));
        Assert.Equal(["Thème 1"], Couverture(planning, "CEJM", "DV1"));   // et non « rien » : le QZ1 ne compte pas pour un DV
        Assert.Equal(["Thème 4"], Couverture(planning, "CEJM", "QZ2"));
        Assert.Equal(["Thème 4"], Couverture(planning, "CEJM", "DV2"));
        // Masquer les quiz ne change rien à la couverture.
        var masques = new PlanningEffectif(planning.Complet, new ReglagesPlanning { MasquerQuiz = true });
        Assert.Equal(["Thème 1"], Couverture(masques, "CEJM", "DV1"));
        Assert.Equal(["Thème 4"], Couverture(masques, "CEJM", "DV2"));
    }

    [Fact]
    public void En_anglais_un_devoir_porte_sur_tout()
    {
        var planning = new PlanningEffectif(Planning() with
        {
            Periodes = [.. Planning().Periodes, new Periode("periode:Anglais|Plus tard", "Anglais", "Plus tard", J(2027, 6, 7), J(2027, 6, 13), ["ang-x"])],
        }, new ReglagesPlanning());
        Assert.Equal(["B2", "Plus tard"], Couverture(planning, "Anglais", "DV1"));
        Assert.Equal(["B2", "Plus tard"], Couverture(planning, "Anglais", "DV2"));
    }

    [Fact]
    public void Masquer_les_quiz_ne_change_pas_la_couverture_d_un_devoir_note()
    {
        var planning = Effectif(new ReglagesPlanning { MasquerQuiz = true });
        Assert.Equal(["SQ2"], Couverture(planning, "Maths", "DV1"));
        Assert.DoesNotContain(planning.Devoirs, d => d.EstQuiz);
    }

    [Fact]
    public void La_date_limite_est_le_dimanche_de_la_semaine()
    {
        Assert.Equal(J(2026, 12, 6), D("Maths", "DV1", J(2026, 11, 30)).DateLimite);
        Assert.Equal(J(2026, 12, 6), D("Maths", "DV1", J(2026, 12, 3)).DateLimite); // jeudi : même semaine
    }

    [Theory]
    [InlineData(-15, false)]
    [InlineData(-14, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Fenetre_de_preparation_de_14_jours(int decalage, bool attendu)
    {
        var devoir = D("Maths", "DV1", J(2026, 11, 30)); // limite 06/12
        Assert.Equal(attendu, PlanningEffectif.EnPreparation(devoir, devoir.DateLimite.AddDays(decalage)));
    }

    // ---- Ordre sans planning -----------------------------------------------------------------

    [Fact]
    public void Sans_planning_les_seances_lues_d_abord_puis_l_ordre_du_cours_en_alternant_les_matieres()
    {
        var resultat = Choisir(null, Aujourdhui, 100, lus: new HashSet<string> { "maths-sq3-s01" });
        Assert.Equal(
        [
            "maths-sq3-s01#1", "maths-sq3-s01#2",                           // lue
            "algo-ref-memento#1", "b1-b11-sq1-s01#1", "maths-sq1-s01#1",    // un tour : Algo, Bloc 1, Maths
            "algo-ref-memento#2", "b1-b11-sq1-s01#2", "maths-sq1-s01#2",
            "b1-b13-sq1-s01#1", "maths-sq2-s01#1",
            "b1-b13-sq1-s01#2", "maths-sq2-s01#2",
        ], resultat);
    }

    [Fact]
    public void Sans_planning_le_maximum_est_respecte_et_zero_ne_donne_rien()
    {
        Assert.Equal(3, Choisir(null, Aujourdhui, 3).Count);
        Assert.Empty(Choisir(null, Aujourdhui, 0));
        Assert.Empty(Choisir(null, Aujourdhui, -5));
    }

    [Fact]
    public void Une_carte_prioritaire_passe_avec_les_seances_lues()
    {
        var cartes = Cartes(Seances).Select(c => c.CarteId == "maths-sq2-s01#2" ? c with { Prioritaire = true } : c).ToList();
        Assert.Equal("maths-sq2-s01#2", Choisir(null, Aujourdhui, 5, cartes: cartes)[0]);
    }

    [Fact]
    public void Une_carte_de_pack_inconnu_est_ignoree()
    {
        var cartes = new List<CandidateNouvelle>(Cartes(Seances)) { new("fantome#1", "fantome", 1) };
        Assert.DoesNotContain("fantome#1", Choisir(null, Aujourdhui, 100, cartes: cartes));
    }

    [Fact]
    public void Interrupteur_general_coupe_comme_sans_planning()
    {
        var coupe = Effectif(new ReglagesPlanning { Suivre = false });
        Assert.Equal(Choisir(null, Aujourdhui, 100), Choisir(coupe, Aujourdhui, 100));
        Assert.Empty(ChoixNouvelles.SuggestionsDeLecture(Seances, new HashSet<string>(), coupe, Aujourdhui));
    }

    // ---- Priorités avec planning -------------------------------------------------------------

    [Fact]
    public void Avec_planning_lues_puis_en_cours_puis_passees_puis_hors_planning_et_rien_du_futur()
    {
        var resultat = Choisir(Effectif(), Aujourdhui, 100, lus: new HashSet<string> { "b1-b11-sq1-s01" });
        Assert.Equal(
        [
            "b1-b11-sq1-s01#1", "b1-b11-sq1-s01#2",                       // 1. lue (même si sa période est passée)
            "b1-b13-sq1-s01#1", "maths-sq2-s01#1",                        // 2. en cours, en alternant les matières
            "b1-b13-sq1-s01#2", "maths-sq2-s01#2",
            "maths-sq1-s01#1", "maths-sq1-s01#2",                         // 3. passée
            "algo-ref-memento#1", "algo-ref-memento#2",                   // hors planning (référence)
        ], resultat);
        Assert.DoesNotContain(resultat, c => c.StartsWith("maths-sq3"));  // 4. futur jamais lu : pas de cartes
    }

    [Fact]
    public void Une_seance_future_deja_lue_est_proposee()
    {
        var resultat = Choisir(Effectif(), Aujourdhui, 100, lus: new HashSet<string> { "maths-sq3-s01" });
        Assert.Equal("maths-sq3-s01#1", resultat[0]);
    }

    [Theory]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(10, 5, 5)]
    [InlineData(1, 1, 0)]
    public void Les_periodes_passees_ont_au_plus_la_moitie_des_nouvelles_du_jour(int maximum, int enCours, int passees)
    {
        var seances = new[] { S("maths-sq2-s01", "Maths", "SQ2"), S("maths-sq1-s01", "Maths", "SQ1") };
        var resultat = Choisir(Effectif(), Aujourdhui, maximum, seances: seances, cartes: Cartes(seances, 20));
        Assert.Equal(enCours, resultat.Count(c => c.StartsWith("maths-sq2")));
        Assert.Equal(passees, resultat.Count(c => c.StartsWith("maths-sq1")));
        Assert.Equal(passees, Math.Min(passees, ChoixNouvelles.PlafondPassees(maximum)));
    }

    [Fact]
    public void Sans_contenu_en_cours_les_periodes_passees_prennent_la_place_libre()
    {
        var seances = new[] { S("maths-sq2-s01", "Maths", "SQ2"), S("maths-sq1-s01", "Maths", "SQ1") };
        var cartes = Cartes(seances, 20).Where(c => c.PackId == "maths-sq1-s01" || c.Position == 1).ToList();
        var resultat = Choisir(Effectif(), Aujourdhui, 6, seances: seances, cartes: cartes);
        Assert.Equal(1, resultat.Count(c => c.StartsWith("maths-sq2")));
        Assert.Equal(5, resultat.Count(c => c.StartsWith("maths-sq1")));
    }

    [Fact]
    public void Les_seances_lues_ne_comptent_pas_dans_le_plafond_des_periodes_passees()
    {
        var seances = new[] { S("maths-sq1-s01", "Maths", "SQ1"), S("b1-b11-sq1-s01", "Bloc 1", "B1.1") };
        // Deux séances passées ; l'une est lue : ses 4 cartes passent d'abord, l'autre garde sa moitié.
        var resultat = Choisir(Effectif(), Aujourdhui, 6, new HashSet<string> { "maths-sq1-s01" }, seances, Cartes(seances, 4));
        Assert.Equal(4, resultat.Count(c => c.StartsWith("maths-sq1")));
        Assert.Equal(2, resultat.Count(c => c.StartsWith("b1-b11")));
    }

    [Fact]
    public void Les_periodes_passees_viennent_de_la_plus_ancienne_a_la_plus_recente()
    {
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [
                P("Maths", "Récente", J(2026, 10, 5), J(2026, 10, 18), "maths-recente"),
                P("Maths", "Ancienne", J(2026, 9, 7), J(2026, 9, 20), "maths-ancienne"),
            ], []), new ReglagesPlanning());
        // L'ordre du cours mettrait « recente » (SQ1) avant « ancienne » (SQ9) : la date de période l'emporte.
        var seances = new[] { S("maths-recente", "Maths", "SQ1"), S("maths-ancienne", "Maths", "SQ9") };
        var resultat = Choisir(planning, Aujourdhui, 100, seances: seances);
        Assert.Equal(["maths-ancienne#1", "maths-ancienne#2", "maths-recente#1", "maths-recente#2"], resultat);
    }

    [Fact]
    public void Matiere_non_suivie_comme_sans_planning()
    {
        var planning = Effectif(new ReglagesPlanning { MatieresIgnorees = new HashSet<string> { "Maths" } });
        var resultat = Choisir(planning, Aujourdhui, 100);
        Assert.Contains("maths-sq3-s01#1", resultat);                     // le futur est proposé : comme avant
        Assert.Equal(RangNouvelle.EnCours, ChoixNouvelles.Rang("maths-sq1-s01", false, planning, Aujourdhui).Rang);
        Assert.Equal(RangNouvelle.HorsPlanning, ChoixNouvelles.Rang("algo-ref-memento", false, planning, Aujourdhui).Rang);
    }

    [Fact]
    public void Contenu_d_un_devoir_proche_en_tete_meme_avant_les_seances_lues()
    {
        // Maths DV1 et Bloc 1 QZ2 (limite 06/12) en préparation depuis le 22/11 : SQ2 et B1.3 passent avant la séance lue.
        var jour = J(2026, 11, 24);
        var resultat = Choisir(Effectif(), jour, 100, lus: new HashSet<string> { "b1-b11-sq1-s01" });
        Assert.Equal(["b1-b13-sq1-s01#1", "maths-sq2-s01#1", "b1-b13-sq1-s01#2", "maths-sq2-s01#2", "b1-b11-sq1-s01#1"], resultat.Take(5));
        Assert.Equal((RangNouvelle.Devoir, J(2026, 12, 6)), ChoixNouvelles.Rang("maths-sq2-s01", false, Effectif(), jour));
    }

    [Fact]
    public void Contenu_futur_d_un_devoir_proche_seulement_s_il_a_ete_lu()
    {
        var planning = new PlanningEffectif(Planning() with
        {
            Periodes = [.. Planning().Periodes, P("Maths", "SQ2 suite", J(2026, 11, 30), J(2026, 12, 13), "maths-sq2b")],
        }, new ReglagesPlanning());
        var jour = J(2026, 11, 24);
        Assert.Equal(RangNouvelle.NonProposee, ChoixNouvelles.Rang("maths-sq2b-s01", false, planning, jour).Rang);
        Assert.Equal(RangNouvelle.Devoir, ChoixNouvelles.Rang("maths-sq2b-s01", true, planning, jour).Rang);
    }

    [Fact]
    public void Quiz_masque_ne_donne_pas_de_preparation()
    {
        var jour = J(2026, 11, 24); // Bloc 1 QZ2 (limite 06/12) en préparation
        Assert.Equal(RangNouvelle.Devoir, ChoixNouvelles.Rang("b1-b13-sq1-s01", false, Effectif(), jour).Rang);
        var masques = Effectif(new ReglagesPlanning { MasquerQuiz = true });
        Assert.Equal(RangNouvelle.EnCours, ChoixNouvelles.Rang("b1-b13-sq1-s01", false, masques, jour).Rang);
    }

    [Fact]
    public void Apres_la_date_limite_retour_a_l_ordre_normal()
    {
        var lendemain = J(2026, 12, 7);
        Assert.NotEqual(RangNouvelle.Devoir, ChoixNouvelles.Rang("maths-sq2-s01", false, Effectif(), lendemain).Rang);
        Assert.DoesNotContain(Effectif().DevoirsEnPreparation(lendemain), d => d.Matiere == "Maths");
    }

    // ---- Surcharges ----------------------------------------------------------------------------

    [Fact]
    public void Une_date_personnelle_change_la_periode_d_un_pack()
    {
        var reglages = new ReglagesPlanning
        {
            Surcharges = new Dictionary<string, Surcharge> { ["periode:Maths|SQ3"] = new(J(2026, 11, 16), J(2026, 12, 13)) },
        };
        Assert.Equal(RangNouvelle.NonProposee, ChoixNouvelles.Rang("maths-sq3-s01", false, Effectif(), Aujourdhui).Rang);
        Assert.Equal(RangNouvelle.EnCours, ChoixNouvelles.Rang("maths-sq3-s01", false, Effectif(reglages), Aujourdhui).Rang);
    }

    [Fact]
    public void Une_date_personnelle_deplace_un_devoir_et_sa_preparation()
    {
        var reglages = new ReglagesPlanning
        {
            // Le jeudi est ramené au lundi de sa semaine.
            Surcharges = new Dictionary<string, Surcharge> { ["devoir:Maths|DV1"] = new(J(2026, 11, 19), null) },
        };
        var devoir = Effectif(reglages).Devoirs.Single(d => d.Cle == "devoir:Maths|DV1");
        Assert.Equal(J(2026, 11, 16), devoir.Semaine);
        Assert.Equal(J(2026, 11, 22), devoir.DateLimite);
        Assert.Contains(devoir, Effectif(reglages).DevoirsEnPreparation(Aujourdhui));
        Assert.DoesNotContain(Effectif().DevoirsEnPreparation(Aujourdhui), d => d.Cle == "devoir:Maths|DV1");
    }

    [Fact]
    public void Une_fin_personnelle_avant_le_debut_garde_une_semaine()
    {
        var planning = PlanningEffectif.AppliquerSurcharges(Planning(),
            new Dictionary<string, Surcharge> { ["periode:Maths|SQ1"] = new(J(2026, 10, 5), J(2026, 9, 1)) });
        var sq1 = planning.Periodes.Single(p => p.Libelle == "SQ1");
        Assert.Equal((J(2026, 10, 5), J(2026, 10, 11)), (sq1.Debut, sq1.Fin));
    }

    [Fact]
    public void Une_surcharge_sans_fin_garde_la_fin_du_fichier_et_une_cle_inconnue_est_ignoree()
    {
        var planning = PlanningEffectif.AppliquerSurcharges(Planning(), new Dictionary<string, Surcharge>
        {
            ["periode:Maths|SQ1"] = new(J(2026, 9, 14), null),
            ["periode:Inconnue|X"] = new(J(2020, 1, 6), null),
        });
        var sq1 = planning.Periodes.Single(p => p.Libelle == "SQ1");
        Assert.Equal((J(2026, 9, 14), J(2026, 10, 4)), (sq1.Debut, sq1.Fin));
        Assert.Equal(Planning().Periodes.Count, planning.Periodes.Count);
    }

    // ---- Échéances et suggestions --------------------------------------------------------------

    [Fact]
    public void Prochaines_echeances_sans_les_devoirs_passes()
    {
        var echeances = Effectif().ProchainesEcheances(Aujourdhui, 3);
        Assert.Equal(["Bloc 1|QZ2", "Maths|DV1", "Anglais|DV2"], echeances.Select(d => $"{d.Matiere}|{d.Code}"));
        Assert.Equal("Bloc 1 – quiz d'auto-évaluation QZ2, semaine du 30/11", Libelles.Echeance(echeances[0]));
        Assert.Equal("Maths – devoir noté DV1, semaine du 30/11", Libelles.Echeance(echeances[1]));
    }

    [Fact]
    public void Echeance_le_jour_meme_encore_affichee_puis_plus_rien()
    {
        var dimanche = J(2026, 12, 6);
        Assert.Contains(Effectif().ProchainesEcheances(dimanche, 10), d => d.Cle == "devoir:Maths|DV1");
        Assert.DoesNotContain(Effectif().ProchainesEcheances(dimanche.AddDays(1), 10), d => d.Cle == "devoir:Maths|DV1");
    }

    [Fact]
    public void Echeances_sans_quiz_masques_ni_matieres_non_suivies()
    {
        var reglages = new ReglagesPlanning { MasquerQuiz = true, MatieresIgnorees = new HashSet<string> { "Anglais" } };
        Assert.Equal(["Maths|DV1"], Effectif(reglages).ProchainesEcheances(Aujourdhui, 3).Select(d => $"{d.Matiere}|{d.Code}"));
    }

    [Fact]
    public void Suggestions_de_lecture_une_matiere_a_la_fois_dans_l_ordre_de_priorite()
    {
        var suggestions = ChoixNouvelles.SuggestionsDeLecture(Seances, new HashSet<string>(), Effectif(), Aujourdhui);
        // En cours : Bloc 1 (B1.3) et Maths (SQ2). Il n'y a pas d'autre matière : la 3e place revient
        // à la période passée la plus ancienne (Maths SQ1, 07/09, avant Bloc 1 B1.1, 21/09).
        Assert.Equal(["b1-b13-sq1-s01", "maths-sq2-s01", "maths-sq1-s01"], suggestions.Select(s => s.Seance.PackId));
        Assert.Equal("Au programme en ce moment", suggestions[0].Raison);
        Assert.Equal("Prévu plus tôt dans l'année, à ton rythme", suggestions[2].Raison);
    }

    [Fact]
    public void Suggestions_sans_les_seances_lues_avec_le_devoir_proche_d_abord()
    {
        var jour = J(2026, 11, 24);
        var suggestions = ChoixNouvelles.SuggestionsDeLecture(Seances, new HashSet<string> { "b1-b11-sq1-s01" }, Effectif(), jour, 2);
        Assert.Equal(["b1-b13-sq1-s01", "maths-sq2-s01"], suggestions.Select(s => s.Seance.PackId).Order());
        Assert.All(suggestions, s => Assert.StartsWith("Utile pour", s.Raison));
        Assert.Contains(suggestions, s => s.Raison == "Utile pour le devoir noté DV1");
        Assert.Contains(suggestions, s => s.Raison == "Utile pour le quiz d'auto-évaluation QZ2");
    }

    [Fact]
    public void Suggestion_du_futur_quand_il_ne_reste_rien_d_autre()
    {
        var seances = new[] { S("maths-sq3-s01", "Maths", "SQ3") };
        var suggestion = Assert.Single(ChoixNouvelles.SuggestionsDeLecture(seances, new HashSet<string>(), Effectif(), Aujourdhui));
        Assert.Equal("Bientôt : à partir du 11/01", suggestion.Raison);
    }

    [Fact]
    public void Pas_de_suggestion_pour_une_matiere_non_suivie_ni_hors_planning()
    {
        var reglages = new ReglagesPlanning { MatieresIgnorees = new HashSet<string> { "Maths", "Bloc 1" } };
        Assert.Empty(ChoixNouvelles.SuggestionsDeLecture(Seances, new HashSet<string>(), Effectif(reglages), Aujourdhui));
    }

    // ---- Absence de planning ---------------------------------------------------------------------

    [Fact]
    public void Sans_planning_ni_suggestion_ni_avancement()
    {
        Assert.Empty(ChoixNouvelles.SuggestionsDeLecture(Seances, new HashSet<string>(), null, Aujourdhui));
        Assert.Empty(AvanceeDevoir.CartesAAvancer([new CarteVue("c", "maths-sq2-s01", 1, Aujourdhui.AddDays(-30), Aujourdhui.AddDays(60))], null, Aujourdhui));
        Assert.Equal((RangNouvelle.EnCours, DateOnly.MinValue), ChoixNouvelles.Rang("maths-sq3-s01", false, null, Aujourdhui));
    }

    // ---- Avancement avant une échéance -----------------------------------------------------------

    private static readonly DateOnly LimiteDv1 = J(2026, 12, 6);

    /// <summary>Carte de la SQ2 de maths revue le 22/11, échéance après le devoir.</summary>
    private static CarteVue Vue(string id, double stabilite, string pack = "maths-sq2-s01", DateOnly? derniere = null, DateOnly? echeance = null) =>
        new(id, pack, stabilite, derniere ?? J(2026, 11, 21), echeance ?? J(2026, 12, 20));

    [Fact]
    public void Le_seuil_de_0_85_a_la_date_limite_decide_de_l_avancement()
    {
        var jour = J(2026, 11, 22);
        var fragile = Vue("fragile", 9);   // rappel prévu le 06/12 juste sous 0,85
        var solide = Vue("solide", 9.4);   // juste au-dessus
        Assert.True(AvanceeDevoir.RetrievabiliteA(fragile, LimiteDv1) < PlanningEffectif.SeuilRetrievabilite);
        Assert.True(AvanceeDevoir.RetrievabiliteA(solide, LimiteDv1) >= PlanningEffectif.SeuilRetrievabilite);
        Assert.Equal(["fragile"], AvanceeDevoir.CartesAAvancer([fragile, solide], Effectif(), jour));
    }

    [Fact]
    public void La_retrievabilite_prevue_suit_fsrs()
    {
        var c = Vue("c", 5, derniere: J(2026, 11, 1));
        Assert.Equal(Fsrs.Retrievabilite(35, 5), AvanceeDevoir.RetrievabiliteA(c, LimiteDv1), 10);
    }

    [Fact]
    public void Pas_d_avancement_si_l_echeance_tombe_avant_la_date_limite_ou_si_la_carte_est_revue_aujourd_hui()
    {
        var jour = J(2026, 11, 22);
        var avant = Vue("avant", 1, echeance: J(2026, 12, 5));
        var limite = Vue("le-jour-meme", 1, echeance: LimiteDv1);
        var revueAujourdhui = Vue("revue", 1, derniere: jour);
        Assert.Empty(AvanceeDevoir.CartesAAvancer([avant, limite, revueAujourdhui], Effectif(), jour));
    }

    [Fact]
    public void Pas_d_avancement_hors_du_contenu_du_devoir()
    {
        var jour = J(2026, 11, 22);
        // maths-sq1 : couverte par DV0, pas par DV1 ; algo-ref : hors planning.
        Assert.Empty(AvanceeDevoir.CartesAAvancer([Vue("a", 1, "maths-sq1-s01"), Vue("b", 1, "algo-ref-memento")], Effectif(), jour));
    }

    [Theory]
    [InlineData(2026, 11, 21, 0)]  // 15 jours avant : pas encore
    [InlineData(2026, 11, 22, 1)]  // 14 jours avant : 10 cartes ÷ 15 jours → 1
    [InlineData(2026, 12, 2, 2)]   // 5 jours restants : ⌈10 ÷ 5⌉ = 2
    [InlineData(2026, 12, 6, 10)]  // dernier jour : tout ce qui reste
    [InlineData(2026, 12, 7, 0)]   // après la date limite : retour au FSRS normal
    public void Les_cartes_avancees_sont_etalees_sur_les_jours_restants(int a, int m, int j, int attendu)
    {
        var cartes = Enumerable.Range(0, 10).Select(i => Vue($"c{i}", 0.5 + i * 0.1)).ToList();
        var avancees = AvanceeDevoir.CartesAAvancer(cartes, Effectif(), J(a, m, j));
        Assert.Equal(attendu, avancees.Count);
        // Les plus fragiles (stabilité la plus basse) d'abord.
        Assert.Equal(cartes.Take(attendu).Select(c => c.CarteId), avancees);
    }

    [Fact]
    public void Le_devoir_le_plus_proche_prend_les_cartes_communes()
    {
        // Deux devoirs couvrent la même période (le second est déplacé à la semaine suivante).
        var planning = new PlanningEffectif(new PlanningCned("p", 1, "p",
            [new Periode("periode:Anglais|B2", "Anglais", "B2", J(2026, 9, 7), J(2027, 5, 16), ["ang"], Continu: true)],
            [D("Anglais", "DV1", J(2026, 11, 30)), D("Anglais", "DV2", J(2026, 12, 7))]), new ReglagesPlanning());
        var cartes = Enumerable.Range(0, 4).Select(i => Vue($"c{i}", 0.5, "ang-x", echeance: J(2027, 1, 30))).ToList();
        var jour = J(2026, 12, 3); // DV1 : 4 jours restants → 1 ; DV2 ne reprend pas les 3 autres aujourd'hui
        Assert.Single(AvanceeDevoir.CartesAAvancer(cartes, planning, jour));
    }

    [Fact]
    public void Les_cartes_avancees_respectent_le_plafond_quotidien()
    {
        var jour = J(2026, 12, 6);
        var carteBase = new Carte("x", "s", TypeCarte.Question, 1, new ContenuCarte());
        CarteAReviser Due(string id, double s, int joursDepuis) => new(carteBase with { Id = id },
            new EtatCarte { CarteId = id, Stabilite = s, DifficulteFsrs = 5, DerniereRevision = jour.AddDays(-joursDepuis), Echeance = jour },
            "maths-sq2-s01", "Maths", "S");
        var dues = new[] { Due("due1", 2, 10), Due("due2", 2, 12) };
        var avancees = new[] { Due("av1", 30, 2), Due("av2", 30, 3), Due("av3", 30, 1) };   // échéance ramenée à aujourd'hui
        var session = ConstructeurSession.Construire([.. dues, .. avancees], [], jour, maxCartes: 3, maxNouvelles: 0);
        Assert.Equal(3, session.Count);
        Assert.Contains(session, c => c.Carte.Id == "due1");
        Assert.Contains(session, c => c.Carte.Id == "due2");   // les cartes vraiment dues passent avant les cartes avancées
    }

    // ---- Lecture du fichier --------------------------------------------------------------------

    private const string Minimal = """
        {
          "formatVersion": 1, "id": "essai", "titre": "Essai",
          "periodes": [ { "matiere": "Maths", "libelle": "SQ1", "debut": "2026-09-07", "fin": "2026-10-04", "cible": ["maths-sq1"] } ],
          "devoirs": [ { "matiere": "Maths", "code": "QZ1", "semaine": "2026-10-05" }, { "matiere": "Maths", "code": "DV1", "note": true, "semaine": "2026-11-30" } ]
        }
        """;

    [Fact]
    public void Un_planning_minimal_est_lu()
    {
        var r = LecteurPlanning.Lire(Minimal);
        Assert.True(r.Valide);
        Assert.Empty(r.Avertissements);
        var p = r.Planning!;
        Assert.Equal(("essai", 1, "Essai"), (p.Id, p.FormatVersion, p.Titre));
        Assert.Equal(new Periode("periode:Maths|SQ1", "Maths", "SQ1", J(2026, 9, 7), J(2026, 10, 4), p.Periodes[0].Cibles), p.Periodes[0]);
        Assert.Equal(["maths-sq1"], p.Periodes[0].Cibles);
        Assert.False(p.Devoirs[0].Note);   // QZ sans « note » : quiz
        Assert.True(p.Devoirs[1].Note);
    }

    [Fact]
    public void Les_drapeaux_sont_lus()
    {
        var r = LecteurPlanning.Lire(Minimal.Replace("\"cible\": [\"maths-sq1\"]", "\"cible\": [], \"approx\": true, \"hypothese\": true, \"continu\": true"));
        var p = r.Planning!.Periodes[0];
        Assert.True(p.Approx && p.Hypothese && p.Continu && p.AConfirmer);
    }

    [Theory]
    [InlineData("pas du json", "JSON valide")]
    [InlineData("[]", "objet JSON")]
    [InlineData("""{ "id": "x", "periodes": [] }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 2, "id": "x", "periodes": [] }""", "format 2")]
    [InlineData("""{ "formatVersion": 1, "periodes": [] }""", "« id »")]
    [InlineData("""{ "formatVersion": 1, "id": "x" }""", "« periodes »")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "debut": "2026-10-05", "fin": "2026-10-04" } ] }""", "avant le début")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "debut": "05/10/2026", "fin": "2026-10-11" } ] }""", "AAAA-MM-JJ")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "fin": "2026-10-11" } ] }""", "« debut » manque")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "libelle": "L", "debut": "2026-10-05", "fin": "2026-10-11" } ] }""", "obligatoires")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "debut": "2026-10-05", "fin": "2026-10-11", "cible": [""] } ] }""", "vide")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "debut": "2026-10-05", "fin": "2026-10-11", "cible": "m" } ] }""", "doit être une liste")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [ { "matiere": "M", "libelle": "L", "debut": "2026-10-05", "fin": "2026-10-11" }, { "matiere": "M", "libelle": "L", "debut": "2026-10-12", "fin": "2026-10-18" } ] }""", "même libellé")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [], "devoirs": [ { "matiere": "M", "code": "DV1" } ] }""", "« semaine » manque")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [], "devoirs": [ { "matiere": "M", "code": "DV1", "semaine": "2026-10-05" }, { "matiere": "M", "code": "DV1", "semaine": "2026-11-02" } ] }""", "même code")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "periodes": [], "devoirs": {} }""", "« devoirs »")]
    public void Un_fichier_incorrect_est_refuse_avec_un_message_precis(string json, string extrait)
    {
        var r = LecteurPlanning.Lire(json);
        Assert.False(r.Valide);
        Assert.Contains(r.Erreurs, e => e.Contains(extrait));
    }

    [Fact]
    public void Dates_hors_lundi_dimanche_donnent_un_avertissement_et_la_semaine_est_ramenee_au_lundi()
    {
        var r = LecteurPlanning.Lire("""
            { "formatVersion": 1, "id": "x",
              "periodes": [ { "matiere": "M", "libelle": "L", "debut": "2026-10-06", "fin": "2026-10-10" } ],
              "devoirs": [ { "matiere": "M", "code": "DV1", "semaine": "2026-10-08" }, { "matiere": "Autre", "code": "DV1", "semaine": "2026-10-05" } ] }
            """);
        Assert.True(r.Valide);
        Assert.Contains(r.Avertissements, a => a.Contains("pas un lundi"));
        Assert.Contains(r.Avertissements, a => a.Contains("pas un dimanche"));
        Assert.Contains(r.Avertissements, a => a.Contains("ramenée au lundi"));
        Assert.Contains(r.Avertissements, a => a.Contains("« Autre » n'ont aucune période"));
        Assert.Equal(J(2026, 10, 5), r.Planning!.Devoirs[0].Semaine);
    }

    [Fact]
    public void Un_prefixe_sans_pack_donne_un_avertissement()
    {
        var planning = LecteurPlanning.Lire(Minimal).Planning!;
        Assert.Empty(LecteurPlanning.VerifierCibles(planning, ["maths-sq1-s01"]));
        var messages = LecteurPlanning.VerifierCibles(planning, ["maths-sq10-s01"]);
        Assert.Contains("« maths-sq1 »", Assert.Single(messages));
    }

    [Fact]
    public void Le_planning_du_cned_fourni_est_valide()
    {
        // Le fichier est hors du dépôt de l'application (dossier Cours/planning) : test ignoré s'il est absent.
        string? fichier = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && fichier is null; d = d.Parent)
        {
            string chemin = Path.Combine(d.FullName, "planning", "planning-1A-SLAM-2026-2027.json");
            if (File.Exists(chemin)) fichier = chemin;
        }
        if (fichier is null) return;

        var r = LecteurPlanning.Lire(File.ReadAllText(fichier));
        Assert.True(r.Valide, string.Join("\n", r.Erreurs));
        var p = new PlanningEffectif(r.Planning!, new ReglagesPlanning());
        // Repères vérifiés à la main : B1.2 Partie II SQ4 dans sa propre période, QZ2 du Bloc 1 sur les périodes de novembre.
        Assert.Equal("B1.2 Partie II – SQ4 et SQ5", p.PeriodeDuPack("b1-b12p2-sq4-s01")!.Libelle);
        var qz2 = p.Devoirs.Single(d => d.Matiere == "Bloc 1" && d.Code == "QZ2");
        Assert.Equal(["B1.2 Partie II – SQ1 à SQ3", "B1.3 – SQ1 à SQ4", "B1.5 – SQ1"], p.Couverture(qz2).Select(x => x.Libelle));
        Assert.Equal(p.Complet.Periodes.Count(x => x.Matiere == "Anglais"),
            p.Couverture(p.Devoirs.First(d => d.Matiere == "Anglais")).Count);
    }
}
