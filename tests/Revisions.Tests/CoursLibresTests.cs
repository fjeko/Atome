using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Modeles;
using Revisions.Core.Packs;
using Revisions.Core.Planification;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Cours libres livrés avec Atome : proposition au premier lancement, interrupteur, séparation d'avec les packs privés.</summary>
public sealed class CoursLibresTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-libres-" + Guid.NewGuid().ToString("N"));
    private readonly string _dossierLibres;
    private readonly BaseDeDonnees _base;
    private readonly ImportateurPacks _importateur;
    private readonly DepotCoursLibres _libres;
    private readonly DepotBibliotheque _biblio;
    private readonly DepotRevisions _revisions;
    private readonly FakeTimeProvider _horloge = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Jour = new(2026, 11, 16);

    public CoursLibresTests()
    {
        _dossierLibres = Path.Combine(_dossier, "contenu-libre", "packs");
        Directory.CreateDirectory(_dossierLibres);
        _base = new BaseDeDonnees(Path.Combine(_dossier, "test.db"));
        _importateur = new ImportateurPacks(_base, _horloge);
        _libres = new DepotCoursLibres(_base, _importateur);
        _biblio = new DepotBibliotheque(_base);
        _revisions = new DepotRevisions(_base);
        // Un cours libre : id « libre-… » et aucun document d'origine (source.fichier = null).
        File.WriteAllText(Path.Combine(_dossierLibres, "libre.json"),
            PackExemple.Json.Replace("maths-sq1-s03", "libre-maths-s03").Replace("\"fichier\": \"cours/maths/td2.pdf\", \"pages\": 6", "\"fichier\": null"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private void ImporterPackPrive() =>
        _importateur.ImporterFichier(Path.Combine(_dossier, "prive.json").Ecrire(PackExemple.Json), null);

    private List<string> Visibles() => _biblio.ListerSeances(Jour).Select(s => s.PackId).Order().ToList();

    // ---- Proposition au premier lancement ---------------------------------------------------

    [Theory]
    [InlineData(null, 0, true, ActionCoursLibres.Proposer)]              // aucun pack, rien décidé : on propose
    [InlineData(null, 3, true, ActionCoursLibres.DesactiverParDefaut)]   // packs privés : pas d'import automatique
    [InlineData(true, 0, true, ActionCoursLibres.Aucune)]                // déjà accepté
    [InlineData(false, 0, true, ActionCoursLibres.Aucune)]               // déjà refusé : on ne redemande pas
    [InlineData(null, 0, false, ActionCoursLibres.Aucune)]               // dossier des cours libres absent
    public void Decision_au_demarrage(bool? choix, int packsPrives, bool disponible, ActionCoursLibres attendu) =>
        Assert.Equal(attendu, ContenuLibre.Evaluer(choix, packsPrives, disponible));

    [Fact]
    public void Plus_tard_ne_fige_rien_et_la_proposition_revient_tant_qu_il_n_y_a_aucun_pack()
    {
        // « Plus tard » : le réglage reste null. Au lancement suivant, toujours aucun pack : on repropose.
        Assert.Null(new DepotProfil(_base).ChargerReglages().CoursLibres);
        Assert.Equal(0, _libres.NombrePacksPrives());
        Assert.Equal(ActionCoursLibres.Proposer, ContenuLibre.Evaluer(null, _libres.NombrePacksPrives(), true));
    }

    [Fact]
    public void Le_choix_est_enregistre_dans_les_reglages()
    {
        var profil = new DepotProfil(_base);
        foreach (bool? valeur in new bool?[] { true, false, null })
        {
            profil.EnregistrerReglages(new Reglages { CoursLibres = valeur });
            Assert.Equal(valeur, profil.ChargerReglages().CoursLibres);
        }
    }

    [Fact]
    public void Les_packs_libres_sans_document_d_origine_sont_acceptes_et_marques_Libre()
    {
        var resultats = _libres.Activer(_dossierLibres);

        Assert.Equal(StatutImport.Importe, Assert.Single(resultats).Statut);
        Assert.Empty(resultats[0].Problemes);
        var seance = Assert.Single(_biblio.ListerSeances(Jour));
        Assert.True(seance.Libre);
        Assert.Equal("", _biblio.ChargerSeance("libre-maths-s03")!.SourceFichier);
    }

    [Fact]
    public void Un_pack_prive_n_est_pas_marque_Libre()
    {
        ImporterPackPrive();
        Assert.False(Assert.Single(_biblio.ListerSeances(Jour)).Libre);
    }

    // ---- Interrupteur ------------------------------------------------------------------------

    [Fact]
    public void Masquer_cache_les_cours_libres_de_la_bibliotheque_et_des_revisions()
    {
        _libres.Activer(_dossierLibres);
        Assert.Equal(["libre-maths-s03"], Visibles());
        Assert.Equal(3, _revisions.CartesDuPack("libre-maths-s03").Count);

        _libres.Masquer();

        Assert.Empty(Visibles());
        Assert.Empty(_revisions.CartesDuPack("libre-maths-s03"));
        Assert.Equal(0, _libres.NombreVisibles());
    }

    [Fact]
    public void Reactiver_rend_les_cours_et_la_progression_sans_rien_perdre()
    {
        _libres.Activer(_dossierLibres);
        var carte = _revisions.CartesDuPack("libre-maths-s03")[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Bien, Jour, 0.9);
        long session = _revisions.CreerSession("seance", Jour, _horloge.GetUtcNow());
        _revisions.EnregistrerReponse(session, carte.Etat, resultat, Note.Bien, Jour, _horloge.GetUtcNow(), 4000, true, 5);

        _libres.Masquer();
        _libres.Activer(_dossierLibres);   // fichiers inchangés : seul le réaffichage agit

        Assert.Equal(["libre-maths-s03"], Visibles());
        var cartes = _revisions.CartesDuPack("libre-maths-s03");
        Assert.Equal(3, cartes.Count);
        Assert.Equal(1, cartes.Single(c => c.Carte.Id == carte.Carte.Id).Etat.Repetitions);
    }

    [Fact]
    public void Une_mise_a_jour_du_pack_libre_garde_la_progression()
    {
        _libres.Activer(_dossierLibres);
        var carte = _revisions.CartesDuPack("libre-maths-s03")[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Bien, Jour, 0.9);
        long session = _revisions.CreerSession("seance", Jour, _horloge.GetUtcNow());
        _revisions.EnregistrerReponse(session, carte.Etat, resultat, Note.Bien, Jour, _horloge.GetUtcNow(), 4000, true, 5);

        // Nouvelle version de l'application : le texte du pack change, les ids restent.
        string fichier = Path.Combine(_dossierLibres, "libre.json");
        File.WriteAllText(fichier, File.ReadAllText(fichier).Replace("Colonnes de $A$", "Nombre de colonnes de $A$"));
        var maj = _libres.Activer(_dossierLibres);

        Assert.Equal(StatutImport.MisAJour, Assert.Single(maj).Statut);
        var apres = _revisions.CartesDuPack("libre-maths-s03").Single(c => c.Carte.Id == carte.Carte.Id);
        Assert.Contains("Nombre de colonnes", apres.Carte.Contenu.Verso);
        Assert.Equal(1, apres.Etat.Repetitions);
    }

    // ---- Séparation d'avec les packs privés --------------------------------------------------

    [Fact]
    public void Les_packs_prives_ne_comptent_pas_les_cours_libres_et_inversement()
    {
        _libres.Activer(_dossierLibres);
        Assert.Equal(0, _libres.NombrePacksPrives());   // sans pack privé, on proposerait encore

        ImporterPackPrive();
        Assert.Equal(1, _libres.NombrePacksPrives());
    }

    [Fact]
    public void Masquer_ne_touche_jamais_aux_packs_prives()
    {
        ImporterPackPrive();
        _libres.Activer(_dossierLibres);
        Assert.Equal(["libre-maths-s03", "maths-sq1-s03"], Visibles());

        _libres.Masquer();

        Assert.Equal(["maths-sq1-s03"], Visibles());
        Assert.Equal(3, _revisions.CartesDuPack("maths-sq1-s03").Count);
    }

    [Fact]
    public void Cours_libres_et_prives_restent_deux_packs_distincts_sans_cartes_en_double()
    {
        // Même séance en privé et en libre : deux packs, chacun avec ses propres cartes (ids différents).
        ImporterPackPrive();
        _libres.Activer(_dossierLibres);

        var seances = _biblio.ListerSeances(Jour);
        Assert.Equal(2, seances.Count);
        Assert.Equal(1, seances.Count(s => s.Libre));
        Assert.Equal(6, seances.Sum(s => s.NbCartes));
        var toutes = _revisions.CartesDuPack("maths-sq1-s03").Concat(_revisions.CartesDuPack("libre-maths-s03")).Select(c => c.Carte.Id).ToList();
        Assert.Equal(toutes.Count, toutes.Distinct().Count());
        Assert.All(_revisions.CartesDuPack("libre-maths-s03"), c => Assert.StartsWith("libre-", c.Carte.Id));
    }

    [Fact]
    public void Un_cours_libre_masque_n_est_pas_reimporte_par_un_import_du_dossier_prive()
    {
        // Le dossier des packs privés et celui des cours libres sont deux dossiers distincts.
        string prives = Path.Combine(_dossier, "packs");
        Directory.CreateDirectory(prives);
        File.WriteAllText(Path.Combine(prives, "prive.json"), PackExemple.Json);
        _libres.Activer(_dossierLibres);
        _libres.Masquer();

        _importateur.ImporterDossier(prives, null);

        Assert.Equal(["maths-sq1-s03"], Visibles());
    }
}

internal static class AideTests
{
    /// <summary>Écrit un fichier de test et renvoie son chemin.</summary>
    public static string Ecrire(this string chemin, string contenu)
    {
        File.WriteAllText(chemin, contenu);
        return chemin;
    }
}
