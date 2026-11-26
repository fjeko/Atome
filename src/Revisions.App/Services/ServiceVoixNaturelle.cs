using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.Core.Voix;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>État de la voix naturelle, affiché dans Réglages → Voix.</summary>
public enum EtatVoixNaturelle { Absente, Telechargement, Prete, Echec }

/// <summary>
/// Voix anglaise naturelle (Piper) : téléchargée une seule fois, en arrière-plan, au premier lancement,
/// dans %LOCALAPPDATA%\RevisionsBTS\voix. Les mises à jour d'Atome ne la retéléchargent pas.
/// Tant qu'elle n'est pas prête (ou si le téléchargement échoue), Atome garde la voix de Windows.
/// </summary>
public sealed partial class ServiceVoixNaturelle : ObservableObject
{
    private readonly TelechargeurVoix _telechargeur;
    private Dictionary<Timbre, SynthesePiper>? _syntheses;
    private int _enCours; // 1 pendant un téléchargement (Interlocked : jamais deux à la fois)

    /// <summary>Taille maximale du cache audio : au-delà, les phrases les plus anciennes sont effacées.</summary>
    public const long TailleCacheMax = 300L * 1024 * 1024;

    public PaquetVoix Paquet { get; } = CatalogueVoix.Piper1;

    /// <summary>Dossier racine des voix (paquets et cache audio).</summary>
    public static string DossierVoix => Path.Combine(BaseDeDonnees.DossierDonnees, "voix");

    public string DossierCache => Path.Combine(DossierVoix, "cache");

    [ObservableProperty] private EtatVoixNaturelle _etat;
    /// <summary>Avancement du téléchargement, en pourcentage.</summary>
    [ObservableProperty] private double _progression;
    /// <summary>Phrase d'état pour les Réglages.</summary>
    [ObservableProperty] private string _message = "";

    public ServiceVoixNaturelle(HttpClient http)
    {
        _telechargeur = new TelechargeurVoix(http, Paquet, Path.Combine(DossierVoix, Paquet.Version));
        if (_telechargeur.EstPrete) Activer();
        else { Etat = EtatVoixNaturelle.Absente; Message = "La voix naturelle n'est pas encore téléchargée : Atome utilise la voix de Windows."; }
    }

    /// <summary>Synthèse Piper pour un timbre (féminin : Cori, masculin : Norman), ou null tant que les voix ne sont pas prêtes.</summary>
    public SynthesePiper? Synthese(Timbre timbre = Timbre.Feminin) => _syntheses?.GetValueOrDefault(timbre);

    public bool EstPrete => _syntheses is not null;

    /// <summary>
    /// WAV d'un dialogue entier : chaque réplique avec la voix de son personnage, mises bout à bout avec un court silence.
    /// Les répliques d'une même voix sont synthétisées en un seul lancement de Piper ; le résultat est gardé en cache.
    /// </summary>
    public async Task<string> DialogueAsync(IReadOnlyList<(string Nom, string Texte)> repliques, double vitesse)
    {
        if (_syntheses is null) throw new InvalidOperationException("Voix naturelle pas encore prête.");
        var timbres = ChoixVoix.AttribuerTimbres(repliques.Select(r => r.Nom));
        Timbre TimbreDe(string nom) => timbres.TryGetValue(nom.Trim(), out var t) ? t : Timbre.Feminin;

        // Clé du dialogue : chaque réplique avec sa voix, dans l'ordre.
        string description = string.Join("\n", repliques.Select(r => $"{TimbreDe(r.Nom)}:{r.Texte}"));
        string cible = Path.Combine(DossierCache, ChoixVoix.CleCache(description, Paquet.Version + "-dialogue" + SynthesePiper.Reglages, vitesse));
        if (File.Exists(cible)) return cible;

        // Une synthèse par voix, les deux voix en parallèle.
        var parVoix = repliques.Select((r, i) => (Index: i, r.Texte, Timbre: TimbreDe(r.Nom))).GroupBy(r => r.Timbre).ToList();
        var chemins = new string[repliques.Count];
        await Task.WhenAll(parVoix.Select(async groupe =>
        {
            var liste = groupe.ToList();
            var wavs = await _syntheses[groupe.Key].SynthetiserLotAsync(liste.Select(r => r.Texte).ToList(), vitesse);
            for (int i = 0; i < liste.Count; i++) chemins[liste[i].Index] = wavs[i];
        }));

        byte[] assemble = AssemblageWav.Assembler(chemins.Select(File.ReadAllBytes).ToList());
        // Fichier provisoire propre à cette demande : deux clics rapides sur « Tout écouter » ne se gênent pas.
        string provisoire = $"{cible}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(provisoire, assemble);
        try { File.Move(provisoire, cible, overwrite: true); }
        catch (IOException) when (File.Exists(cible)) { File.Delete(provisoire); } // l'autre demande l'a déjà mis en place
        return cible;
    }

