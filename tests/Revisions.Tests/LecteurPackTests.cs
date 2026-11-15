using System.Text.Json.Nodes;
using Revisions.Core.Modeles;
using Revisions.Core.Packs;

namespace Revisions.Tests;

/// <summary>Règles de validation de packs/FORMAT.md.</summary>
public class LecteurPackTests
{
    private static ResultatLecture Lire(Action<JsonObject> modification) =>
        LecteurPack.Lire(PackExemple.Modifie(modification));

    private static void AssertRefuse(ResultatLecture resultat, string cheminAttendu)
    {
        Assert.False(resultat.EstValide);
        Assert.Contains(resultat.Erreurs, e => e.Chemin.Contains(cheminAttendu));
    }

    [Fact]
    public void Pack_valide_est_accepte_sans_probleme()
    {
        var resultat = LecteurPack.Lire(PackExemple.Json);

        Assert.True(resultat.EstValide);
        Assert.Empty(resultat.Problemes);
        var pack = resultat.Pack!;
        Assert.Equal("maths-sq1-s03", pack.Id);
        Assert.Null(pack.Sequence);
        Assert.Single(pack.Sections);
        Assert.Equal(3, pack.Cartes.Count);
        Assert.Equal(TypeCarte.Qcm, pack.Cartes[1].Type);
        Assert.Equal(4, pack.Cartes[1].Contenu.Choix!.Count);
        Assert.Equal(2, pack.Cartes[2].Contenu.Etapes!.Count);
    }

    [Theory]
    [InlineData("{ \"formatVersion\": 1, }")]          // virgule finale
    [InlineData("{ /* commentaire */ \"id\": \"x\" }")] // commentaire
    [InlineData("{ \"formatVersion\": 1")]              // texte tronqué
    [InlineData("[1, 2]")]                              // pas un objet
    public void Json_invalide_est_refuse(string json)
    {
        var resultat = LecteurPack.Lire(json);
        Assert.False(resultat.EstValide);
        Assert.NotEmpty(resultat.Erreurs);
    }

    [Fact]
    public void Erreur_de_syntaxe_indique_la_ligne()
    {
        var resultat = LecteurPack.Lire("{\n  \"formatVersion\": 1,\n  oups\n}");
        Assert.Contains("ligne 3", resultat.Erreurs.Single().Message);
    }

    [Theory]
    [InlineData("formatVersion")]
    [InlineData("id")]
    [InlineData("matiere")]
    [InlineData("partie")]
    [InlineData("seance")]
    [InlineData("source")]
    [InlineData("resume")]
    [InlineData("notions")]
    [InlineData("sections")]
    [InlineData("cartes")]
    public void Champ_obligatoire_manquant_est_refuse_et_nomme(string champ) =>
        AssertRefuse(Lire(p => p.Remove(champ)), champ);

    [Theory]
    [InlineData("bloc")]
    [InlineData("sequence")]
    [InlineData("ordre")]
    public void Champ_facultatif_peut_manquer(string champ) =>
        Assert.True(Lire(p => p.Remove(champ)).EstValide);

    [Fact]
    public void Version_plus_recente_demande_une_mise_a_jour()
    {
        var resultat = Lire(p => p["formatVersion"] = 2);
        Assert.False(resultat.EstValide);
        Assert.Contains("plus récente", resultat.Erreurs.Single().Message);
    }

    [Fact]
    public void Identifiant_de_pack_avec_majuscule_est_refuse() =>
        AssertRefuse(Lire(p => p["id"] = "Maths_SQ1"), "id");

    [Theory]
    [InlineData("code")]
    [InlineData("inconnu")]
    public void Type_de_carte_inconnu_est_refuse(string type)
    {
        var resultat = Lire(p => p.Carte(0)["type"] = type);
        AssertRefuse(resultat, "cartes[0].type");
        Assert.Equal("maths-sq1-s03-c001", resultat.Erreurs.First().CarteId);
    }

    [Theory]
    [InlineData(0, "verso")]
    [InlineData(1, "choix")]
    [InlineData(1, "bonneReponse")]
    [InlineData(1, "explication")]
    [InlineData(2, "etapes")]
    [InlineData(2, "reponse")]
    [InlineData(0, "difficulte")]
    [InlineData(0, "section")]
    public void Champ_de_carte_manquant_est_refuse(int carte, string champ) =>
        AssertRefuse(Lire(p => p.Carte(carte).Remove(champ)), $"cartes[{carte}].{champ}");

    [Fact]
    public void Identifiant_de_carte_en_double_est_refuse() =>
        AssertRefuse(Lire(p => p.Carte(1)["id"] = "maths-sq1-s03-c001"), "cartes[1].id");

    [Fact]
    public void Identifiant_de_section_en_double_est_refuse() =>
        AssertRefuse(Lire(p => p["sections"]!.AsArray().Add(JsonNode.Parse(p["sections"]![0]!.ToJsonString()))), "sections[1].id");

    [Fact]
    public void Section_inexistante_est_refusee() =>
        AssertRefuse(Lire(p => p.Carte(0)["section"] = "nulle-part"), "cartes[0].section");

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Bonne_reponse_hors_des_choix_est_refusee(int index) =>
        AssertRefuse(Lire(p => p.Carte(1)["bonneReponse"] = index), "cartes[1].bonneReponse");

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Nombre_de_choix_hors_de_2_a_5_est_refuse(int nombre) =>
        AssertRefuse(Lire(p =>
        {
            p.Carte(1)["choix"] = new JsonArray(Enumerable.Range(0, nombre).Select(i => (JsonNode)$"choix {i}").ToArray());
            p.Carte(1)["bonneReponse"] = 0;
        }), "cartes[1].choix");

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Difficulte_hors_de_1_a_3_est_refusee(int difficulte) =>
        AssertRefuse(Lire(p => p.Carte(0)["difficulte"] = difficulte), "cartes[0].difficulte");

    [Fact]
    public void Notion_sans_carte_donne_un_avertissement_pas_un_refus()
    {
        var resultat = Lire(p =>
        {
            p["notions"]!.AsArray().Add("Non-commutativité");
            var section = JsonNode.Parse(p["sections"]![0]!.ToJsonString())!.AsObject();
            section["id"] = "maths-sq1-s03-s02";
            section["notions"] = new JsonArray("Non-commutativité");
            p["sections"]!.AsArray().Add(section);
        });

        Assert.True(resultat.EstValide);
        Assert.Contains(resultat.Avertissements, a => a.Message.Contains("Non-commutativité") && a.Message.Contains("aucune carte"));
    }

    [Fact]
    public void Bom_est_tolere_avec_un_avertissement()
    {
        var resultat = LecteurPack.Lire("﻿" + PackExemple.Json);
        Assert.True(resultat.EstValide);
        Assert.Single(resultat.Avertissements);
    }

    [Fact]
    public void Le_pack_de_demonstration_est_valide()
    {
        // On remonte depuis le dossier des tests compilés jusqu'à trouver app/samples.
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "samples", "exemple.json")))
            dossier = dossier.Parent;
        Assert.NotNull(dossier);

        var resultat = LecteurPack.Lire(File.ReadAllText(Path.Combine(dossier.FullName, "samples", "exemple.json")));

        Assert.True(resultat.EstValide);
        Assert.Empty(resultat.Problemes);
    }

    [Fact]
    public void Champs_inconnus_sont_ignores() =>
        Assert.True(Lire(p => p["futurChamp"] = "valeur").EstValide);
}
