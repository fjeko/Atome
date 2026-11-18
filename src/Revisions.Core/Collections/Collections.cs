using System.Text;
using Revisions.Core.Packs;

namespace Revisions.Core.Collections;

/// <summary>Une collection de packs, avec ses compteurs, pour les Réglages et les filtres de la Bibliothèque.</summary>
/// <param name="Cle">Identifiant stable (« libres », « mes-packs », « manuel » ou « importe:… »).</param>
/// <param name="Nom">Nom affiché.</param>
/// <param name="NbCours">Nombre de cours (packs).</param>
/// <param name="NbCartes">Nombre de cartes (hors cartes retirées par une mise à jour d'un pack).</param>
/// <param name="Visible">Vrai si la collection est affichée et révisée.</param>
public sealed record InfoCollection(string Cle, string Nom, int NbCours, int NbCartes, bool Visible);

/// <summary>
/// Règles sur les collections. Chaque pack appartient à une collection, déduite de sa provenance :
/// cours libres livrés avec Atome, dossier de packs de l'étudiant, fichier .atome importé, import manuel.
/// </summary>
public static class Collections
{
    public const string Libres = "libres";
    public const string MesPacks = "mes-packs";
    public const string Manuel = "manuel";
    private const string PrefixeImporte = "importe:";

    public const string NomLibres = "Cours libres";
    public const string NomMesPacks = "Mes packs";
    public const string NomManuel = "Imports manuels";

    /// <summary>Phrase affichée quand l'étudiant essaie de masquer la dernière collection visible.</summary>
    public const string MessageDerniere =
        "Il faut garder au moins une collection active : sinon Atome n'aurait plus rien à te montrer. Active-en une autre avant de couper celle-ci.";

    /// <summary>
    /// Vrai si des cours existent mais que toutes les collections qui en contiennent sont masquées
    /// (l'accueil propose alors de rouvrir les Réglages plutôt que de dire « rien à réviser »).
    /// </summary>
    public static bool ToutesMasquees(IEnumerable<InfoCollection> collections)
    {
        var avecCours = collections.Where(c => c.NbCours > 0).ToList();
        return avecCours.Count > 0 && avecCours.All(c => !c.Visible);
    }

    /// <summary>Clé d'une collection créée par l'import d'un fichier .atome (« importe:nom-du-fichier »).</summary>
    public static string CleImportee(string nom)
    {
        var sb = new StringBuilder();
        foreach (char c in nom.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        string slug = sb.ToString().Trim('-');
        return PrefixeImporte + (slug.Length > 0 ? slug : "sans-nom");
    }

    /// <summary>Nom affiché d'un contenu importé : l'auteur si le manifeste le donne, sinon le nom du fichier.</summary>
    public static string NomImporte(string? auteur, string nomFichier) =>
        !string.IsNullOrWhiteSpace(auteur) ? $"Contenu de {auteur.Trim()}" : $"Importé : {Path.GetFileNameWithoutExtension(nomFichier)}";

    /// <summary>
    /// Collection d'un pack à son import. Un cours libre (id « libre-… ») est toujours dans « Cours libres ».
    /// Un pack déjà connu garde sa collection (réimporter ne le déplace pas) ; sinon on prend celle de l'import,
    /// et à défaut « Mes packs ».
    /// </summary>
    public static string Resoudre(string packId, string? collectionExistante, string? collectionImport)
    {
        if (packId.StartsWith(ContenuLibre.PrefixeId, StringComparison.Ordinal)) return Libres;
        return collectionExistante ?? collectionImport ?? MesPacks;
    }

    /// <summary>Nom à afficher quand seule la clé est connue (collections créées avant les noms personnalisés).</summary>
    public static string NomParDefaut(string cle) => cle switch
    {
        Libres => NomLibres,
        MesPacks => NomMesPacks,
        Manuel => NomManuel,
        _ => cle.StartsWith(PrefixeImporte, StringComparison.Ordinal) ? "Importé : " + cle[PrefixeImporte.Length..] : cle,
    };

    /// <summary>Rang d'affichage : cours libres, mes packs, contenus importés (alphabétique), imports manuels.</summary>
    public static IOrderedEnumerable<InfoCollection> Trier(IEnumerable<InfoCollection> collections) =>
        collections.OrderBy(c => c.Cle switch { Libres => 0, MesPacks => 1, Manuel => 3, _ => 2 })
                   .ThenBy(c => c.Nom, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// Ajoute la ligne « Cours libres » (masquée) quand ils sont livrés avec Atome mais pas encore chargés en base :
    /// l'étudiant peut ainsi les activer depuis les Réglages.
    /// </summary>
    public static List<InfoCollection> AvecLibresAbsents(IEnumerable<InfoCollection> collections, int nbCoursLivres)
    {
        var liste = collections.ToList();
        if (nbCoursLivres > 0 && liste.All(c => c.Cle != Libres))
            liste.Add(new InfoCollection(Libres, NomLibres, nbCoursLivres, 0, false));
        return Trier(liste).ToList();
    }

    /// <summary>
    /// Peut-on masquer cette collection ? Non si elle est la seule à rester réellement visible
    /// (visible ET contenant au moins un cours : une collection vide ne montre rien à l'étudiant).
    /// </summary>
    public static bool PeutMasquer(IEnumerable<InfoCollection> collections, string cle) =>
        collections.Any(c => c.Visible && c.NbCours > 0 && c.Cle != cle);
}
