using System.Text;

namespace Revisions.Core.Voix;

/// <summary>
/// Met bout à bout plusieurs fichiers WAV (les répliques d'un dialogue, chacune avec sa voix) pour n'en jouer
/// qu'un seul, avec un court silence entre deux répliques. Les fichiers doivent avoir le même format
/// (Piper : PCM 16 bits, mono, 22 050 Hz pour les deux voix d'Atome).
/// </summary>
public static class AssemblageWav
{
    /// <summary>Format et données audio d'un fichier WAV.</summary>
    public sealed record Audio(byte[] Format, byte[] Donnees)
    {
        /// <summary>Octets par seconde (lus dans le bloc « fmt »).</summary>
        public int OctetsParSeconde => BitConverter.ToInt32(Format, 8);
        /// <summary>Taille d'un échantillon, toutes voies comprises (on coupe toujours sur un échantillon entier).</summary>
        public int Alignement => BitConverter.ToInt16(Format, 12);
    }

    /// <summary>Lit un WAV : on cherche les blocs « fmt » et « data » (d'autres blocs peuvent exister).</summary>
    public static Audio Lire(byte[] wav)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("Ce n'est pas un fichier WAV.");
        byte[]? format = null, donnees = null;
        int position = 12;
        while (position + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, position, 4);
            int taille = BitConverter.ToInt32(wav, position + 4);
            int debut = position + 8;
            taille = Math.Min(taille, wav.Length - debut); // un fichier tronqué : on garde ce qui est là
            if (id == "fmt ") format = wav[debut..(debut + taille)];
            else if (id == "data") donnees = wav[debut..(debut + taille)];
            position = debut + taille + (taille % 2); // les blocs sont alignés sur 2 octets
        }
        if (format is null || format.Length < 16 || donnees is null) throw new InvalidDataException("WAV incomplet.");
        return new Audio(format, donnees);
    }

    /// <summary>Assemble les fichiers (dans l'ordre) en un seul WAV, avec <paramref name="silenceMs"/> millisecondes entre deux.</summary>
    public static byte[] Assembler(IReadOnlyList<byte[]> fichiers, int silenceMs = 350)
    {
        if (fichiers.Count == 0) throw new ArgumentException("Aucun fichier à assembler.", nameof(fichiers));
        var audios = fichiers.Select(Lire).ToList();
        byte[] format = audios[0].Format;
        if (audios.Any(a => !a.Format.AsSpan(0, 16).SequenceEqual(format.AsSpan(0, 16))))
            throw new InvalidDataException("Les fichiers n'ont pas le même format audio.");

        int silence = audios[0].OctetsParSeconde * silenceMs / 1000;
        silence -= silence % audios[0].Alignement;
        long total = audios.Sum(a => (long)a.Donnees.Length) + (long)silence * (audios.Count - 1);

        using var sortie = new MemoryStream();
        using (var w = new BinaryWriter(sortie, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write((int)(4 + 8 + format.Length + 8 + total));
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(format.Length);
            w.Write(format);
            w.Write("data"u8);
            w.Write((int)total);
            for (int i = 0; i < audios.Count; i++)
            {
                if (i > 0) w.Write(new byte[silence]); // silence = des zéros en PCM
                w.Write(audios[i].Donnees);
            }
        }
        return sortie.ToArray();
    }
}
