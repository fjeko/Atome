using System.Text;

namespace Revisions.Core.TdGroupe;

/// <summary>
/// Tout ce qu'il faut pour générer un TD : c'est exactement ce que contient le code partagé.
/// </summary>
/// <param name="Type">Préfixe du type de TD (« MAT » pour les matrices, « SEA » pour une séance).</param>
/// <param name="Version">Version du générateur qui a créé le code.</param>
/// <param name="Niveau">Niveau de difficulté, de 1 à 3.</param>
/// <param name="Nombre">Nombre d'exercices.</param>
/// <param name="Graine">Graine du hasard (0 à 9999).</param>
/// <param name="PackId">Pour un TD « séance » : l'identifiant du pack (sinon null).</param>
public sealed record ParametresTd(string Type, int Version, int Niveau, int Nombre, int Graine, string? PackId = null)
{
    /// <summary>
    /// Graine du générateur aléatoire : la graine du code, le niveau et le nombre d'exercices réunis sur 64 bits,
    /// pour que deux TD de même graine mais de niveaux différents n'aient pas les mêmes tirages.
    /// NE PAS MODIFIER : tous les TD déjà partagés changeraient.
    /// </summary>
    public ulong GraineInterne => ((ulong)(uint)Graine << 16) | ((ulong)(uint)Niveau << 8) | (uint)Nombre;
}

/// <summary>Un TD généré : la liste des exercices à faire ensemble.</summary>
/// <param name="Code">Code à dicter aux camarades.</param>
/// <param name="Titre">Titre affiché (« Matrices · niveau 2 »).</param>
/// <param name="Empreinte">Deux mots calculés à partir du contenu : si tout le monde lit les mêmes, c'est le même TD.</param>
public sealed record Td(ParametresTd Parametres, string Code, string Titre, IReadOnlyList<ExerciceTd> Exercices, string Empreinte);

/// <summary>Un exercice : un énoncé et ses sous-questions a), b), c).</summary>
/// <param name="Titre">Titre court (« Produit de deux matrices »).</param>
/// <param name="Enonce">Énoncé commun aux sous-questions (Markdown + LaTeX).</param>
/// <param name="Indice">Rappel de méthode, montré seulement si on le demande (Markdown + LaTeX).</param>
/// <param name="SectionId">Section du cours à revoir (« Revoir la notion »), ou null.</param>
public sealed record ExerciceTd(string Titre, string Enonce, IReadOnlyList<QuestionTd> Questions, string Indice, string? SectionId);

/// <summary>Une sous-question.</summary>
/// <param name="Lettre">« a », « b », « c »…</param>
/// <param name="Intitule">Texte de la question (Markdown + LaTeX).</param>
/// <param name="Libelle">Rappel très court en texte simple, affiché à côté de la zone de réponse (« A + B »).</param>
/// <param name="Attendu">Bonne réponse et forme de la saisie (grille, nombre, choix…).</param>
/// <param name="Correction">Correction pas à pas : une étape par appui sur Espace (Markdown + LaTeX).</param>
public sealed record QuestionTd(string Lettre, string Intitule, string Libelle, ReponseAttendue Attendu, IReadOnlyList<string> Correction);

/// <summary>Forme de la réponse attendue. Chaque forme a sa zone de saisie à l'écran.</summary>
public abstract record ReponseAttendue
{
    /// <summary>Réponse écrite en texte simple (pour la comparaison et l'empreinte).</summary>
    public abstract string Texte { get; }
}

/// <summary>Une matrice, saisie dans une grille à la bonne dimension.</summary>
/// <param name="EnTetesLignes">Noms affichés devant chaque ligne (sommets « A », « B »…), ou null.</param>
/// <param name="EnTetesColonnes">Noms affichés au-dessus de chaque colonne (tâches, sommets…), ou null.</param>
public sealed record ReponseMatrice(Matrice Valeur, IReadOnlyList<string>? EnTetesLignes = null, IReadOnlyList<string>? EnTetesColonnes = null) : ReponseAttendue
{
    public override string Texte => Valeur.ToString();
}

