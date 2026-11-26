using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Revisions.Core.Voix;

namespace Revisions.Tests;

/// <summary>Voix anglaise : choix du moteur, clé du cache audio, téléchargement vérifié par SHA-256.</summary>
public class VoixTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "atome-voix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, true);
    }

    // ---- Choix de la voix ----------------------------------------------------------------------

    [Theory]
    [InlineData("en-GB", true, true, true, MoteurVoix.Piper)]
    [InlineData("en-US", true, false, false, MoteurVoix.Piper)]
    [InlineData("en-GB", false, true, true, MoteurVoix.Windows)] // Piper pas encore téléchargé : voix de Windows
    [InlineData("en-GB", false, false, true, MoteurVoix.Sapi)]   // le PC de Florian : seulement Zira (SAPI)
    [InlineData("en-GB", false, false, false, MoteurVoix.Aucun)]
    [InlineData("fr-FR", true, true, false, MoteurVoix.Windows)] // Piper ne parle qu'anglais
    [InlineData("fr-FR", true, false, false, MoteurVoix.Aucun)]
    public void Piper_d_abord_puis_Windows_puis_SAPI(string langue, bool piper, bool windows, bool sapi, MoteurVoix attendu) =>
        Assert.Equal(attendu, ChoixVoix.Choisir(langue, piper, windows, sapi));

    [Theory]
    [InlineData("en-a1-oral", "Anglais", "en-GB")]
    [InlineData("en-b2-u03", null, "en-GB")]
    [InlineData("cned-anglais-s1", "Anglais (CNED)", "en-GB")]
    [InlineData("maths-s01", "Mathématiques", null)]
    [InlineData("entreprise-s01", "CEJM", null)] // « en… » sans tiret : pas un pack d'anglais
    public void Seuls_les_packs_d_anglais_ont_des_boutons_ecouter(string packId, string? matiere, string? attendu) =>
        Assert.Equal(attendu, ChoixVoix.LanguePourPack(packId, matiere));

    // ---- Cache audio ---------------------------------------------------------------------------

    [Fact]
    public void La_cle_du_cache_ignore_les_espaces_en_trop()
    {
        string a = ChoixVoix.CleCache("Nice to meet you.", "piper-1", 0.9);
        Assert.Equal(a, ChoixVoix.CleCache("  Nice   to\nmeet you. ", "piper-1", 0.9));
        Assert.Equal(a, ChoixVoix.CleCache("Nice to meet you.", "piper-1", 0.9001)); // arrondi au centième
        Assert.EndsWith(".wav", a);
        Assert.Equal(36, a.Length);
    }

    [Fact]
    public void La_cle_du_cache_change_avec_le_texte_la_voix_ou_la_vitesse()
    {
        string a = ChoixVoix.CleCache("Nice to meet you.", "piper-1", 0.9);
        Assert.NotEqual(a, ChoixVoix.CleCache("Nice to meet you!", "piper-1", 0.9));
        Assert.NotEqual(a, ChoixVoix.CleCache("Nice to meet you.", "piper-2", 0.9));
        Assert.NotEqual(a, ChoixVoix.CleCache("Nice to meet you.", "piper-1", 1.0));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.9, 1.111)]
    [InlineData(0.5, 2.0)]
    [InlineData(0.1, 2.0)] // bornée
    [InlineData(2.0, 0.5)]
    public void La_vitesse_devient_une_echelle_de_duree(double vitesse, double attendu) =>
        Assert.Equal(attendu, ChoixVoix.EchelleDuree(vitesse));

    [Fact]
    public void Les_arguments_de_Piper_utilisent_le_point_decimal()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR"); // virgule décimale
            var args = SynthesePiper.Arguments(@"C:\v\voix.onnx", 0.9);
            Assert.Equal(["--model", @"C:\v\voix.onnx", "--config", @"C:\v\voix.onnx.json", "--json-input", "--noise_scale", "0.5", "--noise_w", "0.6", "--length_scale", "1.111"], args);
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }
    }

    [Fact]
    public void Chaque_phrase_part_a_Piper_sur_une_seule_ligne_JSON()
    {
        string ligne = SynthesePiper.LigneJson("He said \"hi\"\nthen left.", @"C:\cache\a.wav.tmp");
        Assert.DoesNotContain('\n', ligne);
        using var json = System.Text.Json.JsonDocument.Parse(ligne);
        Assert.Equal("He said \"hi\"\nthen left.", json.RootElement.GetProperty("text").GetString());
        Assert.Equal(@"C:\cache\a.wav.tmp", json.RootElement.GetProperty("output_file").GetString());
    }

    // ---- Une voix par personnage ---------------------------------------------------------------

    [Fact]
    public void Anna_et_Tom_n_ont_pas_la_meme_voix()
    {
        var t = ChoixVoix.AttribuerTimbres(["Anna", "Tom", "Anna", "Tom"]);
        Assert.Equal(Timbre.Feminin, t["Anna"]);
        Assert.Equal(Timbre.Masculin, t["Tom"]);
    }

    [Fact]
    public void Un_role_inconnu_prend_la_voix_qui_reste()
    {
        // « You » parle avec un serveur, puis avec Max : il prend toujours l'autre voix.
        var serveur = ChoixVoix.AttribuerTimbres(["Waiter", "You"]);
        Assert.NotEqual(serveur["Waiter"], serveur["You"]);
        Assert.Equal(Timbre.Feminin, ChoixVoix.AttribuerTimbres(["Max", "Receptionist"])["Receptionist"]);
        Assert.Equal(Timbre.Masculin, ChoixVoix.AttribuerTimbres(["Jade", "B"])["B"]);
        var ab = ChoixVoix.AttribuerTimbres(["A", "B", "A"]);
        Assert.NotEqual(ab["A"], ab["B"]);
    }

    [Fact]
    public void Sans_nom_la_voix_par_defaut_est_feminine()
    {
        Assert.Empty(ChoixVoix.AttribuerTimbres(["", "  "]));
        Assert.Equal(Timbre.Feminin, ChoixVoix.AttribuerTimbres(["Léa"])["léa"]); // insensible à la casse
    }

    // ---- Assemblage des répliques ---------------------------------------------------------------

    /// <summary>Petit WAV PCM 16 bits mono à 22 050 Hz, comme ceux de Piper.</summary>
    private static byte[] Wav(params short[] echantillons)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + echantillons.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(echantillons.Length * 2);
        foreach (short e in echantillons) w.Write(e);
        return m.ToArray();
    }

    [Fact]
    public void Les_repliques_sont_mises_bout_a_bout_avec_un_silence()
    {
        byte[] assemble = AssemblageWav.Assembler([Wav(1, 2), Wav(3)], silenceMs: 100);

        var audio = AssemblageWav.Lire(assemble);
        int silence = 44100 * 100 / 1000; // 4 410 octets = 2 205 échantillons
        Assert.Equal(2 * 2 + silence + 2, audio.Donnees.Length);
        Assert.Equal(1, BitConverter.ToInt16(audio.Donnees, 0));
        Assert.Equal(2, BitConverter.ToInt16(audio.Donnees, 2));
        Assert.All(audio.Donnees[4..(4 + silence)], o => Assert.Equal(0, o));
        Assert.Equal(3, BitConverter.ToInt16(audio.Donnees, 4 + silence));
        Assert.Equal(assemble.Length - 8, BitConverter.ToInt32(assemble, 4)); // taille RIFF cohérente
    }

    [Fact]
    public void Deux_formats_differents_ne_s_assemblent_pas()
    {
        byte[] autre = Wav(1);
        BitConverter.GetBytes(16000).CopyTo(autre, 24); // autre fréquence
        Assert.Throws<InvalidDataException>(() => AssemblageWav.Assembler([Wav(1), autre]));
        Assert.Throws<InvalidDataException>(() => AssemblageWav.Lire("pas un wav"u8.ToArray()));
    }

    [Fact]
    public async Task Une_phrase_deja_en_cache_ne_relance_pas_Piper()
    {
        // L'exécutable n'existe pas : si la synthèse le lançait, elle échouerait.
        var synthese = new SynthesePiper(Path.Combine(_dossier, "absent.exe"), "voix.onnx", Path.Combine(_dossier, "cache"), "piper-1");
        string attendu = synthese.CheminCache("Hello there.", 0.9);
        Directory.CreateDirectory(Path.GetDirectoryName(attendu)!);
        await File.WriteAllBytesAsync(attendu, new byte[100]);
        Assert.Equal(attendu, await synthese.SynthetiserAsync(" Hello   there. ", 0.9));
    }

    [Fact]
    public void Le_menage_supprime_les_phrases_les_plus_anciennes()
    {
        string cache = Path.Combine(_dossier, "cache");
        Directory.CreateDirectory(cache);
        for (int i = 0; i < 5; i++)
        {
            string f = Path.Combine(cache, $"{i}.wav");
            File.WriteAllBytes(f, new byte[1000]);
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-10 + i)); // 0 = la plus ancienne
        }
        File.WriteAllBytes(Path.Combine(cache, "reste.wav.tmp"), [1]);

        Assert.Equal(2, SynthesePiper.NettoyerCache(cache, 3000));
        Assert.Equal(["2.wav", "3.wav", "4.wav"], Directory.GetFiles(cache).Select(Path.GetFileName).Order());
    }

    // ---- Téléchargement ------------------------------------------------------------------------

    /// <summary>Contenu des faux fichiers : une archive avec piper/piper.exe, un modèle et sa configuration.</summary>
    private static readonly Dictionary<string, byte[]> Contenus = new()
    {
        ["piper_windows_amd64.zip"] = Archive(),
        ["voix.onnx"] = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray(),
        ["voix.onnx.json"] = "{\"audio\":{}}"u8.ToArray(),
    };

    private static byte[] Archive()
    {
        using var memoire = new MemoryStream();
        using (var zip = new ZipArchive(memoire, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var exe = zip.CreateEntry("piper/piper.exe").Open();
            exe.Write("MZ faux exécutable"u8);
        }
        return memoire.ToArray();
    }

    private static string Sha(byte[] octets) => Convert.ToHexStringLower(SHA256.HashData(octets));

    /// <summary>Paquet de test : chaque fichier a deux adresses, « principale » puis « secours ».</summary>
    private static PaquetVoix Paquet(Func<string, string>? empreinte = null) => new("test-1", "Voix de test", "en-GB",
        [new VoixPiper("test", "Voix de test", "voix.onnx", Timbre.Feminin)],
        Contenus.Select(c => new FichierVoix(c.Key, c.Value.Length, empreinte?.Invoke(c.Key) ?? Sha(c.Value),
            [$"https://principale.test/{c.Key}", $"https://secours.test/{c.Key}"])).ToList());

    /// <summary>Faux serveur qui sert les fichiers, et respecte l'en-tête Range (réponse 206).</summary>
    private static HttpResponseMessage Servir(HttpRequestMessage requete, byte[]? remplacement = null)
    {
        string nom = requete.RequestUri!.Segments[^1];
        byte[] octets = remplacement ?? Contenus[nom];
        if (requete.Headers.Range?.Ranges.FirstOrDefault() is { From: long debut })
        {
            var partiel = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(octets[(int)debut..]) };
            partiel.Content.Headers.ContentRange = new ContentRangeHeaderValue(debut, octets.Length - 1, octets.Length);
            return partiel;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(octets) };
    }

    private TelechargeurVoix Telechargeur(HttpMessageHandler serveur, PaquetVoix? paquet = null) =>
        new(new HttpClient(serveur), paquet ?? Paquet(), _dossier) { Attente = _ => TimeSpan.Zero };

    [Fact]
    public async Task Le_paquet_est_telecharge_verifie_et_decompresse()
    {
        var serveur = new FauxServeur(r => Servir(r));
        var t = Telechargeur(serveur);
        var avancement = new List<ProgressionVoix>();

        var resultat = await t.InstallerAsync(new ProgressionSynchrone(avancement.Add));

        Assert.True(resultat.Prete);
        Assert.False(resultat.DejaPresente);
        Assert.True(t.EstPrete);
        Assert.True(File.Exists(Path.Combine(_dossier, "moteur", "piper", "piper.exe")));
        Assert.False(File.Exists(Path.Combine(_dossier, "piper_windows_amd64.zip.part"))); // archive effacée
        Assert.Equal(Contenus["voix.onnx"], await File.ReadAllBytesAsync(t.Modele(Paquet().Voix[0])));
        Assert.Equal(1.0, avancement[^1].Fraction);
        Assert.All(serveur.Requetes, r => Assert.Equal("principale.test", r.RequestUri!.Host));
    }

    [Fact]
    public async Task Une_voix_deja_installee_ne_se_retelecharge_pas()
    {
        await Telechargeur(new FauxServeur(r => Servir(r))).InstallerAsync();
        var serveur = new FauxServeur(r => Servir(r));

        var resultat = await Telechargeur(serveur).InstallerAsync();

        Assert.True(resultat.DejaPresente);
        Assert.Empty(serveur.Requetes);
    }

    [Fact]
    public async Task Un_telechargement_coupe_reprend_ou_il_s_etait_arrete()
    {
        Directory.CreateDirectory(_dossier);
        byte[] modele = Contenus["voix.onnx"];
        await File.WriteAllBytesAsync(Path.Combine(_dossier, "voix.onnx.part"), modele[..2000]);
        var serveur = new FauxServeur(r => Servir(r));

        Assert.True((await Telechargeur(serveur).InstallerAsync()).Prete);

        var demande = serveur.Requetes.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/voix.onnx"));
        Assert.Equal(2000, demande.Headers.Range!.Ranges.Single().From);
        Assert.Equal(modele, await File.ReadAllBytesAsync(Path.Combine(_dossier, "voix.onnx")));
    }

    [Fact]
    public async Task Un_serveur_qui_ignore_la_reprise_fait_repartir_de_zero()
    {
        Directory.CreateDirectory(_dossier);
        await File.WriteAllBytesAsync(Path.Combine(_dossier, "voix.onnx.part"), new byte[2000]); // morceau faux
        // Réponse 200 (fichier entier) même avec un en-tête Range.
        var serveur = new FauxServeur(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Contenus[r.RequestUri!.Segments[^1]]) });

        Assert.True((await Telechargeur(serveur).InstallerAsync()).Prete);
        Assert.Equal(Contenus["voix.onnx"], await File.ReadAllBytesAsync(Path.Combine(_dossier, "voix.onnx")));
    }

    [Fact]
    public async Task Un_fichier_abime_est_refuse_et_l_adresse_de_secours_est_essayee()
    {
        byte[] abime = (byte[])Contenus["voix.onnx"].Clone();
        abime[10] ^= 0xFF; // même taille, un octet changé : l'empreinte SHA-256 ne correspond plus
        var serveur = new FauxServeur(r => r.RequestUri!.Host == "principale.test" && r.RequestUri.AbsolutePath.EndsWith("/voix.onnx")
            ? Servir(r, abime) : Servir(r));

        Assert.True((await Telechargeur(serveur).InstallerAsync()).Prete);

        Assert.Equal(Contenus["voix.onnx"], await File.ReadAllBytesAsync(Path.Combine(_dossier, "voix.onnx")));
        Assert.Equal(3, serveur.Requetes.Count(r => r.RequestUri!.Host == "principale.test" && r.RequestUri.AbsolutePath.EndsWith("/voix.onnx"))); // 3 essais
        Assert.Contains(serveur.Requetes, r => r.RequestUri!.Host == "secours.test");
    }

    [Fact]
    public async Task Une_Release_pas_encore_publiee_renvoie_directement_vers_la_source_d_origine()
    {
        var serveur = new FauxServeur(r => r.RequestUri!.Host == "principale.test"
            ? new HttpResponseMessage(HttpStatusCode.NotFound) : Servir(r));

        Assert.True((await Telechargeur(serveur).InstallerAsync()).Prete);

        // Un seul essai par fichier sur l'adresse absente (pas trois) : une erreur 404 ne se corrige pas en réessayant.
        Assert.Equal(3, serveur.Requetes.Count(r => r.RequestUri!.Host == "principale.test"));
    }

    [Fact]
    public async Task Sans_reseau_rien_n_est_installe_et_aucune_exception_ne_remonte()
    {
        var t = Telechargeur(new ServeurEnPanne(new HttpRequestException("hors ligne")));

        var resultat = await t.InstallerAsync();

        Assert.False(resultat.Prete);
        Assert.Contains("hors ligne", resultat.Erreur);
        Assert.False(t.EstPrete);
        Assert.False(File.Exists(t.Temoin));
    }

    [Fact]
    public async Task Une_empreinte_qui_ne_correspond_jamais_n_installe_rien()
    {
        var t = Telechargeur(new FauxServeur(r => Servir(r)), Paquet(nom => nom == "voix.onnx.json" ? new string('0', 64) : null));

        var resultat = await t.InstallerAsync();

        Assert.False(resultat.Prete);
        Assert.Contains("SHA-256", resultat.Erreur);
        Assert.False(File.Exists(Path.Combine(_dossier, "voix.onnx.json")));
    }

    [Fact]
    public async Task Un_fichier_plus_gros_que_prevu_est_refuse()
    {
        var serveur = new FauxServeur(r => Servir(r, r.RequestUri!.AbsolutePath.EndsWith(".json") ? new byte[10_000] : null));

        var resultat = await Telechargeur(serveur).InstallerAsync();

        Assert.False(resultat.Prete);
    }

    [Fact]
    public void Le_catalogue_de_la_vraie_voix_est_coherent()
    {
        var p = CatalogueVoix.Piper1;
        Assert.Equal(1 + 2 * p.Voix.Count, p.Fichiers.Count); // le moteur, puis modèle + configuration par voix
        Assert.All(p.Fichiers, f =>
        {
            Assert.Matches("^[0-9a-f]{64}$", f.Sha256);
            Assert.StartsWith(CatalogueVoix.Release, f.Adresses[0]); // la Release d'Atome d'abord
            Assert.All(f.Adresses, a => Assert.StartsWith("https://", a));
        });
        Assert.All(p.Voix, v =>
        {
            Assert.Contains(p.Fichiers, f => f.Nom == v.Modele);
            Assert.Contains(p.Fichiers, f => f.Nom == v.Modele + ".json");
        });
        Assert.Equal(Timbre.Feminin, p.VoixDe(Timbre.Feminin).Timbre);
        Assert.Equal(Timbre.Masculin, p.VoixDe(Timbre.Masculin).Timbre);
        Assert.Single(p.Fichiers, f => f.EstArchive);
    }

    /// <summary>IProgress qui rapporte tout de suite (Progress&lt;T&gt; rapporterait plus tard, sur un autre fil).</summary>
    private sealed class ProgressionSynchrone(Action<ProgressionVoix> action) : IProgress<ProgressionVoix>
    {
        public void Report(ProgressionVoix value) => action(value);
    }
}

/// <summary>Cartes à dire à voix haute : pas de zone « Ta réponse ».</summary>
public class CarteOraleTests
{
    private static Revisions.Core.Modeles.Carte Question(string recto) =>
        new("c1", "s1", Revisions.Core.Modeles.TypeCarte.Question, 1, new() { Recto = recto, Verso = "…" });

    [Theory]
    [InlineData("🎙️ Réponds à voix haute : *What's your name?*", true)]
    [InlineData("Dis à voix haute la date du jour.", true)]
    [InlineData("Comment répondre à *Nice to meet you* ?", false)]
    public void Une_carte_orale_se_reconnait_a_sa_consigne(string recto, bool attendu) =>
        Assert.Equal(attendu, Revisions.Core.Modeles.CarteOrale.Est(Question(recto)));
}
