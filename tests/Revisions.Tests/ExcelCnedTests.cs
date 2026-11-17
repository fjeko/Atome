using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Planning;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>
/// Import de l'Excel « Proposition de planning » du CNED, sur des classeurs synthétiques fabriqués ici
/// (les vrais fichiers du CNED ne sont pas dans le dépôt).
/// </summary>
public sealed class ExcelCnedTests
{
    private static readonly DateOnly Jour = new(2026, 11, 17);
    private const int Serie0 = 46265;   // lundi 2026-08-31, colonne C

    // ---- Fabrique de classeurs .xlsx ------------------------------------------------------------

    /// <summary>Grille minimale : on pose des cellules par référence (« D7 »), des fusions (« D7:F7 »), et on obtient un .xlsx.</summary>
    private sealed class Grille
    {
        private readonly Dictionary<string, object> _cellules = [];
        private readonly List<string> _fusions = [];

        /// <summary>Ligne 2 : les lundis, à partir de la colonne C (46265 = lundi 2026-08-31).</summary>
        public Grille(int semaines = 40)
        {
            for (int i = 0; i < semaines; i++) _cellules[LecteurXlsx.NomColonne(3 + i) + "2"] = Serie0 + 7 * i;
        }

        public Grille Cellule(string reference, object valeur) { _cellules[reference] = valeur; return this; }
        public Grille Fusion(string plage) { _fusions.Add(plage); return this; }

        /// <summary>Colonne (lettres) de la i-ème semaine : 0 → C, 1 → D…</summary>
        public static string Col(int semaine) => LecteurXlsx.NomColonne(3 + semaine);

        public MemoryStream Xlsx()
        {
            var textes = new List<string>();
            int Index(string t) { int i = textes.IndexOf(t); if (i < 0) { textes.Add(t); i = textes.Count - 1; } return i; }

            var lignes = _cellules.GroupBy(c => { LecteurXlsx.TryReference(c.Key, out int l, out _); return l; }).OrderBy(g => g.Key);
            var feuille = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            foreach (var ligne in lignes)
            {
                feuille.Append($"<row r=\"{ligne.Key}\">");
                foreach (var c in ligne.OrderBy(c => { LecteurXlsx.TryReference(c.Key, out _, out int col); return col; }))
                    feuille.Append(c.Value is string s
                        ? $"<c r=\"{c.Key}\" t=\"s\"><v>{Index(s)}</v></c>"
                        : $"<c r=\"{c.Key}\"><v>{c.Value}</v></c>");
                feuille.Append("</row>");
            }
            feuille.Append("</sheetData>");
            if (_fusions.Count > 0)
                feuille.Append($"<mergeCells count=\"{_fusions.Count}\">{string.Concat(_fusions.Select(f => $"<mergeCell ref=\"{f}\"/>"))}</mergeCells>");
            feuille.Append("</worksheet>");

