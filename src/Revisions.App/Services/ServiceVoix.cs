using Revisions.Core.Voix;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Revisions.App.Services;

/// <summary>
/// Lecture à voix haute, hors ligne. Trois moteurs, du plus naturel au plus robotique (choix : <see cref="ChoixVoix.Choisir"/>) :
/// <list type="number">
/// <item>Piper, la voix anglaise naturelle téléchargée une fois (<see cref="ServiceVoixNaturelle"/>) ;</item>
/// <item>la synthèse vocale moderne de Windows (WinRT) ;</item>
/// <item>l'ancien moteur SAPI (« Microsoft Zira Desktop »…), invisible pour la synthèse moderne.</item>
/// </list>
/// Sert aux boutons 🔊 des cours et des cartes d'anglais, et aux questions d'écoute du test de positionnement.
/// Aucune erreur ne remonte : sans voix, <see cref="Disponible"/> est faux.
/// </summary>
public sealed class ServiceVoix(ServiceVoixNaturelle naturelle, EtatApplication etat) : IDisposable
{
    private SpeechSynthesizer? _synthese;
    private MediaPlayer? _lecteur;
    private System.Speech.Synthesis.SpeechSynthesizer? _sapi;
    /// <summary>Numéro de la dernière demande : une lecture plus ancienne qui finit sa synthèse ne se joue plus.</summary>
    private int _demande;

