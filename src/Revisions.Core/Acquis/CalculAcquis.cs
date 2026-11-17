using System.Text;
using Revisions.Core.Modeles;

namespace Revisions.Core.Acquis;

/// <summary>Où en est l'étudiant d'une partie du cours, hors de l'application.</summary>
public enum Avancement { PasVu, Vu, Revise }

/// <summary>Compréhension ressentie, de la plus fragile à la plus solide.</summary>
public enum Comprehension { Decouverte, Fragile, Correct, Solide }

/// <summary>
/// Ce qui a été réglé sur un niveau de l'arbre (matière, partie, séquence ou séance).
/// Une valeur null veut dire « rien de réglé ici » : on hérite du niveau parent.
/// </summary>
public sealed record ReglageAcquis(Avancement? Avancement, Comprehension? Comprehension);

/// <summary>Acquis effectif d'une séance, une fois l'héritage résolu.</summary>
/// <param name="Avancement">Pas vu, vu ou révisé.</param>
/// <param name="Comprehension">Compréhension réglée, ou null si aucune (voir <see cref="CalculAcquis.ComprehensionEffective"/>).</param>
public sealed record AcquisEffectif(Avancement Avancement, Comprehension? Comprehension);

/// <summary>
/// État de départ d'une carte qui n'a encore aucun historique.
/// Stabilite = null : la carte reste « nouvelle ». Prioritaire : elle passe avant les autres nouvelles cartes.
/// </summary>
public sealed record EtatDepart(bool Prioritaire, double? Stabilite, double? Difficulte, DateOnly? Echeance, DateOnly? DerniereRevision)
{
    public static readonly EtatDepart Nouvelle = new(false, null, null, null, null);
}

/// <summary>
/// Logique des « acquis » : héritage des réglages dans l'arbre, et état FSRS de départ des cartes jamais révisées.
/// </summary>
public static class CalculAcquis
{
    /// <summary>Sépare les niveaux dans le chemin enregistré en base (caractère qui n'apparaît jamais dans un titre).</summary>
    public const char Separateur = '\u001f';

    /// <summary>Plages d'intervalle (en jours) selon la compréhension. L'intervalle de chaque carte y est tiré
    /// de façon stable, ce qui étale les échéances au lieu de les empiler le même jour.</summary>
    private static readonly Dictionary<Comprehension, (int Min, int Max, double Difficulte)> Plages = new()
    {
        [Comprehension.Fragile] = (1, 2, 6.0),
        [Comprehension.Correct] = (5, 9, 5.0),   // autour de 7 jours
        [Comprehension.Solide] = (16, 26, 4.0),  // autour de 21 jours
    };

    /// <summary>Chemin enregistré en base pour une suite de clés de niveaux.</summary>
    public static string Chemin(IEnumerable<string> cles) => string.Join(Separateur, cles);

    /// <summary>
    /// Résout l'héritage : pour chacune des deux valeurs (avancement, compréhension), le réglage le plus profond
    /// sur le chemin gagne. Un enfant réglé individuellement garde donc sa propre valeur.
    /// </summary>
    public static AcquisEffectif Resoudre(IReadOnlyDictionary<string, ReglageAcquis> reglages, IReadOnlyList<string> cles)
    {
        Avancement? avancement = null;
        Comprehension? comprehension = null;
        var chemin = new StringBuilder();
        for (int i = 0; i < cles.Count; i++)
        {
            if (i > 0) chemin.Append(Separateur);
            chemin.Append(cles[i]);
            if (reglages.TryGetValue(chemin.ToString(), out var r))
            {
                avancement = r.Avancement ?? avancement;
                comprehension = r.Comprehension ?? comprehension;
            }
        }
        return new AcquisEffectif(avancement ?? Avancement.PasVu, comprehension);
    }

    /// <summary>
    /// Compréhension à utiliser quand l'étudiant n'en a pas indiqué : « vu » = je découvre, « révisé » = fragile.
    /// Null pour « pas vu » (la compréhension n'a pas de sens).
    /// </summary>
    public static Comprehension? ComprehensionEffective(AcquisEffectif acquis) => acquis.Avancement switch
    {
        Avancement.PasVu => null,
        Avancement.Vu => acquis.Comprehension ?? Comprehension.Decouverte,
        _ => acquis.Comprehension ?? Comprehension.Fragile,
    };

    /// <summary>
    /// État de départ d'une carte jamais révisée.
    /// Pas vu : nouvelle carte. Je découvre : nouvelle carte mais prioritaire.
    /// Fragile, correct, solide : état FSRS de départ avec une échéance étalée.
    /// </summary>
    public static EtatDepart EtatInitial(AcquisEffectif acquis, string carteId, DateOnly jour)
    {
        switch (ComprehensionEffective(acquis))
        {
            case null: return EtatDepart.Nouvelle;
            case Comprehension.Decouverte: return EtatDepart.Nouvelle with { Prioritaire = true };
        }
        var (min, max, difficulte) = Plages[ComprehensionEffective(acquis)!.Value];
        int jours = min + (int)(Hachage(carteId) % (uint)(max - min + 1));
        // À 90 % de rétention, l'intervalle d'une carte est égal à sa stabilité : on prend S = intervalle.
        return new EtatDepart(false, jours, difficulte, jour.AddDays(jours), jour);
    }

    /// <summary>Empreinte stable d'un texte (FNV-1a) : le même identifiant donne toujours le même nombre.</summary>
    private static uint Hachage(string texte)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(texte)) h = (h ^ b) * 16777619;
        return h;
    }
}
