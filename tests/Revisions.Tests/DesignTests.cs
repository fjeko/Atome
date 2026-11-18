using Microsoft.Data.Sqlite;
using Revisions.Core.Apparence;
using Revisions.Core.Mascotte;
using Revisions.Core.Modeles;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>
/// Système de design (docs/design-atome.md) : contrastes WCAG AA de toutes les paires texte / fond,
/// dans chaque thème et avec chaque couleur d'accent ; couleurs des matières ; banque de messages de la mascotte ;
/// réglages d'apparence et migration du thème par défaut.
/// </summary>
public class DesignTests
{
    private static readonly Couleur AccentWindows = Couleur.Parse("#0067C0");

    /// <summary>Toutes les combinaisons à vérifier : chaque thème, Windows clair ou sombre, chaque accent proposé ou aucun.</summary>
    public static IEnumerable<object[]> Combinaisons()
    {
        var accents = Themes.Accents.Select(a => (Couleur?)a.Couleur).Append(null);
        foreach (var theme in Themes.Tous)
            foreach (bool systemeSombre in new[] { false, true })
                foreach (var accent in accents)
                    yield return [theme.Cle, systemeSombre, accent?.Hex ?? "aucun"];
    }

    [Theory]
    [MemberData(nameof(Combinaisons))]
    public void Toutes_les_paires_texte_fond_respectent_le_contraste_WCAG_AA(string cleTheme, bool systemeSombre, string accent)
    {
        Couleur? perso = accent == "aucun" ? null : Couleur.Parse(accent);
        var palette = Themes.Resoudre(Themes.Trouver(cleTheme), perso, systemeSombre, AccentWindows);
        var charte = Charte.Calculer(palette);

        var echecs = charte.Paires().Where(p => !p.Conforme)
            .Select(p => $"{p.Nom} : {p.Devant} sur {p.Fond} = {p.Contraste:0.00} (minimum {p.Minimum})")
            .ToList();
        Assert.True(echecs.Count == 0, $"Thème {cleTheme}, accent {accent} :\n" + string.Join("\n", echecs));
    }

    [Fact]
    public void Chaque_teinte_de_matiere_a_ses_nuances_dans_les_deux_modes()
    {
        foreach (bool sombre in new[] { false, true })
        {
            var charte = Charte.Calculer(Themes.Resoudre(Themes.Trouver(sombre ? "sombre" : "clair"), null, false, AccentWindows));
            Assert.Equal(Enum.GetValues<Teinte>().Length, charte.Matieres.Count);
            // Fond pâle : clair en thème clair, profond en thème sombre.
            foreach (var n in charte.Matieres.Values)
                Assert.Equal(sombre, n.Pale.Luminance < 0.2);
        }
    }

    [Fact]
    public void Les_couleurs_de_matiere_restent_fixes_quel_que_soit_l_accent()
    {
        var sansAccent = Charte.Calculer(Themes.Resoudre(Themes.Trouver("sombre"), null, false, AccentWindows));
        var avecAccent = Charte.Calculer(Themes.Resoudre(Themes.Trouver("sombre"), Couleur.Parse("#D8498F"), false, AccentWindows));
        Assert.Equal(sansAccent.Matieres[Teinte.Bleu], avecAccent.Matieres[Teinte.Bleu]);
        Assert.Equal(sansAccent.Matieres[Teinte.Orange], avecAccent.Matieres[Teinte.Orange]);
    }

    [Theory]
    [InlineData("#D64545", false)] // rouge : se confondrait avec « Raté »
    [InlineData("#2E9E5B", false)] // vert : se confondrait avec « Bien »
    [InlineData("#D9A300", false)] // jaune : se confondrait avec « Difficile »
    [InlineData("#6B7785", false)] // presque gris
    [InlineData("#8B5CF6", true)]  // violet : « Facile » garde l'accent
    [InlineData("#2F7DE1", true)]
    public void Le_bouton_Facile_ne_se_confond_jamais_avec_les_autres_notes(string accent, bool garde)
    {
        var charte = Charte.Calculer(Themes.Resoudre(Themes.Trouver("sombre"), Couleur.Parse(accent), false, AccentWindows));
        Assert.Equal(garde, charte.Facile == charte.AccentDoux);
    }

