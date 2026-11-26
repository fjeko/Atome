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

    /// <summary>Vrai si une voix existe pour cette langue.</summary>
    public static bool Disponible(string langue) => Voix(langue) is not null;

    /// <summary>Nom de la voix utilisée pour cette langue (« Microsoft Hazel »), ou null.</summary>
    public static string? NomVoix(string langue) => Voix(langue)?.DisplayName;

    /// <summary>Lit un texte. Renvoie faux si aucune voix n'est disponible ou si la lecture a échoué.</summary>
    /// <param name="vitesse">1 = vitesse normale ; 0,8 = un peu plus lent.</param>
    public async Task<bool> LireAsync(string texte, string langue, double vitesse = 0.9)
    {
        try
        {
            if (Voix(langue) is not { } voix) return false;
            Arreter();
            _synthese ??= new SpeechSynthesizer();
            _synthese.Voice = voix;
            _synthese.Options.SpeakingRate = Math.Clamp(vitesse, 0.5, 2.0);
            var flux = await _synthese.SynthesizeTextToStreamAsync(texte);
            _lecteur ??= new MediaPlayer();
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
        try { _lecteur?.Pause(); }
        catch (Exception e) { Journal.Info("Voix", "Arrêt impossible : " + e.Message); }
    }

    public void Dispose()
    {
        Arreter();
        _lecteur?.Dispose();
        _synthese?.Dispose();
        _lecteur = null;
        _synthese = null;
    }
}
