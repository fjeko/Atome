using Revisions.Core.Temps;

namespace Revisions.Core.Planning;

/// <summary>
/// Planning proposé par le CNED (voir planning/FORMAT.md) : des périodes de travail par matière et des devoirs.
/// C'est un repère, jamais une contrainte : rien dans l'application ne reproche de ne pas le suivre.
/// </summary>
public sealed record PlanningCned(
    string Id,
    int FormatVersion,
    string Titre,
    IReadOnlyList<Periode> Periodes,
    IReadOnlyList<Devoir> Devoirs)
{
    /// <summary>Matières du planning, dans l'ordre où elles apparaissent dans le fichier.</summary>
    public IReadOnlyList<string> Matieres =>
        Periodes.Select(p => p.Matiere).Concat(Devoirs.Select(d => d.Matiere)).Distinct().ToList();
}

/// <summary>
/// Une période de travail : du lundi <see cref="Debut"/> au dimanche <see cref="Fin"/> inclus.
/// </summary>
/// <param name="Cle">Identifiant stable dans le planning (« periode:matière|libellé ») : sert aux surcharges.</param>
/// <param name="Cibles">Préfixes d'id de pack. Un pack est concerné si son id est égal au préfixe ou commence par « préfixe- ».</param>
/// <param name="Approx">Fin de période déduite (à confirmer).</param>
/// <param name="Hypothese">Répartition supposée (à confirmer).</param>
/// <param name="Continu">Travail au fil de l'année (anglais) : un devoir de la matière porte sur tout.</param>
public sealed record Periode(
    string Cle,
    string Matiere,
    string Libelle,
    DateOnly Debut,
    DateOnly Fin,
    IReadOnlyList<string> Cibles,
    bool Approx = false,
    bool Hypothese = false,
    bool Continu = false)
{
    /// <summary>Les dates ne sont pas sûres : l'interface affiche « à confirmer ».</summary>
    public bool AConfirmer => Approx || Hypothese;

    /// <summary>
    /// Vrai si l'id de pack est concerné par l'un des préfixes.
    /// <paramref name="longueurPrefixe"/> reçoit la longueur du préfixe le plus long qui correspond.
    /// </summary>
    public bool Concerne(string packId, out int longueurPrefixe)
    {
        longueurPrefixe = -1;
        foreach (string prefixe in Cibles)
            if (CorrespondPrefixe(packId, prefixe) && prefixe.Length > longueurPrefixe)
                longueurPrefixe = prefixe.Length;
        return longueurPrefixe >= 0;
    }

    /// <summary>« b1-b16 » concerne « b1-b16 » et « b1-b16-sq1-s01 », mais pas « b1-b161 ».</summary>
    public static bool CorrespondPrefixe(string packId, string prefixe) =>
        prefixe.Length > 0 &&
        (packId == prefixe || packId.StartsWith(prefixe + "-", StringComparison.Ordinal));
}

/// <summary>Un devoir du CNED, daté par sa semaine.</summary>
/// <param name="Cle">Identifiant stable dans le planning (« devoir:matière|code ») : sert aux surcharges.</param>
/// <param name="Code">QZ1, DV1…</param>
/// <param name="Note">Vrai pour un devoir noté (DV), faux pour un quiz d'auto-évaluation (QZ).</param>
/// <param name="Semaine">Lundi de la semaine du devoir.</param>
public sealed record Devoir(
    string Cle,
    string Matiere,
    string Code,
    bool Note,
    DateOnly Semaine,
    string? Remarque = null)
{
    /// <summary>Rendu attendu au plus tard le dimanche de la semaine.</summary>
    public DateOnly DateLimite => JourDeRevision.LundiDe(Semaine).AddDays(6);

    public bool EstQuiz => !Note;
}
