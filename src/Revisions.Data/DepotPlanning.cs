using System.IO;
using System.Text.Json;
using Dapper;
using Revisions.Core.Planning;

namespace Revisions.Data;

/// <summary>Résultat de l'import d'un fichier de planning.</summary>
public sealed record ResultatImportPlanning(string Fichier, ResultatLecturePlanning Lecture, bool Enregistre);

/// <summary>
/// Planning du CNED : le fichier importé (gardé tel quel en base), les choix de l'étudiant
/// (suivre ou non, matières, quiz) et ses dates personnelles. Le fichier d'origine n'est jamais modifié.
/// </summary>
public sealed class DepotPlanning(BaseDeDonnees baseDeDonnees, TimeProvider horloge)
{
    private const string CleActif = "planning_actif";
    private const string CleSuivre = "planning_suivre";
    private const string CleMasquerQuiz = "planning_masquer_quiz";
    private const string CleMatieresIgnorees = "planning_matieres_ignorees";

    /// <summary>
    /// Valide puis enregistre un planning. Le planning devient le planning actif
    /// s'il n'y en a pas encore, ou si <paramref name="activer"/> est vrai (import manuel).
    /// Un fichier avec des erreurs n'est pas enregistré.
    /// </summary>
    public ResultatImportPlanning Importer(string json, string source, bool activer)
    {
        var lecture = LecteurPlanning.Lire(json);
        if (lecture.Planning is not { } planning) return new ResultatImportPlanning(source, lecture, false);

        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        connexion.Execute(
            """
            INSERT INTO planning (id, contenu_json, source, date_import) VALUES (@id, @json, @source, @date)
            ON CONFLICT(id) DO UPDATE SET contenu_json = excluded.contenu_json, source = excluded.source, date_import = excluded.date_import
            """,
            new { id = planning.Id, json, source, date = horloge.GetUtcNow().ToString("O") }, transaction);
        string? actif = connexion.ExecuteScalar<string?>("SELECT valeur FROM reglage WHERE cle = @CleActif", new { CleActif }, transaction);
        bool actifExiste = actif is not null &&
                           connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM planning WHERE id = @actif", new { actif }, transaction) > 0;
        if (activer || !actifExiste) EcrireValeur(connexion, CleActif, planning.Id, transaction);
        transaction.Commit();
        return new ResultatImportPlanning(source, lecture, true);
    }

    /// <summary>
    /// Import automatique au démarrage : chaque fichier .json du dossier « planning » est lu.
    /// Un fichier identique à celui déjà en base est ignoré (rien à faire).
    /// </summary>
    public List<ResultatImportPlanning> ImporterDossier(string? dossier)
    {
        var resultats = new List<ResultatImportPlanning>();
        if (string.IsNullOrEmpty(dossier) || !Directory.Exists(dossier)) return resultats;
        foreach (string fichier in Directory.EnumerateFiles(dossier, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            string json;
            try { json = File.ReadAllText(fichier); }
            catch (IOException e)
            {
                resultats.Add(new ResultatImportPlanning(fichier, new ResultatLecturePlanning(null, [e.Message], []), false));
                continue;
            }
            if (DejaEnBase(json)) continue;
            resultats.Add(Importer(json, fichier, activer: false));
        }
        return resultats;
    }

    private bool DejaEnBase(string json)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM planning WHERE contenu_json = @json", new { json }) > 0;
    }

    /// <summary>Le planning actif, relu et validé, ou null s'il n'y en a pas (l'application fonctionne alors comme avant).</summary>
    public PlanningCned? ChargerActif() => LireActif()?.Planning;