/// <summary>Un nombre entier, saisi dans un champ simple.</summary>
public sealed record ReponseNombre(int Valeur) : ReponseAttendue
{
    public override string Texte => Valeur.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Un choix parmi quelques propositions (« Oui / Non », une dimension, un QCM de pack).</summary>
/// <param name="Choix">Propositions (Markdown court ; texte simple pour les générateurs).</param>
/// <param name="Bonne">Index de la bonne proposition.</param>
public sealed record ReponseChoix(IReadOnlyList<string> Choix, int Bonne) : ReponseAttendue
{
    public override string Texte => Choix[Bonne];
}

/// <summary>
/// Réponse rédigée, impossible à vérifier automatiquement (exercice d'un pack) :
/// on montre la correction et chacun dit lui-même s'il avait juste.
/// </summary>
public sealed record ReponseLibre(string Reponse) : ReponseAttendue
{
    public override string Texte => Reponse;
}

/// <summary>Façon de comparer une réponse texte.</summary>
public enum FormeTexte
{
    /// <summary>Caractère par caractère, sans tenir compte des espaces ni des majuscules (« 192.168.1.0 », « rwxr-x--- »).</summary>
    Exacte,
    /// <summary>Suite de sommets ou de tâches : séparateurs ignorés, l'ordre compte (« A-C-F » = « A C F » = « ACF »).</summary>
    Chemin,
    /// <summary>Ensemble de lettres : séparateurs et ordre ignorés (« B, D » = « D B »).</summary>
    Ensemble,
}

/// <summary>Réponse courte vérifiée automatiquement : adresse IP, droits Linux, chemin, ensemble de sommets.</summary>
/// <param name="Attendue">Bonne réponse, telle qu'on l'écrit dans la correction.</param>
/// <param name="Forme">Façon de comparer.</param>
/// <param name="Exemple">Exemple de forme attendue, affiché dans la zone vide (« 192.168.0.0 »), ou null.</param>
/// <param name="Variantes">Autres écritures acceptées (« -rwxr-x--- » pour « rwxr-x--- »), ou null.</param>
public sealed record ReponseTexte(string Attendue, FormeTexte Forme = FormeTexte.Exacte, string? Exemple = null, IReadOnlyList<string>? Variantes = null)
    : ReponseAttendue
{
    public override string Texte => Attendue;
}

/// <summary>
/// Empreinte d'un TD : deux mots faciles à lire à l'oral, calculés à partir de tout son contenu.
/// Si deux camarades ont le même code mais pas la même empreinte, leurs contenus diffèrent
/// (par exemple un pack de séance qui n'est pas dans la même version).
/// </summary>
public static class EmpreinteTd
{
    // 64 mots : 64 × 64 = 4 096 empreintes possibles. NE PAS MODIFIER (les empreintes changeraient).
    private static readonly string[] Mots =
    [
        "abricot", "baleine", "cactus", "dauphin", "éclair", "fougère", "girafe", "hibou",
        "igloo", "jaguar", "kiwi", "lama", "mangue", "narval", "olive", "panda",
        "quartz", "renard", "sapin", "tortue", "ukulélé", "volcan", "wapiti", "yaourt",
        "zèbre", "ananas", "bison", "cerise", "dragon", "étoile", "flamant", "gecko",
        "hérisson", "iris", "jonquille", "koala", "lotus", "marmotte", "noisette", "orque",
        "pélican", "quiche", "radis", "saumon", "tulipe", "vanille", "yéti", "zénith",
        "airelle", "boussole", "comète", "dune", "érable", "falaise", "glacier", "hamac",
        "lagon", "myrtille", "nuage", "orage", "pivoine", "rivière", "savane", "tonnerre",
    ];

    /// <summary>Calcule l'empreinte (FNV-1a 32 bits sur le texte UTF-8 de tous les exercices).</summary>
    public static string Calculer(IEnumerable<ExerciceTd> exercices)
    {
        var texte = new StringBuilder();
        foreach (var e in exercices)
        {
            texte.Append(e.Enonce).Append('\u001F');
            foreach (var q in e.Questions) texte.Append(q.Intitule).Append('\u001F').Append(q.Attendu.Texte).Append('\u001E');
        }
        uint hash = 2166136261;
        foreach (byte octet in Encoding.UTF8.GetBytes(texte.ToString()))
        {
            hash ^= octet;
            hash = unchecked(hash * 16777619);
        }
        return $"{Mots[hash % 64]}-{Mots[(hash >> 6) % 64]}";
    }
}
