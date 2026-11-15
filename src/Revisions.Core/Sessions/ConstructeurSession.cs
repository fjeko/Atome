using Revisions.Core.Modeles;
using Revisions.Core.Planification;

namespace Revisions.Core.Sessions;

/// <summary>Une carte prête à être présentée en session, avec ce qu'il faut pour l'afficher et la planifier.</summary>
public sealed record CarteAReviser(
    Carte Carte,
    EtatCarte Etat,
    string PackId,
    string Matiere,
    string Seance);

/// <summary>Choisit les cartes d'une session et les met dans le bon ordre.</summary>
public static class ConstructeurSession
{
    /// <summary>
    /// Construit la session du jour.
    /// 1. Les cartes dues passent d'abord, les plus fragiles en tête (probabilité de rappel la plus basse).
    /// 2. On complète avec des nouvelles cartes, dans la limite demandée.
    /// 3. On mélange les matières (une carte de chaque à tour de rôle) et on répartit les nouvelles.
    /// </summary>
    /// <param name="dues">Toutes les cartes dues.</param>
    /// <param name="nouvelles">Nouvelles cartes candidates, déjà dans l'ordre de découverte souhaité.</param>
    /// <param name="jour">Jour de révision.</param>
    /// <param name="maxCartes">Taille maximale de la session.</param>
    /// <param name="maxNouvelles">Nombre maximal de nouvelles cartes.</param>
    public static List<CarteAReviser> Construire(
        IEnumerable<CarteAReviser> dues,
        IEnumerable<CarteAReviser> nouvelles,
        DateOnly jour,
        int maxCartes,
        int maxNouvelles)
    {
        if (maxCartes <= 0) return [];

        var duesChoisies = dues
            .OrderBy(c => Planificateur.RetrievabiliteActuelle(c.Etat, jour))
            .Take(maxCartes)
            .ToList();

        int placesRestantes = maxCartes - duesChoisies.Count;
        var nouvellesChoisies = nouvelles.Take(Math.Min(Math.Max(0, maxNouvelles), placesRestantes)).ToList();

        return Repartir(EntrelacerParMatiere(duesChoisies), nouvellesChoisies);
    }

    /// <summary>
    /// Alterne les matières : on prend la première carte de chaque matière, puis la deuxième, etc.
    /// L'ordre « plus fragile d'abord » est conservé à l'intérieur de chaque matière.
    /// </summary>
    public static List<CarteAReviser> EntrelacerParMatiere(IReadOnlyList<CarteAReviser> cartes)
    {
        var files = cartes.GroupBy(c => c.Matiere).Select(g => new Queue<CarteAReviser>(g)).ToList();
        var resultat = new List<CarteAReviser>(cartes.Count);
        while (resultat.Count < cartes.Count)
        {
            foreach (var file in files)
                if (file.Count > 0) resultat.Add(file.Dequeue());
        }
        return resultat;
    }

    /// <summary>Insère les nouvelles cartes à intervalles réguliers parmi les cartes à revoir.</summary>
    private static List<CarteAReviser> Repartir(List<CarteAReviser> revisions, List<CarteAReviser> nouvelles)
    {
        if (nouvelles.Count == 0) return revisions;
        var resultat = new List<CarteAReviser>(revisions.Count + nouvelles.Count);
        // Nombre de révisions à placer avant chaque nouvelle carte.
        double pas = (double)revisions.Count / (nouvelles.Count + 1);
        int indexRevision = 0;
        for (int i = 0; i < nouvelles.Count; i++)
        {
            int jusqua = (int)Math.Round(pas * (i + 1));
            while (indexRevision < jusqua) resultat.Add(revisions[indexRevision++]);
            resultat.Add(nouvelles[i]);
        }
        while (indexRevision < revisions.Count) resultat.Add(revisions[indexRevision++]);
        return resultat;
    }
}
