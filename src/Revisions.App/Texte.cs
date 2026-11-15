namespace Revisions.App;

/// <summary>Petites aides pour écrire des phrases correctes.</summary>
public static class Texte
{
    /// <summary>« 1 carte », « 3 cartes » : choisit le singulier ou le pluriel selon le nombre.</summary>
    public static string Pluriel(int nombre, string singulier, string pluriel) =>
        $"{nombre} {(nombre <= 1 ? singulier : pluriel)}";
}
