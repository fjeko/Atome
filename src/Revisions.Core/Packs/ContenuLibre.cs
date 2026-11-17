namespace Revisions.Core.Packs;

/// <summary>Ce qu'Atome doit faire des cours libres intégrés au démarrage.</summary>
public enum ActionCoursLibres
{
    /// <summary>Rien de nouveau (choix déjà fait, ou rien à proposer).</summary>
    Aucune,
    /// <summary>Aucun pack : proposer de charger les cours libres (Oui / Plus tard).</summary>
    Proposer,
    /// <summary>Des packs privés existent déjà : cours libres désactivés par défaut, sans rien importer.</summary>
    DesactiverParDefaut,
}

/// <summary>Règles sur les cours libres (packs CC BY-SA livrés avec Atome, dossier « contenu-libre »).</summary>
public static class ContenuLibre
{
    /// <summary>Tous les identifiants de packs libres commencent ainsi (ids stables : la progression survit aux mises à jour).</summary>
    public const string PrefixeId = "libre-";

    /// <summary>Décision au démarrage.</summary>
    /// <param name="choix">Réglage « cours libres » : null tant que rien n'est décidé.</param>
    /// <param name="nombrePacksPrives">Nombre de packs déjà présents en base (hors cours libres).</param>
    /// <param name="contenuDisponible">Le dossier des cours libres existe à côté de l'application.</param>
    public static ActionCoursLibres Evaluer(bool? choix, int nombrePacksPrives, bool contenuDisponible)
    {
        if (choix is not null || !contenuDisponible) return ActionCoursLibres.Aucune;
        return nombrePacksPrives == 0 ? ActionCoursLibres.Proposer : ActionCoursLibres.DesactiverParDefaut;
    }
}
