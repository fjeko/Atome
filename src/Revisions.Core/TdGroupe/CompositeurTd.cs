using Revisions.Core.Modeles;

namespace Revisions.Core.TdGroupe;

/// <summary>Un exercice d'un TD solo, avec ce qu'il faut pour l'historique et la remise en révision.</summary>
public sealed record ExerciceSolo(ExerciceTd Exercice, string Prefixe, string Theme, SujetTd Sujet, int Minutes);

/// <summary>Bilan d'un sujet dans l'historique des TD (clé « PREFIXE/sujet »).</summary>
/// <param name="Faits">Nombre de fois où il a été fait.</param>
/// <param name="RateRecemment">Raté lors de l'une des 3 dernières fois.</param>
public sealed record BilanSujet(int Faits, bool RateRecemment);

/// <summary>Ce que l'étudiant a choisi pour son TD solo.</summary>
/// <param name="Generateurs">Thèmes cochés.</param>
/// <param name="Minutes">Durée visée.</param>
/// <param name="Niveau">1 à 3.</param>
/// <param name="SujetPermis">Filtre facultatif (par exemple : seulement les notions des séances lues).</param>
/// <param name="Historique">Bilan des sujets déjà faits (clé « PREFIXE/sujet »).</param>
public sealed record DemandeTdSolo(
    IReadOnlyList<IGenerateurASujets> Generateurs,
    int Minutes,
    int Niveau,
    Func<IGenerateurASujets, SujetTd, bool>? SujetPermis = null,
    IReadOnlyDictionary<string, BilanSujet>? Historique = null);

/// <summary>
/// Compose un TD solo d'une durée donnée (voir docs/td-solo.md) : tirage pondéré des sujets
/// (raté récemment ×3, jamais fait ×2, sinon ×1 ; poids divisé par deux à chaque reprise),
/// jusqu'à la durée visée à 10 % près, puis rangement par thème et dans l'ordre du cours.
/// </summary>
public static class CompositeurTd
{
    public static string Cle(string prefixe, string sujet) => $"{prefixe}/{sujet}";

    public static List<ExerciceSolo> Composer(DemandeTdSolo d, GenerateurAleatoire r)
    {
        var candidats = (from g in d.Generateurs
                         from s in g.Sujets
                         where s.NiveauMin <= d.Niveau && (d.SujetPermis?.Invoke(g, s) ?? true)
                         select (Generateur: g, Sujet: s)).ToList();
        if (candidats.Count == 0) return [];

        var poids = candidats.Select(c => (double)Poids(d.Historique, Cle(c.Generateur.Prefixe, c.Sujet.Cle))).ToArray();
        var choisis = new List<int>();
        int total = 0, dernier = -1;
        double plafond = d.Minutes * 1.1;
        while (total < d.Minutes)
        {
            // Sujets qui tiennent dans le temps restant (sauf pour le tout premier exercice), pas deux fois de suite.
            var possibles = Enumerable.Range(0, candidats.Count)
                .Where(i => (choisis.Count == 0 || total + candidats[i].Sujet.MinutesPour(d.Niveau) <= plafond) && (i != dernier || candidats.Count == 1))
                .ToList();
            if (possibles.Count == 0) break;
            int k = Tirer(r, possibles, poids);
            choisis.Add(k);
            total += candidats[k].Sujet.MinutesPour(d.Niveau);
            poids[k] /= 2;
            dernier = k;
        }

        // Rangement : thèmes dans l'ordre de la liste, sujets dans l'ordre du cours ; chaque exercice est créé à la fin.
        return choisis
            .OrderBy(i => IndexDe(d.Generateurs, candidats[i].Generateur))
            .ThenBy(i => IndexDe(candidats[i].Generateur.Sujets, candidats[i].Sujet))
            .Select(i =>
            {
                var (g, s) = candidats[i];
                var exercice = g.CreerExercice(s.Cle, r, d.Niveau);
                if (s.TypeExamen) exercice = exercice with { TypeExamen = true };
                return new ExerciceSolo(exercice, g.Prefixe, g.Theme, s, s.MinutesPour(d.Niveau));
            })
            .ToList();
    }

    private static int Poids(IReadOnlyDictionary<string, BilanSujet>? historique, string cle) =>
        historique?.GetValueOrDefault(cle) switch
        {
            null or { Faits: 0 } => 2,
            { RateRecemment: true } => 3,
            _ => 1,
        };

    private static int Tirer(GenerateurAleatoire r, List<int> possibles, double[] poids)
    {
        double somme = possibles.Sum(i => poids[i]);
        double cible = r.Entier(0, 999_999) / 1_000_000.0 * somme;
        foreach (int i in possibles)
        {
            cible -= poids[i];
            if (cible < 0) return i;
        }
        return possibles[^1];
    }

    private static int IndexDe<T>(IReadOnlyList<T> liste, T element) where T : class
    {
        for (int i = 0; i < liste.Count; i++) if (ReferenceEquals(liste[i], element)) return i;
        return int.MaxValue;
    }
}

/// <summary>
/// Thème « Exercices rédigés » d'une matière, pour le TD solo : les cartes de type **exercice** des packs
/// (pas les questions ni les QCM), une par sujet. Réponse auto-évaluée, comme le TD « séance ».
/// Ce n'est pas un TD de groupe (il dépend des packs importés) : il n'est pas dans le catalogue des codes.
/// </summary>
public sealed class GenerateurRediges : GenerateurASujets
{
    private readonly Dictionary<string, Carte> _cartes;
    private readonly ContenuSeance _contenu;

    /// <param name="matiere">Nom de la matière (« CEJM »).</param>
    /// <param name="cartes">Cartes exercice de la matière, dans l'ordre du cours.</param>
    /// <param name="titresSections">Titres des sections (pour le « rappel » de l'indice).</param>
    public GenerateurRediges(string matiere, IReadOnlyList<Carte> cartes, IReadOnlyDictionary<string, string> titresSections)
    {
        Matiere = matiere;
        var exercices = cartes.Where(c => c.Type == TypeCarte.Exercice && !string.IsNullOrWhiteSpace(c.Contenu.Enonce)).ToList();
        _cartes = exercices.ToDictionary(c => c.Id);
        _contenu = new ContenuSeance(matiere, exercices, titresSections);
        Sujets = exercices.Select(c => new SujetTd(c.Id, titresSections.GetValueOrDefault(c.SectionId, "Exercice"), c.SectionId,
            [4 + 2 * c.Difficulte, 4 + 2 * c.Difficulte, 4 + 2 * c.Difficulte], NiveauMin: Math.Max(1, c.Difficulte - 1))).ToList();
    }

    public override string Prefixe => "RED";
    public override string Matiere { get; }
    public override string Theme => $"Exercices rédigés · {Matiere}";
    public override IReadOnlyList<SujetTd> Sujets { get; }

    public override ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau) =>
        GenerateurSeance.Convertir(_cartes[cle], r, _contenu);
}
