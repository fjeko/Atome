using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Revisions.App.Services;

/// <summary>
/// Lecture à voix haute avec les voix installées dans Windows (synthèse vocale de Windows 10 et 11, hors ligne).
/// Sert aux questions d'écoute du test de positionnement. Une voix anglaise s'ajoute dans
/// Paramètres → Heure et langue → Voix. Aucune erreur ne remonte : sans voix, <see cref="Disponible"/> est faux.
/// </summary>
public sealed class ServiceVoix : IDisposable
{
    private SpeechSynthesizer? _synthese;
    private MediaPlayer? _lecteur;

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

    /// <summary>
    /// Repli sur l'ancien moteur vocal (SAPI, System.Speech) : certaines voix anglaises, comme
    /// « Microsoft Zira Desktop », n'existent que là et sont invisibles pour la synthèse moderne.
    /// </summary>
    private static System.Speech.Synthesis.InstalledVoice? VoixSapi(string langue)
    {
        try
        {
            using var s = new System.Speech.Synthesis.SpeechSynthesizer();
            var voix = s.GetInstalledVoices().Where(v => v.Enabled).ToList();
            string prefixe = langue.Split('-')[0];
            return voix.FirstOrDefault(v => string.Equals(v.VoiceInfo.Culture.Name, langue, StringComparison.OrdinalIgnoreCase))
                ?? voix.FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith(prefixe + "-", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e)
        {
            Journal.Info("Voix", "Moteur vocal SAPI indisponible : " + e.Message);
            return null;
        }
    }

    /// <summary>Vrai si une voix existe pour cette langue (moteur moderne ou, à défaut, SAPI).</summary>
    public static bool Disponible(string langue) => Voix(langue) is not null || VoixSapi(langue) is not null;

    /// <summary>Nom de la voix utilisée pour cette langue (« Microsoft Hazel »), ou null.</summary>
    public static string? NomVoix(string langue) => Voix(langue)?.DisplayName ?? VoixSapi(langue)?.VoiceInfo.Name;

    private System.Speech.Synthesis.SpeechSynthesizer? _sapi;

    private int _volume = 100;

    /// <summary>Volume de lecture, de 0 à 100 (réglage « VolumeVoix ») : tout de suite avec la voix moderne, à la phrase suivante avec SAPI.</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_lecteur is not null) _lecteur.Volume = _volume / 100.0;
        }
    }

    /// <summary>Lit un texte. Renvoie faux si aucune voix n'est disponible ou si la lecture a échoué.</summary>
    /// <param name="vitesse">1 = vitesse normale ; 0,8 = un peu plus lent.</param>
    public async Task<bool> LireAsync(string texte, string langue, double vitesse = 0.9)
    {
        try
        {
            if (Voix(langue) is not { } voix)
            {
                // Repli SAPI : SpeakAsync joue directement sur la sortie audio par défaut.
                if (VoixSapi(langue) is not { } ancienne) return false;
                Arreter();
                _sapi ??= new System.Speech.Synthesis.SpeechSynthesizer();
                _sapi.SetOutputToDefaultAudioDevice();
                _sapi.SelectVoice(ancienne.VoiceInfo.Name);
                _sapi.Volume = Math.Clamp(Volume, 0, 100);
                _sapi.Rate = (int)Math.Clamp(Math.Round((vitesse - 1) * 10), -10, 10); // 0,9 → -1
                _sapi.SpeakAsync(texte);
                return true;
            }
            Arreter();
            _synthese ??= new SpeechSynthesizer();
            _synthese.Voice = voix;
            _synthese.Options.SpeakingRate = Math.Clamp(vitesse, 0.5, 2.0);
            var flux = await _synthese.SynthesizeTextToStreamAsync(texte);
            _lecteur ??= new MediaPlayer();
            _lecteur.Volume = Math.Clamp(Volume, 0, 100) / 100.0;
            _lecteur.Source = MediaSource.CreateFromStream(flux, flux.ContentType);
            _lecteur.Play();
            return true;
        }
        catch (Exception e)
        {
            Journal.Erreur("Voix", e);
            return false;
        }
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
