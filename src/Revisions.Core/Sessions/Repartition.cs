namespace Revisions.Core.Sessions;

/// <summary>Nombre de cartes d'une matière dans une session (tuiles « Au menu » de l'accueil).</summary>
public sealed record PartMatiere(string Matiere, int Nombre)
{
    /// <summary>« 1 carte », « 4 cartes ».</summary>
    public string Libelle => Nombre <= 1 ? $"{Nombre} carte" : $"{Nombre} cartes";
}

/// <summary>Répartition d'une session par matière : de la plus représentée à la moins représentée.</summary>
public static class Repartition
{
    /// <param name="matieres">La matière de chaque carte de la session.</param>
    public static IReadOnlyList<PartMatiere> ParMatiere(IEnumerable<string> matieres) =>
        matieres.GroupBy(m => m)
                .Select(g => new PartMatiere(g.Key, g.Count()))
                .OrderByDescending(p => p.Nombre)
                .ThenBy(p => p.Matiere, StringComparer.CurrentCulture)
                .ToList();
}
