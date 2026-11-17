using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Modeles;
using Revisions.Core.Signalements;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Signalements d'erreurs : migration, enregistrement, export.</summary>
public sealed class SignalementsTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "atome-signalements-" + Guid.NewGuid().ToString("N"));
    private readonly BaseDeDonnees _base;
    private readonly DepotSignalements _depot;
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    public SignalementsTests()
    {
        Directory.CreateDirectory(_dossier);
        _base = new BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        // Un vrai pack importé : le signalement y lit l'empreinte et la source.
        File.WriteAllText(Path.Combine(_dossier, "pack.json"), PackExemple.Json);
        new ImportateurPacks(_base, new FakeTimeProvider(Maintenant)).ImporterFichier(Path.Combine(_dossier, "pack.json"), null);
        _depot = new DepotSignalements(_base);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private Signalement SignalerCarte(MotifSignalement motif = MotifSignalement.ReponseFausse, string? commentaire = "Colonnes, pas lignes") =>
        _depot.Enregistrer(CibleSignalement.Carte, "maths-sq1-s03-c001", "maths-sq1-s03", motif, commentaire, null,
            "Question ?", "Réponse.", Maintenant);

    [Fact]
    public void La_migration_cree_la_table_et_la_version_du_schema()
    {
        using var connexion = _base.Ouvrir();
        Assert.Equal(1, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='signalement'"));
        Assert.Equal(5, connexion.ExecuteScalar<long>("PRAGMA user_version;"));
    }

    [Fact]
    public void Un_signalement_garde_le_texte_affiche_et_les_infos_du_pack()
    {
        var envoye = SignalerCarte(MotifSignalement.Faute);

        var lu = Assert.Single(_depot.Lister());
        Assert.Equal(envoye.Id, lu.Id);
        Assert.True(Guid.TryParse(lu.Id, out _));
        Assert.Equal(CibleSignalement.Carte, lu.Cible);
        Assert.Equal("maths-sq1-s03-c001", lu.CibleId);
        Assert.Equal("maths-sq1-s03", lu.PackId);
        Assert.Equal(MotifSignalement.Faute, lu.Motif);
        Assert.Equal("Colonnes, pas lignes", lu.Commentaire);
        Assert.Equal("Question ?", lu.TexteRecto);
        Assert.Equal("Réponse.", lu.TexteVerso);
        Assert.Equal(Maintenant, lu.DateHeure);
        Assert.False(lu.Exporte);
        Assert.Equal(64, lu.PackEmpreinte?.Length); // SHA-256 en hexadécimal
        Assert.Equal("cours/maths/td2.pdf", lu.SourceFichier);
        Assert.Equal(2, lu.PageSource); // page de la section de la carte
        Assert.Contains("matrices", lu.Seance);
    }

    [Fact]
    public void Un_signalement_de_section_garde_titre_et_contenu()
    {
        _depot.Enregistrer(CibleSignalement.Section, "maths-sq1-s03-s01", "maths-sq1-s03", MotifSignalement.Affichage,
            "  ", "Quand ?", "Formule : $A$.", null, Maintenant);

        var lu = Assert.Single(_depot.Lister());
        Assert.Equal(CibleSignalement.Section, lu.Cible);
        Assert.Equal("Quand ?", lu.Titre);
        Assert.Equal("Formule : $A$.", lu.TexteRecto);
        Assert.Null(lu.Commentaire); // un commentaire vide n'est pas gardé
        Assert.Equal(2, lu.PageSource);
    }

    [Fact]
    public void Marquer_exporte_et_supprimer()
    {
        var a = SignalerCarte();
        var b = SignalerCarte(MotifSignalement.Autre, null);
        Assert.Equal(2, _depot.CompterNouveaux());

        _depot.MarquerExportes([a.Id]);
        Assert.Equal(1, _depot.CompterNouveaux());
        Assert.True(_depot.Lister().Single(s => s.Id == a.Id).Exporte);

        _depot.Supprimer(b.Id);
        Assert.Equal([a.Id], _depot.Lister().Select(s => s.Id));
    }

    [Fact]
    public void L_export_respecte_le_format_et_exclut_les_signalements_supprimes()
    {
        var garde = SignalerCarte();
        var supprime = SignalerCarte(MotifSignalement.QuestionPasClaire, "autre");
        _depot.Supprimer(supprime.Id);

        string json = ExportSignalements.Creer(_depot.Lister(), " Flo ", Maintenant, "0.1.0");
        using var document = JsonDocument.Parse(json);
        var racine = document.RootElement;

        Assert.Equal(1, racine.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Flo", racine.GetProperty("auteur").GetString());
        Assert.Equal("0.1.0", racine.GetProperty("versionAtome").GetString());
        Assert.Equal(Maintenant.ToString("O"), racine.GetProperty("dateExport").GetString());

        var s = Assert.Single(racine.GetProperty("signalements").EnumerateArray());
        Assert.Equal(garde.Id, s.GetProperty("id").GetString());
        Assert.Equal("carte", s.GetProperty("type").GetString());
        Assert.Equal("maths-sq1-s03-c001", s.GetProperty("cibleId").GetString());
        Assert.Equal("maths-sq1-s03", s.GetProperty("packId").GetString());
        Assert.Equal("reponse_fausse", s.GetProperty("motif").GetString());
        Assert.Equal("Colonnes, pas lignes", s.GetProperty("commentaire").GetString());
        Assert.Equal("Question ?", s.GetProperty("affiche").GetProperty("recto").GetString());
        Assert.Equal("Réponse.", s.GetProperty("affiche").GetProperty("verso").GetString());
        Assert.Equal("cours/maths/td2.pdf", s.GetProperty("pack").GetProperty("sourceFichier").GetString());
        Assert.Equal(64, s.GetProperty("pack").GetProperty("empreinte").GetString()!.Length);
        Assert.DoesNotContain(supprime.Id, json);
    }

    [Fact]
    public void L_export_sans_auteur_et_pour_une_section()
    {
        _depot.Enregistrer(CibleSignalement.Section, "maths-sq1-s03-s01", "maths-sq1-s03", MotifSignalement.Autre,
            null, "Titre", "Contenu", null, Maintenant);

        using var document = JsonDocument.Parse(ExportSignalements.Creer(_depot.Lister(), "  ", Maintenant, "0.1.0"));
        Assert.False(document.RootElement.TryGetProperty("auteur", out _));
        var s = document.RootElement.GetProperty("signalements")[0];
        Assert.Equal("section", s.GetProperty("type").GetString());
        Assert.Equal("Contenu", s.GetProperty("affiche").GetProperty("contenu").GetString());
        Assert.Equal("Titre", s.GetProperty("affiche").GetProperty("titre").GetString());
    }

    [Theory]
    [InlineData("Flo", "signalements-Flo-2026-11-17.json")]
    [InlineData("Marie Curie", "signalements-Marie-Curie-2026-11-17.json")]
    [InlineData("a/b:c", "signalements-abc-2026-11-17.json")]
    [InlineData("  ", "signalements-2026-11-17.json")]
    [InlineData(null, "signalements-2026-11-17.json")]
    public void Nom_du_fichier_d_export(string? auteur, string attendu) =>
        Assert.Equal(attendu, ExportSignalements.NomFichier(auteur, new DateOnly(2026, 11, 17)));

    [Fact]
    public void Texte_des_cartes_qcm_et_exercice()
    {
        var qcm = new Carte("c", "s", TypeCarte.Qcm, 1, new ContenuCarte
        {
            Question = "Q ?", Choix = ["un", "deux"], BonneReponse = 1, Explication = "Parce que.",
        });
        Assert.Equal("Q ?\nA. un\nB. deux", TexteCarte.Recto(qcm));
        Assert.Equal("Bonne réponse : B. deux\nParce que.", TexteCarte.Verso(qcm));

        var exo = new Carte("c", "s", TypeCarte.Exercice, 1, new ContenuCarte
        {
            Enonce = "Calcule.", Etapes = ["d'abord"], Reponse = "42",
        });
        Assert.Equal("Calcule.", TexteCarte.Recto(exo));
        Assert.Equal("Étape 1 : d'abord\n42", TexteCarte.Verso(exo));
    }
}
