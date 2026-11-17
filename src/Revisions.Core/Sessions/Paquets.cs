namespace Revisions.Core.Sessions;

/// <summary>
/// Mélange « par paquets » : les cartes d'une même matière sont regroupées en paquets de 4 à 6
/// (de la même partie autant que possible), et les paquets alternent entre les matières.
/// On garde l'intérêt de l'alternance (mélanger aide à retenir) sans changer de sujet à chaque carte.
/// </summary>
public static class Paquets
{
    /// <summary>Taille maximale d'un paquet.</summary>
    public const int TailleMax = 6;

    /// <summary>Taille visée d'un paquet (les paquets font entre 4 et 6 cartes quand c'est possible).</summary>
    public const int TailleMin = 4;

    /// <summary>
    /// Range les cartes en paquets.
    /// </summary>
    /// <param name="cartes">Cartes déjà dans l'ordre de priorité (plus urgentes d'abord).</param>
    /// <param name="matiere">Matière d'une carte.</param>
    /// <param name="partie">Partie d'une carte.</param>
    /// <remarks>
    /// 1. Les matières passent dans l'ordre de leur carte la plus prioritaire.
    /// 2. Dans une matière, les cartes d'une même partie sont rapprochées (les parties dans l'ordre de leur
    ///    carte la plus prioritaire), sans changer l'ordre à l'intérieur d'une partie.
    /// 3. Chaque matière est découpée en paquets (voir <see cref="Decouper{T}"/>).
    /// 4. On prend un paquet de chaque matière à tour de rôle.
    /// </remarks>
    public static List<T> Composer<T>(IReadOnlyList<T> cartes, Func<T, string> matiere, Func<T, string> partie)
    {
        var files = cartes
            .GroupBy(matiere)
            .Select(g => new Queue<List<T>>(Decouper(g.GroupBy(partie).SelectMany(p => p).ToList())))
            .ToList();

        var resultat = new List<T>(cartes.Count);
        while (files.Count > 0)
        {
            foreach (var file in files) resultat.AddRange(file.Dequeue());
            files.RemoveAll(f => f.Count == 0);
        }
        return resultat;
    }

    /// <summary>
    /// Découpe une liste en paquets de tailles presque égales, au plus <see cref="TailleMax"/> :
    /// 7 cartes → 4 + 3, 10 → 5 + 5, 13 → 5 + 4 + 4. Moins de 4 cartes : un seul petit paquet.
    /// </summary>
    public static List<List<T>> Decouper<T>(IReadOnlyList<T> cartes)
    {
        if (cartes.Count == 0) return [];
        int nombre = (cartes.Count + TailleMax - 1) / TailleMax;
        int taille = cartes.Count / nombre, enPlus = cartes.Count % nombre;
        var paquets = new List<List<T>>(nombre);
        int debut = 0;
        for (int i = 0; i < nombre; i++)
        {
            int n = taille + (i < enPlus ? 1 : 0);
            paquets.Add(cartes.Skip(debut).Take(n).ToList());
            debut += n;
        }
        return paquets;
    }
}

/// <summary>Durée estimée d'une session, à partir des temps de réponse passés.</summary>
public static class EstimationDuree
{
    /// <summary>Estimation tant qu'il n'y a pas assez d'historique : environ 20 secondes par carte.</summary>
    public const double SecondesParDefaut = 20;

    /// <summary>Nombre de réponses à partir duquel on se fie à l'historique.</summary>
    public const int ReponsesMinimum = 10;

    /// <summary>
    /// Secondes par carte : la médiane des temps de réponse donnés, bornée entre 5 s et 2 min
    /// (une carte laissée ouverte pendant une pause ne doit pas fausser l'estimation).
    /// </summary>
    public static double SecondesParCarte(IEnumerable<int> dureesMs)
    {
        var durees = dureesMs.Where(d => d > 0).Order().ToList();
        return durees.Count < ReponsesMinimum ? SecondesParDefaut : Math.Clamp(durees[durees.Count / 2] / 1000.0, 5, 120);
    }

    /// <summary>Minutes estimées (au moins 1 dès qu'il y a une carte).</summary>
    public static int Minutes(int nombreCartes, double secondesParCarte) =>
        nombreCartes <= 0 ? 0 : Math.Max(1, (int)Math.Round(nombreCartes * secondesParCarte / 60));

    /// <summary>« 12 cartes · environ 4 min », « 1 carte · environ 1 min » ou « Aucune carte pour l'instant ».</summary>
    public static string Texte(int nombreCartes, double secondesParCarte) =>
        nombreCartes <= 0
            ? "Aucune carte pour l'instant"
            : $"{nombreCartes} carte{(nombreCartes > 1 ? "s" : "")} · environ {Minutes(nombreCartes, secondesParCarte)} min";
}
