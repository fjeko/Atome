namespace Revisions.Core.TdGroupe;

/// <summary>
/// Un générateur de TD (matrices, séance d'un pack, et plus tard graphes, ordonnancement…).
/// Règle d'or : <see cref="Generer"/> est déterministe. Mêmes paramètres = même TD, sur tous les PC.
/// Pour changer la façon de générer, on augmente <see cref="VersionActuelle"/> et on garde l'ancienne
/// façon tant qu'elle figure dans <see cref="VersionsPrisesEnCharge"/>.
/// </summary>
public interface IGenerateurTd
{
    /// <summary>Préfixe du code (« MAT »), en majuscules.</summary>
    string Prefixe { get; }

    /// <summary>Nom affiché (« Matrices (SQ1 Calcul matriciel) »).</summary>
    string Nom { get; }

    /// <summary>Version utilisée pour créer un nouveau TD.</summary>
    int VersionActuelle { get; }

    /// <summary>Versions que ce générateur sait encore reproduire à l'identique.</summary>
    IReadOnlyCollection<int> VersionsPrisesEnCharge { get; }

    /// <summary>Génère le TD. Peut lever <see cref="TdImpossibleException"/> (pack absent, par exemple).</summary>
    Td Generer(ParametresTd parametres);
}

/// <summary>Le TD ne peut pas être généré sur ce PC (message prêt à afficher).</summary>
public sealed class TdImpossibleException(string message) : Exception(message);

/// <summary>Résultat de l'ouverture d'un code.</summary>
/// <param name="Td">Le TD, ou null en cas de problème.</param>
/// <param name="Erreur">Message clair à afficher en cas de problème.</param>
/// <param name="MiseAJourNecessaire">Le code vient d'une version d'Atome plus récente : il faut mettre Atome à jour.</param>
public sealed record OuvertureTd(Td? Td, string? Erreur, bool MiseAJourNecessaire = false);

/// <summary>Liste des générateurs connus : crée un TD ou ouvre un code reçu.</summary>
public sealed class CatalogueTd(IEnumerable<IGenerateurTd> generateurs)
{
    private readonly IReadOnlyList<IGenerateurTd> _generateurs = generateurs.ToList();

    public IReadOnlyList<IGenerateurTd> Generateurs => _generateurs;

    public IGenerateurTd? Trouver(string prefixe) =>
        _generateurs.FirstOrDefault(g => string.Equals(g.Prefixe, prefixe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Crée un nouveau TD avec la version actuelle du générateur.</summary>
    public Td Creer(IGenerateurTd generateur, int niveau, int nombre, int graine, string? packId = null) =>
        generateur.Generer(new ParametresTd(generateur.Prefixe, generateur.VersionActuelle, niveau, nombre, graine, packId));

    /// <summary>Ouvre un code dicté par un camarade : lecture, contrôle de version, puis génération.</summary>
    public OuvertureTd Ouvrir(string? code)
    {
        var (p, erreur) = CodeTd.Lire(code);
        if (p is null) return new OuvertureTd(null, erreur);

        var generateur = Trouver(p.Type);
        if (generateur is null)
            return new OuvertureTd(null,
                $"Ce type de TD ({p.Type}) n'existe pas dans ta version d'Atome. Mets Atome à jour (Réglages → Vérifier maintenant).",
                MiseAJourNecessaire: true);

        if (!generateur.VersionsPrisesEnCharge.Contains(p.Version))
        {
            return p.Version > generateur.VersionActuelle
                ? new OuvertureTd(null,
                    $"Ce code a été créé avec une version d'Atome plus récente que la tienne. Mets Atome à jour " +
                    "(Réglages → Vérifier maintenant) pour avoir exactement le même TD que les autres.",
                    MiseAJourNecessaire: true)
                : new OuvertureTd(null,
                    "Ce code vient d'une ancienne version d'Atome, qui générait les exercices autrement. " +
                    "Demande à ton camarade de mettre Atome à jour, puis de créer un nouveau code.");
        }

        try
        {
            return new OuvertureTd(generateur.Generer(p), null);
        }
        catch (TdImpossibleException exception)
        {
            return new OuvertureTd(null, exception.Message);
        }
    }

    /// <summary>Graine au hasard pour un nouveau TD (le seul endroit où le « vrai » hasard intervient).</summary>
    public static int NouvelleGraine() => Random.Shared.Next(0, CodeTd.GraineMax + 1);
}
