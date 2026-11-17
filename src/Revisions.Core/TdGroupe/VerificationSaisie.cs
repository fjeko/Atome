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
    /// Lit un entier saisi : espaces ignorés, signe moins ordinaire « - » ou typographique « − » (copié d'un PDF), « + » toléré.
    /// Renvoie null si la case est vide ou ne contient pas un entier.
    /// </summary>
    public static int? LireEntier(string? saisie)
    {
        if (saisie is null) return null;
        string texte = saisie.Replace(" ", "").Replace(" ", "").Replace('−', '-').Replace('–', '-');
        if (texte.Length == 0) return null;
        return int.TryParse(texte, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int valeur) ? valeur : null;
    }

    /// <summary>Caractère accepté pendant la frappe dans une case : chiffre ou signe.</summary>
    public static bool CaractereAutorise(char c) => char.IsAsciiDigit(c) || c is '-' or '−' or '+';

    /// <summary>Vérifie une grille : <paramref name="saisies"/> contient les cases ligne après ligne.</summary>
    public static ResultatVerification Matrice(Matrice attendue, IReadOnlyList<string?> saisies)
    {
        if (saisies.Count != attendue.Lignes * attendue.Colonnes)
            throw new ArgumentException("La grille n'a pas la dimension de la matrice attendue.", nameof(saisies));
        var cases = attendue.Coefficients().Select((valeur, k) => LireEntier(saisies[k]) == valeur).ToList();
        return new ResultatVerification(cases.All(c => c), cases, saisies.All(s => LireEntier(s) is not null));
    }

    /// <summary>Vérifie un nombre.</summary>
    public static ResultatVerification Nombre(int attendu, string? saisie)
    {
        int? lu = LireEntier(saisie);
        return new ResultatVerification(lu == attendu, [lu == attendu], lu is not null);
    }

    /// <summary>Vérifie un choix (index de la proposition choisie, ou null si rien n'est choisi).</summary>
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
