using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Revisions.Core.MisesAJour;

/// <summary>Petit stockage « clé → valeur » (la table des réglages de la base, en vrai).</summary>
public interface IStockageCleValeur
{
    string? LireValeur(string cle);
    void EcrireValeur(string cle, string? valeur);
}

/// <summary>Issue d'une vérification des mises à jour.</summary>
public enum IssueVerification
{
    /// <summary>Atome est déjà à la dernière version.</summary>
    AJour,
    /// <summary>Une version plus récente existe : on la propose.</summary>
    NouvelleVersion,
    /// <summary>Une version plus récente existe, mais l'utilisateur a choisi de l'ignorer.</summary>
    VersionIgnoree,
    /// <summary>Le dépôt n'a encore publié aucune Release (réponse 404).</summary>
    AucuneRelease,
    /// <summary>Trop de demandes à l'API GitHub depuis cette connexion (60 par heure sans compte).</summary>
    QuotaDepasse,
    /// <summary>Hors ligne, délai dépassé, erreur du serveur ou réponse illisible.</summary>
    Erreur,
}

/// <summary>Résultat d'une vérification.</summary>
/// <param name="Issue">Ce qui s'est passé.</param>
/// <param name="Release">La dernière Release connue (si GitHub en a renvoyé une).</param>
/// <param name="Detail">Explication technique, pour le journal.</param>
/// <param name="DepuisMemoire">Vrai si GitHub n'a pas été contacté (dernière vérification il y a moins de 24 h).</param>
public sealed record ResultatVerification(IssueVerification Issue, ReleaseGitHub? Release = null, string? Detail = null, bool DepuisMemoire = false)
{
    /// <summary>Vrai s'il faut afficher le bandeau.</summary>
    public bool AProposer => Issue == IssueVerification.NouvelleVersion && Release is not null;
}

/// <summary>Ce qu'est devenue la dernière installation lancée depuis Atome.</summary>
public enum BilanInstallation
{
    /// <summary>Atome tourne bien dans la version installée (ou une plus récente).</summary>
    Reussie,
    /// <summary>Atome tourne toujours dans l'ancienne version : le setup a été bloqué, annulé ou a échoué.</summary>
    Echouee,
}

/// <summary>Erreur de mise à jour avec un message prêt à montrer à l'utilisateur.</summary>
public sealed class ErreurMiseAJour(string message, Exception? cause = null) : Exception(message, cause);

/// <summary>
/// Vérification et téléchargement des nouvelles versions d'Atome, à partir des Releases du dépôt GitHub public.
/// </summary>
/// <remarks>
/// <para>Ce qui est envoyé à GitHub : une simple requête anonyme, avec « Atome/&lt;version&gt; » comme User-Agent.
/// Aucune donnée personnelle, aucun identifiant.</para>
/// <para>Le client HTTP est fourni de l'extérieur (injection) : les tests lui donnent un faux serveur.</para>
/// </remarks>
public sealed class ServiceMiseAJour(HttpClient http, IStockageCleValeur stockage, TimeProvider horloge, VersionSemantique versionActuelle)
{
    /// <summary>Au plus une vérification automatique par intervalle.</summary>
    public static readonly TimeSpan Intervalle = TimeSpan.FromHours(24);

    /// <summary>Délai d'attente de la vérification : court, pour ne jamais faire attendre.</summary>
    public static readonly TimeSpan DelaiVerification = TimeSpan.FromSeconds(8);

    /// <summary>Délai maximal du téléchargement de l'installateur.</summary>
    public static readonly TimeSpan DelaiTelechargement = TimeSpan.FromMinutes(30);

    /// <summary>Taille maximale acceptée pour l'installateur (garde-fou).</summary>
    public const long TailleMaximale = 500L * 1024 * 1024;

    // Clés dans la table des réglages.
    internal const string CleDerniereVerification = "maj_derniere_verification";
    internal const string CleReleaseConnue = "maj_release_connue";
    internal const string CleVersionIgnoree = "maj_version_ignoree";
    internal const string CleInstallationLancee = "maj_installation_lancee";

    public VersionSemantique VersionActuelle => versionActuelle;

    // ---- Vérification --------------------------------------------------------------------------

    /// <summary>
    /// Vrai si une vérification automatique est due : jamais faite, faite il y a 24 h ou plus,
    /// ou datée dans le futur (horloge du PC changée entre-temps).
    /// </summary>
    public static bool VerificationDue(DateTimeOffset? derniere, DateTimeOffset maintenant) =>
        derniere is not { } d || maintenant - d >= Intervalle || d > maintenant + TimeSpan.FromHours(1);

