using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Revisions.Core.MisesAJour;

namespace Revisions.Tests;

/// <summary>Faux serveur HTTP : chaque requête est confiée à une fonction du test (aucun accès réseau).</summary>
internal sealed class FauxServeur(Func<HttpRequestMessage, HttpResponseMessage> repondre) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requetes { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        Requetes.Add(requete);
        return Task.FromResult(repondre(requete));
    }
}

/// <summary>Faux serveur qui lève une exception (hors ligne, délai dépassé…).</summary>
internal sealed class ServeurEnPanne(Exception erreur) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation) =>
        Task.FromException<HttpResponseMessage>(erreur);
}

/// <summary>Stockage clé/valeur en mémoire, à la place de la table des réglages.</summary>
internal sealed class StockageMemoire : IStockageCleValeur
{
    public Dictionary<string, string> Valeurs { get; } = [];
    public string? LireValeur(string cle) => Valeurs.GetValueOrDefault(cle);
    public void EcrireValeur(string cle, string? valeur)
    {
        if (valeur is null) Valeurs.Remove(cle); else Valeurs[cle] = valeur;
    }
}

public class VersionSemantiqueTests
{
    private static VersionSemantique V(string texte)
    {
        Assert.True(VersionSemantique.TryParse(texte, out var v), texte);
        return v;
    }

