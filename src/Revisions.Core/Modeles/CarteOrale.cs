namespace Revisions.Core.Modeles;

/// <summary>
/// Cartes « à dire à voix haute » (anglais oral : « 🎙️ Réponds à voix haute : … »). Pour elles, la zone
/// « Ta réponse » est masquée : on répond en parlant, puis on regarde le modèle de réponse.
/// </summary>
public static class CarteOrale
{
    public static bool Est(Carte carte)
    {
        string texte = (carte.Contenu.Recto ?? carte.Contenu.Enonce ?? "").TrimStart();
        return texte.StartsWith("🎙", StringComparison.Ordinal)
            || texte.Contains("à voix haute", StringComparison.OrdinalIgnoreCase);
    }
}