    /// <summary>Date de la dernière vérification automatique ou manuelle, ou null.</summary>
    public DateTimeOffset? DerniereVerification =>
        DateTimeOffset.TryParse(stockage.LireValeur(CleDerniereVerification), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var d) ? d : null;

    /// <summary>
    /// Vérifie s'il existe une version plus récente.
    /// <list type="bullet">
    /// <item>Automatique (<paramref name="manuelle"/> = false) : GitHub n'est contacté qu'une fois par 24 h ; entre deux,
    /// on réutilise la dernière Release connue (c'est ce qui fait revenir la proposition après « Plus tard »).
    /// Une version ignorée n'est pas proposée.</item>
    /// <item>Manuelle (bouton « Vérifier maintenant ») : GitHub est toujours contacté, et une version ignorée est quand même montrée.</item>
    /// </list>
    /// Ne lève jamais d'exception pour un problème réseau : le résultat dit ce qui s'est passé.
    /// </summary>
    public async Task<ResultatVerification> VerifierAsync(bool manuelle, CancellationToken annulation = default)
    {
        var maintenant = horloge.GetUtcNow();
        if (!manuelle && !VerificationDue(DerniereVerification, maintenant))
        {
            var connue = ReleaseConnue();
            return connue is null
                ? new ResultatVerification(IssueVerification.AJour, null, "Vérifié il y a moins de 24 h.", DepuisMemoire: true)
                : Evaluer(connue, manuelle) with { DepuisMemoire = true };
        }

        // La date est notée dès la tentative : au plus une demande par 24 h, même hors ligne.
        stockage.EcrireValeur(CleDerniereVerification, maintenant.ToString("o", CultureInfo.InvariantCulture));

        var resultat = await DemanderDerniereReleaseAsync(annulation);
        if (resultat.Release is { } release) stockage.EcrireValeur(CleReleaseConnue, release.EnJson());
        else if (resultat.Issue == IssueVerification.AucuneRelease) stockage.EcrireValeur(CleReleaseConnue, null);
        return resultat.Release is null ? resultat : Evaluer(resultat.Release, manuelle) with { Detail = resultat.Detail };
    }

    /// <summary>Décide si une Release doit être proposée.</summary>
    internal ResultatVerification Evaluer(ReleaseGitHub release, bool manuelle)
    {
        // Brouillons et pré-versions : jamais proposés (l'API « latest » les exclut déjà, on vérifie quand même).
        if (release.Brouillon || release.PreVersion || release.Version.EstPreVersion)
            return new ResultatVerification(IssueVerification.AJour, release, "Brouillon ou pré-version : ignorée.");
        if (release.Version <= versionActuelle)
            return new ResultatVerification(IssueVerification.AJour, release);
        if (!manuelle && VersionIgnoree is { } ignoree && release.Version <= ignoree)
            return new ResultatVerification(IssueVerification.VersionIgnoree, release);
        return new ResultatVerification(IssueVerification.NouvelleVersion, release);
    }

    /// <summary>Appelle l'API GitHub et traduit la réponse.</summary>
    private async Task<ResultatVerification> DemanderDerniereReleaseAsync(CancellationToken annulation)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(DelaiVerification);
        try
        {
            using var requete = Requete(DepotAtome.DerniereRelease);
            requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            requete.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, delai.Token);

            if (EstQuotaDepasse(reponse))
                return new ResultatVerification(IssueVerification.QuotaDepasse, null,
                    $"Quota de l'API GitHub dépassé (HTTP {(int)reponse.StatusCode}).");
            if (reponse.StatusCode == HttpStatusCode.NotFound)
                return new ResultatVerification(IssueVerification.AucuneRelease, null, "Aucune Release publiée (HTTP 404).");
            if (!reponse.IsSuccessStatusCode)
                return new ResultatVerification(IssueVerification.Erreur, null, $"Réponse inattendue de GitHub : HTTP {(int)reponse.StatusCode}.");