    /// <summary>Le planning actif avec ses avertissements de lecture, ou null.</summary>
    public ResultatLecturePlanning? LireActif()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        string? json = connexion.ExecuteScalar<string?>(
            "SELECT p.contenu_json FROM planning p JOIN reglage r ON r.cle = @CleActif AND r.valeur = p.id", new { CleActif });
        return json is null ? null : LecteurPlanning.Lire(json);
    }

    /// <summary>Chemin du fichier d'où vient le planning actif (pour l'écran Planning).</summary>
    public string? SourceActive()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<string?>(
            "SELECT p.source FROM planning p JOIN reglage r ON r.cle = @CleActif AND r.valeur = p.id", new { CleActif });
    }

    /// <summary>Les choix de l'étudiant pour ce planning.</summary>
    public ReglagesPlanning ChargerReglages(string planningId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var valeurs = connexion.Query<(string Cle, string Valeur)>(
                "SELECT cle, valeur FROM reglage WHERE cle IN @cles", new { cles = new[] { CleSuivre, CleMasquerQuiz, CleMatieresIgnorees } })
            .ToDictionary(l => l.Cle, l => l.Valeur);
        var surcharges = connexion.Query<(string Element, string Debut, string? Fin)>(
                "SELECT element, debut, fin FROM planning_surcharge WHERE planning_id = @planningId", new { planningId })
            .ToDictionary(l => l.Element, l => new Surcharge(BaseDeDonnees.Jour(l.Debut)!.Value, BaseDeDonnees.Jour(l.Fin)));

        HashSet<string> ignorees = [];
        if (valeurs.TryGetValue(CleMatieresIgnorees, out var json))
        {
            try { ignorees = JsonSerializer.Deserialize<HashSet<string>>(json) ?? []; }
            catch (JsonException) { /* valeur abîmée : on suit toutes les matières */ }
        }
        return new ReglagesPlanning
        {
            Suivre = !valeurs.TryGetValue(CleSuivre, out var s) || s != "0",
            MasquerQuiz = valeurs.TryGetValue(CleMasquerQuiz, out var q) && q == "1",
            MatieresIgnorees = ignorees,
            Surcharges = surcharges,
        };
    }

    /// <summary>Le planning tel qu'il s'applique (dates personnelles, matières suivies), ou null sans planning.</summary>
    public PlanningEffectif? Effectif()
    {
        var planning = ChargerActif();
        return planning is null ? null : new PlanningEffectif(planning, ChargerReglages(planning.Id));
    }

    public void DefinirSuivre(bool suivre) => Ecrire(CleSuivre, suivre ? "1" : "0");

    public void DefinirMasquerQuiz(bool masquer) => Ecrire(CleMasquerQuiz, masquer ? "1" : "0");

    /// <summary>Suivre ou non une matière du planning.</summary>
    public void DefinirMatiereSuivie(string planningId, string matiere, bool suivie)
    {
        var ignorees = new HashSet<string>(ChargerReglages(planningId).MatieresIgnorees);
        if (suivie) ignorees.Remove(matiere);
        else ignorees.Add(matiere);
        Ecrire(CleMatieresIgnorees, JsonSerializer.Serialize(ignorees.Order(StringComparer.Ordinal)));
    }

    /// <summary>Enregistre une date personnelle pour une période ou un devoir, ou l'efface (null : retour aux dates du fichier).</summary>
    public void DefinirSurcharge(string planningId, string element, Surcharge? surcharge)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        if (surcharge is null)
        {
            connexion.Execute("DELETE FROM planning_surcharge WHERE planning_id = @planningId AND element = @element", new { planningId, element });
            return;
        }
        connexion.Execute(
            """
            INSERT INTO planning_surcharge (planning_id, element, debut, fin) VALUES (@planningId, @element, @debut, @fin)
            ON CONFLICT(planning_id, element) DO UPDATE SET debut = excluded.debut, fin = excluded.fin
            """,
            new
            {
                planningId, element, debut = BaseDeDonnees.Iso(surcharge.Debut),
                fin = surcharge.Fin is { } f ? BaseDeDonnees.Iso(f) : null,
            });
    }

    /// <summary>Efface toutes les dates personnelles du planning.</summary>
    public void EffacerSurcharges(string planningId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("DELETE FROM planning_surcharge WHERE planning_id = @planningId", new { planningId });
    }

    private void Ecrire(string cle, string valeur)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        EcrireValeur(connexion, cle, valeur, null);
    }

    private static void EcrireValeur(System.Data.IDbConnection connexion, string cle, string valeur, System.Data.IDbTransaction? transaction) =>
        connexion.Execute(
            "INSERT INTO reglage (cle, valeur) VALUES (@cle, @valeur) ON CONFLICT(cle) DO UPDATE SET valeur = excluded.valeur",
            new { cle, valeur }, transaction);
}