    private void Activer()
    {
        // Une synthèse par voix ; « piper-1-cori » entre dans la clé du cache (deux voix = deux fichiers différents).
        _syntheses = Paquet.Voix.ToDictionary(v => v.Timbre,
            v => new SynthesePiper(_telechargeur.Executable, _telechargeur.Modele(v), DossierCache, $"{Paquet.Version}-{v.Id}"));
        Etat = EtatVoixNaturelle.Prete;
        Progression = 100;
        Message = $"Voix naturelle prête : {Paquet.NomAffiche}.";
    }

    /// <summary>
    /// Appelée au démarrage : fait le ménage du cache, puis, si la voix manque, la télécharge après un court délai
    /// (le démarrage d'Atome reste rapide). Plusieurs essais espacés ; ensuite on réessaiera au prochain lancement.
    /// </summary>
    public async Task DemarrerAsync(TimeSpan delaiInitial)
    {
        try { await Task.Run(() => SynthesePiper.NettoyerCache(DossierCache, TailleCacheMax)); }
        catch (Exception e) { Journal.Info("Voix", "Ménage du cache impossible : " + e.Message); }
        if (EstPrete) return;
        await Task.Delay(delaiInitial);
        TimeSpan[] pauses = [TimeSpan.Zero, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15)];
        foreach (var pause in pauses)
        {
            if (pause > TimeSpan.Zero) await Task.Delay(pause);
            if (await TelechargerAsync()) return;
        }
    }

    /// <summary>Bouton « Réessayer » des Réglages.</summary>
    [RelayCommand]
    private async Task ReessayerAsync() => await TelechargerAsync();

    /// <summary>Un essai complet. Renvoie vrai si la voix est prête à la fin.</summary>
    private async Task<bool> TelechargerAsync()
    {
        if (EstPrete) return true;
        if (Interlocked.Exchange(ref _enCours, 1) == 1) return false;
        try
        {
            Etat = EtatVoixNaturelle.Telechargement;
            Message = $"Téléchargement de la voix naturelle ({Paquet.TailleTotale / 1_000_000} Mo, une seule fois)…";
            int dernierPourcent = -1;
            var progression = new Progress<ProgressionVoix>(p =>
            {
                int pourcent = (int)(p.Fraction * 100);
                if (pourcent == dernierPourcent) return; // une mise à jour par pour cent suffit
                dernierPourcent = pourcent;
                Progression = pourcent;
            });
            Journal.Info("Voix", "Téléchargement de la voix naturelle…");
            var resultat = await Task.Run(() => _telechargeur.InstallerAsync(progression));
            if (resultat.Prete)
            {
                Journal.Info("Voix", "Voix naturelle installée.");
                Activer();
                return true;
            }
            Journal.Info("Voix", "Échec du téléchargement de la voix : " + resultat.Erreur);
            Etat = EtatVoixNaturelle.Echec;
            Message = "Téléchargement impossible pour l'instant (connexion ?). Atome réessaiera ; en attendant, il utilise la voix de Windows.";
            return false;
        }
        catch (Exception e)
        {
            Journal.Erreur("Voix", e);
            Etat = EtatVoixNaturelle.Echec;
            Message = "Téléchargement impossible pour l'instant. Atome utilise la voix de Windows.";
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _enCours, 0);
        }
    }
}