    [Fact]
    public void Tsl_donne_les_couleurs_attendues()
    {
        Assert.Equal(Couleur.Parse("#FF0000"), Couleur.DepuisTsl(0, 1, 0.5));
        Assert.Equal(Couleur.Parse("#00FF00"), Couleur.DepuisTsl(120, 1, 0.5));
        Assert.Equal(Couleur.Parse("#0000FF"), Couleur.DepuisTsl(240, 1, 0.5));
        Assert.Equal(Couleur.Parse("#808080"), Couleur.DepuisTsl(42, 0, 0.5));
        Assert.Equal(Couleur.Blanc, Couleur.DepuisTsl(200, 0.7, 1));
    }

    // ---- Repères des matières -------------------------------------------------------------------

    [Theory]
    [InlineData("Bloc 1 – Support et mise à disposition de services informatiques", null, Teinte.Bleu)]
    [InlineData("Bloc 1 – Support et mise à disposition de services informatiques", "B1.2 Partie I – SQ2 Créer une application de bureau", Teinte.Violet)]
    [InlineData("Bloc 1 – Support et mise à disposition de services informatiques", "B1.2 Partie II – SQ1 Installer de nouveaux postes", Teinte.Bleu)]
    [InlineData("Bloc 1 – Support et mise à disposition de services informatiques", "B1.2 – Répondre aux incidents › Thème 1 – Premiers programmes en C#", Teinte.Violet)]
    [InlineData("Bloc 2 – SLAM : conception et développement d'applications", null, Teinte.Indigo)]
    [InlineData("Bloc 3 – Cybersécurité des services informatiques", null, Teinte.Corail)]
    [InlineData("Mathématiques pour l'informatique", null, Teinte.Orange)]
    [InlineData("Algorithmique appliquée", null, Teinte.Ocre)]
    [InlineData("CEJM – Culture économique, juridique et managériale", null, Teinte.Vert)]
    [InlineData("Anglais", null, Teinte.Rose)]
    [InlineData("", null, Teinte.Gris)]
    [InlineData(null, null, Teinte.Gris)]
    public void Chaque_matiere_a_sa_teinte(string? matiere, string? partie, Teinte attendue) =>
        Assert.Equal(attendue, ReperesMatieres.Pour(matiere, partie).Teinte);

    [Fact]
    public void Une_matiere_inconnue_recoit_toujours_la_meme_teinte_de_repli()
    {
        var premiere = ReperesMatieres.Pour("Physique appliquée");
        var seconde = ReperesMatieres.Pour("physique appliquee");
        Assert.Equal(premiere, seconde);
        Assert.Contains(premiere.Teinte, new[] { Teinte.Sarcelle, Teinte.Cyan, Teinte.Brun, Teinte.Lime });
        Assert.Equal(ReperesMatieres.IconeLivre, premiere.Icone);
    }

    // ---- Mascotte ----------------------------------------------------------------------------------

    [Fact]
    public void La_banque_a_tous_les_contextes_avec_5_a_10_variantes()
    {
        string[] attendus =
        [
            BanqueMessages.Bonjour, BanqueMessages.BonRetour, BanqueMessages.Serie, BanqueMessages.DevoirProche,
            BanqueMessages.PlafondAtteint, BanqueMessages.RienAFaire, BanqueMessages.FinSession, BanqueMessages.NiveauGagne,
        ];
        foreach (string contexte in attendus)
            Assert.True(BanqueMessages.Contextes.ContainsKey(contexte), contexte);
        foreach (var (contexte, (_, messages)) in BanqueMessages.Contextes)
        {
            Assert.InRange(messages.Count, 5, 10);
            Assert.Equal(messages.Count, messages.Distinct().Count());
        }
    }