            string json = await reponse.Content.ReadAsStringAsync(delai.Token);
            return ReleaseGitHub.Lire(json) is { } release
                ? new ResultatVerification(IssueVerification.AJour, release)
                : new ResultatVerification(IssueVerification.Erreur, null, "Réponse de GitHub illisible (tag ou JSON invalide).");
        }
        catch (OperationCanceledException) when (!annulation.IsCancellationRequested)
        {
            return new ResultatVerification(IssueVerification.Erreur, null, $"Pas de réponse de GitHub en {DelaiVerification.TotalSeconds:0} s.");
        }
        catch (HttpRequestException e)
        {
            return new ResultatVerification(IssueVerification.Erreur, null, "GitHub injoignable (hors ligne ?) : " + e.Message);
        }
    }

    /// <summary>
    /// Quota dépassé : GitHub répond 429, ou 403 avec « x-ratelimit-remaining: 0 »
    /// (https://docs.github.com/rest/using-the-rest-api/rate-limits-for-the-rest-api).
    /// </summary>
    internal static bool EstQuotaDepasse(HttpResponseMessage reponse)
    {
        if (reponse.StatusCode == HttpStatusCode.TooManyRequests) return true;
        return reponse.StatusCode == HttpStatusCode.Forbidden
               && reponse.Headers.TryGetValues("x-ratelimit-remaining", out var restant)
               && restant.FirstOrDefault() == "0";
    }

    /// <summary>Requête GET avec un User-Agent générique (exigé par GitHub), sans aucune donnée personnelle.</summary>
    private HttpRequestMessage Requete(Uri adresse)
    {
        var requete = new HttpRequestMessage(HttpMethod.Get, adresse);
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("Atome", versionActuelle.ToString()));
        return requete;
    }

    /// <summary>Dernière Release reçue de GitHub, gardée entre deux lancements.</summary>
    public ReleaseGitHub? ReleaseConnue() =>
        stockage.LireValeur(CleReleaseConnue) is { } json ? ReleaseGitHub.Lire(json) : null;

    // ---- « Ignorer cette version » ---------------------------------------------------------------

    /// <summary>Version que l'utilisateur a choisi d'ignorer (les suivantes seront proposées).</summary>
    public VersionSemantique? VersionIgnoree =>
        VersionSemantique.TryParse(stockage.LireValeur(CleVersionIgnoree), out var v) ? v : null;

    public void Ignorer(VersionSemantique version) => stockage.EcrireValeur(CleVersionIgnoree, version.ToString());

    // ---- Suivi de l'installation -----------------------------------------------------------------

    /// <summary>Noté juste avant de lancer le setup, pour savoir au prochain démarrage s'il a abouti.</summary>
    public void NoterInstallationLancee(VersionSemantique version) =>
        stockage.EcrireValeur(CleInstallationLancee, version.ToString());

    /// <summary>Oublie la tentative notée (le setup n'a finalement pas pu être lancé).</summary>
    public void AnnulerInstallationLancee() => stockage.EcrireValeur(CleInstallationLancee, null);

    /// <summary>
    /// Au démarrage : bilan de l'installation lancée la dernière fois (une seule fois), ou null s'il n'y en a pas.
    /// </summary>
    public (BilanInstallation Bilan, VersionSemantique Version)? BilanInstallationPrecedente()
    {
        if (!VersionSemantique.TryParse(stockage.LireValeur(CleInstallationLancee), out var visee)) return null;
        stockage.EcrireValeur(CleInstallationLancee, null);
        return (versionActuelle >= visee ? BilanInstallation.Reussie : BilanInstallation.Echouee, visee);
    }

    // ---- Téléchargement --------------------------------------------------------------------------

    /// <summary>
    /// Télécharge l'installateur de la Release dans <paramref name="dossier"/> et vérifie son empreinte SHA-256.
    /// Renvoie le chemin du fichier vérifié. Lève <see cref="ErreurMiseAJour"/> (message clair) si l'installateur
    /// ou son empreinte manquent, si l'empreinte ne correspond pas, ou si le téléchargement échoue ;
    /// <see cref="OperationCanceledException"/> si l'utilisateur annule. Rien n'est laissé sur le disque en cas d'échec.
    /// </summary>
    /// <param name="progression">Reçoit l'avancement, de 0 à 1.</param>
    public async Task<string> TelechargerAsync(ReleaseGitHub release, string dossier, IProgress<double>? progression, CancellationToken annulation)
    {
        var fichiers = ChoixFichiers.Trouver(release)
            ?? throw new ErreurMiseAJour($"La version {release.Version} ne contient pas d'installateur {ChoixFichiers.NomSetup(release.Version)}.");
        if (fichiers.Empreinte is null)
            throw new ErreurMiseAJour(
                "L'empreinte de sécurité (fichier .sha256) n'a pas été publiée avec cette version : par prudence, l'installation est refusée. " +
                "Tu peux télécharger la mise à jour à la main depuis la page de la Release.");
        if (fichiers.Setup.Taille > TailleMaximale)
            throw new ErreurMiseAJour("L'installateur annoncé est anormalement gros : installation refusée.");

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(DelaiTelechargement);
        string cible = Path.Combine(dossier, fichiers.Setup.Nom);
        string partiel = cible + ".part";
        bool termine = false;
        try
        {
            // 1) L'empreinte attendue (petit fichier texte).
            string attendue = await TelechargerEmpreinteAsync(fichiers, delai.Token);

            // 2) L'installateur, avec la progression.
            Directory.CreateDirectory(dossier);
            using (var requete = Requete(new Uri(fichiers.Setup.UrlTelechargement)))
            using (var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, delai.Token))
            {
                if (!reponse.IsSuccessStatusCode)
                    throw new ErreurMiseAJour($"Le téléchargement a échoué (HTTP {(int)reponse.StatusCode}). Réessaie plus tard.");
                long total = reponse.Content.Headers.ContentLength ?? fichiers.Setup.Taille;
                if (total > TailleMaximale)
                    throw new ErreurMiseAJour("L'installateur est anormalement gros : installation refusée.");

                await using var source = await reponse.Content.ReadAsStreamAsync(delai.Token);
                await using var destination = new FileStream(partiel, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var tampon = new byte[81920];
                long recus = 0;
                int lus;
                while ((lus = await source.ReadAsync(tampon, delai.Token)) > 0)
                {
                    recus += lus;
                    if (recus > TailleMaximale) throw new ErreurMiseAJour("L'installateur est anormalement gros : installation refusée.");
                    await destination.WriteAsync(tampon.AsMemory(0, lus), delai.Token);
                    if (total > 0) progression?.Report(Math.Min(1.0, (double)recus / total));
                }
            }

            // 3) Contrôle d'intégrité, avant tout lancement.
            if (!await EmpreinteSha256.VerifierAsync(partiel, attendue, delai.Token))
                throw new ErreurMiseAJour(
                    "Le fichier téléchargé ne correspond pas à l'empreinte publiée (téléchargement abîmé ou fichier modifié) : " +
                    "installation refusée. Réessaie plus tard, ou télécharge la mise à jour à la main.");

            File.Move(partiel, cible, overwrite: true);
            progression?.Report(1.0);
            termine = true;
            return cible;
        }
        catch (OperationCanceledException) when (!annulation.IsCancellationRequested)
        {
            throw new ErreurMiseAJour("Le téléchargement a pris trop de temps. Réessaie plus tard.");
        }
        catch (HttpRequestException e)
        {
            throw new ErreurMiseAJour("Le téléchargement a échoué : vérifie ta connexion à Internet.", e);
        }
        catch (IOException e)
        {
            throw new ErreurMiseAJour("Impossible d'enregistrer l'installateur sur le disque : " + e.Message, e);
        }
        finally
        {
            if (!termine) SupprimerSansErreur(partiel);
        }
    }

    private async Task<string> TelechargerEmpreinteAsync(FichiersMiseAJour fichiers, CancellationToken annulation)
    {
        using var requete = Requete(new Uri(fichiers.Empreinte!.UrlTelechargement));
        using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        if (!reponse.IsSuccessStatusCode)
            throw new ErreurMiseAJour($"L'empreinte de sécurité n'a pas pu être téléchargée (HTTP {(int)reponse.StatusCode}) : installation refusée.");
        // Un fichier .sha256 fait moins de 200 octets : au-delà de 4 Ko, ce n'en est pas un.
        if (reponse.Content.Headers.ContentLength > 4096)
            throw new ErreurMiseAJour("Le fichier d'empreinte est invalide : installation refusée.");
        string contenu = await reponse.Content.ReadAsStringAsync(annulation);
        if (contenu.Length > 4096)
            throw new ErreurMiseAJour("Le fichier d'empreinte est invalide : installation refusée.");
        return EmpreinteSha256.Lire(contenu, fichiers.Setup.Nom)
               ?? throw new ErreurMiseAJour("Le fichier d'empreinte est illisible : installation refusée.");
    }

    private static void SupprimerSansErreur(string chemin)
    {
        try { if (File.Exists(chemin)) File.Delete(chemin); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
