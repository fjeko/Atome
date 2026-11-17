using System.Security.Cryptography;

namespace Revisions.Core.MisesAJour;

/// <summary>
/// Vérification d'intégrité : l'installateur téléchargé doit avoir exactement l'empreinte SHA-256
/// publiée à côté de lui dans la Release. Un seul octet différent (téléchargement abîmé, fichier
/// remplacé) donne une empreinte complètement différente.
/// </summary>
public static class EmpreinteSha256
{
    /// <summary>
    /// Lit l'empreinte dans le contenu d'un fichier « .sha256 ». Formats acceptés :
    /// <list type="bullet">
    /// <item>l'empreinte seule (64 caractères hexadécimaux) ;</item>
    /// <item>le format de sha256sum : « empreinte  nom-du-fichier » (ou « empreinte *nom-du-fichier ») ;</item>
    /// </list>
    /// Si un nom de fichier est indiqué, il doit être celui attendu. Renvoie null si rien d'utilisable.
    /// </summary>
    public static string? Lire(string contenu, string nomFichierAttendu)
    {
        foreach (string brute in contenu.Split('\n'))
        {
            string ligne = brute.Trim().TrimStart('﻿'); // un BOM UTF-8 éventuel (PowerShell 5.1)
            if (ligne.Length == 0) continue;

            string[] morceaux = ligne.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            string empreinte = morceaux[0];
            if (!EstEmpreinte(empreinte)) return null;
            if (morceaux.Length == 2)
            {
                string nom = morceaux[1].Trim().TrimStart('*');
                if (!string.Equals(nom, nomFichierAttendu, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return empreinte.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Calcule l'empreinte SHA-256 d'un flux, en hexadécimal minuscule.</summary>
    public static async Task<string> CalculerAsync(Stream flux, CancellationToken annulation = default) =>
        Convert.ToHexStringLower(await SHA256.HashDataAsync(flux, annulation));

    /// <summary>Calcule l'empreinte d'un fichier et la compare à celle attendue (sans tenir compte des majuscules).</summary>
    public static async Task<bool> VerifierAsync(string cheminFichier, string empreinteAttendue, CancellationToken annulation = default)
    {
        await using var flux = File.OpenRead(cheminFichier);
        string calculee = await CalculerAsync(flux, annulation);
        return string.Equals(calculee, empreinteAttendue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EstEmpreinte(string texte) => texte.Length == 64 && texte.All(char.IsAsciiHexDigit);
}
