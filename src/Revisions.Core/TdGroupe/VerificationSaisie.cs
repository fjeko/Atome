using System.Globalization;

namespace Revisions.Core.TdGroupe;

/// <summary>Résultat de la vérification d'une sous-question.</summary>
/// <param name="Juste">Toute la réponse est juste.</param>
/// <param name="Cases">Pour une matrice : chaque case (ligne après ligne), juste ou non. Sinon une seule case.</param>
/// <param name="Complete">Toutes les cases étaient remplies.</param>
public sealed record ResultatVerification(bool Juste, IReadOnlyList<bool> Cases, bool Complete);

/// <summary>Vérification automatique des réponses saisies, case par case.</summary>
public static class VerificationSaisie
{
    /// <summary>
    /// Lit un nombre saisi, seulement au moment de la vérification (jamais pendant la frappe) :
    /// espaces ignorés, signe moins ordinaire « - » ou typographique « − » (copié d'un PDF), « + » toléré,
    /// virgule ou point comme séparateur décimal (« -0,5 » = « -0.5 »).
    /// Renvoie null si la case est vide ou ne contient pas un nombre complet (« - » ou « 3, » : frappe pas finie).
    /// </summary>
    public static decimal? LireNombre(string? saisie)
    {
        if (saisie is null) return null;
        string texte = saisie.Replace(" ", "").Replace(" ", "").Replace('−', '-').Replace('–', '-').Replace(',', '.');
        if (texte.Length == 0 || texte.EndsWith('.')) return null;
        return decimal.TryParse(texte, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
            out decimal valeur) ? valeur : null;
    }

    /// <summary>Lit un entier saisi (« 4,0 » compte comme 4). Renvoie null si la case est vide ou ne contient pas un entier.</summary>
    public static int? LireEntier(string? saisie) =>
        LireNombre(saisie) is { } n && n == decimal.Truncate(n) && n >= int.MinValue && n <= int.MaxValue ? (int)n : null;

    /// <summary>Caractère accepté pendant la frappe dans une case : chiffre, signe ou séparateur décimal.</summary>
    public static bool CaractereAutorise(char c) => char.IsAsciiDigit(c) || c is '-' or '−' or '–' or '+' or ',' or '.';

    /// <summary>
    /// Texte acceptable dans une case pendant la frappe ou un collage, même incomplet (« - », « -0, », « 3, ») :
    /// on filtre seulement les caractères ; la conversion en nombre attend la vérification.
    /// </summary>
    public static bool SaisieAutorisee(string? texte) =>
        texte is not null && texte.All(c => CaractereAutorise(c) || c is ' ' or ' ');

    /// <summary>Vérifie une grille : <paramref name="saisies"/> contient les cases ligne après ligne.</summary>
    public static ResultatVerification Matrice(Matrice attendue, IReadOnlyList<string?> saisies)
    {
        if (saisies.Count != attendue.Lignes * attendue.Colonnes)
            throw new ArgumentException("La grille n'a pas la dimension de la matrice attendue.", nameof(saisies));
        var cases = attendue.Coefficients().Select((valeur, k) => LireNombre(saisies[k]) == valeur).ToList();
        return new ResultatVerification(cases.All(c => c), cases, saisies.All(s => LireNombre(s) is not null));
    }

    /// <summary>Vérifie un nombre.</summary>
    public static ResultatVerification Nombre(int attendu, string? saisie)
    {
        decimal? lu = LireNombre(saisie);
        return new ResultatVerification(lu == attendu, [lu == attendu], lu is not null);
    }

    /// <summary>Vérifie un choix (index de la proposition choisie, ou null si rien n'est choisi).</summary>
    /// <summary>Réponse texte : comparée selon sa forme (voir <see cref="FormeTexte"/>).</summary>
    public static ResultatVerification Texte(ReponseTexte attendue, string? saisie)
    {
        string? lue = NormaliserTexte(saisie, attendue.Forme);
        bool juste = lue is { Length: > 0 } && new[] { attendue.Attendue }.Concat(attendue.Variantes ?? [])
            .Any(bonne => NormaliserTexte(bonne, attendue.Forme) == lue);
        return new ResultatVerification(juste, [juste], lue is { Length: > 0 });
    }

    /// <summary>
    /// Forme comparable d'une réponse texte : minuscules, sans espaces, tirets et flèches unifiés.
    /// Pour un chemin, les séparateurs disparaissent ; pour un ensemble, les lettres sont en plus triées.
    /// Les noms de sommets ou de tâches font une seule lettre, sauf « FIN » et « DEBUT » : ils sont gardés entiers.
    /// </summary>
    public static string? NormaliserTexte(string? saisie, FormeTexte forme)
    {
        if (saisie is null) return null;
        string texte = new string(saisie.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant()
            .Replace('−', '-').Replace('–', '-').Replace('—', '-').Replace("→", "-").Replace("->", "-").Replace('>', '-');
        if (forme == FormeTexte.Exacte) return texte;

        texte = texte.Replace("début", "debut");
        var morceaux = new List<string>();
        for (int i = 0; i < texte.Length; i++)
        {
            if (!char.IsLetterOrDigit(texte[i])) continue;
            string reste = texte[i..];
            string? mot = reste.StartsWith("fin") ? "fin" : reste.StartsWith("debut") ? "debut" : null;
            if (mot is not null) { morceaux.Add(mot); i += mot.Length - 1; }
            else morceaux.Add(texte[i].ToString());
        }
        if (forme == FormeTexte.Ensemble) morceaux = morceaux.Distinct().Order(StringComparer.Ordinal).ToList();
        return string.Join("-", morceaux);
    }

    public static ResultatVerification Choix(int bonne, int? choisi) =>
        new(choisi == bonne, [choisi == bonne], choisi is not null);
}

/// <summary>Bilan personnel d'un exercice, pour le récap de fin.</summary>
public enum IssueExercice
{
    /// <summary>Tout juste, sans indice.</summary>
    PremierCoup,
    /// <summary>Tout juste, après avoir regardé l'indice.</summary>
    AvecIndice,
    /// <summary>Au moins une sous-question pas tout à fait juste.</summary>
    PasEncore,
}

/// <summary>Récap de fin de TD et XP gagnée.</summary>
public static class RecapTd
{
    /// <summary>XP d'un exercice : l'essai est toujours récompensé (R1 : jamais de punition).</summary>
    public static int Xp(IssueExercice issue) => issue switch
    {
        IssueExercice.PremierCoup => 6,
        IssueExercice.AvecIndice => 4,
        _ => 2,
    };

    /// <summary>XP du TD entier.</summary>
    public static int Xp(IEnumerable<IssueExercice> issues) => issues.Sum(Xp);

    /// <summary>Petite phrase à dire à l'oral pour comparer : « J'ai fait 7/10, dont 2 avec un indice. »</summary>
    public static string Phrase(IReadOnlyList<IssueExercice> issues)
    {
        int reussis = issues.Count(i => i != IssueExercice.PasEncore);
        int avecIndice = issues.Count(i => i == IssueExercice.AvecIndice);
        string phrase = $"J'ai fait {reussis}/{issues.Count}";
        if (avecIndice > 0) phrase += avecIndice == 1 ? ", dont 1 avec un indice" : $", dont {avecIndice} avec un indice";
        return phrase + ".";
    }
}