            var flux = new MemoryStream();
            using (var zip = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true))
            {
                Ecrire(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                Ecrire(zip, "xl/workbook.xml",
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"Feuil1\" sheetId=\"1\" r:id=\"rId7\"/></sheets></workbook>");
                Ecrire(zip, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId7\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Ecrire(zip, "xl/sharedStrings.xml",
                    "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    string.Concat(textes.Select(t => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(t)}</t></si>")) + "</sst>");
                Ecrire(zip, "xl/worksheets/sheet1.xml", feuille.ToString());
            }
            flux.Position = 0;
            return flux;
        }

        private static void Ecrire(ZipArchive zip, string nom, string contenu)
        {
            using var w = new StreamWriter(zip.CreateEntry(nom).Open(), new UTF8Encoding(false));
            w.Write(contenu);
        }
    }

    private static ResultatExcelCned Convertir(Grille g, string fichier = "Proposition-planning-1ASLAM-2026-2027.xlsx") =>
        ImporteurExcelCned.Convertir(g.Xlsx(), fichier, Jour);

    private static string D(int semaine) => new DateOnly(2026, 8, 31).AddDays(7 * (semaine - 0)).ToString("yyyy-MM-dd");

    private static PlanningCned Planning(ResultatExcelCned r)
    {
        Assert.True(r.Valide, string.Join(" / ", r.Erreurs));
        return r.Planning!;
    }

    // ---- Id et fichiers invalides ---------------------------------------------------------------

    [Theory]
    [InlineData("Proposition-planning-1ASISR-2026-2027.xlsx", "cned-1a-sisr-2026-2027")]
    [InlineData("Proposition-planning-1ASLAM-2026-2027(1).xlsx", "cned-1a-slam-2026-2027")]
    [InlineData(@"C:\Users\Moi\Downloads\Proposition planning 2A SLAM 2027-2028 (2).xlsx", "cned-2a-slam-2027-2028")]
    [InlineData("mon planning.xlsx", "cned-mon-planning")]
    public void L_id_vient_du_nom_du_fichier(string fichier, string attendu) =>
        Assert.Equal(attendu, ImporteurExcelCned.IdDepuisNomDeFichier(fichier));

    [Fact]
    public void Un_fichier_qui_n_est_pas_un_xlsx_donne_une_erreur()
    {
        var r = ImporteurExcelCned.Convertir(new MemoryStream(Encoding.UTF8.GetBytes("pas un zip")), "x.xlsx", Jour);
        Assert.False(r.Valide);
        Assert.Contains("xlsx", r.Erreurs[0]);
    }

    [Fact]
    public void Une_grille_sans_ligne_de_dates_est_refusee()
    {
        var g = new Grille(0).Cellule("A7", "Bloc de compétences 1").Cellule("D7", "B1.3");
        var r = Convertir(g);
        Assert.False(r.Valide);
        Assert.Contains("dates", r.Erreurs[0]);
    }

    // ---- Dates, fusions ---------------------------------------------------------------------------

    [Fact]
    public void Une_cellule_fusionnee_donne_du_lundi_de_depart_au_dimanche_de_fin()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A8").Cellule("B7", 2)
            .Cellule("D7", "B1.3-SQ1-4").Fusion("D7:I7");
        var p = Planning(Convertir(g));

        var periode = Assert.Single(p.Periodes);
        Assert.Equal("Bloc 1", periode.Matiere);
        Assert.Equal(DateOnly.Parse(D(1)), periode.Debut);          // colonne D
        Assert.Equal(DateOnly.Parse(D(6)).AddDays(6), periode.Fin);  // colonne I, jusqu'au dimanche
        Assert.False(periode.Approx);
        Assert.Equal(new[] { "b1-b13-sq1", "b1-b13-sq2", "b1-b13-sq3", "b1-b13-sq4" }, periode.Cibles);
        Assert.Equal("cned-1a-slam-2026-2027", p.Id);
    }

    [Fact]
    public void Une_periode_non_fusionnee_dure_jusqu_a_la_veille_de_la_suivante_sur_la_meme_ligne()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A9").Cellule("B7", 2)
            .Cellule("D7", "B1.6-SQ1-3")                          // non fusionnée : 2 semaines jusqu'à F
            .Cellule("F7", "B1.1-SQ1-2").Fusion("F7:G7");
        var p = Planning(Convertir(g));

        var b16 = p.Periodes.Single(x => x.Cibles.Contains("b1-b16-sq1"));
        Assert.Equal(DateOnly.Parse(D(1)), b16.Debut);
        Assert.Equal(DateOnly.Parse(D(3)).AddDays(-1), b16.Fin);   // veille du lundi de la colonne F
        Assert.True(b16.Approx);
        Assert.Contains(p.Periodes, x => !x.Approx && x.Cibles.Contains("b1-b11-sq1"));
    }

    [Fact]
    public void Une_cellule_seule_collee_a_la_suivante_n_est_pas_une_fin_deduite()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 2 - option SLAM").Fusion("A7:A8").Cellule("B7", 2)
            .Cellule("D7", "B2.3 - SLAM SQ3")                      // une seule semaine
            .Cellule("E7", "B2.2 - SLAM");
        var periodes = Planning(Convertir(g)).Periodes;
        var sq3 = periodes.Single(x => x.Cibles.Contains("b2-b23-sq3"));
        Assert.Equal(DateOnly.Parse(D(1)).AddDays(6), sq3.Fin);
        Assert.False(sq3.Approx);
    }

    [Fact]
    public void La_derniere_periode_non_fusionnee_finit_la_semaine_avant_le_devoir_suivant()
    {
        var g = new Grille()
            .Cellule("A14", "Bloc de compétences 3").Fusion("A14:A15").Cellule("B14", 1)
            .Cellule("D14", "B3.4")                                // dernière de sa ligne
            .Cellule(Grille.Col(4) + "15", "QZ1")                  // même semaine que le début + 3 : devoir suivant
            .Cellule(Grille.Col(6) + "15", "DV1");
        var p = Planning(Convertir(g));

        var periode = Assert.Single(p.Periodes);
        // Le devoir suivant (strictement après la colonne D) est QZ1, semaine 4 : la période finit le dimanche avant.
        Assert.Equal(DateOnly.Parse(D(4)).AddDays(-1), periode.Fin);
        Assert.True(periode.Approx);
        Assert.Contains(Convertir(g).Avertissements, a => a.Contains("déduite"));
    }

    // ---- Devoirs ----------------------------------------------------------------------------------

    [Fact]
    public void Les_devoirs_prennent_la_semaine_de_leur_colonne_et_leur_type_vient_du_code()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A9").Cellule("B7", 1)
            .Cellule("D7", "B1.3-SQ1-4").Fusion("D7:K7")
            .Cellule(Grille.Col(3) + "9", "QZ1").Cellule(Grille.Col(9) + "9", "DV1");
        var p = Planning(Convertir(g));

        var qz = p.Devoirs.Single(d => d.Code == "QZ1");
        var dv = p.Devoirs.Single(d => d.Code == "DV1");
        Assert.False(qz.Note);
        Assert.True(dv.Note);
        Assert.Equal(DateOnly.Parse(D(3)), qz.Semaine);
        Assert.Equal(DateOnly.Parse(D(9)).AddDays(6), dv.DateLimite);
        Assert.All(p.Devoirs, d => Assert.Equal("Bloc 1", d.Matiere));
    }

    [Fact]
    public void DV3_sans_DV2_est_traite_comme_DV2()
    {
        var g = new Grille()
            .Cellule("A26", "Bloc 6 - Mathématiques pour l'informatique - Algorithmique appliquée").Fusion("A26:A27").Cellule("B26", 2)
            .Cellule("I26", "Séquence 1").Fusion("I26:K26")
            .Cellule("K27", "QZ1").Cellule("U27", "DV1").Cellule("AB27", "QZ2").Cellule("AC27", "DV3");
        var r = Convertir(g);
        var p = Planning(r);

        Assert.Equal(new[] { "DV1", "DV2" }, p.Devoirs.Where(d => d.Note).Select(d => d.Code).ToArray());
        var dv2 = p.Devoirs.Single(d => d.Code == "DV2");
        Assert.Equal(new DateOnly(2026, 8, 31).AddDays(7 * 26), dv2.Semaine);           // colonne AC
        Assert.Contains("DV3", dv2.Remarque);
        Assert.Contains(r.Avertissements, a => a.Contains("DV3") && a.Contains("DV2"));
        Assert.Equal(new[] { "algo-sq1" }, p.Periodes.Single().Cibles);
        Assert.Equal("Algorithmique", p.Periodes.Single().Matiere);
    }

    [Fact]
    public void Le_nombre_de_devoirs_annonce_en_colonne_B_est_verifie()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A9").Cellule("B7", 3)
            .Cellule("D7", "B1.3").Fusion("D7:K7").Cellule("K9", "DV1");
        var r = Convertir(g);
        Assert.Contains(r.Avertissements, a => a.Contains("annonce 3"));
    }

    // ---- Pièges : CEJM, anglais -------------------------------------------------------------------

    [Fact]
    public void CEJM_Theme_1_4_5_du_fichier_sont_les_themes_1_2_3()
    {
        var g = new Grille()
            .Cellule("A29", "Bloc 7 - Culture économique juridique et managériale ").Fusion("A29:A30").Cellule("B29", 3)
            .Cellule("E29", "Thème 1").Fusion("E29:M29")
            .Cellule("P29", "Thème 4").Fusion("P29:X29")
            .Cellule("AB29", "Thème 5").Fusion("AB29:AJ29");
        var r = Convertir(g);
        var p = Planning(r);

        Assert.Equal(new[] { "Thème 1 (séquences 1 à 6)", "Thème 2 (séquences 7 à 9)", "Thème 3 (séquences 10 à 14)" }, p.Periodes.Select(x => x.Libelle).ToArray());
        Assert.Equal(new[] { "cejm-sq01", "cejm-sq02", "cejm-sq03", "cejm-sq04", "cejm-sq05", "cejm-sq06" }, p.Periodes[0].Cibles);
        Assert.Equal(new[] { "cejm-sq07", "cejm-sq08", "cejm-sq09" }, p.Periodes[1].Cibles);
        Assert.Equal(new[] { "cejm-sq10", "cejm-sq11", "cejm-sq12", "cejm-sq13", "cejm-sq14" }, p.Periodes[2].Cibles);
        Assert.All(p.Periodes, x => Assert.Equal("CEJM", x.Matiere));
        Assert.Contains(r.Avertissements, a => a.Contains("CEJM") && a.Contains("Thème 4"));
    }

    [Fact]
    public void CEJM_avec_des_themes_deja_numerotes_1_2_3_ne_donne_pas_d_avertissement()
    {
        var g = new Grille()
            .Cellule("A29", "Bloc 7 - Culture économique juridique et managériale").Fusion("A29:A30")
            .Cellule("E29", "Thème 2").Fusion("E29:M29");
        var r = Convertir(g);
        Assert.Equal(new[] { "cejm-sq07", "cejm-sq08", "cejm-sq09" }, Planning(r).Periodes.Single().Cibles);
        Assert.DoesNotContain(r.Avertissements, a => a.Contains("CEJM"));
    }

    [Fact]
    public void Anglais_90h_est_une_periode_continue()
    {
        var g = new Grille()
            .Cellule("A20", "Bloc 5 - Expression et communication en langue anglaise").Fusion("A20:A21").Cellule("B20", 2)
            .Cellule("D20", "90h").Fusion("D20:AM20")
            .Cellule("L21", "DV1").Cellule("AM21", "DV2");
        var p = Planning(Convertir(g));

        var anglais = Assert.Single(p.Periodes);
        Assert.Equal("Anglais", anglais.Matiere);
        Assert.True(anglais.Continu);
        Assert.Equal(new[] { "ang" }, anglais.Cibles);
        Assert.Equal(DateOnly.Parse(D(1)), anglais.Debut);
        Assert.Equal(2, p.Devoirs.Count(d => d.Matiere == "Anglais"));
    }

    // ---- Libellé → cibles -------------------------------------------------------------------------

    [Theory]
    [InlineData("Bloc de compétences 1", "B1.2-PARTIE I-SQ1-3", "B1.2 Partie I – SQ1 à SQ3", "b1-b12p1-sq1,b1-b12p1-sq2,b1-b12p1-sq3")]
    [InlineData("Bloc de compétences 1", "B1.2-PARTIE II-SQ4-5", "B1.2 Partie II – SQ4 et SQ5", "b1-b12p2-sq4,b1-b12p2-sq5")]
    [InlineData("Bloc de compétences 1", "B1.5-SQ1", "B1.5 – SQ1", "b1-b15-sq1")]
    [InlineData("Bloc de compétences 1", "B1.4 - SQ1 et SQ2", "B1.4 – SQ1 et SQ2", "b1-b14-sq1,b1-b14-sq2")]
    [InlineData("Bloc de compétences 3", "B3.1", "B3.1", "b3-b31")]
    [InlineData("Bloc de compétences 2 - option SLAM", "B2.1 - SLAM SQ1-3", "B2.1 – SQ1 à SQ3", "b2-b21-sq1,b2-b21-sq2,b2-b21-sq3")]
    [InlineData("Bloc de compétences 2 - option SLAM", "B2.2 - SLAM", "B2.2", "b2-b22")]
    [InlineData("Bloc de compétences 2 - option SISR", "B2.3 - SISR", "B2.3 – SISR", "")]
    [InlineData("Bloc 6 - Mathématiques pour l'informatique - Mathématiques (enseignement général)", "Séquence 2", "Séquence 2", "maths-sq2")]
    [InlineData("Bloc 6 - Mathématiques pour l'informatique - Algorithmique appliquée", "Séquence 3", "Séquence 3", "algo-sq3")]
    [InlineData("Bloc 4 - Culture générale et Expression*", "Module 1 - Activité 1 ", "Module 1 Activité 1", "")]
    [InlineData("Bloc 4 - Culture générale et Expression*", "Module 1 -Activité 2", "Module 1 Activité 2", "")]
    [InlineData("Ateliers de professionnalisation", "Atelier 1", "Atelier 1", "")]
    public void Les_libelles_donnent_les_bonnes_cibles(string matiere, string libelle, string attendu, string cibles)
    {
        var g = new Grille().Cellule("A7", matiere).Fusion("A7:A8").Cellule("D7", libelle).Fusion("D7:G7");
        var periode = Assert.Single(Planning(Convertir(g)).Periodes);
        Assert.Equal(attendu, periode.Libelle);
        string[] ciblesAttendues = cibles.Length == 0 ? [] : cibles.Split(',');
        Assert.Equal(ciblesAttendues, periode.Cibles.ToArray());
    }

    [Fact]
    public void Un_libelle_qui_ne_ressemble_a_rien_donne_une_periode_sans_cartes_et_un_avertissement()
    {
        var g = new Grille().Cellule("A7", "Bloc de compétences 1").Fusion("A7:A8").Cellule("D7", "Projet surprise").Fusion("D7:G7");
        var r = Convertir(g);
        var periode = Assert.Single(Planning(r).Periodes);
        Assert.Empty(periode.Cibles);
        Assert.Contains(r.Avertissements, a => a.Contains("Projet surprise") && a.Contains("non reconnu"));
    }

    [Fact]
    public void Une_matiere_inconnue_est_gardee_sans_cartes_avec_un_avertissement()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc 9 - Jardinage avancé").Fusion("A7:A8").Cellule("B7", 1)
            .Cellule("D7", "Les tomates").Fusion("D7:G7").Cellule("H8", "DV1");
        var r = Convertir(g);
        var p = Planning(r);

        Assert.Equal("Bloc 9 - Jardinage avancé", p.Periodes.Single().Matiere);
        Assert.Empty(p.Periodes.Single().Cibles);
        Assert.Single(p.Devoirs);
        Assert.Contains(r.Avertissements, a => a.Contains("Jardinage") && a.Contains("non reconnue"));
    }

    [Fact]
    public void Les_lignes_de_notes_et_le_total_de_devoirs_sont_ignores()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A8").Cellule("D7", "B1.3").Fusion("D7:G7")
            .Cellule("A36", "Nombre de devoirs").Cellule("B36", 19)
            .Cellule("A38", "QZ= quiz en auto-évaluation (sans remontée de note)").Fusion("A38:E38")
            .Cellule("A39", "*Le module 2 est centré sur la maîtrise de la langue française").Fusion("A39:AP39");
        var r = Convertir(g);
        Assert.Single(Planning(r).Periodes);
        Assert.DoesNotContain(r.Avertissements, a => a.Contains("non reconnue"));
    }

    // ---- Stage, chaîne complète -----------------------------------------------------------------

    [Fact]
    public void Le_stage_donne_ses_dates_dans_le_json()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A8").Cellule("D7", "B1.3").Fusion("D7:G7")
            .Cellule("A35", "Stage en milieu professionnel").Cellule("B35", 0)
            .Cellule("Z35", "période possible").Fusion("Z35:AL35")
            .Cellule("AM35", "période souhaitable**").Fusion("AM35:BC35");
        var r = Convertir(g);
        Assert.Contains("\"possible\": \"" + D(23) + "\"", r.Json);
        Assert.Contains("\"souhaitable\": \"" + D(36) + "\"", r.Json);
        Assert.Single(Planning(r).Periodes);   // le stage n'est pas une matière
    }

    [Fact]
    public void Le_json_produit_est_relu_par_l_import_de_planning_existant()
    {
        var g = new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A9").Cellule("B7", 1)
            .Cellule("D7", "B1.2-PARTIE II-SQ1-3").Cellule("G7", "B1.1-SQ1-2").Fusion("G7:H7")
            .Cellule("H9", "DV1");
        var r = Convertir(g, "Proposition-planning-1ASISR-2026-2027.xlsx");
        var relu = LecteurPlanning.Lire(r.Json!);
        Assert.True(relu.Valide, string.Join("/", relu.Erreurs));
        Assert.Equal("cned-1a-sisr-2026-2027", relu.Planning!.Id);
        Assert.Contains("SISR", relu.Planning.Titre);
        Assert.Equal(r.Planning!.Periodes.Select(p => p.Cle), relu.Planning.Periodes.Select(p => p.Cle));
    }

    // ---- Remplacement du planning actif -------------------------------------------------------------

    [Fact]
    public void Remplacer_garde_les_dates_personnelles_des_elements_identiques()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "excel-cned-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var depot = new DepotPlanning(new BaseDeDonnees(Path.Combine(dossier, "test.db")), new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero)));

            var ancien = Convertir(Grille1(), "Proposition-planning-1ASLAM-2026-2027.xlsx");
            Assert.True(depot.Remplacer(ancien.Json!, "slam.xlsx").Enregistre);
            var periode = Planning(ancien).Periodes[0];
            depot.DefinirSurcharge("cned-1a-slam-2026-2027", periode.Cle, new Surcharge(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 25)));
            depot.DefinirSurcharge("cned-1a-slam-2026-2027", "periode:Bloc 1|Disparue", new Surcharge(new DateOnly(2026, 10, 5), null));

            // Autre fichier (SISR) : même période « B1.3 », une autre en plus.
            var nouveau = Convertir(Grille1().Cellule("K7", "B1.4-SQ1-2").Fusion("K7:L7"), "Proposition-planning-1ASISR-2026-2027.xlsx");
            Assert.Equal(1, depot.SurchargesConservees(nouveau.Planning!));
            Assert.True(depot.Remplacer(nouveau.Json!, "sisr.xlsx").Enregistre);

            Assert.Equal("cned-1a-sisr-2026-2027", depot.ChargerActif()!.Id);
            var surcharges = depot.ChargerReglages("cned-1a-sisr-2026-2027").Surcharges;
            Assert.Equal(new[] { periode.Cle }, surcharges.Keys.ToArray());
            Assert.Equal(new DateOnly(2026, 10, 25), surcharges[periode.Cle].Fin);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, recursive: true);
        }

        static Grille Grille1() => new Grille()
            .Cellule("A7", "Bloc de compétences 1").Fusion("A7:A8").Cellule("B7", 0)
            .Cellule("D7", "B1.3-SQ1-4").Fusion("D7:I7");
    }
}