    [Fact]
    public void Aucun_message_n_est_culpabilisant()
    {
        // Règle R1 : jamais de reproche, de retard ni de menace sur la série.
        string[] interdits = ["tu as raté", "en retard", "perds pas", "perdre ta série", "dommage", "tu aurais dû", "honte", "pas assez"];
        foreach (var (contexte, (_, messages)) in BanqueMessages.Contextes)
            foreach (string message in messages)
            {
                foreach (string mot in interdits)
                    Assert.False(message.Contains(mot, StringComparison.OrdinalIgnoreCase), $"{contexte} : « {message} »");
                Assert.True(message.Length <= 90, $"Message trop long : « {message} »");
            }
    }

    [Fact]
    public void Les_valeurs_sont_remplacees_et_l_humeur_suit_le_contexte()
    {
        var hasard = new Random(3);
        for (int i = 0; i < 20; i++)
        {
            var m = BanqueMessages.Tirer(BanqueMessages.NiveauGagne, hasard, new Dictionary<string, string> { ["niveau"] = "7" });
            Assert.DoesNotContain("{", m.Texte);
            Assert.Equal(Humeur.Fiere, m.Humeur);
        }
        Assert.Equal(Humeur.Endormie, BanqueMessages.Tirer(BanqueMessages.BonRetour).Humeur);
        Assert.Equal(Humeur.Curieuse, BanqueMessages.Tirer(BanqueMessages.RienAFaire).Humeur);
        Assert.Equal(Humeur.Contente, BanqueMessages.Tirer("inconnu").Humeur);
    }

    // ---- Contexte du message d'accueil -------------------------------------------------------------

    private static SituationAccueil Situation(bool retour = false, bool plafond = false, bool rien = false, bool deja = false,
        bool debutSemaine = false, int serie = 0, string? devoir = null, int joursDevoir = -1) =>
        new(retour, retour ? 9 : 1, plafond, rien, deja, debutSemaine, serie, devoir, joursDevoir);

    [Fact]
    public void Le_retour_apres_une_pause_passe_avant_tout()
    {
        var (contexte, valeurs) = ContexteAccueil.Choisir(Situation(retour: true, plafond: true, serie: 4, debutSemaine: true));
        Assert.Equal(BanqueMessages.BonRetour, contexte);
        Assert.Equal("9", valeurs["jours"]);
    }

    [Theory]
    [InlineData(false, true, false, BanqueMessages.PlafondAtteint)]
    [InlineData(false, false, true, BanqueMessages.RienAFaire)]
    [InlineData(true, false, false, BanqueMessages.Rebonjour)]
    [InlineData(false, false, false, BanqueMessages.Bonjour)]
    public void Le_contexte_suit_la_journee(bool deja, bool plafond, bool rien, string attendu) =>
        Assert.Equal(attendu, ContexteAccueil.Choisir(Situation(deja: deja, plafond: plafond, rien: rien)).Contexte);

    [Fact]
    public void Un_devoir_dans_la_semaine_et_une_serie_en_debut_de_semaine_sont_mentionnes()
    {
        Assert.Equal(BanqueMessages.DevoirProche, ContexteAccueil.Choisir(Situation(devoir: "le DV1 (Maths)", joursDevoir: 4)).Contexte);
        Assert.Equal(BanqueMessages.Bonjour, ContexteAccueil.Choisir(Situation(devoir: "le DV1 (Maths)", joursDevoir: 20)).Contexte);
        Assert.Equal(BanqueMessages.Serie, ContexteAccueil.Choisir(Situation(serie: 3, debutSemaine: true)).Contexte);
        // Une série d'une seule semaine, ou au milieu de la semaine : un simple bonjour.
        Assert.Equal(BanqueMessages.Bonjour, ContexteAccueil.Choisir(Situation(serie: 1, debutSemaine: true)).Contexte);
        Assert.Equal(BanqueMessages.Bonjour, ContexteAccueil.Choisir(Situation(serie: 3)).Contexte);
    }

