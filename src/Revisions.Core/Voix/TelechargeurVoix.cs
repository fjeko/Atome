using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Revisions.Core.MisesAJour;

namespace Revisions.Core.Voix;

/// <summary>Avancement du téléchargement : octets déjà présents sur le disque sur le total du paquet.</summary>
public readonly record struct ProgressionVoix(long Recus, long Total)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Recus / Total, 0, 1);
}

/// <summary>Résultat d'une installation : <see cref="Prete"/> vrai si la voix est utilisable.</summary>
public sealed record ResultatInstallationVoix(bool Prete, bool DejaPresente, string? Erreur = null);

/// <summary>
/// Télécharge une seule fois le moteur Piper et la voix, dans un dossier de %LOCALAPPDATA%.
/// <list type="bullet">
/// <item>chaque fichier est vérifié avec son empreinte SHA-256 (un fichier abîmé ou remplacé est refusé) ;</item>
/// <item>un téléchargement coupé reprend où il s'était arrêté (en-tête HTTP « Range ») ;</item>
/// <item>en cas d'échec, on réessaie quelques fois, puis sur l'adresse suivante ; aucune erreur ne remonte à l'écran ;</item>
/// <item>l'archive du moteur est décompressée dans « moteur », puis un fichier témoin « installe.ok » est écrit en dernier.</item>
/// </list>
/// </summary>
public sealed class TelechargeurVoix(HttpClient http, PaquetVoix paquet, string dossier)
{
    /// <summary>Nombre d'essais par adresse avant de passer à la suivante.</summary>
    public int Essais { get; init; } = 3;

    /// <summary>Attente avant le n-ième nouvel essai (modifiable par les tests pour ne pas attendre).</summary>
    public Func<int, TimeSpan> Attente { get; init; } = essai => TimeSpan.FromSeconds(5 * essai * essai);

