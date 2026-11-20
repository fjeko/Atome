using Dapper;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Annuler la dernière note : état FSRS et file de session exactement restaurés, historique marqué et non supprimé.</summary>
public sealed class AnnulerNoteTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "revisions-annuler-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Jour = new(2026, 11, 16);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void Annuler_restaure_l_etat_de_la_carte_et_ignore_la_ligne_d_historique()
    {
        Directory.CreateDirectory(_dossier);
        var baseDeDonnees = new BaseDeDonnees(Path.Combine(_dossier, "t.db"));
        string pack = Path.Combine(_dossier, "pack.json");
        File.WriteAllText(pack, PackExemple.Json);
        new ImportateurPacks(baseDeDonnees, TimeProvider.System).ImporterFichier(pack, null);
        var depot = new DepotRevisions(baseDeDonnees);
        var notation = new ServiceNotation(depot, new Planificateur(new Fsrs()));

        var carte = depot.CartesDuPack("maths-sq1-s03")[0];
        long session = depot.CreerSession("seance", Jour, DateTimeOffset.UtcNow);
        notation.Noter(session, carte, Note.Facile, retour: false, Jour, DateTimeOffset.UtcNow, 3000, 0.9);
        Assert.NotNull(depot.CartesDuPack("maths-sq1-s03")[0].Etat.Stabilite);
        Assert.Single(depot.DernieresNotes(carte.Carte.Id, 4));

        Assert.True(depot.AnnulerDerniereNotation(session, carte.Etat));

        var apres = depot.CartesDuPack("maths-sq1-s03")[0].Etat;
        Assert.Null(apres.Stabilite);                     // la carte est de nouveau « nouvelle »
        Assert.Equal(0, apres.Repetitions);
        Assert.Empty(depot.DernieresNotes(carte.Carte.Id, 4));
        using var c = baseDeDonnees.Ouvrir();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM revision WHERE annulee = 1")); // marquée, pas supprimée
        Assert.Equal(0, new DepotProfil(baseDeDonnees).CartesFaites(Jour));                       // ignorée par les chiffres
        Assert.False(depot.AnnulerDerniereNotation(session, carte.Etat));                         // rien d'autre à annuler
    }

    [Fact]
    public void La_file_de_session_revient_exactement_en_arriere()
    {
        CarteAReviser Carte(string id) =>
            new(new Carte(id, "s", TypeCarte.Question, 1, new ContenuCarte()), new EtatCarte { CarteId = id }, "p", "Maths", "S", "P");
        var session = new SessionEnCours([Carte("a"), Carte("b"), Carte("c")]);
        var photo = session.Capturer();
        session.Avancer(ratee: true);
        Assert.Equal(4, session.Total);
        session.Restaurer(photo);
        Assert.Equal(3, session.Total);
        Assert.Equal("a", session.Courante!.Carte.Id);
        Assert.False(session.CouranteEstUnRetour);
    }
}