    [Theory]
    [InlineData("v1.2.0", 1, 2, 0, null)]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("V10.0.1", 10, 0, 1, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1.2.0.0", 1, 2, 0, null)]
    [InlineData("0.2.0+abc123", 0, 2, 0, null)]
    [InlineData("1.3.0-beta.2", 1, 3, 0, "beta.2")]
    [InlineData("  v2.0.0-rc1+build.5 ", 2, 0, 0, "rc1")]
    public void Lit_les_numeros_valides(string texte, int majeure, int mineure, int correctif, string? pre)
    {
        var v = V(texte);
        Assert.Equal(new VersionSemantique(majeure, mineure, correctif, pre), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.x")]
    [InlineData("1..2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("latest")]
    [InlineData("-1.2.3")]
    public void Refuse_les_numeros_invalides(string? texte) =>
        Assert.False(VersionSemantique.TryParse(texte, out _));

    [Theory]
    [InlineData("1.2.0", "1.10.0")]          // nombres, pas texte
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.2.0-beta", "1.2.0")]      // la préversion passe avant la finale
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")] // nombre avant texte
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]     // nombres comparés comme des nombres
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void Compare_selon_SemVer(string plusPetite, string plusGrande)
    {
        Assert.True(V(plusPetite) < V(plusGrande));
        Assert.True(V(plusGrande) > V(plusPetite));
    }

    [Fact]
    public void Ignore_le_v_et_la_partie_build() =>
        Assert.Equal(0, V("v1.2.0").CompareTo(V("1.2.0+sha.123")));

    [Fact]
    public void Affiche_sans_v() => Assert.Equal("1.3.0-beta.2", V("v1.3.0-beta.2").ToString());
}

public class ChoixFichiersTests
{
    private const string Base = "https://github.com/fjeko/Atome/releases/download/v1.2.0/";

    private static ReleaseGitHub Release(params (string Nom, string Url)[] fichiers) =>
        new("v1.2.0", new VersionSemantique(1, 2, 0), "Atome 1.2.0", "", "https://github.com/fjeko/Atome/releases/tag/v1.2.0",
            false, false, fichiers.Select(f => new FichierRelease(f.Nom, f.Url, 100)).ToList());

    [Fact]
    public void Trouve_le_setup_de_la_version_et_son_empreinte()
    {
        var release = Release(
            ("Atome-portable.zip", Base + "Atome-portable.zip"),
            ("Atome-Setup-1.1.0.exe", Base + "Atome-Setup-1.1.0.exe"),
            ("Atome-Setup-1.2.0.exe", Base + "Atome-Setup-1.2.0.exe"),
            ("Atome-Setup-1.2.0.exe.sha256", Base + "Atome-Setup-1.2.0.exe.sha256"));
        var choix = ChoixFichiers.Trouver(release);
        Assert.NotNull(choix);
        Assert.Equal("Atome-Setup-1.2.0.exe", choix.Setup.Nom);
        Assert.Equal("Atome-Setup-1.2.0.exe.sha256", choix.Empreinte?.Nom);
    }

    [Fact]
    public void Sans_empreinte_le_setup_est_trouve_mais_l_empreinte_manque()
    {
        var choix = ChoixFichiers.Trouver(Release(("Atome-Setup-1.2.0.exe", Base + "Atome-Setup-1.2.0.exe")));
        Assert.NotNull(choix);
        Assert.Null(choix.Empreinte);
    }

    [Fact]
    public void Pas_de_setup_de_la_bonne_version_donne_null()
    {
        Assert.Null(ChoixFichiers.Trouver(Release(("Atome-portable.zip", Base + "Atome-portable.zip"))));
        Assert.Null(ChoixFichiers.Trouver(Release(("Atome-Setup-1.1.0.exe", Base + "Atome-Setup-1.1.0.exe"))));
    }

    [Fact]
    public void Refuse_un_setup_heberge_ailleurs()
    {
        Assert.Null(ChoixFichiers.Trouver(Release(("Atome-Setup-1.2.0.exe", "https://exemple.com/Atome-Setup-1.2.0.exe"))));
        Assert.Null(ChoixFichiers.Trouver(Release(("Atome-Setup-1.2.0.exe", "http://github.com/fjeko/Atome/releases/download/v1.2.0/Atome-Setup-1.2.0.exe"))));
        Assert.Null(ChoixFichiers.Trouver(Release(("Atome-Setup-1.2.0.exe", "https://github.com/autre/Atome/releases/download/v1.2.0/Atome-Setup-1.2.0.exe"))));
    }

    [Theory]
    [InlineData("https://github.com/fjeko/Atome/releases/tag/v1.2.0", true)]
    [InlineData("https://github.com/fjeko/Atome/releases", true)]
    [InlineData("https://github.com/fjeko/AtomeFaux/releases", false)]
    [InlineData("https://evil.com/fjeko/Atome/releases", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void Verifie_les_pages_avant_de_les_ouvrir(string? adresse, bool attendu) =>
        Assert.Equal(attendu, ChoixFichiers.PageDeConfiance(adresse));
}

public class EmpreinteSha256Tests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"; // SHA-256 de « test »

    [Theory]
    [InlineData(Hash)]
    [InlineData(Hash + "  Atome-Setup-1.2.0.exe")]
    [InlineData(Hash + " *Atome-Setup-1.2.0.exe\r\n")]
    [InlineData("\uFEFF" + Hash + "\r\n")]
    [InlineData("\n\n" + "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    public void Lit_les_formats_courants(string contenu) =>
        Assert.Equal(Hash, EmpreinteSha256.Lire(contenu, "Atome-Setup-1.2.0.exe"));

    [Theory]
    [InlineData("")]
    [InlineData("pas une empreinte")]
    [InlineData("9f86d081")]
    [InlineData(Hash + "  Autre-Fichier.exe")]
    public void Refuse_un_contenu_inutilisable(string contenu) =>
        Assert.Null(EmpreinteSha256.Lire(contenu, "Atome-Setup-1.2.0.exe"));

    [Fact]
    public async Task Calcule_et_compare_l_empreinte_d_un_fichier()
    {
        string chemin = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(chemin, "test");
            Assert.True(await EmpreinteSha256.VerifierAsync(chemin, Hash));
            Assert.True(await EmpreinteSha256.VerifierAsync(chemin, Hash.ToUpperInvariant()));
            Assert.False(await EmpreinteSha256.VerifierAsync(chemin, new string('0', 64)));
        }
        finally { File.Delete(chemin); }
    }
}

public class DetectionInstallationTests
{
    [Fact]
    public void Installee_quand_le_desinstalleur_Inno_est_present()
    {
        var fichiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\x\AppData\Local\Programs\Atome\unins000.exe",
            @"C:\Users\x\AppData\Local\Programs\Atome\unins000.dat",
        };
        Assert.Equal(ModeInstallation.Installee,
            DetectionInstallation.Detecter(@"C:\Users\x\AppData\Local\Programs\Atome", fichiers.Contains));
    }

    [Fact]
    public void Portable_sans_desinstalleur()
    {
        var fichiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Atome\Atome.exe", @"D:\Atome\unins000.exe" };
        Assert.Equal(ModeInstallation.Portable, DetectionInstallation.Detecter(@"D:\Atome", fichiers.Contains));
    }

    [Theory]
    [InlineData(@"C:\Cours\app\src\Revisions.App\bin\Debug\net10.0-windows")]
    [InlineData(@"C:\Cours\app\src\Revisions.App\bin\Release\net10.0-windows")]
    [InlineData(@"C:\Cours\app\publish")]
    public void Developpement_dans_le_dossier_du_code_source(string dossier)
    {
        var fichiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Cours\app\Revisions.slnx" };
        Assert.Equal(ModeInstallation.Developpement, DetectionInstallation.Detecter(dossier, fichiers.Contains));
    }

    [Fact]
    public void Le_code_source_l_emporte_sur_le_desinstalleur()
    {
        var fichiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Cours\app\Revisions.slnx", @"C:\Cours\app\publish\unins000.exe", @"C:\Cours\app\publish\unins000.dat",
        };
        Assert.Equal(ModeInstallation.Developpement, DetectionInstallation.Detecter(@"C:\Cours\app\publish", fichiers.Contains));
    }
}

public class ResumeNotesTests
{
    [Fact]
    public void Commence_apres_la_rubrique_Nouveautes_et_nettoie_le_Markdown()
    {
        const string notes = """
            Application Windows de révision pour le BTS SIO.

            ## Installer
            Télécharge **Atome-Setup-x.y.z.exe**.

            ## Nouveautés
            ## What's Changed
            * **Mise à jour** automatique by @fjeko in https://github.com/fjeko/Atome/pull/3
            - Voir [le guide](https://exemple.com) en `ligne`

            **Full Changelog**: https://github.com/fjeko/Atome/compare/v1.1.0...v1.2.0
            """;
        Assert.Equal(["• Mise à jour automatique", "• Voir le guide en ligne"], ResumeNotes.Resumer(notes));
    }

    [Fact]
    public void Sans_rubrique_prend_le_debut_et_limite_le_nombre_de_lignes()
    {
        var resume = ResumeNotes.Resumer("# Titre\nUn\nDeux\nTrois\nQuatre", maxLignes: 2);
        Assert.Equal(["Un", "Deux"], resume);
    }

    [Fact]
    public void Notes_vides() => Assert.Empty(ResumeNotes.Resumer(null));
}

public class MiseAJourDonneesTests
{
    [Fact]
    public void Reglage_active_par_defaut_et_sauvegarde_avant_mise_a_jour()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "revisions-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string chemin = Path.Combine(dossier, "test.db");
            var baseDeDonnees = new Revisions.Data.BaseDeDonnees(chemin);
            var profil = new Revisions.Data.DepotProfil(baseDeDonnees);

            // Activé par défaut, et la désactivation est retenue.
            var reglages = profil.ChargerReglages();
            Assert.True(reglages.VerifierMisesAJour);
            reglages.VerifierMisesAJour = false;
            profil.EnregistrerReglages(reglages);
            Assert.False(profil.ChargerReglages().VerifierMisesAJour);

            // La copie avant mise à jour est faite même si celle du jour existe, sans gêner la rotation quotidienne.
            baseDeDonnees.SauvegarderSiNecessaire(new DateOnly(2026, 11, 17));
            string copie = baseDeDonnees.SauvegarderAvantMiseAJour("1.2.0");
            Assert.Equal("avant-maj-1.2.0.db", Path.GetFileName(copie));
            Assert.True(File.Exists(copie));
            baseDeDonnees.SauvegarderAvantMiseAJour("1.2.0"); // une seconde fois : remplacée, pas d'erreur
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(copie)!).Length);
        }
        finally
        {
            Revisions.Data.BaseDeDonnees.FermerConnexions();
            Directory.Delete(dossier, recursive: true);
        }
    }
}

