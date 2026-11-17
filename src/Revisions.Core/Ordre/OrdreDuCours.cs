using Revisions.Core.Bibliotheque;

namespace Revisions.Core.Ordre;

/// <summary>
/// Ordre naturel du cours, valable pour toutes les matières :
/// matière › partie (tri naturel, chiffres romains compris) › ordre de la séance › titre de la séance › id du pack.
/// Le champ « ordre » repart de 1 dans chaque partie : il ne sert donc qu'à départager les séances d'une même partie.
/// </summary>
public sealed class OrdreDuCours : IComparer<ResumeSeance>
{
    public static OrdreDuCours Instance { get; } = new();

    /// <summary>Clé de matière, la même que dans la bibliothèque : le bloc d'abord, pour garder les matières rangées par bloc.</summary>
    public static string CleMatiere(ResumeSeance s) => $"{s.Bloc} {s.Matiere}".Trim();

    public int Compare(ResumeSeance? a, ResumeSeance? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int r = TriNaturel.Comparer(CleMatiere(a), CleMatiere(b));
        if (r == 0) r = TriNaturel.Comparer(a.Partie, b.Partie);
        if (r == 0) r = TriNaturel.Comparer(a.Sequence ?? "", b.Sequence ?? "");
        if (r == 0) r = (a.Ordre ?? int.MaxValue).CompareTo(b.Ordre ?? int.MaxValue);
        if (r == 0) r = TriNaturel.Comparer(a.Seance, b.Seance);
        if (r == 0) r = string.CompareOrdinal(a.PackId, b.PackId);
        return r;
    }
}
