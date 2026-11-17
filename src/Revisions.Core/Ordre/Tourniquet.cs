namespace Revisions.Core.Ordre;

/// <summary>
/// « Tourniquet » : on prend un élément de chaque groupe à tour de rôle (une carte de maths,
/// une de Bloc 1, une d'anglais…, puis on recommence). L'ordre à l'intérieur de chaque groupe est conservé.
/// </summary>
public static class Tourniquet
{
    /// <param name="elements">Éléments déjà triés dans l'ordre voulu à l'intérieur de chaque groupe.</param>
    /// <param name="groupe">Clé du groupe d'un élément (la matière).</param>
    /// <remarks>
    /// Les groupes passent dans l'ordre où ils apparaissent dans la liste : comme elle est déjà triée,
    /// la matière du premier élément passe en premier (par exemple celle de la plus ancienne période passée).
    /// </remarks>
    public static List<T> Entrelacer<T>(IEnumerable<T> elements, Func<T, string> groupe)
    {
        // GroupBy garde l'ordre de première apparition des groupes et l'ordre des éléments dans chaque groupe.
        var files = elements.GroupBy(groupe).Select(g => new Queue<T>(g)).ToList();
        var resultat = new List<T>(files.Sum(f => f.Count));
        while (files.Count > 0)
        {
            foreach (var file in files) resultat.Add(file.Dequeue());
            files.RemoveAll(f => f.Count == 0);
        }
        return resultat;
    }
}
