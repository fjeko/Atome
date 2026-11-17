using System.IO.Compression;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.Modeles;
using Revisions.Core.Partage;
using Revisions.Core.Planification;
using Revisions.Data;

namespace Revisions.Tests;

/// <summary>Export et import d'un contenu .atome (packs, planning, documents d'origine).</summary>
public sealed class PartageTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "atome-partage-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private static readonly DateOnly Jour = new(2026, 11, 16);

    public PartageTests()
    {
        // Un « dossier Cours » d'exemple : packs/, planning/, cours/ … et des fichiers de progression qui ne doivent pas partir.
        _source = Path.Combine(_dossier, "source");
        Ecrire("packs/maths/sq1/s03.json", PackExemple.Json);
        Ecrire("packs/FORMAT.md", "# Format");
        Ecrire("planning/planning.json", "{}");
        Ecrire("cours/maths/td2.pdf", "%PDF faux");
        Ecrire("cours/Bloc 1/notes.md", "notes");
        Ecrire("revisions.db", "progression");
        Ecrire("cours/copie.db", "base cachée dans cours");
        Ecrire("reglages.json", "reglages");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    private void Ecrire(string relatif, string contenu)
    {
        string chemin = Path.Combine(_source, relatif);
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllText(chemin, contenu);
    }

    private string Exporter(bool documents)
    {
        string archive = Path.Combine(_dossier, "contenu.atome");
        PartageContenu.Exporter(_source, archive, documents);
        return archive;
    }

    private static List<string> Noms(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.Select(e => e.FullName).Order().ToList();
    }

    [Fact]
    public void Export_sans_documents_contient_packs_planning_et_manifeste()
    {
        var noms = Noms(Exporter(documents: false));

        Assert.Equal(["manifeste.json", "packs/FORMAT.md", "packs/maths/sq1/s03.json", "planning/planning.json"], noms);
    }

    [Fact]
    public void Export_avec_documents_garde_les_memes_chemins_relatifs()
    {
        string archive = Exporter(documents: true);

        Assert.Contains("cours/maths/td2.pdf", Noms(archive));
        Assert.Contains("cours/Bloc 1/notes.md", Noms(archive));
        var manifeste = PartageContenu.LireManifeste(archive);
        Assert.True(manifeste.DocumentsInclus);
        Assert.Equal(1, manifeste.NbPacks);
        Assert.Equal(2, manifeste.NbDocuments);
        Assert.Equal(1, manifeste.FormatVersion);
    }

    [Fact]
    public void Export_n_emporte_jamais_la_progression()
    {
        foreach (bool documents in new[] { false, true })
        {
            var noms = Noms(Exporter(documents));
            Assert.DoesNotContain(noms, n => n.EndsWith(".db") || n.Contains("reglages"));
        }
    }

    [Fact]
    public void Taille_des_documents_est_estimee()
    {
        Assert.Equal("%PDF faux".Length + "notes".Length, PartageContenu.EstimerTailleDocuments(_source));
    }

    [Fact]
    public void Manifeste_absent_ou_trop_recent_est_refuse()
    {
        string vide = Path.Combine(_dossier, "vide.atome");
        using (var zip = ZipFile.Open(vide, ZipArchiveMode.Create)) zip.CreateEntry("packs/a.json");
        Assert.Throws<ArchiveInvalideException>(() => PartageContenu.LireManifeste(vide));

        string futur = Path.Combine(_dossier, "futur.atome");
        using (var zip = ZipFile.Open(futur, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("manifeste.json").Open()))
            w.Write("""{"formatVersion":99,"application":"Atome","date":"2026-11-17T10:00:00+01:00","nbPacks":0,"documentsInclus":false,"nbDocuments":0}""");
        Assert.Throws<ArchiveInvalideException>(() => PartageContenu.LireManifeste(futur));
    }

    [Theory]
    [InlineData("packs/../../evil.txt")]
    [InlineData("../evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("packs/..\\..\\evil.txt")]
    public void Zip_slip_et_chemins_absolus_sont_refuses(string nomDangereux)
    {
        string archive = Exporter(documents: false);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        using (var w = new StreamWriter(zip.CreateEntry(nomDangereux).Open()))
            w.Write("piège");

        string cible = Path.Combine(_dossier, "cible", "contenu");
        Assert.Throws<ArchiveInvalideException>(() => PartageContenu.Extraire(archive, cible));

        // Rien n'a été écrit, ni dans la cible ni à côté.
        Assert.False(Directory.Exists(cible));
        Assert.False(File.Exists(Path.Combine(_dossier, "cible", "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_dossier, "evil.txt")));
    }

    [Fact]
    public void Extraction_recree_l_arborescence_et_les_documents_sont_la_ou_les_packs_les_attendent()
    {
        string cible = Path.Combine(_dossier, "chez-le-camarade");
        var resume = PartageContenu.Extraire(Exporter(documents: true), cible);

        Assert.Equal(1, resume.NbPacks);
        Assert.True(resume.DocumentsInclus);
        Assert.True(File.Exists(Path.Combine(cible, "packs", "maths", "sq1", "s03.json")));
        Assert.True(File.Exists(Path.Combine(cible, "planning", "planning.json")));
        // source.fichier du pack d'exemple = « cours/maths/td2.pdf », relatif au dossier qui contient packs/.
        Assert.True(File.Exists(Path.Combine(cible, "cours", "maths", "td2.pdf")));
        Assert.False(File.Exists(Path.Combine(cible, "manifeste.json")));
    }

    [Fact]
    public void Import_puis_reimport_ne_perd_pas_l_historique()
    {
        string cible = Path.Combine(_dossier, "contenu");
        string archive = Exporter(documents: false);
        var horloge = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var baseDeDonnees = new BaseDeDonnees(Path.Combine(_dossier, "camarade.db"));
        var importateur = new ImportateurPacks(baseDeDonnees, horloge);
        var revisions = new DepotRevisions(baseDeDonnees);

        PartageContenu.Extraire(archive, cible);
        var premier = importateur.ImporterDossier(Path.Combine(cible, "packs"), cible);
        Assert.Contains(premier, r => r.Statut == StatutImport.Importe);

        // Le camarade révise une carte.
        var carte = revisions.CartesDuPack("maths-sq1-s03")[0];
        var resultat = new Planificateur(new Fsrs()).Appliquer(carte.Etat, Note.Bien, Jour, 0.9);
        long session = revisions.CreerSession("seance", Jour, horloge.GetUtcNow());
        revisions.EnregistrerReponse(session, carte.Etat, resultat, Note.Bien, Jour, horloge.GetUtcNow(), 4000, true, 5);

        // Le contenu est renvoyé (version corrigée du pack), on réimporte par-dessus.
        Ecrire("packs/maths/sq1/s03.json", PackExemple.Modifie(p => p.Carte(0)["recto"] = "Recto corrigé"));
        PartageContenu.Extraire(Exporter(documents: false), cible);
        var second = importateur.ImporterDossier(Path.Combine(cible, "packs"), cible);

        Assert.Contains(second, r => r.Statut == StatutImport.MisAJour);
        using var connexion = baseDeDonnees.Ouvrir();
        Assert.Equal(1, connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM revision"));
        var premiere = revisions.CartesDuPack("maths-sq1-s03").Single(c => c.Carte.Id == "maths-sq1-s03-c001");
        Assert.Equal("Recto corrigé", premiere.Carte.Contenu.Recto);
        Assert.False(premiere.Etat.EstNouvelle);
    }
}
