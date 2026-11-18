namespace Revisions.Core.Mascotte;

/// <summary>Ce que l'accueil sait de la journée, pour choisir ce que dit la mascotte.</summary>
/// <param name="RetourApresPause">Premier jour de retour après une pause (mode reprise).</param>
/// <param name="JoursAbsence">Jours depuis la dernière révision (pour « ça fait 9 jours »).</param>
/// <param name="PlafondAtteint">Le plafond de cartes du jour est atteint.</param>
/// <param name="RienAFaire">Aucune carte à réviser maintenant.</param>
/// <param name="DejaOuvertAujourdhui">L'application a déjà été ouverte aujourd'hui.</param>
/// <param name="PremiereOuvertureDeLaSemaine">Aucune révision encore cette semaine.</param>
/// <param name="SerieSemaines">Série en cours, en semaines.</param>
/// <param name="ProchainDevoir">Libellé du prochain devoir (« le DV1 de maths »), ou null.</param>
/// <param name="JoursAvantDevoir">Jours avant sa date limite.</param>
public sealed record SituationAccueil(
    bool RetourApresPause,
    int JoursAbsence,
    bool PlafondAtteint,
    bool RienAFaire,
    bool DejaOuvertAujourdhui,
    bool PremiereOuvertureDeLaSemaine,
    int SerieSemaines,
    string? ProchainDevoir,
    int JoursAvantDevoir);

/// <summary>
/// Choisit le contexte du message de la mascotte à l'accueil. Ordre de priorité : bon retour après une pause,
/// plafond atteint, rien à faire, devoir dans la semaine, série (au premier passage de la semaine),
/// re-bonjour (deuxième ouverture du jour), puis un simple bonjour.
/// </summary>
public static class ContexteAccueil
{
    public static (string Contexte, IReadOnlyDictionary<string, string> Valeurs) Choisir(SituationAccueil s)
    {
        var valeurs = new Dictionary<string, string>
        {
            ["jours"] = s.JoursAbsence.ToString(),
            ["semaines"] = s.SerieSemaines.ToString(),
            ["devoir"] = s.ProchainDevoir ?? "",
        };
        string contexte =
            s.RetourApresPause ? BanqueMessages.BonRetour
            : s.PlafondAtteint ? BanqueMessages.PlafondAtteint
            : s.RienAFaire ? BanqueMessages.RienAFaire
            : s.ProchainDevoir is not null && s.JoursAvantDevoir is >= 0 and <= 7 && !s.DejaOuvertAujourdhui ? BanqueMessages.DevoirProche
            : s.SerieSemaines >= 2 && s.PremiereOuvertureDeLaSemaine ? BanqueMessages.Serie
            : s.DejaOuvertAujourdhui ? BanqueMessages.Rebonjour
            : BanqueMessages.Bonjour;
        return (contexte, valeurs);
    }
}
