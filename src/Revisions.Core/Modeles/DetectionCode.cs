using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Revisions.Core.Modeles;

/// <summary>
/// Reconnaît les cartes qui demandent d'écrire du code (« Réécris ce menu avec un switch… »), pour leur
/// proposer une vraie zone de code au lieu d'une simple ligne de réponse. Le format des packs ne change pas :
/// tout se déduit du contenu de la carte.
/// <list type="bullet">
/// <item>Signe principal : la réponse attendue (verso, réponse ou étapes) contient un bloc de code Markdown (```).</item>
/// <item>Signes secondaires, qui doivent aller ensemble : un verbe de consigne (écris, réécris, complète, corrige…)
/// et un indice de programmation (langage ou mot de code dans la consigne, matière de programmation,
/// ou extrait de code dans la réponse).</item>
/// </list>
/// </summary>
public static partial class DetectionCode
{
    /// <summary>Vrai si la carte attend du code. Un QCM n'en attend jamais (on choisit une réponse).</summary>
    /// <param name="matiere">Matière de la carte (facultative), utilisée comme indice secondaire.</param>
    public static bool AttendDuCode(Carte carte, string? matiere = null)
    {
        if (carte.Type == TypeCarte.Qcm) return false;
        var c = carte.Contenu;
        var reponses = new List<string?> { c.Verso, c.Reponse };
        if (c.Etapes is not null) reponses.AddRange(c.Etapes);

        // 1. Signe principal : un bloc de code dans la réponse attendue.
        if (reponses.Any(r => r is not null && r.Contains("```"))) return true;

        // 2. Signes secondaires : un verbe de consigne ET un indice de programmation.
        string consigne = SansAccents(c.Recto ?? c.Enonce ?? "");
        if (!VerbeDeConsigne().IsMatch(consigne)) return false;
        return IndiceDeCode().IsMatch(consigne)
               || EstMatiereDeCode(matiere)
               || reponses.Any(r => r is not null && ExtraitDeCode().IsMatch(r));
    }

    /// <summary>Matière de programmation : algorithmique, SLAM, développement, C#, Python, SQL…</summary>
    public static bool EstMatiereDeCode(string? matiere) =>
        !string.IsNullOrWhiteSpace(matiere) && MatiereDeCode().IsMatch(SansAccents(matiere));

    /// <summary>Minuscules sans accents, pour comparer « Réécris » et « reecris » de la même façon.</summary>
    private static string SansAccents(string texte) =>
        new string(texte.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant();

    // Verbes qui demandent de produire du code (à l'impératif ou à l'infinitif).
    [GeneratedRegex(@"\b(r?e?ecri(s|re|vez)|complete[rz]?|corrige[rz]?|modifie[rz]?|tradui(s|re|sez)|implemente[rz]?|programme[rz]?|redige[rz]?)\b")]
    private static partial Regex VerbeDeConsigne();

    // Langage ou notion de programmation cités dans la consigne, ou code entre accents graves.
    [GeneratedRegex(@"(c#|\bpython\b|\bsql\b|\bjava\b|\bphp\b|javascript|\bhtml\b|\bcss\b|\brequete|\bcode\b|\bprogramme\b|\bfonction\b|\bmethode\b|\bclasse\b|\bboucle\b|\bswitch\b|\bif\b|\bwhile\b|\bfor\b|\bselect\b|pseudo-?code|\balgorithme\b|\bscript\b|\binstruction|`[^`]+`)")]
    private static partial Regex IndiceDeCode();

    // Extrait de code dans la réponse : du code entre accents graves qui contient de la ponctuation de code.
    [GeneratedRegex(@"`[^`\n]*[;{}()=][^`\n]*`")]
    private static partial Regex ExtraitDeCode();

    [GeneratedRegex(@"(algo|slam|programmation|developpement|c#|python|\bsql\b|base de donnees)")]
    private static partial Regex MatiereDeCode();
}

/// <summary>
/// Petites règles d'édition de la zone de code, sans rien d'affichage (testables) : garder l'indentation
/// à la ligne suivante, indenter ou désindenter un groupe de lignes de 4 espaces.
/// </summary>
public static class EditionCode
{
    public const string Retrait = "    ";

    /// <summary>
    /// Ce qu'Entrée insère à la position donnée : un saut de ligne suivi de l'indentation de la ligne courante,
    /// plus un cran après une ligne qui ouvre un bloc (« { » ou « : » en fin de ligne, avant le curseur).
    /// </summary>
    public static string NouvelleLigne(string texte, int position)
    {
        int debut = DebutDeLigne(texte, position);
        string avant = texte[debut..position];
        string indentation = new(avant.TakeWhile(ch => ch is ' ' or '\t').ToArray());
        string fin = avant.TrimEnd();
        if (fin.EndsWith('{') || fin.EndsWith(':')) indentation += Retrait;
        return "\n" + indentation;
    }

    /// <summary>Plage des lignes entières touchées par la sélection (début de la première, fin de la dernière, sans le saut de ligne).</summary>
    public static (int Debut, int Fin) Lignes(string texte, int debutSelection, int longueurSelection)
    {
        int debut = DebutDeLigne(texte, debutSelection);
        int dernier = debutSelection + longueurSelection;
        // Une sélection qui s'arrête juste après un saut de ligne ne touche pas la ligne suivante.
        if (longueurSelection > 0 && dernier > 0 && texte[dernier - 1] == '\n') dernier--;
        int fin = texte.IndexOf('\n', dernier);
        if (fin < 0) fin = texte.Length;
        if (fin > 0 && fin <= texte.Length && fin > debut && texte[fin - 1] == '\r') fin--;
        return (debut, fin);
    }

    /// <summary>Ajoute 4 espaces au début de chaque ligne du bloc.</summary>
    public static string Indenter(string bloc) => string.Join("\n", bloc.Split('\n').Select(l => Retrait + l));

    /// <summary>Retire jusqu'à 4 espaces (ou une tabulation) au début de chaque ligne du bloc.</summary>
    public static string Desindenter(string bloc) => string.Join("\n", bloc.Split('\n').Select(l =>
    {
        if (l.StartsWith('\t')) return l[1..];
        int n = 0;
        while (n < Retrait.Length && n < l.Length && l[n] == ' ') n++;
        return l[n..];
    }));

    private static int DebutDeLigne(string texte, int position) =>
        position <= 0 ? 0 : texte.LastIndexOf('\n', position - 1) + 1;
}
