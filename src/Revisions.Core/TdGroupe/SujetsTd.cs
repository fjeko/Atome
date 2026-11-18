using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// Une sorte d'exercice d'un générateur (« Dates au plus tôt », « Adresse du réseau »…).
/// Le TD solo compose ses feuilles à partir des sujets.
/// </summary>
/// <param name="Cle">Identifiant stable (enregistré dans l'historique des TD : ne pas renommer).</param>
/// <param name="Nom">Nom affiché.</param>
/// <param name="SectionId">Section du cours à revoir, ou null.</param>
/// <param name="Minutes">Durée estimée aux niveaux 1, 2 et 3 (en minutes, à la main sur papier).</param>
/// <param name="NiveauMin">Niveau à partir duquel le sujet est proposé (un « projet complet » n'existe qu'au niveau 3).</param>
/// <param name="TypeExamen">Reprend le déroulé d'un exercice des sujets officiels du BTS SIO.</param>
public sealed record SujetTd(string Cle, string Nom, string? SectionId, int[] Minutes, int NiveauMin = 1, bool TypeExamen = false)
{
    public int MinutesPour(int niveau) => Minutes[Math.Clamp(niveau, 1, 3) - 1];
}

/// <summary>Générateur qui sait créer un exercice d'un sujet donné (pour le TD solo).</summary>
public interface IGenerateurASujets : IGenerateurTd
{
    /// <summary>Matière (« Mathématiques », « Réseau », « Cybersécurité », « SLAM »).</summary>
    string Matiere { get; }

    /// <summary>Thème court (« Graphes »).</summary>
    string Theme { get; }

    /// <summary>Sujets, dans l'ordre du cours.</summary>
    IReadOnlyList<SujetTd> Sujets { get; }

    /// <summary>Crée un exercice du sujet <paramref name="cle"/>.</summary>
    ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau);
}

/// <summary>
/// Base des générateurs « à sujets » (graphes, MPM, IPv4…). En groupe, le TD passe par tous les sujets
/// dans l'ordre du cours, comme les matrices.
/// Version 1 pour tous : tout changement qui modifie un TD généré impose une version 2 (voir <see cref="IGenerateurTd"/>).
/// </summary>
public abstract class GenerateurASujets : IGenerateurASujets
{
    public abstract string Prefixe { get; }
    public abstract string Matiere { get; }
    public abstract string Theme { get; }
    public string Nom => $"{Theme} ({Matiere})";
    public int VersionActuelle => 1;
    public IReadOnlyCollection<int> VersionsPrisesEnCharge { get; } = [1];
    public abstract IReadOnlyList<SujetTd> Sujets { get; }
    public abstract ExerciceTd CreerExercice(string cle, GenerateurAleatoire r, int niveau);

    public Td Generer(ParametresTd p)
    {
        if (!VersionsPrisesEnCharge.Contains(p.Version))
            throw new TdImpossibleException($"Version du générateur « {Theme} » inconnue : mets Atome à jour.");
        var r = new GenerateurAleatoire(p.GraineInterne);
        var sujets = Sujets.Where(s => s.NiveauMin <= p.Niveau).ToList();
        var choisis = new List<SujetTd>();
        while (p.Nombre - choisis.Count >= sujets.Count) choisis.AddRange(sujets);
        if (p.Nombre > choisis.Count)
        {
            var tirage = sujets.ToList();
            r.Melanger(tirage);
            var reste = tirage.Take(p.Nombre - choisis.Count).ToHashSet();
            choisis.AddRange(sujets.Where(reste.Contains));
        }
        var exercices = choisis.Select(s =>
        {
            var e = CreerExercice(s.Cle, r, p.Niveau);
            return s.TypeExamen ? e with { TypeExamen = true } : e;
        }).ToList();
        return new Td(p, CodeTd.Ecrire(p), $"{Theme} · {NomsNiveaux[p.Niveau - 1].ToLowerInvariant()}", exercices, EmpreinteTd.Calculer(exercices));
    }

    public static IReadOnlyList<string> NomsNiveaux { get; } = ["Découverte", "Entraînement", "Comme à l'examen"];

    // ==== Outils communs ======================================================================

    /// <summary>Lettres des sommets ou des tâches : A, B, C…</summary>
    protected static string Lettre(int i) => ((char)('A' + i)).ToString();

    protected static string S(int n) => n > 1 ? "s" : "";

    /// <summary>Propositions d'un choix : la bonne, puis jusqu'à trois autres différentes, mélangées.</summary>
    protected static ReponseChoix ChoixMelanges(GenerateurAleatoire r, string bonne, IEnumerable<string> autres)
    {
        var choix = new List<string> { bonne };
        foreach (string autre in autres)
        {
            if (choix.Count == 4) break;
            if (!choix.Contains(autre)) choix.Add(autre);
        }
        r.Melanger(choix);
        return new ReponseChoix(choix, choix.IndexOf(bonne));
    }

    /// <summary>« Oui » / « Non », toujours dans cet ordre (plus naturel à l'écran).</summary>
    protected static ReponseChoix OuiNon(bool oui) => new(["Oui", "Non"], oui ? 0 : 1);

    /// <summary>Tableau Markdown : une ligne d'en-têtes puis les lignes.</summary>
    protected static string Tableau(IReadOnlyList<string> entetes, IEnumerable<IReadOnlyList<string>> lignes)
    {
        var texte = new System.Text.StringBuilder();
        texte.Append("| ").Append(string.Join(" | ", entetes)).Append(" |\n");
        texte.Append('|').Append(string.Concat(entetes.Select(_ => " --- |"))).Append('\n');
        foreach (var ligne in lignes) texte.Append("| ").Append(string.Join(" | ", ligne)).Append(" |\n");
        return texte.ToString();
    }

    protected static string Liste(IEnumerable<string> elements, string vide = "aucun")
    {
        var l = elements.ToList();
        return l.Count == 0 ? vide : string.Join(", ", l);
    }

    protected static string Nombre(int n) => Invariant($"{n}");
}
