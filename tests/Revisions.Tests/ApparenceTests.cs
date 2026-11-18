using Microsoft.Data.Sqlite;
using Revisions.Core.Apparence;
using Revisions.Core.Modeles;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Couleurs, thèmes, contrastes et enregistrement des choix de personnalisation.</summary>
public class ApparenceTests
{
    private static readonly Couleur AccentWindows = Couleur.Parse("#0067C0");

    // ---- Couleur -----------------------------------------------------------------------------

    [Theory]
    [InlineData("#2F7DE1", 0x2F, 0x7D, 0xE1)]
    [InlineData("2f7de1", 0x2F, 0x7D, 0xE1)]
    [InlineData(" #FFF ", 255, 255, 255)]
    public void Une_couleur_se_lit_depuis_son_code(string texte, byte r, byte g, byte b)
    {
        Assert.True(Couleur.TryParse(texte, out var couleur));
        Assert.Equal(new Couleur(r, g, b), couleur);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rouge")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Un_texte_qui_n_est_pas_une_couleur_est_refuse(string? texte) =>
        Assert.False(Couleur.TryParse(texte, out _));

    [Fact]
    public void Le_code_hexadecimal_fait_l_aller_retour() =>
        Assert.Equal("#2F7DE1", Couleur.Parse("#2f7de1").Hex);

    [Fact]
    public void Contraste_noir_sur_blanc_vaut_21()
    {
        Assert.Equal(21, Couleur.Contraste(Couleur.Noir, Couleur.Blanc), 3);
        Assert.Equal(1, Couleur.Contraste(Couleur.Blanc, Couleur.Blanc), 3);
    }

    [Fact]
    public void Le_texte_lisible_est_noir_sur_clair_et_blanc_sur_fonce()
    {
        Assert.Equal(Couleur.Noir, Couleur.Parse("#FFE066").TexteLisible);
        Assert.Equal(Couleur.Blanc, Couleur.Parse("#1B2A49").TexteLisible);
    }

    [Fact]
    public void Ajuster_pour_contraste_atteint_le_minimum_demande()
    {
        var fondSombre = Couleur.Parse("#1F1F1F");
        var bleuFonce = Couleur.Parse("#10206B"); // illisible tel quel sur fond sombre
        Assert.True(Couleur.Contraste(bleuFonce, fondSombre) < 4.5);

        var ajuste = bleuFonce.AjusterPourContraste(fondSombre, 4.5);
        Assert.True(Couleur.Contraste(ajuste, fondSombre) >= 4.5);

        // Une couleur déjà lisible n'est pas modifiée.
        var clair = Couleur.Parse("#60CDFF");
        Assert.Equal(clair, clair.AjusterPourContraste(fondSombre, 4.5));
    }

    // ---- Thèmes ------------------------------------------------------------------------------

    [Fact]
    public void Il_y_a_les_trois_themes_neutres_et_quatre_a_six_themes_colores()
    {
        Assert.Equal(["systeme", "clair", "sombre"], Themes.Tous.Take(3).Select(t => t.Cle));
        Assert.InRange(Themes.Tous.Count(t => t.Fond is not null), 4, 6);
        Assert.Equal(Themes.Tous.Count, Themes.Tous.Select(t => t.Cle).Distinct().Count());
    }

    [Fact]
    public void Une_cle_inconnue_donne_le_theme_par_defaut()
    {
        Assert.Equal("systeme", Themes.Trouver("n-existe-pas").Cle);
        Assert.Equal("systeme", Themes.Trouver(null).Cle);
        Assert.Equal("ocean", Themes.Trouver("ocean").Cle);
    }

    [Fact]
    public void Le_theme_comme_windows_suit_le_mode_du_systeme()
    {
        var theme = Themes.Trouver("systeme");
        Assert.True(Themes.Resoudre(theme, null, systemeSombre: true, AccentWindows).Sombre);
        Assert.False(Themes.Resoudre(theme, null, systemeSombre: false, AccentWindows).Sombre);
    }

    [Fact]
    public void L_accent_choisi_passe_avant_celui_du_theme_qui_passe_avant_celui_de_windows()
    {
        var rose = Couleur.Parse("#D8498F");
        var ocean = Themes.Trouver("ocean");

        var perso = Themes.Resoudre(ocean, rose, false, AccentWindows);
        Assert.Equal(rose, perso.Accent);
        Assert.True(perso.AccentImpose);

        Assert.Equal(ocean.Accent, Themes.Resoudre(ocean, null, false, AccentWindows).Accent);

        var neutre = Themes.Resoudre(Themes.Trouver("clair"), null, false, AccentWindows);
        Assert.Equal(AccentWindows, neutre.Accent);
        Assert.False(neutre.AccentImpose); // on laisse Windows décider
    }

    /// <summary>Toutes les combinaisons proposées : chaque thème, avec son accent puis avec chaque pastille.</summary>
    public static IEnumerable<object[]> Combinaisons()
    {
        foreach (var theme in Themes.Tous)
        foreach (bool systemeSombre in new[] { false, true })
        {
            yield return [theme.Cle, "", systemeSombre];
            foreach (var (_, couleur) in Themes.Accents) yield return [theme.Cle, couleur.Hex, systemeSombre];
        }
    }

    [Theory]
    [MemberData(nameof(Combinaisons))]
    public void Chaque_combinaison_reste_lisible(string cleTheme, string accentHex, bool systemeSombre)
    {
        var theme = Themes.Trouver(cleTheme);
        Couleur? accent = accentHex.Length == 0 ? null : Couleur.Parse(accentHex);
        var p = Themes.Resoudre(theme, accent, systemeSombre, AccentWindows);

        // Texte principal : contraste renforcé (7). Texte secondaire et liens : texte normal (4,5).
        Assert.True(Couleur.Contraste(p.Texte, p.Feuille) >= 7, "texte sur feuille");
        Assert.True(Couleur.Contraste(p.Texte, p.FondDoux) >= 7, "texte sur encadré");
        Assert.True(Couleur.Contraste(p.Discret, p.Feuille) >= 4.5, "texte discret sur feuille");
        Assert.True(Couleur.Contraste(p.AccentTexte, p.Feuille) >= 4.5, "lien sur feuille");
        Assert.True(Couleur.Contraste(p.TexteSurAccent, p.Accent) >= 4.5, "texte d'un bouton d'accent");
        if (p.Fond is { } fond)
            Assert.True(Couleur.Contraste(p.Texte, fond) >= 7, "texte sur le fond des fenêtres");
    }

    // ---- Enregistrement des choix ----------------------------------------------------------

    [Fact]
    public void Le_theme_et_la_couleur_d_accent_sont_conserves_d_un_lancement_a_l_autre()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "revisions-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string chemin = Path.Combine(dossier, "test.db");
            var profil = new DepotProfil(new BaseDeDonnees(chemin));

            // Par défaut : thème sombre, pas de couleur imposée.
            var defaut = profil.ChargerReglages();
            Assert.Equal("sombre", defaut.Theme);
            Assert.Null(defaut.CouleurAccent);

            profil.EnregistrerReglages(new Reglages { Theme = "foret", CouleurAccent = "#d8498f", PlafondQuotidien = 55 });

            // On repart d'une base rouverte, comme au lancement suivant.
            var relu = new DepotProfil(new BaseDeDonnees(chemin)).ChargerReglages();
            Assert.Equal("foret", relu.Theme);
            Assert.Equal("#D8498F", relu.CouleurAccent);
            Assert.Equal(55, relu.PlafondQuotidien);

            // Revenir à « celle du thème » efface bien la couleur.
            relu.CouleurAccent = null;
            profil.EnregistrerReglages(relu);
            Assert.Null(profil.ChargerReglages().CouleurAccent);

            // Des valeurs abîmées retombent sur des choix sûrs.
            profil.EcrireValeur("theme", "inconnu");
            profil.EcrireValeur("couleur_accent", "pas-une-couleur");
            var repare = profil.ChargerReglages();
            Assert.Equal("systeme", repare.Theme);
            Assert.Null(repare.CouleurAccent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, recursive: true);
        }
    }
}
