using System.IO;
using System.Media;

namespace Revisions.App.Services;

/// <summary>
/// Trois sons courts et doux (bonne réponse, fin de session, niveau), désactivés par défaut.
/// Ils sont fabriqués au vol (notes sinusoïdales qui s'éteignent doucement) : aucun fichier audio,
/// donc aucune licence à gérer.
/// </summary>
public static class ServiceSons
{
    /// <summary>Réglage « Sons » (Réglages → Apparence).</summary>
    public static bool Actifs { get; set; }

    public enum Son { BonneReponse, FinSession, Niveau }

    private static readonly Dictionary<Son, byte[]> Cache = [];

    // Notes (en hertz) : une quinte légère, un petit arpège majeur, un arpège montant.
    private static double[] Notes(Son son) => son switch
    {
        Son.BonneReponse => [659.3, 987.8],
        Son.FinSession => [523.3, 659.3, 784.0],
        _ => [523.3, 659.3, 784.0, 1046.5],
    };

    public static void Jouer(Son son)
    {
        if (!Actifs) return;
        try
        {
            if (!Cache.TryGetValue(son, out var wav)) Cache[son] = wav = Fabriquer(Notes(son));
            var lecteur = new SoundPlayer(new MemoryStream(wav));
            lecteur.Play(); // asynchrone : ne bloque jamais l'interface
        }
        catch (Exception exception)
        {
            Journal.Erreur("Son", exception); // pas de carte son : tant pis, on continue sans bruit
        }
    }

    /// <summary>Fichier WAV (16 bits, mono) : chaque note dure 110 ms et s'éteint doucement.</summary>
    private static byte[] Fabriquer(double[] notes)
    {
        const int frequence = 22050;
        const double dureeNote = 0.11, volume = 0.16;
        int parNote = (int)(frequence * dureeNote);
        int total = parNote * (notes.Length + 2); // la dernière note résonne un peu plus longtemps
        var echantillons = new short[total];
        for (int n = 0; n < notes.Length; n++)
        {
            int debut = n * parNote, longueur = n == notes.Length - 1 ? total - debut : parNote * 2;
            for (int i = 0; i < longueur && debut + i < total; i++)
            {
                double t = (double)i / frequence;
                double attaque = Math.Min(1, i / (frequence * 0.008));       // pas de « clic » au début
                double extinction = Math.Exp(-t * 9);                         // son doux, qui s'éteint
                double valeur = Math.Sin(2 * Math.PI * notes[n] * t) * attaque * extinction * volume;
                echantillons[debut + i] = (short)Math.Clamp(echantillons[debut + i] + valeur * short.MaxValue, short.MinValue, short.MaxValue);
            }
        }

        using var flux = new MemoryStream();
        using var w = new BinaryWriter(flux);
        int octets = total * 2;
        w.Write("RIFF"u8); w.Write(36 + octets); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(frequence); w.Write(frequence * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(octets);
        foreach (short e in echantillons) w.Write(e);
        return flux.ToArray();
    }
}
