using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Collections;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Collections de packs : appartenance, masquage sans perte, réaffichage sans avalanche.</summary>
public sealed class CollectionsTests : IDisposable
{
    private static readonly DateOnly Jour = new(2026, 11, 16);
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-coll-" + Guid.NewGuid().ToString("N"));
    private readonly BaseDeDonnees _base;
    private readonly ImportateurPacks _importateur;
    private readonly DepotCollections _collections;
    private readonly DepotBibliotheque _biblio;
    private readonly DepotRevisions _revisions;
    private readonly FakeTimeProvider _horloge = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    public CollectionsTests()
    {
        Directory.CreateDirectory(_dossier);
        _base = new BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        _importateur = new ImportateurPacks(_base, _horloge);
        _collections = new DepotCollections(_base);
        _biblio = new DepotBibliotheque(_base);
        _revisions = new DepotRevisions(_base);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private static string Contenu(string id) => PackExemple.Json.Replace("maths-sq1-s03", id);

    private string Fichier(string nom, string id) => Path.Combine(_dossier, nom).Ecrire(Contenu(id));

    private void ImporterLibre() => _importateur.ImporterFichier(Fichier("libre.json", "libre-maths-s03"), null);
    private void ImporterPerso() => _importateur.ImporterFichier(Fichier("perso.json", "perso-s03"), null);

    private List<string> Visibles() => _biblio.ListerSeances(Jour).Select(s => s.PackId).Order().ToList();

    private void FixerEcheances(string packId, DateOnly echeance)
    {
        using var c = _base.Ouvrir();
        c.Execute("UPDATE etat_carte SET stabilite = 5, difficulte_fsrs = 5, echeance = @e, derniere_revision = @e, repetitions = 1 WHERE carte_id IN (SELECT id FROM carte WHERE pack_id = @packId)",
            new { e = echeance.ToString("yyyy-MM-dd"), packId });
    }

    // ---- Logique pure (Core) -----------------------------------------------------------------

    [Theory]
    [InlineData("libre-x", null, null, "libres")]
    [InlineData("libre-x", "mes-packs", "manuel", "libres")]   // un cours libre reste libre
    [InlineData("en-a1-u01", "mes-packs", null, "anglais")]   // l'anglais général a sa propre collection
    [InlineData("x", null, null, "mes-packs")]
    [InlineData("x", null, "manuel", "manuel")]
    [InlineData("x", "mes-packs", "importe:ami", "mes-packs")] // réimporter ne déplace pas un pack connu
    public void Appartenance_d_un_pack(string id, string? existante, string? import, string attendu) =>
        Assert.Equal(attendu, Collections.Resoudre(id, existante, import));

    [Fact]
    public void Cle_et_nom_d_un_contenu_importe()
    {
        Assert.Equal("importe:contenu-de-lea", Collections.CleImportee("Contenu de lea"));
        Assert.Equal("Contenu de Léa", Collections.NomImporte(" Léa ", "x.atome"));
        Assert.Equal("Importé : cours-bloc1", Collections.NomImporte(null, @"C:\tmp\cours-bloc1.atome"));
    }

    [Fact]
    public void On_ne_peut_pas_masquer_la_derniere_collection_visible()
    {
        var liste = new List<InfoCollection>
        {
            new("libres", "Cours libres", 10, 50, true),
            new("mes-packs", "Mes packs", 5, 20, false),
        };
        Assert.False(Collections.PeutMasquer(liste, "libres"));
        liste[1] = liste[1] with { Visible = true };
        Assert.True(Collections.PeutMasquer(liste, "libres"));
    }

    [Fact]
    public void Les_cours_libres_livres_mais_non_charges_apparaissent_masques()
    {
        var liste = Collections.AvecLibresAbsents([new InfoCollection("mes-packs", "Mes packs", 3, 9, true)], 440);
        Assert.Equal(["libres", "mes-packs"], liste.Select(c => c.Cle));
        Assert.False(liste[0].Visible);
        Assert.Equal(440, liste[0].NbCours);
    }

    [Fact]
    public void Etalement_au_plus_N_par_jour_les_plus_en_retard_d_abord()
    {
        var cartes = Enumerable.Range(0, 10).Select(i => new EcheanceCarte($"c{i:00}", Jour.AddDays(-10 + i))).ToList();
        var decalees = Reaffichage.Etaler(cartes, Jour, null, 4).ToDictionary(c => c.CarteId, c => c.Echeance);

        Assert.Equal(6, decalees.Count);                  // les 4 plus en retard restent dues aujourd'hui
        Assert.False(decalees.ContainsKey("c00"));
        Assert.Equal(Jour.AddDays(1), decalees["c04"]);
        Assert.Equal(Jour.AddDays(2), decalees["c09"]);
        Assert.All(decalees.Values.GroupBy(d => d), g => Assert.True(g.Count() <= 4));
    }

    [Fact]
    public void Etalement_ignore_ce_qui_etait_deja_en_retard_avant_le_masquage_et_les_petits_lots()
    {
        var cartes = new[]
        {
            new EcheanceCarte("avant", Jour.AddDays(-30)),
            new EcheanceCarte("a", Jour.AddDays(-2)), new EcheanceCarte("b", Jour.AddDays(-1)),
            new EcheanceCarte("futur", Jour.AddDays(3)),
        };
        Assert.Empty(Reaffichage.Etaler(cartes, Jour, Jour.AddDays(-5), 5)); // 2 cartes seulement : rien à étaler
        Assert.Single(Reaffichage.Etaler(cartes, Jour, Jour.AddDays(-5), 1), c => c.CarteId == "b");
    }

    // ---- Import et appartenance --------------------------------------------------------------

    [Fact]
    public void Les_packs_sont_ranges_selon_leur_provenance()
    {
        ImporterLibre();
        ImporterPerso();
        _importateur.ImporterFichier(Fichier("m.json", "manuel-s03"), null, null, Collections.Manuel, Collections.NomManuel);
        _importateur.ImporterFichier(Fichier("i.json", "ami-s03"), null, null, "importe:ami", "Contenu de Léa");

        var liste = _collections.Lister();
        var parCle = liste.ToDictionary(c => c.Cle);
        Assert.Equal(["libres", "mes-packs", "importe:ami", "manuel"], liste.Select(c => c.Cle)); // ordre d'affichage
        Assert.Equal(new InfoCollection("libres", "Cours libres", 1, 3, true), parCle["libres"]);
        Assert.Equal("Contenu de Léa", parCle["importe:ami"].Nom);
        Assert.Equal(1, parCle["manuel"].NbCours);
    }

    // ---- Masquage et réaffichage -------------------------------------------------------------

    [Fact]
    public void Masquer_retire_cours_cartes_et_nouvelles_cartes_sans_toucher_aux_autres()
    {
        ImporterLibre();
        ImporterPerso();

        Assert.Equal(ResultatBascule.Fait, _collections.DefinirVisible("libres", false, Jour));

        Assert.Equal(["perso-s03"], Visibles());
        Assert.Empty(_revisions.CartesDuPack("libre-maths-s03"));
        Assert.DoesNotContain(_revisions.CandidatesNouvelles(), c => c.PackId.StartsWith("libre-"));
        Assert.Equal(3, _revisions.CandidatesNouvelles().Count);
        Assert.False(_collections.Lister().Single(c => c.Cle == "libres").Visible);
    }

    [Fact]
    public void Les_cartes_dues_d_une_collection_masquee_sortent_des_sessions()
    {
        ImporterLibre();
        ImporterPerso();
        FixerEcheances("libre-maths-s03", Jour.AddDays(-1));
        FixerEcheances("perso-s03", Jour.AddDays(-1));
        Assert.Equal(6, _revisions.NombreDeDues(Jour));

        _collections.DefinirVisible("libres", false, Jour);

        Assert.Equal(3, _revisions.NombreDeDues(Jour));
        Assert.All(_revisions.CartesDues(Jour), c => Assert.Equal("perso-s03", c.PackId));
        Assert.DoesNotContain(_revisions.CartesVuesNonDues(Jour), c => c.PackId.StartsWith("libre-"));
    }

    [Fact]
    public void On_ne_masque_pas_la_derniere_collection_visible()
    {
        ImporterLibre();
        Assert.Equal(ResultatBascule.DerniereCollection, _collections.DefinirVisible("libres", false, Jour));
        Assert.Equal(["libre-maths-s03"], Visibles());
        Assert.Equal(ResultatBascule.Inconnue, _collections.DefinirVisible("n-existe-pas", true, Jour));
    }

    [Fact]
    public void Reafficher_conserve_la_progression_a_l_identique()
    {
        ImporterLibre();
        ImporterPerso();
        var carte = _revisions.CartesDuPack("libre-maths-s03")[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Bien, Jour, 0.9);
        long session = _revisions.CreerSession("seance", Jour, _horloge.GetUtcNow());
        _revisions.EnregistrerReponse(session, carte.Etat, resultat, Note.Bien, Jour, _horloge.GetUtcNow(), 3000, true, 5);
        var avant = _revisions.CartesDuPack("libre-maths-s03").Single(c => c.Carte.Id == carte.Carte.Id).Etat;

        _collections.DefinirVisible("libres", false, Jour);
        _collections.DefinirVisible("libres", true, Jour.AddDays(1));   // échéance pas encore atteinte : rien ne bouge

        var apres = _revisions.CartesDuPack("libre-maths-s03").Single(c => c.Carte.Id == carte.Carte.Id).Etat;
        Assert.Equal(avant, apres);
        Assert.Equal(3, _revisions.CartesDuPack("libre-maths-s03").Count);
        using var c = _base.Ouvrir();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM revision WHERE carte_id = @id", new { id = carte.Carte.Id }));
    }

    [Fact]
    public void Reafficher_apres_une_longue_pause_etale_les_retards_sans_toucher_a_la_memoire()
    {
        ImporterLibre();
        ImporterPerso();
        FixerEcheances("libre-maths-s03", Jour.AddDays(5));
        _collections.DefinirVisible("libres", false, Jour);

        // Trois semaines plus tard, les trois cartes sont dues depuis longtemps ; on étale à 1 par jour.
        var retour = Jour.AddDays(21);
        _collections.DefinirVisible("libres", true, retour, parJour: 1);

        var cartes = _revisions.CartesDuPack("libre-maths-s03");
        var echeances = cartes.Select(c => c.Etat.Echeance!.Value).Order().ToList();
        Assert.Equal([Jour.AddDays(5), retour.AddDays(1), retour.AddDays(2)], echeances);   // la plus en retard reste due, les autres sont étalées
        Assert.All(cartes, c => Assert.Equal(5, c.Etat.Stabilite));      // mémoire intacte
        Assert.Equal(1, _revisions.CartesDues(retour).Count(c => c.PackId.StartsWith("libre-")));
    }

    [Fact]
    public void Un_pack_qui_arrive_dans_une_collection_masquee_reste_masque_meme_apres_mise_a_jour()
    {
        ImporterLibre();
        ImporterPerso();
        _collections.DefinirVisible("mes-packs", false, Jour);

        // Mise à jour du fichier d'un pack de la collection masquée : ses cartes ne reviennent pas.
        string fichier = Path.Combine(_dossier, "perso.json");
        File.WriteAllText(fichier, Contenu("perso-s03").Replace("Le produit de matrices.", "Le produit, version 2."));
        var r = _importateur.ImporterFichier(fichier, null);
        Assert.Equal(StatutImport.MisAJour, r.Statut);
        Assert.Empty(_revisions.CartesDuPack("perso-s03"));
        Assert.Equal(["libre-maths-s03"], Visibles());

        // Un pack nouveau dans cette collection arrive masqué aussi.
        _importateur.ImporterFichier(Fichier("perso2.json", "perso2-s03"), null);
        Assert.Equal(["libre-maths-s03"], Visibles());

        _collections.DefinirVisible("mes-packs", true, Jour);
        Assert.Equal(["libre-maths-s03", "perso-s03", "perso2-s03"], Visibles());
        Assert.Equal(3, _revisions.CartesDuPack("perso2-s03").Count);
    }

    // ---- Migration ---------------------------------------------------------------------------

    [Fact]
    public void La_migration_range_les_packs_existants_et_garde_les_cours_libres_deja_masques()
    {
        // On ramène la base à l'état d'avant la migration 7, avec un cours libre masqué.
        ImporterLibre();
        ImporterPerso();
        using (var c = _base.Ouvrir())
        {
            c.Execute("UPDATE pack SET masque = 1 WHERE id LIKE 'libre-%'");
            c.Execute("DROP TABLE collection");
            c.Execute("ALTER TABLE pack DROP COLUMN collection");
            c.Execute("ALTER TABLE pack DROP COLUMN derniere_lecture");
            c.Execute("ALTER TABLE etat_carte DROP COLUMN suspendue");
            c.Execute("ALTER TABLE revision DROP COLUMN annulee");
            c.Execute("PRAGMA user_version = 6");
        }
        SqliteConnection.ClearAllPools();

        var liste = new DepotCollections(new BaseDeDonnees(_base.Chemin)).Lister().ToDictionary(x => x.Cle);

        Assert.False(liste["libres"].Visible);
        Assert.True(liste["mes-packs"].Visible);
        Assert.Equal(1, liste["libres"].NbCours);
    }

    [Fact]
    public void La_migration_14_sort_l_anglais_general_de_mes_packs()
    {
        // Avant la version 14, un pack « en-… » était rangé dans « Mes packs ».
        _importateur.ImporterFichier(Fichier("en.json", "en-a1-u01"), null);
        ImporterPerso();
        using (var c = _base.Ouvrir())
        {
            c.Execute("UPDATE pack SET collection = 'mes-packs' WHERE id LIKE 'en-%'");
            c.Execute("DELETE FROM collection WHERE cle = 'anglais'");
            c.Execute("PRAGMA user_version = 13");
        }
        SqliteConnection.ClearAllPools();

        var liste = new DepotCollections(new BaseDeDonnees(_base.Chemin)).Lister().ToDictionary(x => x.Cle);

        Assert.Equal("Anglais", liste["anglais"].Nom);
        Assert.True(liste["anglais"].Visible);
        Assert.Equal(1, liste["anglais"].NbCours);
        Assert.Equal(1, liste["mes-packs"].NbCours);
    }
}