    /// <summary>Délai maximal pour un fichier (le modèle fait plus de 60 Mo).</summary>
    public TimeSpan DelaiParFichier { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>Dossier du paquet, par exemple %LOCALAPPDATA%\RevisionsBTS\voix\piper-1.</summary>
    public string Dossier => dossier;

    /// <summary>Fichier témoin écrit tout à la fin : sa présence veut dire « tout est vérifié et en place ».</summary>
    public string Temoin => Path.Combine(dossier, "installe.ok");

    /// <summary>Exécutable de Piper une fois l'archive décompressée.</summary>
    public string Executable => Path.Combine(dossier, "moteur", "piper", "piper.exe");

    /// <summary>Modèle d'une voix (le fichier .onnx.json de configuration est juste à côté).</summary>
    public string Modele(VoixPiper voix) => Path.Combine(dossier, voix.Modele);

    /// <summary>Vrai si l'installation est complète (témoin, moteur et modèles de toutes les voix présents).</summary>
    public bool EstPrete => File.Exists(Temoin) && File.Exists(Executable)
        && paquet.Voix.All(v => File.Exists(Modele(v)) && File.Exists(Modele(v) + ".json"));

    /// <summary>Télécharge ce qui manque, vérifie et installe. Ne lève pas d'exception (sauf annulation).</summary>
    public async Task<ResultatInstallationVoix> InstallerAsync(IProgress<ProgressionVoix>? progression = null, CancellationToken annulation = default)
    {
        if (EstPrete) return new(true, true);
        try
        {
            Directory.CreateDirectory(dossier);
            long faits = 0;
            foreach (var fichier in paquet.Fichiers)
            {
                long avant = faits;
                // Relais synchrone (et non Progress<T>, qui rapporte plus tard) : les valeurs arrivent dans l'ordre.
                var relais = progression is null ? null : new RelaisSynchrone(n => progression.Report(new(avant + n, paquet.TailleTotale)));
                await ObtenirAsync(fichier, relais, annulation);
                faits += fichier.Taille;
                progression?.Report(new(faits, paquet.TailleTotale));
            }
            File.WriteAllText(Temoin, paquet.Version);
            return EstPrete ? new(true, false) : new(false, false, "Fichiers incomplets après l'installation.");
        }
        catch (OperationCanceledException) when (annulation.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            return new(false, false, e.Message);
        }
    }

    /// <summary>Rapporte la progression tout de suite, dans le fil courant (à la différence de Progress&lt;T&gt;).</summary>
    private sealed class RelaisSynchrone(Action<long> action) : IProgress<long>
    {
        public void Report(long value) => action(value);
    }

    /// <summary>Témoin propre à un fichier : il est déjà téléchargé, vérifié et en place.</summary>
    private string TemoinFichier(FichierVoix f) => Path.Combine(dossier, f.Nom + ".ok");

    /// <summary>Télécharge un fichier (en reprenant un morceau existant), le vérifie et le met en place.</summary>
    private async Task ObtenirAsync(FichierVoix fichier, IProgress<long>? progression, CancellationToken annulation)
    {
        string final = Path.Combine(dossier, fichier.Nom);
        if (File.Exists(TemoinFichier(fichier)) && (fichier.EstArchive || File.Exists(final))) return;

        string partiel = final + ".part";
        string? derniereErreur = null;
        foreach (string adresse in fichier.Adresses)
        {
            for (int essai = 1; essai <= Essais; essai++)
            {
                if (essai > 1) await Task.Delay(Attente(essai - 1), annulation);
                try
                {
                    await TelechargerVersAsync(adresse, partiel, fichier.Taille, progression, annulation);
                    if (await EmpreinteSha256.VerifierAsync(partiel, fichier.Sha256, annulation))
                    {
                        MettreEnPlace(fichier, partiel, final);
                        return;
                    }
                    // Mauvaise empreinte : le morceau ne vaut rien, on repart de zéro (même adresse, puis la suivante).
                    File.Delete(partiel);
                    derniereErreur = $"Empreinte SHA-256 incorrecte pour {fichier.Nom}.";
                }
                catch (OperationCanceledException) when (annulation.IsCancellationRequested) { throw; }
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // Fichier absent à cette adresse (Release pas encore publiée) : inutile de réessayer, adresse suivante.
                    derniereErreur = $"{fichier.Nom} : {e.Message}";
                    break;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
                {
                    derniereErreur = $"{fichier.Nom} : {e.Message}";
                }
            }
        }
        throw new IOException(derniereErreur ?? $"Impossible de télécharger {fichier.Nom}.");
    }

    /// <summary>
    /// Complète le fichier partiel jusqu'à la taille attendue. Si le serveur accepte la reprise (réponse 206),
    /// on ajoute à la suite ; sinon (200), on recommence le fichier depuis le début.
    /// </summary>
    private async Task TelechargerVersAsync(string adresse, string partiel, long taille, IProgress<long>? progression, CancellationToken annulation)
    {
        long deja = File.Exists(partiel) ? new FileInfo(partiel).Length : 0;
        if (deja > taille) { File.Delete(partiel); deja = 0; }
        if (deja == taille) return; // déjà complet : il ne reste qu'à vérifier

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(DelaiParFichier);
        using var requete = new HttpRequestMessage(HttpMethod.Get, adresse);
        requete.Headers.UserAgent.ParseAdd("Atome-voix/1");
        if (deja > 0) requete.Headers.Range = new RangeHeaderValue(deja, null);

        using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, delai.Token);
        bool reprise = reponse.StatusCode == HttpStatusCode.PartialContent;
        if (!reprise && reponse.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"réponse {(int)reponse.StatusCode} de {new Uri(adresse).Host}", null, reponse.StatusCode);
        if (!reprise) deja = 0;

        await using var source = await reponse.Content.ReadAsStreamAsync(delai.Token);
        await using var cible = new FileStream(partiel, reprise ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var tampon = new byte[81920];
        long total = deja;
        progression?.Report(total);
        int lus;
        while ((lus = await source.ReadAsync(tampon, delai.Token)) > 0)
        {
            total += lus;
            // Plus d'octets que prévu : ce n'est pas le bon fichier, inutile de tout télécharger.
            if (total > taille) throw new InvalidDataException("fichier plus gros que prévu");
            await cible.WriteAsync(tampon.AsMemory(0, lus), delai.Token);
            progression?.Report(total);
        }
    }

    /// <summary>Fichier vérifié : on le renomme (ou on décompresse l'archive), puis on écrit son témoin.</summary>
    private void MettreEnPlace(FichierVoix fichier, string partiel, string final)
    {
        if (fichier.EstArchive)
        {
            // Décompression dans un dossier provisoire, puis renommage : jamais de moteur à moitié décompressé.
            // ExtractToDirectory refuse les chemins qui sortiraient du dossier (« ../ »).
            string provisoire = Path.Combine(dossier, "moteur.tmp");
            string moteur = Path.Combine(dossier, "moteur");
            if (Directory.Exists(provisoire)) Directory.Delete(provisoire, true);
            ZipFile.ExtractToDirectory(partiel, provisoire);
            if (Directory.Exists(moteur)) Directory.Delete(moteur, true);
            Directory.Move(provisoire, moteur);
            File.Delete(partiel); // l'archive n'est plus utile
        }
        else
        {
            File.Move(partiel, final, overwrite: true);
        }
        File.WriteAllText(TemoinFichier(fichier), fichier.Sha256);
    }
}