    [Fact]
    public void Un_devoir_en_tete_de_phrase_prend_une_majuscule()
    {
        for (int graine = 0; graine < 30; graine++)
        {
            var m = BanqueMessages.Tirer(BanqueMessages.DevoirProche, new Random(graine), new Dictionary<string, string> { ["devoir"] = "le DV1 (Maths)" });
            Assert.True(char.IsUpper(m.Texte[0]), m.Texte);
        }
    }

    // ---- Données d'affichage de l'accueil ----------------------------------------------------------

    [Fact]
    public void La_session_du_jour_est_repartie_par_matiere_de_la_plus_presente_a_la_moins_presente()
    {
        var parts = Revisions.Core.Sessions.Repartition.ParMatiere(["Maths", "Anglais", "Maths", "CEJM", "Anglais", "Maths"]);
        Assert.Equal(["Maths", "Anglais", "CEJM"], parts.Select(p => p.Matiere));
        Assert.Equal([3, 2, 1], parts.Select(p => p.Nombre));
    }

    [Fact]
    public void Toutes_les_collections_masquees_se_reconnaissent()
    {
        static Revisions.Core.Collections.InfoCollection Col(string cle, int cours, bool visible) => new(cle, cle, cours, cours * 10, visible);
        Assert.True(Revisions.Core.Collections.Collections.ToutesMasquees([Col("libres", 440, false), Col("mes-packs", 12, false)]));
        Assert.False(Revisions.Core.Collections.Collections.ToutesMasquees([Col("libres", 440, false), Col("mes-packs", 12, true)]));
        // Une collection vide visible ne compte pas ; aucune collection du tout, ce n'est pas « masqué ».
        Assert.True(Revisions.Core.Collections.Collections.ToutesMasquees([Col("libres", 440, false), Col("manuel", 0, true)]));
        Assert.False(Revisions.Core.Collections.Collections.ToutesMasquees([]));
    }

    // ---- Réglages d'apparence ----------------------------------------------------------------------

    private static void AvecBase(Action<DepotProfil, string> essai)
    {
        string dossier = Path.Combine(Path.GetTempPath(), "revisions-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string chemin = Path.Combine(dossier, "test.db");
            essai(new DepotProfil(new BaseDeDonnees(chemin)), chemin);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void Au_premier_lancement_le_theme_est_sombre_et_les_sons_coupes() => AvecBase((profil, _) =>
    {
        var r = profil.ChargerReglages();
        Assert.Equal("sombre", r.Theme);
        Assert.False(r.Sons);
        Assert.False(r.ReduireAnimations);
        Assert.Equal(100, r.TailleTexte);
        Assert.Equal("normale", r.Mascotte);
    });

    [Theory]
    [InlineData("crepuscule")]
    [InlineData("clair")]
    [InlineData("systeme")]
    [InlineData("sable")]
    public void Un_theme_deja_choisi_est_garde(string cle) => AvecBase((profil, _) =>
    {
        profil.EcrireValeur("theme", cle);
        Assert.Equal(cle, profil.ChargerReglages().Theme);
    });

    [Fact]
    public void Les_reglages_d_apparence_sont_conserves_et_bornes() => AvecBase((profil, chemin) =>
    {
        profil.EnregistrerReglages(new Reglages { TailleTexte = 125, ReduireAnimations = true, Sons = true, Mascotte = "reduite", NavigationRepliee = true });
        var relu = new DepotProfil(new BaseDeDonnees(chemin)).ChargerReglages();
        Assert.Equal(125, relu.TailleTexte);
        Assert.True(relu.ReduireAnimations);
        Assert.True(relu.Sons);
        Assert.Equal("reduite", relu.Mascotte);
        Assert.True(relu.NavigationRepliee);

        profil.EcrireValeur("taille_texte", "400");
        profil.EcrireValeur("mascotte", "geante");
        var borne = profil.ChargerReglages();
        Assert.Equal(Reglages.TailleTexteMax, borne.TailleTexte);
        Assert.Equal("normale", borne.Mascotte);
    });
}
