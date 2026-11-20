using Revisions.Core.Modeles;
using Revisions.Core.Sessions;

namespace Revisions.Tests;

/// <summary>Cartes coriaces : détection (3 « Raté » sur les 4 dernières notes) et retrait d'une carte de la file de session.</summary>
public sealed class CartesCoriacesTests
{
    [Theory]
    [InlineData(new[] { 1, 1, 1 }, true)]
    [InlineData(new[] { 1, 1, 3, 1 }, true)]
    [InlineData(new[] { 1, 3, 1, 3 }, false)]
    [InlineData(new[] { 1, 1 }, false)]
    [InlineData(new[] { 3, 3, 3, 3, 1, 1, 1 }, false)] // seules les 4 plus récentes comptent
    public void Coriace_si_trois_echecs_sur_les_quatre_dernieres_notes(int[] notes, bool attendu) =>
        Assert.Equal(attendu, CartesCoriaces.EstCoriace(notes));

    private static CarteAReviser Carte(string id) =>
        new(new Carte(id, "s", TypeCarte.Question, 1, new ContenuCarte()), new EtatCarte { CarteId = id }, "p", "Maths", "Séance", "Partie");

    [Fact]
    public void Retirer_enleve_la_carte_de_la_suite_sans_toucher_au_passe()
    {
        var session = new SessionEnCours([Carte("a"), Carte("b"), Carte("c")]);
        session.Avancer(ratee: true);          // « a » ratée : elle est replacée plus loin
        Assert.Equal("b", session.Courante!.Carte.Id);
        Assert.False(session.Retirer("a"));    // la retirer ne change pas la carte courante
        session.Avancer(false);
        session.Avancer(false);
        Assert.True(session.EstTerminee);      // « a » n'est jamais revenue
        Assert.Equal(3, session.Faites);
    }

    [Fact]
    public void Retirer_la_carte_courante_passe_a_la_suivante()
    {
        var session = new SessionEnCours([Carte("a"), Carte("b")]);
        Assert.True(session.Retirer("a"));
        Assert.Equal("b", session.Courante!.Carte.Id);
    }
}

/// <summary>Une carte mise de côté disparaît des sessions mais reste retrouvable, avec son historique.</summary>
public sealed class CartesMisesDeCoteTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-cote-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void Mettre_de_cote_puis_remettre_en_jeu()
    {
        Directory.CreateDirectory(_dossier);
        var baseDeDonnees = new Revisions.Data.BaseDeDonnees(Path.Combine(_dossier, "t.db"));
        string pack = Path.Combine(_dossier, "pack.json");
        File.WriteAllText(pack, PackExemple.Json);
        new Revisions.Data.ImportateurPacks(baseDeDonnees, TimeProvider.System).ImporterFichier(pack, null);
        var depot = new Revisions.Data.DepotRevisions(baseDeDonnees);

        var cartes = depot.CartesDuPack("maths-sq1-s03");
        var carte = cartes[0];
        depot.DefinirSuspendue(carte.Carte.Id, true);

        Assert.DoesNotContain(depot.CartesDuPack("maths-sq1-s03"), c => c.Carte.Id == carte.Carte.Id);
        Assert.DoesNotContain(depot.CandidatesNouvelles(), c => c.CarteId == carte.Carte.Id);
        Assert.Single(depot.CartesMisesDeCote());

        depot.DefinirSuspendue(carte.Carte.Id, false);
        Assert.Contains(depot.CartesDuPack("maths-sq1-s03"), c => c.Carte.Id == carte.Carte.Id);
        Assert.Empty(depot.CartesMisesDeCote());
    }
}