public class ServiceMiseAJourTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private const string Base = "https://github.com/fjeko/Atome/releases/download/v1.2.0/";

    private readonly StockageMemoire _stockage = new();
    private readonly FakeTimeProvider _horloge = new(Maintenant);

    private static string JsonRelease(string tag = "v1.2.0", bool draft = false, bool prerelease = false, bool avecEmpreinte = true)
    {
        string version = tag.TrimStart('v');
        string assets = $$"""{"name":"Atome-Setup-{{version}}.exe","browser_download_url":"{{Base}}Atome-Setup-{{version}}.exe","size":4}""";
        if (avecEmpreinte)
            assets += $$""",{"name":"Atome-Setup-{{version}}.exe.sha256","browser_download_url":"{{Base}}Atome-Setup-{{version}}.exe.sha256","size":90}""";
        return $$"""
            {"tag_name":"{{tag}}","name":"Atome {{version}}","body":"## Nouveautés\n- Plus rapide",
             "html_url":"https://github.com/fjeko/Atome/releases/tag/{{tag}}","draft":{{(draft ? "true" : "false")}},
             "prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{assets}}]}
            """;
    }

    private static HttpResponseMessage Reponse(HttpStatusCode code, string contenu = "", params (string, string)[] entetes)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent(contenu) };
        foreach (var (nom, valeur) in entetes) r.Headers.TryAddWithoutValidation(nom, valeur);
        return r;
    }

    private ServiceMiseAJour Service(HttpMessageHandler serveur, string versionActuelle = "1.1.0")
    {
        VersionSemantique.TryParse(versionActuelle, out var v);
        return new ServiceMiseAJour(new HttpClient(serveur), _stockage, _horloge, v);
    }

    // ---- Vérification ----

    [Fact]
    public async Task Propose_une_version_plus_recente_et_envoie_un_User_Agent_generique()
    {
        var serveur = new FauxServeur(_ => Reponse(HttpStatusCode.OK, JsonRelease()));
        var resultat = await Service(serveur).VerifierAsync(manuelle: false);

        Assert.True(resultat.AProposer);
        Assert.Equal("1.2.0", resultat.Release!.Version.ToString());
        var requete = Assert.Single(serveur.Requetes);
        Assert.Equal("https://api.github.com/repos/fjeko/Atome/releases/latest", requete.RequestUri!.ToString());
        Assert.Equal("Atome/1.1.0", requete.Headers.UserAgent.ToString());
        Assert.Null(requete.Headers.Authorization); // sans authentification
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    public async Task Rien_a_proposer_si_deja_a_jour(string versionActuelle)
    {
        var resultat = await Service(new FauxServeur(_ => Reponse(HttpStatusCode.OK, JsonRelease())), versionActuelle).VerifierAsync(false);
        Assert.Equal(IssueVerification.AJour, resultat.Issue);
        Assert.False(resultat.AProposer);
    }

    [Theory]
    [InlineData("v1.2.0", true, false)]
    [InlineData("v1.2.0", false, true)]
    [InlineData("v1.2.0-beta.1", false, false)]
    public async Task Ignore_les_brouillons_et_les_pre_versions(string tag, bool draft, bool prerelease)
    {
        var serveur = new FauxServeur(_ => Reponse(HttpStatusCode.OK, JsonRelease(tag, draft, prerelease)));
        Assert.False((await Service(serveur).VerifierAsync(manuelle: true)).AProposer);
    }

    [Fact]
    public async Task Une_seule_verification_par_heure_mais_la_proposition_revient_au_lancement_suivant()
    {
        var serveur = new FauxServeur(_ => Reponse(HttpStatusCode.OK, JsonRelease()));
        Assert.True((await Service(serveur).VerifierAsync(false)).AProposer);

        // « Plus tard », puis nouveau lancement 20 min après : pas de nouvelle requête, mais toujours proposé.
        _horloge.Advance(TimeSpan.FromMinutes(20));
        var resultat = await Service(serveur).VerifierAsync(false);
        Assert.True(resultat.AProposer);
        Assert.True(resultat.DepuisMemoire);
        Assert.Single(serveur.Requetes);

        // Une heure après la première vérification : on redemande à GitHub.
        _horloge.Advance(TimeSpan.FromMinutes(40));
        await Service(serveur).VerifierAsync(false);
        Assert.Equal(2, serveur.Requetes.Count);
    }

    [Fact]
    public async Task Verifier_maintenant_ignore_l_intervalle()
    {
        var serveur = new FauxServeur(_ => Reponse(HttpStatusCode.OK, JsonRelease()));
        await Service(serveur).VerifierAsync(false);
        await Service(serveur).VerifierAsync(manuelle: true);
        Assert.Equal(2, serveur.Requetes.Count);
    }

    [Fact]
    public async Task Demande_conditionnelle_avec_l_ETag_et_reponse_304_reutilise_la_release_connue()
    {
        var serveur = new FauxServeur(requete => requete.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Reponse(HttpStatusCode.OK, JsonRelease(), ("ETag", "W/\"abc123\"")));
        Assert.True((await Service(serveur).VerifierAsync(false)).AProposer);
        Assert.Empty(serveur.Requetes[0].Headers.IfNoneMatch);

        // Une heure plus tard : la demande porte l'ETag, GitHub répond 304, la version reste proposée.
        _horloge.Advance(TimeSpan.FromHours(1));
        var resultat = await Service(serveur).VerifierAsync(false);
        Assert.Equal("W/\"abc123\"", serveur.Requetes[1].Headers.IfNoneMatch.Single().ToString());
        Assert.True(resultat.AProposer);
        Assert.False(resultat.DepuisMemoire);
        Assert.Equal("1.2.0", resultat.Release!.Version.ToString());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-0.9, false)]
    [InlineData(-1.0, true)]
    [InlineData(-100.0, true)]
    [InlineData(5.0, true)]    // date dans le futur : l'horloge a été reculée, on revérifie
    [InlineData(0.5, false)]
    public void Intervalle_d_une_heure(double? decalageHeures, bool due)
    {
        DateTimeOffset? derniere = decalageHeures is { } h ? Maintenant.AddHours(h) : null;
        Assert.Equal(due, ServiceMiseAJour.VerificationDue(derniere, Maintenant));
    }

    [Fact]
    public async Task Ignorer_cette_version_est_memorise_mais_la_suivante_est_proposee()
    {
        string json = JsonRelease();
        var serveur = new FauxServeur(_ => Reponse(HttpStatusCode.OK, json));
        var service = Service(serveur);
        var resultat = await service.VerifierAsync(false);
        service.Ignorer(resultat.Release!.Version);

        // Lancement suivant (depuis la mémoire), puis vérification réelle le lendemain : plus proposée.
        Assert.Equal(IssueVerification.VersionIgnoree, (await Service(serveur).VerifierAsync(false)).Issue);
        _horloge.Advance(TimeSpan.FromDays(1));
        Assert.Equal(IssueVerification.VersionIgnoree, (await Service(serveur).VerifierAsync(false)).Issue);

        // « Vérifier maintenant » la montre quand même.
        Assert.True((await Service(serveur).VerifierAsync(manuelle: true)).AProposer);

        // La version suivante est proposée normalement.
        json = JsonRelease("v1.3.0");
        _horloge.Advance(TimeSpan.FromDays(1));
        var suivante = await Service(serveur).VerifierAsync(false);
        Assert.True(suivante.AProposer);
        Assert.Equal("1.3.0", suivante.Release!.Version.ToString());
    }

    // ---- Erreurs réseau et quota ----

    [Fact]
    public async Task Hors_ligne_rien_a_proposer_et_pas_d_exception()
    {
        var resultat = await Service(new ServeurEnPanne(new HttpRequestException("Hôte inconnu"))).VerifierAsync(false);
        Assert.Equal(IssueVerification.Erreur, resultat.Issue);
        Assert.False(resultat.AProposer);
        Assert.Contains("injoignable", resultat.Detail);
        // La tentative compte : pas de nouvel essai avant une heure.
        Assert.False(ServiceMiseAJour.VerificationDue(Service(new ServeurEnPanne(new Exception())).DerniereVerification, Maintenant));
    }

    [Fact]
    public async Task Delai_depasse_rien_a_proposer()
    {
        var resultat = await Service(new ServeurEnPanne(new TaskCanceledException("délai"))).VerifierAsync(false);
        Assert.Equal(IssueVerification.Erreur, resultat.Issue);
    }

    [Fact]
    public async Task L_annulation_par_l_appli_n_est_pas_avalee()
    {
        using var annulation = new CancellationTokenSource();
        annulation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new ServeurEnPanne(new TaskCanceledException())).VerifierAsync(false, annulation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "0", IssueVerification.QuotaDepasse)]
    [InlineData(HttpStatusCode.TooManyRequests, null, IssueVerification.QuotaDepasse)]
    [InlineData(HttpStatusCode.Forbidden, "12", IssueVerification.Erreur)]
    [InlineData(HttpStatusCode.InternalServerError, null, IssueVerification.Erreur)]
    [InlineData(HttpStatusCode.NotFound, null, IssueVerification.AucuneRelease)]
    public async Task Codes_d_erreur_de_GitHub(HttpStatusCode code, string? restant, IssueVerification attendue)
    {
        var entetes = restant is null ? [] : new[] { ("x-ratelimit-remaining", restant) };
        var resultat = await Service(new FauxServeur(_ => Reponse(code, "{}", entetes))).VerifierAsync(false);
        Assert.Equal(attendue, resultat.Issue);
        Assert.False(resultat.AProposer);
    }

    [Fact]
    public async Task Reponse_illisible()
    {
        var resultat = await Service(new FauxServeur(_ => Reponse(HttpStatusCode.OK, "<html>pas du JSON"))).VerifierAsync(false);
        Assert.Equal(IssueVerification.Erreur, resultat.Issue);
    }

    // ---- Suivi de l'installation ----

    [Fact]
    public void Bilan_de_l_installation_precedente()
    {
        Assert.Null(Service(new ServeurEnPanne(new Exception())).BilanInstallationPrecedente());

        Service(new ServeurEnPanne(new Exception()), "1.1.0").NoterInstallationLancee(new VersionSemantique(1, 2, 0));
        Assert.Equal(BilanInstallation.Echouee, Service(new ServeurEnPanne(new Exception()), "1.1.0").BilanInstallationPrecedente()?.Bilan);
        Assert.Null(Service(new ServeurEnPanne(new Exception()), "1.1.0").BilanInstallationPrecedente()); // une seule fois

        Service(new ServeurEnPanne(new Exception()), "1.1.0").NoterInstallationLancee(new VersionSemantique(1, 2, 0));
        Assert.Equal(BilanInstallation.Reussie, Service(new ServeurEnPanne(new Exception()), "1.2.0").BilanInstallationPrecedente()?.Bilan);
    }

    // ---- Téléchargement et intégrité ----

    private static readonly byte[] Setup = "MZ-faux-setup"u8.ToArray();
    private static string Sha(byte[] octets) => Convert.ToHexStringLower(SHA256.HashData(octets));

    private static FauxServeur ServeurDeTelechargement(string? empreinte, byte[]? setup = null) => new(requete =>
    {
        string url = requete.RequestUri!.ToString();
        if (url.EndsWith(".sha256"))
            return empreinte is null ? Reponse(HttpStatusCode.NotFound) : Reponse(HttpStatusCode.OK, empreinte);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(setup ?? Setup) };
    });

    private static string DossierTemporaire() => Path.Combine(Path.GetTempPath(), "atome-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Telecharge_et_verifie_le_setup()
    {
        string dossier = DossierTemporaire();
        try
        {
            var service = Service(ServeurDeTelechargement($"{Sha(Setup)}  Atome-Setup-1.2.0.exe\n"));
            var progressions = new List<double>();
            string chemin = await service.TelechargerAsync(ReleaseGitHub.Lire(JsonRelease())!, dossier,
                new ProgressionImmediate(progressions.Add), CancellationToken.None);

            Assert.Equal(Path.Combine(dossier, "Atome-Setup-1.2.0.exe"), chemin);
            Assert.Equal(Setup, await File.ReadAllBytesAsync(chemin));
            Assert.Equal(1.0, progressions[^1]);
        }
        finally { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); }
    }

    [Fact]
    public async Task Refuse_si_l_empreinte_ne_correspond_pas_et_ne_laisse_rien()
    {
        string dossier = DossierTemporaire();
        try
        {
            var service = Service(ServeurDeTelechargement(Sha("autre chose"u8.ToArray())));
            var erreur = await Assert.ThrowsAsync<ErreurMiseAJour>(() =>
                service.TelechargerAsync(ReleaseGitHub.Lire(JsonRelease())!, dossier, null, CancellationToken.None));
            Assert.Contains("ne correspond pas", erreur.Message);
            Assert.Empty(Directory.GetFiles(dossier));
        }
        finally { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); }
    }

    [Fact]
    public async Task Refuse_si_l_empreinte_n_est_pas_publiee()
    {
        var serveur = ServeurDeTelechargement(Sha(Setup));
        var erreur = await Assert.ThrowsAsync<ErreurMiseAJour>(() =>
            Service(serveur).TelechargerAsync(ReleaseGitHub.Lire(JsonRelease(avecEmpreinte: false))!, DossierTemporaire(), null, CancellationToken.None));
        Assert.Contains(".sha256", erreur.Message);
        Assert.Empty(serveur.Requetes); // rien n'a été téléchargé
    }

    [Fact]
    public async Task Refuse_si_l_empreinte_est_introuvable_sur_le_serveur()
    {
        await Assert.ThrowsAsync<ErreurMiseAJour>(() =>
            Service(ServeurDeTelechargement(null)).TelechargerAsync(ReleaseGitHub.Lire(JsonRelease())!, DossierTemporaire(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Telechargement_hors_ligne_donne_un_message_clair()
    {
        var erreur = await Assert.ThrowsAsync<ErreurMiseAJour>(() =>
            Service(new ServeurEnPanne(new HttpRequestException("coupé"))).TelechargerAsync(
                ReleaseGitHub.Lire(JsonRelease())!, DossierTemporaire(), null, CancellationToken.None));
        Assert.Contains("connexion", erreur.Message);
    }

    [Fact]
    public async Task L_annulation_du_telechargement_ne_laisse_rien()
    {
        string dossier = DossierTemporaire();
        try
        {
            using var annulation = new CancellationTokenSource();
            // Le serveur « répond » puis l'utilisateur annule pendant la lecture.
            var serveur = new FauxServeur(requete => requete.RequestUri!.ToString().EndsWith(".sha256")
                ? Reponse(HttpStatusCode.OK, Sha(Setup))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FluxQuiAnnule(annulation)) });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Service(serveur).TelechargerAsync(ReleaseGitHub.Lire(JsonRelease())!, dossier, null, annulation.Token));
            Assert.Empty(Directory.GetFiles(dossier));
        }
        finally { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); }
    }

    /// <summary>IProgress qui appelle tout de suite (Progress&lt;T&gt; passe par le contexte de synchronisation).</summary>
    private sealed class ProgressionImmediate(Action<double> action) : IProgress<double>
    {
        public void Report(double value) => action(value);
    }

    /// <summary>Flux qui donne quelques octets, puis déclenche l'annulation à la lecture suivante.</summary>
    private sealed class FluxQuiAnnule(CancellationTokenSource annulation) : MemoryStream(new byte[100_000])
    {
        private int _lectures;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (++_lectures == 2) await annulation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return await base.ReadAsync(buffer, token);
        }
    }
}
