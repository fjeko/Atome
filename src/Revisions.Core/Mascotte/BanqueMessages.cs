using System.Text.Json;

namespace Revisions.Core.Mascotte;

/// <summary>Humeurs de la mascotte (cahier de design, section 3).</summary>
public enum Humeur
{
    /// <summary>Par défaut.</summary>
    Contente,
    /// <summary>Série en cours, devoir proche.</summary>
    Encourageante,
    /// <summary>Retour après une pause.</summary>
    Endormie,
    /// <summary>Fin de session, niveau gagné, plafond atteint.</summary>
    Fiere,
    /// <summary>Rien à réviser : « et si tu lisais un nouveau cours ? ».</summary>
    Curieuse,
}

/// <summary>Une phrase de la mascotte et l'humeur qui va avec.</summary>
public sealed record MessageMascotte(string Texte, Humeur Humeur);

/// <summary>
/// La banque de messages de la mascotte, lue dans le fichier de ressources <c>messages-mascotte.json</c>
/// (incorporé à la DLL). Chaque contexte a son humeur et 5 à 10 variantes tirées au hasard.
/// </summary>
public static class BanqueMessages
{
    // Noms des contextes, tels qu'ils sont écrits dans le fichier.
    public const string Bonjour = "bonjour";
    public const string Rebonjour = "rebonjour";
    public const string BonRetour = "bon_retour";
    public const string Serie = "serie";
    public const string DevoirProche = "devoir_proche";
    public const string PlafondAtteint = "plafond_atteint";
    public const string RienAFaire = "rien_a_faire";
    public const string FinSession = "fin_session";
    public const string NiveauGagne = "niveau_gagne";
    public const string Bibliotheque = "bibliotheque";
    public const string Statistiques = "statistiques";

    /// <summary>Les contextes et leurs variantes (chargés une seule fois).</summary>
    public static IReadOnlyDictionary<string, (Humeur Humeur, IReadOnlyList<string> Messages)> Contextes { get; } = Charger();

    private static Dictionary<string, (Humeur, IReadOnlyList<string>)> Charger()
    {
        using var flux = typeof(BanqueMessages).Assembly.GetManifestResourceStream("Mascotte/messages-mascotte.json")
                         ?? throw new InvalidOperationException("Banque de messages de la mascotte introuvable.");
        using var document = JsonDocument.Parse(flux);
        var contextes = new Dictionary<string, (Humeur, IReadOnlyList<string>)>();
        foreach (var propriete in document.RootElement.EnumerateObject())
        {
            if (propriete.Value.ValueKind != JsonValueKind.Object) continue; // « _lisezmoi »
            var humeur = Enum.Parse<Humeur>(propriete.Value.GetProperty("humeur").GetString()!, ignoreCase: true);
            var messages = propriete.Value.GetProperty("messages").EnumerateArray().Select(m => m.GetString()!).ToList();
            contextes[propriete.Name] = (humeur, messages);
        }
        return contextes;
    }

    /// <summary>
    /// Tire une phrase au hasard pour un contexte, en remplaçant les valeurs entre accolades
    /// (« {jours} » → « 9 »). Un contexte inconnu donne un simple bonjour.
    /// </summary>
    public static MessageMascotte Tirer(string contexte, Random? hasard = null, IReadOnlyDictionary<string, string>? valeurs = null)
    {
        if (!Contextes.TryGetValue(contexte, out var choix)) choix = Contextes[Bonjour];
        string texte = choix.Messages[(hasard ?? Random.Shared).Next(choix.Messages.Count)];
        if (valeurs is not null)
            foreach (var (cle, valeur) in valeurs)
                texte = texte.Replace("{" + cle + "}", valeur);
        // Une valeur placée en tête de phrase (« le DV1 approche ») prend une majuscule.
        if (texte.Length > 0 && char.IsLower(texte[0])) texte = char.ToUpper(texte[0]) + texte[1..];
        return new MessageMascotte(texte, choix.Humeur);
    }
}