    /// <summary>Voix de Windows pour une langue (« en-GB ») : d'abord la langue exacte, sinon une autre variante (« en-US »).</summary>
    public static VoiceInformation? Voix(string langue)
    {
        try
        {
            var toutes = SpeechSynthesizer.AllVoices;
            string prefixe = langue.Split('-')[0];
            return toutes.FirstOrDefault(v => string.Equals(v.Language, langue, StringComparison.OrdinalIgnoreCase))
                ?? toutes.FirstOrDefault(v => v.Language.StartsWith(prefixe + "-", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e)
        {
            Journal.Info("Voix", "Synthèse vocale indisponible : " + e.Message);
            return null;
        }
    }

    /// <summary>Nom des voix SAPI déjà cherchées, par langue (la recherche crée un moteur : on ne la refait pas).</summary>
    private static readonly Dictionary<string, string?> _sapiParLangue = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Repli sur l'ancien moteur vocal (SAPI, System.Speech) : certaines voix anglaises, comme
    /// « Microsoft Zira Desktop », n'existent que là et sont invisibles pour la synthèse moderne.
    /// </summary>
    private static string? VoixSapi(string langue)
    {
        lock (_sapiParLangue)
        {
            if (_sapiParLangue.TryGetValue(langue, out var connue)) return connue;
            string? nom = null;
            try
            {
                using var s = new System.Speech.Synthesis.SpeechSynthesizer();
                var voix = s.GetInstalledVoices().Where(v => v.Enabled).ToList();
                string prefixe = langue.Split('-')[0];
                nom = (voix.FirstOrDefault(v => string.Equals(v.VoiceInfo.Culture.Name, langue, StringComparison.OrdinalIgnoreCase))
                    ?? voix.FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith(prefixe + "-", StringComparison.OrdinalIgnoreCase)))?.VoiceInfo.Name;
            }
            catch (Exception e)
            {
                Journal.Info("Voix", "Moteur vocal SAPI indisponible : " + e.Message);
            }
            return _sapiParLangue[langue] = nom;
        }
    }

    /// <summary>Moteur utilisé pour cette langue en ce moment (Piper dès que la voix naturelle est prête).</summary>
    public MoteurVoix Moteur(string langue) =>
        ChoixVoix.Choisir(langue, naturelle.EstPrete, Voix(langue) is not null, VoixSapi(langue) is not null);

    /// <summary>Vrai si une voix existe pour cette langue.</summary>
    public bool Disponible(string langue) => Moteur(langue) != MoteurVoix.Aucun;

    /// <summary>Nom de la voix utilisée pour cette langue (« Cori… », « Microsoft Hazel »), ou null.</summary>
    public string? NomVoix(string langue) => Moteur(langue) switch
    {
        MoteurVoix.Piper => naturelle.Paquet.NomAffiche,
        MoteurVoix.Windows => Voix(langue)?.DisplayName,
        MoteurVoix.Sapi => VoixSapi(langue),
        _ => null,
    };

    /// <summary>
    /// Volume de lecture, de 0 à 100 : c'est le réglage « VolumeVoix » (Réglages → Voix, ou le curseur du test de positionnement).
    /// Appliqué tout de suite avec Piper et la voix moderne, à la phrase suivante avec SAPI.
    /// </summary>
    public int Volume
    {
        get => etat.Reglages.VolumeVoix;
        set
        {
            etat.Reglages.VolumeVoix = Math.Clamp(value, 0, 100);
            if (_lecteur is not null) _lecteur.Volume = Volume / 100.0;
        }
    }

    /// <summary>Vitesse par défaut, d'après le réglage « VitesseVoix » (en %) : 1 = normale, 0,8 = plus lent.</summary>
    public double Vitesse => etat.Reglages.VitesseVoix / 100.0;

    /// <summary>Lit un texte. Renvoie faux si aucune voix n'est disponible ou si la lecture a échoué.</summary>
    /// <param name="vitesse">Vitesse de cette lecture ; par défaut, celle des Réglages.</param>
    /// <param name="timbre">Voix naturelle à utiliser : féminine (Cori, par défaut) ou masculine (Norman).</param>
    public Task<bool> LireAsync(string texte, string langue, double? vitesse = null, Timbre timbre = Timbre.Feminin) =>
        LireInterneAsync(texte, langue, vitesse, naturelle.Synthese(timbre) is { } piper ? v => piper.SynthetiserAsync(texte, v) : null);

    /// <summary>
    /// Lit un dialogue en entier, chaque personnage avec sa voix (voir <see cref="ChoixVoix.AttribuerTimbres"/>).
    /// Avec la voix de Windows (en attendant Piper), tout est lu d'une seule voix.
    /// </summary>
    public Task<bool> LireDialogueAsync(IReadOnlyList<(string Nom, string Texte)> repliques, string langue) =>
        LireInterneAsync(string.Join(" ", repliques.Select(r => r.Texte)), langue, null,
            naturelle.EstPrete ? v => naturelle.DialogueAsync(repliques, v) : null);

    /// <summary>
    /// Lecture commune : <paramref name="wavPiper"/> fabrique le fichier WAV avec Piper (null si Piper n'est pas prêt) ;
    /// en cas d'échec, <paramref name="texte"/> est lu par la voix de Windows.
    /// </summary>
    private async Task<bool> LireInterneAsync(string texte, string langue, double? vitesse, Func<double, Task<string>>? wavPiper)
    {
        double v = Math.Clamp(vitesse ?? Vitesse, 0.5, 2.0);
        int demande = Interlocked.Increment(ref _demande);
        Arreter();
        try
        {
            var moteur = Moteur(langue);
            if (moteur == MoteurVoix.Piper && wavPiper is not null)
            {
                try
                {
                    string wav = await wavPiper(v);
                    if (demande != _demande) return true; // un autre bouton a été cliqué entre-temps
                    Jouer(MediaSource.CreateFromUri(new Uri(wav)));
                    return true;
                }
                catch (Exception e)
                {
                    // Piper en panne (fichier effacé, antivirus…) : on lit quand même, avec la voix de Windows.
                    Journal.Info("Voix", "Piper indisponible, repli sur la voix de Windows : " + e.Message);
                    moteur = ChoixVoix.Choisir(langue, false, Voix(langue) is not null, VoixSapi(langue) is not null);
                }
            }

            if (moteur == MoteurVoix.Windows && Voix(langue) is { } voix)
            {
                _synthese ??= new SpeechSynthesizer();
                _synthese.Voice = voix;
                _synthese.Options.SpeakingRate = v;
                var flux = await _synthese.SynthesizeTextToStreamAsync(texte);
                if (demande != _demande) return true;
                Jouer(MediaSource.CreateFromStream(flux, flux.ContentType));
                return true;
            }

            if (moteur == MoteurVoix.Sapi && VoixSapi(langue) is { } ancienne)
            {
                // SpeakAsync joue directement sur la sortie audio par défaut.
                _sapi ??= new System.Speech.Synthesis.SpeechSynthesizer();
                _sapi.SetOutputToDefaultAudioDevice();
                _sapi.SelectVoice(ancienne);
                _sapi.Volume = Volume;
                _sapi.Rate = (int)Math.Clamp(Math.Round((v - 1) * 10), -10, 10); // 0,9 → -1
                _sapi.SpeakAsync(texte);
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            Journal.Erreur("Voix", e);
            return false;
        }
    }

    private void Jouer(MediaSource source)
    {
        _lecteur ??= new MediaPlayer();
        _lecteur.Volume = Volume / 100.0;
        _lecteur.Source = source;
        _lecteur.Play();
    }

    /// <summary>Arrête la lecture en cours.</summary>
    public void Arreter()
    {
        try { _lecteur?.Pause(); _sapi?.SpeakAsyncCancelAll(); }
        catch (Exception e) { Journal.Info("Voix", "Arrêt impossible : " + e.Message); }
    }

    public void Dispose()
    {
        Arreter();
        _lecteur?.Dispose();
        _synthese?.Dispose();
        _sapi?.Dispose();
        _sapi = null;
        _lecteur = null;
        _synthese = null;
    }
}
