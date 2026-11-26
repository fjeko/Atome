using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Revisions.Core.Voix;

/// <summary>
/// Synthèse avec Piper, pour une voix : on lance piper.exe (un programme séparé), on lui envoie une ligne JSON
/// par phrase (« text » et « output_file ») et il écrit un fichier WAV par phrase. Un seul lancement suffit pour
/// toutes les phrases d'un dialogue (charger le modèle prend environ une seconde).
/// Chaque fichier est gardé dans un cache : la même phrase, avec la même voix et à la même vitesse, se relit
/// tout de suite, sans relancer Piper.
/// </summary>
public sealed class SynthesePiper(string executable, string modele, string dossierCache, string nomVoix)
{
    /// <summary>Temps maximal pour un lancement de Piper.</summary>
    public TimeSpan Delai { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Version des réglages de synthèse, dans la clé du cache : les changer refait les phrases au lieu de rejouer les anciennes.</summary>
    public const string Reglages = "-n50w60";

    /// <summary>Phrases plus longues : coupées, pour ne pas bloquer Piper sur un pavé entier.</summary>
    public const int LongueurMaximale = 1500;

    /// <summary>Nom de la voix dans les clés du cache (par exemple « piper-1-cori »).</summary>
    public string NomVoix => nomVoix;

    /// <summary>Un seul Piper à la fois pour cette voix : deux clics rapides ne lancent pas deux synthèses en parallèle.</summary>
    private readonly SemaphoreSlim _unSeul = new(1, 1);

    /// <summary>Chemin du fichier en cache pour ce texte et cette vitesse (qu'il existe ou non).</summary>
    public string CheminCache(string texte, double vitesse) => Path.Combine(dossierCache, ChoixVoix.CleCache(Preparer(texte), nomVoix + Reglages, vitesse));

    private static string Preparer(string texte)
    {
        string phrase = ChoixVoix.Normaliser(texte);
        return phrase.Length > LongueurMaximale ? phrase[..LongueurMaximale] : phrase;
    }

    /// <summary>Renvoie le WAV d'une phrase : depuis le cache, sinon en lançant Piper. Lève une exception si Piper échoue.</summary>
    public async Task<string> SynthetiserAsync(string texte, double vitesse, CancellationToken annulation = default) =>
        (await SynthetiserLotAsync([texte], vitesse, annulation))[0];

    /// <summary>
    /// Renvoie le WAV de chaque phrase, dans l'ordre. Les phrases absentes du cache sont toutes synthétisées
    /// par un seul lancement de Piper.
    /// </summary>
    public async Task<IReadOnlyList<string>> SynthetiserLotAsync(IReadOnlyList<string> textes, double vitesse, CancellationToken annulation = default)
    {
        var chemins = textes.Select(t => CheminCache(t, vitesse)).ToList();
        if (chemins.All(File.Exists))
        {
            foreach (string c in chemins) Toucher(c);
            return chemins;
        }

        await _unSeul.WaitAsync(annulation);
        try
        {
            // Ce qui manque encore (une autre demande a pu en fabriquer une partie pendant qu'on attendait).
            var aFaire = textes.Select((t, i) => (Texte: Preparer(t), Cible: chemins[i]))
                .Where(p => !File.Exists(p.Cible)).DistinctBy(p => p.Cible).ToList();
            if (aFaire.Count > 0)
            {
                Directory.CreateDirectory(dossierCache);
                var lot = aFaire.Select(p => (p.Texte, Provisoire: p.Cible + ".tmp")).ToList();
                await LancerPiperAsync(lot, vitesse, annulation);
                foreach (var p in aFaire) File.Move(p.Cible + ".tmp", p.Cible, overwrite: true);
            }
            foreach (string c in chemins) Toucher(c);
            return chemins;
        }
        finally
        {
            _unSeul.Release();
        }
    }

    /// <summary>« Récemment utilisé » : le ménage du cache garde ce fichier.</summary>
    private static void Toucher(string chemin)
    {
        try { File.SetLastWriteTimeUtc(chemin, DateTime.UtcNow); } catch (IOException) { /* en cours de lecture : sans importance */ }
    }

    /// <summary>Arguments de la ligne de commande de Piper (séparés pour être testés).</summary>
    public static IReadOnlyList<string> Arguments(string modele, double vitesse) =>
    [
        "--model", modele,
        "--config", modele + ".json",
        "--json-input",
        // Moins de hasard que les réglages par défaut des voix (0,667 et 0,8) : une diction plus stable,
        // sans syllabes avalées ni « bafouillages » sur les exclamations courtes (« Oh, cool! »).
        "--noise_scale", "0.5",
        "--noise_w", "0.6",
        "--length_scale", ChoixVoix.EchelleDuree(vitesse).ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>Une ligne envoyée à Piper : le texte et le fichier à écrire (JSON sur une seule ligne).</summary>
    public static string LigneJson(string texte, string fichier) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = texte, ["output_file"] = fichier });

    private async Task LancerPiperAsync(IReadOnlyList<(string Texte, string Provisoire)> lot, double vitesse, CancellationToken annulation)
    {
        var demarrage = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // UTF-8 sans BOM : Piper lirait sinon trois octets parasites au début de la première ligne.
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        foreach (string a in Arguments(modele, vitesse)) demarrage.ArgumentList.Add(a);

        using var piper = Process.Start(demarrage) ?? throw new IOException("Piper n'a pas démarré.");
        var erreurs = piper.StandardError.ReadToEndAsync(annulation);
        var sorties = piper.StandardOutput.ReadToEndAsync(annulation);
        foreach (var (texte, provisoire) in lot)
            await piper.StandardInput.WriteLineAsync(LigneJson(texte, provisoire).AsMemory(), annulation);
        piper.StandardInput.Close(); // fin des phrases : Piper s'arrête après la dernière

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(Delai);
        try
        {
            await piper.WaitForExitAsync(delai.Token);
        }
        catch (OperationCanceledException)
        {
            try { piper.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* déjà terminé */ }
            throw;
        }
        await Task.WhenAll(erreurs, sorties);

        // 44 octets = en-tête WAV sans aucun son.
        if (piper.ExitCode != 0 || lot.Any(p => !File.Exists(p.Provisoire) || new FileInfo(p.Provisoire).Length <= 44))
        {
            string detail = (await erreurs).Trim();
            if (detail.Length > 300) detail = detail[^300..];
            throw new IOException($"Piper a échoué (code {piper.ExitCode}). {detail}");
        }
    }

    /// <summary>
    /// Ménage du cache : supprime les fichiers les moins récemment utilisés jusqu'à repasser sous la limite.
    /// Renvoie le nombre de fichiers supprimés.
    /// </summary>
    public static int NettoyerCache(string dossierCache, long octetsMax)
    {
        if (!Directory.Exists(dossierCache)) return 0;
        var fichiers = new DirectoryInfo(dossierCache).GetFiles("*.wav").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = fichiers.Sum(f => f.Length);
        int supprimes = 0;
        foreach (var f in fichiers)
        {
            if (total <= octetsMax) break;
            try { total -= f.Length; f.Delete(); supprimes++; }
            catch (IOException) { /* fichier en cours de lecture : on le garde */ }
        }
        // Restes d'une synthèse interrompue.
        foreach (var f in new DirectoryInfo(dossierCache).GetFiles("*.tmp"))
            try { f.Delete(); } catch (IOException) { }
        return supprimes;
    }
}
