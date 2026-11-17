using System.Globalization;
using System.Text.Json;
using Revisions.Core.Temps;

namespace Revisions.Core.Planning;

/// <summary>Résultat de la lecture d'un fichier de planning.</summary>
/// <param name="Planning">Le planning, ou null s'il y a au moins une erreur.</param>
/// <param name="Erreurs">Problèmes qui empêchent d'utiliser le fichier.</param>
/// <param name="Avertissements">Points à vérifier, sans gravité (le planning reste utilisable).</param>
public sealed record ResultatLecturePlanning(
    PlanningCned? Planning,
    IReadOnlyList<string> Erreurs,
    IReadOnlyList<string> Avertissements)
{
    public bool Valide => Planning is not null;
}

/// <summary>
/// Lit et valide un fichier de planning (planning/FORMAT.md). Parcours manuel du JSON
/// pour donner des messages précis (« Période « Maths – SQ1 » : la fin est avant le début »).
/// </summary>
public static class LecteurPlanning
{
    /// <summary>Plus haute version du format comprise par l'application. Les versions antérieures restent acceptées.</summary>
    public const int VersionMax = 1;

    public static ResultatLecturePlanning Lire(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            return new ResultatLecturePlanning(null, [$"Le fichier n'est pas un JSON valide : {e.Message}"], []);
        }
        using (document) return Lire(document.RootElement);
    }

    private static ResultatLecturePlanning Lire(JsonElement racine)
    {
        var erreurs = new List<string>();
        var avertissements = new List<string>();
        if (racine.ValueKind != JsonValueKind.Object)
            return new ResultatLecturePlanning(null, ["Le fichier doit contenir un objet JSON."], []);

        int version = racine.TryGetProperty("formatVersion", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
        if (version < 1) erreurs.Add("« formatVersion » manque ou n'est pas un entier positif.");
        else if (version > VersionMax)
            erreurs.Add($"Ce planning est au format {version} : cette version de l'application ne connaît que les formats 1 à {VersionMax}.");

        string id = Texte(racine, "id") ?? "";
        if (id.Length == 0) erreurs.Add("« id » manque.");
        string titre = Texte(racine, "titre") ?? id;

        var cles = new HashSet<string>();
        var periodes = LirePeriodes(racine, cles, erreurs, avertissements);
        var devoirs = LireDevoirs(racine, cles, erreurs, avertissements);

        foreach (string matiere in devoirs.Select(d => d.Matiere).Distinct())
            if (!periodes.Any(p => p.Matiere == matiere))
                avertissements.Add($"Les devoirs de « {matiere} » n'ont aucune période : ils seront affichés, sans préparation des cartes.");

        return erreurs.Count > 0
            ? new ResultatLecturePlanning(null, erreurs, avertissements)
            : new ResultatLecturePlanning(new PlanningCned(id, version, titre, periodes, devoirs), [], avertissements);
    }

    private static List<Periode> LirePeriodes(JsonElement racine, HashSet<string> cles, List<string> erreurs, List<string> avertissements)
    {
        var periodes = new List<Periode>();
        if (!racine.TryGetProperty("periodes", out var tableau) || tableau.ValueKind != JsonValueKind.Array)
        {
            erreurs.Add("« periodes » manque ou n'est pas une liste.");
            return periodes;
        }

        int i = 0;
        foreach (var e in tableau.EnumerateArray())
        {
            i++;
            string matiere = Texte(e, "matiere") ?? "";
            string libelle = Texte(e, "libelle") ?? "";
            if (matiere.Length == 0 || libelle.Length == 0)
            {
                erreurs.Add($"Période {i} : « matiere » et « libelle » sont obligatoires.");
                continue;
            }
            string ou = $"Période « {matiere} – {libelle} »";
            var debut = Date(e, "debut", ou, erreurs);
            var fin = Date(e, "fin", ou, erreurs);

            var cibles = new List<string>();
            if (e.TryGetProperty("cible", out var c))
            {
                if (c.ValueKind != JsonValueKind.Array) erreurs.Add($"{ou} : « cible » doit être une liste.");
                else
                    foreach (var p in c.EnumerateArray())
                    {
                        string prefixe = p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : "";
                        if (prefixe.Length == 0) erreurs.Add($"{ou} : un préfixe de « cible » est vide.");
                        else cibles.Add(prefixe);
                    }
            }

            if (debut is null || fin is null) continue;
            if (fin < debut)
            {
                erreurs.Add($"{ou} : la fin ({fin:dd/MM/yyyy}) est avant le début ({debut:dd/MM/yyyy}).");
                continue;
            }
            if (debut.Value.DayOfWeek != DayOfWeek.Monday) avertissements.Add($"{ou} : le début n'est pas un lundi.");
            if (fin.Value.DayOfWeek != DayOfWeek.Sunday) avertissements.Add($"{ou} : la fin n'est pas un dimanche.");

            string cle = $"periode:{matiere}|{libelle}";
            if (!cles.Add(cle))
            {
                erreurs.Add($"{ou} : deux périodes ont la même matière et le même libellé.");
                continue;
            }
            periodes.Add(new Periode(cle, matiere, libelle, debut.Value, fin.Value, cibles,
                Booleen(e, "approx"), Booleen(e, "hypothese"), Booleen(e, "continu")));
        }
        return periodes;
    }

    private static List<Devoir> LireDevoirs(JsonElement racine, HashSet<string> cles, List<string> erreurs, List<string> avertissements)
    {
        var devoirs = new List<Devoir>();
        if (!racine.TryGetProperty("devoirs", out var tableau)) return devoirs;  // facultatif
        if (tableau.ValueKind != JsonValueKind.Array)
        {
            erreurs.Add("« devoirs » doit être une liste.");
            return devoirs;
        }

        int i = 0;
        foreach (var e in tableau.EnumerateArray())
        {
            i++;
            string matiere = Texte(e, "matiere") ?? "";
            string code = Texte(e, "code") ?? "";
            if (matiere.Length == 0 || code.Length == 0)
            {
                erreurs.Add($"Devoir {i} : « matiere » et « code » sont obligatoires.");
                continue;
            }
            string ou = $"Devoir « {matiere} – {code} »";
            var semaine = Date(e, "semaine", ou, erreurs);
            if (semaine is null) continue;
            if (semaine.Value.DayOfWeek != DayOfWeek.Monday)
            {
                avertissements.Add($"{ou} : la semaine n'est pas donnée par son lundi (ramenée au lundi).");
                semaine = JourDeRevision.LundiDe(semaine.Value);
            }
            // Sans « note », on déduit le type du code : QZ = quiz d'auto-évaluation, le reste = devoir noté.
            bool note = e.TryGetProperty("note", out var nn) && nn.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? nn.GetBoolean()
                : !code.StartsWith("QZ", StringComparison.OrdinalIgnoreCase);
            string cle = $"devoir:{matiere}|{code}";
            if (!cles.Add(cle))
            {
                erreurs.Add($"{ou} : deux devoirs ont la même matière et le même code.");
                continue;
            }
            devoirs.Add(new Devoir(cle, matiere, code, note, semaine.Value, Texte(e, "remarque")));
        }
        return devoirs;
    }

    /// <summary>
    /// Avertit pour chaque préfixe de « cible » qui ne correspond à aucun pack importé
    /// (faute de frappe, ou pack pas encore créé).
    /// </summary>
    public static List<string> VerifierCibles(PlanningCned planning, IReadOnlyCollection<string> idsDePacks)
    {
        var messages = new List<string>();
        foreach (var periode in planning.Periodes)
            foreach (string prefixe in periode.Cibles)
                if (!idsDePacks.Any(id => Periode.CorrespondPrefixe(id, prefixe)))
                    messages.Add($"« {periode.Matiere} – {periode.Libelle} » : aucun pack ne correspond au préfixe « {prefixe} ».");
        return messages;
    }

    private static string? Texte(JsonElement e, string nom) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim()
            : null;

    private static bool Booleen(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateOnly? Date(JsonElement e, string nom, string ou, List<string> erreurs)
    {
        string? texte = Texte(e, nom);
        if (texte is null)
        {
            erreurs.Add($"{ou} : « {nom} » manque.");
            return null;
        }
        if (DateOnly.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        erreurs.Add($"{ou} : « {nom} » n'est pas une date AAAA-MM-JJ (« {texte} »).");
        return null;
    }
}
