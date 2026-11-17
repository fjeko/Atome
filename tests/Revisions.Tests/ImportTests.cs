using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Import et réimport des packs dans une vraie base SQLite (fichier temporaire).</summary>
public sealed class ImportTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-tests-" + Guid.NewGuid().ToString("N"));
    private readonly BaseDeDonnees _base;
    private readonly ImportateurPacks _importateur;
    private readonly DepotRevisions _revisions;
    private readonly FakeTimeProvider _horloge = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Jour = new(2026, 11, 16);

    public ImportTests()
    {
        Directory.CreateDirectory(_dossier);
        _base = new BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        _importateur = new ImportateurPacks(_base, _horloge);
        _revisions = new DepotRevisions(_base);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // libère le fichier avant de le supprimer
        Directory.Delete(_dossier, recursive: true);
    }

    private string Ecrire(string nom, string json)
    {
        string chemin = Path.Combine(_dossier, nom);
        File.WriteAllText(chemin, json);
        return chemin;
    }

    private long Compter(string table)
    {
        using var connexion = _base.Ouvrir();
        return connexion.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
    }

    /// <summary>Simule une révision de la première carte du pack.</summary>
    private void ReviserPremiereCarte()
    {
        var carte = _revisions.CartesDuPack("maths-sq1-s03")[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Bien, Jour, 0.9);
        long session = _revisions.CreerSession("seance", Jour, _horloge.GetUtcNow());
        _revisions.EnregistrerReponse(session, carte.Etat, resultat, Note.Bien, Jour, _horloge.GetUtcNow(), 4000, true, 5);
    }

    [Fact]
    public void Premier_import_cree_le_pack_et_des_cartes_nouvelles()
    {
        var resultat = _importateur.ImporterFichier(Ecrire("pack.json", PackExemple.Json), null);

        Assert.Equal(StatutImport.Importe, resultat.Statut);
        Assert.Equal(3, Compter("carte"));
        Assert.Equal(3, Compter("etat_carte"));
        Assert.All(_revisions.CartesDuPack("maths-sq1-s03"), c => Assert.True(c.Etat.EstNouvelle));
        var biblio = new DepotBibliotheque(_base);
        var journee = new SelectionDuJour(_revisions, biblio, new DepotAcquis(_base, biblio), new DepotPlanning(_base, _horloge)).Charger(Jour);
        Assert.Equal(3, journee.Nouvelles(10).Count);
    }

    [Fact]
    public void Les_nouvelles_cartes_se_debloquent_a_la_lecture_et_la_session_porte_motifs_et_partie()
    {
        _importateur.ImporterFichier(Ecrire("pack.json", PackExemple.Json), null);
        var biblio = new DepotBibliotheque(_base);
        var selection = new SelectionDuJour(_revisions, biblio, new DepotAcquis(_base, biblio), new DepotPlanning(_base, _horloge));

        // Cours pas encore lu : aucune nouvelle carte, mais le choix « cours non lus » les propose.
        var avant = selection.Charger(Jour).Composer(Revisions.Core.Sessions.DemandeSession.DuJour, 40, 10);
        Assert.Empty(avant.Cartes);
        Assert.Equal(3, avant.NouvellesBloquees);
        Assert.Equal(3, selection.Charger(Jour, nouvellesNonLues: true).Composer(Revisions.Core.Sessions.DemandeSession.DuJour, 40, 10).Cartes.Count);

        // Ouverture en lecture : débloquée, et comptée dans « Ce que je viens de lire ».
        biblio.MarquerLue("maths-sq1-s03", _horloge.GetLocalNow());
        Assert.Equal(Jour, biblio.DernieresLectures()["maths-sq1-s03"]);
        var journee = selection.Charger(Jour);
        var session = journee.Composer(Revisions.Core.Sessions.DemandeSession.LectureRecente, 40, 10);
        var cartes = journee.Charger(session);
        Assert.Equal(3, cartes.Count);
        Assert.All(cartes, c => Assert.Equal("Nouvelle · tu as lu ce cours", c.Motif!.Texte));
        Assert.All(cartes, c => Assert.False(string.IsNullOrEmpty(c.Partie)));

        // Une révision ratée : la carte devient un point faible.
        var carte = cartes[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Rate, Jour, 0.9);
        long id = _revisions.CreerSession("quotidienne", Jour, _horloge.GetUtcNow());
        _revisions.EnregistrerReponse(id, carte.Etat, resultat, Note.Rate, Jour, _horloge.GetUtcNow(), 4000, false, 1);
        var faibles = selection.Charger(Jour).Composer(Revisions.Core.Sessions.DemandeSession.PointsFaibles, 40, 10);
        Assert.Equal("Point faible · ratée 1 fois", Assert.Single(faibles.Cartes).Motif.Texte);
    }

    [Fact]
    public void Reimport_identique_ne_change_rien()
    {
        string fichier = Ecrire("pack.json", PackExemple.Json);
        _importateur.ImporterFichier(fichier, null);

        var resultat = _importateur.ImporterFichier(fichier, null);

        Assert.Equal(StatutImport.Inchange, resultat.Statut);
        Assert.Equal(1, Compter("journal_import"));
    }

    [Fact]
    public void Reimport_modifie_conserve_l_historique_et_archive_les_cartes_disparues()
    {
        string fichier = Ecrire("pack.json", PackExemple.Json);
        _importateur.ImporterFichier(fichier, null);
        ReviserPremiereCarte();

        // Nouvelle version : texte de la carte 1 changé, carte 3 supprimée, carte 4 ajoutée.
        File.WriteAllText(fichier, PackExemple.Modifie(p =>
        {
            p.Carte(0)["recto"] = "Nouveau recto";
            var cartes = p["cartes"]!.AsArray();
            var nouvelle = System.Text.Json.Nodes.JsonNode.Parse(cartes[0]!.ToJsonString())!.AsObject();
            nouvelle["id"] = "maths-sq1-s03-c004";
            cartes.RemoveAt(2);
            cartes.Add(nouvelle);
        }));
        var resultat = _importateur.ImporterFichier(fichier, null);

        Assert.Equal(StatutImport.MisAJour, resultat.Statut);
        var cartes = _revisions.CartesDuPack("maths-sq1-s03");
        Assert.Equal(["maths-sq1-s03-c001", "maths-sq1-s03-c002", "maths-sq1-s03-c004"], cartes.Select(c => c.Carte.Id).Order());

        var premiere = cartes.Single(c => c.Carte.Id == "maths-sq1-s03-c001");
        Assert.Equal("Nouveau recto", premiere.Carte.Contenu.Recto);   // contenu mis à jour
        Assert.False(premiere.Etat.EstNouvelle);                        // état conservé
        Assert.Equal(1, premiere.Etat.Repetitions);
        Assert.Equal(1, Compter("revision"));                           // historique conservé
        Assert.True(cartes.Single(c => c.Carte.Id == "maths-sq1-s03-c004").Etat.EstNouvelle);
        Assert.Equal(4, Compter("carte"));                              // la carte 3 est archivée, pas supprimée
    }

    [Fact]
    public void Carte_archivee_qui_revient_retrouve_son_historique()
    {
        string fichier = Ecrire("pack.json", PackExemple.Json);
        _importateur.ImporterFichier(fichier, null);
        ReviserPremiereCarte();

        File.WriteAllText(fichier, PackExemple.Modifie(p => p["cartes"]!.AsArray().RemoveAt(0)));
        _importateur.ImporterFichier(fichier, null);
        Assert.Equal(2, _revisions.CartesDuPack("maths-sq1-s03").Count);

        File.WriteAllText(fichier, PackExemple.Json);
        _importateur.ImporterFichier(fichier, null);

        var revenue = _revisions.CartesDuPack("maths-sq1-s03").Single(c => c.Carte.Id == "maths-sq1-s03-c001");
        Assert.Equal(1, revenue.Etat.Repetitions);
    }

    [Fact]
    public void Pack_invalide_est_refuse_et_la_base_reste_intacte()
    {
        var resultat = _importateur.ImporterFichier(
            Ecrire("pack.json", PackExemple.Modifie(p => p.Carte(0)["section"] = "nulle-part")), null);

        Assert.Equal(StatutImport.Refuse, resultat.Statut);
        Assert.Equal(0, Compter("pack"));
        Assert.Equal(0, Compter("carte"));
    }

    [Fact]
    public void Identifiant_de_carte_pris_par_un_autre_pack_est_refuse()
    {
        _importateur.ImporterFichier(Ecrire("a.json", PackExemple.Json), null);

        var resultat = _importateur.ImporterFichier(
            Ecrire("b.json", PackExemple.Modifie(p =>
            {
                p["id"] = "autre-pack";
                p["sections"]![0]!["id"] = "autre-pack-s01";
                foreach (var carte in p["cartes"]!.AsArray()) carte!["section"] = "autre-pack-s01";
            })), null);

        Assert.Equal(StatutImport.Refuse, resultat.Statut);
        Assert.Contains(resultat.Problemes, p => p.Message.Contains("maths-sq1-s03"));
        Assert.Equal(1, Compter("pack"));
    }

    [Fact]
    public void Deux_fichiers_avec_le_meme_pack_dans_un_dossier_le_second_est_refuse()
    {
        Ecrire("a.json", PackExemple.Json);
        Ecrire("b.json", PackExemple.Modifie(p => p["resume"] = "Autre résumé."));

        var resultats = _importateur.ImporterDossier(_dossier, null);

        Assert.Equal(StatutImport.Importe, resultats[0].Statut);
        Assert.Equal(StatutImport.Refuse, resultats[1].Statut);
    }

    [Fact]
    public void Document_d_origine_introuvable_donne_un_avertissement()
    {
        var resultat = _importateur.ImporterFichier(Ecrire("pack.json", PackExemple.Json), _dossier);

        Assert.Equal(StatutImport.Importe, resultat.Statut);
        Assert.Contains(resultat.Problemes, p => p.Chemin == "source.fichier");
    }

    [Fact]
    public void Reponse_enregistree_met_a_jour_l_etat_l_historique_et_l_xp()
    {
        _importateur.ImporterFichier(Ecrire("pack.json", PackExemple.Json), null);
        ReviserPremiereCarte();

        var profil = new DepotProfil(_base);
        Assert.Equal(5, profil.XpTotal());
        Assert.Equal(1, profil.CartesFaites(Jour));
        Assert.Equal(1, profil.NouvellesVues(Jour));
        Assert.Equal([Jour], profil.JoursActifs());
        Assert.Equal(0, _revisions.NombreDeDues(Jour));
        Assert.Equal(1, _revisions.NombreDeDues(Jour.AddDays(30)));
    }
}
