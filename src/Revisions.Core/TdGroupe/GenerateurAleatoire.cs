namespace Revisions.Core.TdGroupe;

/// <summary>
/// Générateur de nombres pseudo-aléatoires « maison », entièrement déterministe :
/// la même graine donne toujours la même suite de nombres, sur n'importe quel PC et avec n'importe quelle
/// version de .NET. C'est indispensable pour le TD en groupe : chacun génère le TD sur sa machine,
/// et tout le monde doit obtenir exactement les mêmes exercices.
/// </summary>
/// <remarks>
/// <para>
/// On n'utilise pas <see cref="System.Random"/> : Microsoft ne garantit pas que son algorithme reste le même
/// d'une version de .NET à l'autre (il a déjà changé en .NET 6).
/// </para>
/// <para>
/// Algorithme : <b>SplitMix64</b> (Steele, Lea et Flood, 2014), celui qui sert à initialiser les générateurs
/// de Java et de la famille xoshiro. À chaque appel, on ajoute une constante fixe à l'état (64 bits), puis on
/// « mélange » les bits du résultat par deux multiplications et trois décalages. Calcul sur des entiers non
/// signés 64 bits, débordements compris : aucune virgule flottante, donc aucun écart possible entre processeurs.
/// </para>
/// <para>
/// Ne JAMAIS modifier cette classe : changer un seul nombre ici changerait tous les TD déjà partagés.
/// Les tests fixent les premières valeurs produites pour le détecter.
/// </para>
/// </remarks>
public sealed class GenerateurAleatoire
{
    private ulong _etat;

    /// <summary>Crée un générateur à partir d'une graine (le même nombre redonne la même suite).</summary>
    public GenerateurAleatoire(ulong graine) => _etat = graine;

    /// <summary>Nombre suivant sur 64 bits (SplitMix64).</summary>
    public ulong Suivant()
    {
        unchecked
        {
            _etat += 0x9E3779B97F4A7C15UL; // partie fractionnaire du nombre d'or × 2^64
            ulong z = _etat;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Entier entre <paramref name="min"/> et <paramref name="max"/> inclus, sans biais.
    /// On rejette les tirages de la « zone de trop » (méthode du rejet) plutôt que d'utiliser
    /// un simple modulo, qui favoriserait légèrement les petites valeurs.
    /// </summary>
    public int Entier(int min, int max)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max), "max doit être supérieur ou égal à min.");
        ulong etendue = (ulong)((long)max - min) + 1;
        ulong limite = ulong.MaxValue - (ulong.MaxValue % etendue); // multiple de l'étendue
        ulong tirage;
        do tirage = Suivant();
        while (tirage >= limite);
        return (int)((long)min + (long)(tirage % etendue));
    }

    /// <summary>Entier entre min et max inclus, différent de zéro (utile pour un coefficient « k »).</summary>
    public int EntierNonNul(int min, int max)
    {
        int valeur;
        do valeur = Entier(min, max);
        while (valeur == 0);
        return valeur;
    }

    /// <summary>Vrai ou faux, une chance sur deux.</summary>
    public bool PileOuFace() => Entier(0, 1) == 1;

    /// <summary>Un élément de la liste, au hasard.</summary>
    public T Choisir<T>(IReadOnlyList<T> liste) => liste[Entier(0, liste.Count - 1)];

    /// <summary>Mélange la liste sur place (algorithme de Fisher-Yates, du dernier élément vers le premier).</summary>
    public void Melanger<T>(IList<T> liste)
    {
        for (int i = liste.Count - 1; i > 0; i--)
        {
            int j = Entier(0, i);
            (liste[i], liste[j]) = (liste[j], liste[i]);
        }
    }
}
