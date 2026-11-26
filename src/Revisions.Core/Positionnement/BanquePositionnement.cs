using System.Text.Json;
using System.Text.RegularExpressions;

namespace Revisions.Core.Positionnement;

/// <summary>Ce que mesure une question du test de positionnement.</summary>
public enum Competence { Grammaire, Vocabulaire, Lecture, Ecoute }

/// <summary>
/// Un niveau du test (« A1 », « B2 »…), du plus facile au plus difficile.
/// <paramref name="Partie"/> est le champ « partie » des packs de ce niveau : c'est lui qui reçoit les acquis.
/// </summary>
public sealed record NiveauPositionnement(string Code, string Nom, string Partie, string Description);

/// <summary>
/// Une question à choix multiple. La bonne réponse est toujours le premier choix du fichier :
/// l'ordre est mélangé à l'affichage.
/// </summary>
/// <param name="Niveau">Indice du niveau dans <see cref="BanquePositionnement.Niveaux"/>.</param>
/// <param name="Texte">Petit texte à lire avant la question (compréhension écrite), ou null.</param>
/// <param name="Ecoute">Texte lu à voix haute et jamais affiché (compréhension orale), ou null.</param>
public sealed record QuestionPositionnement(string Id, int Niveau, Competence Competence, string Question,
    IReadOnlyList<string> Choix, string? Texte, string? Ecoute);

/// <summary>
/// Banque de questions d'un test de positionnement pour une matière (fichier « *.positionnement.json »
/// rangé avec les packs). Le format est décrit dans docs/FORMAT.md, partie « Test de positionnement ».
/// </summary>
public sealed class BanquePositionnement
{
    public required string Id { get; init; }
    public required string Matiere { get; init; }
    public string? Bloc { get; init; }
    public required string Titre { get; init; }
    /// <summary>Quelques lignes affichées avant le test (consignes), ou null.</summary>
    public string? Consignes { get; init; }
    /// <summary>Langue des questions d'écoute (« en-GB »), pour choisir une voix de Windows ; null s'il n'y en a pas.</summary>
    public string? LangueEcoute { get; init; }
    /// <summary>Indice du niveau par lequel le test commence.</summary>
    public int Depart { get; init; }
    public required IReadOnlyList<NiveauPositionnement> Niveaux { get; init; }
    public required IReadOnlyList<QuestionPositionnement> Questions { get; init; }

    /// <summary>Clé de la matière dans la bibliothèque et dans les acquis (bloc puis nom, comme <c>ArbreBibliotheque</c>).</summary>
    public string CleMatiere => $"{Bloc} {Matiere}".Trim();

    /// <summary>Vrai si au moins une question demande d'écouter.</summary>
    public bool AEcoute => Questions.Any(q => q.Ecoute is not null);

    public int IndiceNiveau(string code) =>
        Niveaux.ToList().FindIndex(n => string.Equals(n.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Résultat de la lecture d'une banque : la banque, ou la liste des erreurs.</summary>
public sealed record LectureBanque(BanquePositionnement? Banque, IReadOnlyList<string> Erreurs);

/// <summary>Lit et valide un fichier de test de positionnement. Ne lève jamais d'exception.</summary>
public static partial class LecteurBanque
{
    /// <summary>Fin du nom des fichiers de test : ils sont rangés avec les packs, mais ne sont pas des packs.</summary>
    public const string Extension = ".positionnement.json";

    public const int VersionMax = 1;

    /// <summary>Nombre minimal de choix par question (une bonne réponse et au moins deux pièges).</summary>
    public const int ChoixMin = 3;

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex IdValide();

    /// <summary>Vrai si le fichier est un test de positionnement (et non un pack).</summary>
    public static bool EstFichierDeTest(string chemin) =>
        chemin.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static LectureBanque Lire(string json)
    {
        var erreurs = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
            var r = document.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return new(null, ["Le fichier doit être un objet JSON { … }."]);

            int version = r.TryGetProperty("formatPositionnement", out var v) && v.TryGetInt32(out int n) ? n : 0;
            if (version < 1) erreurs.Add("formatPositionnement : champ obligatoire (1).");
            else if (version > VersionMax) erreurs.Add($"formatPositionnement : ce test est au format {version}, il demande une version plus récente d'Atome.");

            string? id = Texte(r, "id", erreurs, true);
            if (id is not null && !IdValide().IsMatch(id)) erreurs.Add($"id : « {id} » : seuls les minuscules, les chiffres et les tirets sont autorisés.");
            string? matiere = Texte(r, "matiere", erreurs, true);
            string? bloc = Texte(r, "bloc", erreurs, false);
            string? titre = Texte(r, "titre", erreurs, true);
            string? consignes = Texte(r, "consignes", erreurs, false);
            string? langue = Texte(r, "langueEcoute", erreurs, false);
            string? depart = Texte(r, "depart", erreurs, false);

            var niveaux = new List<NiveauPositionnement>();
            if (r.TryGetProperty("niveaux", out var tn) && tn.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var e in tn.EnumerateArray())
                {
                    var err = new List<string>();
                    string? code = Texte(e, "code", err, true);
                    string? nom = Texte(e, "nom", err, true);
                    string? partie = Texte(e, "partie", err, true);
                    string? description = Texte(e, "description", err, true);
                    erreurs.AddRange(err.Select(x => $"niveaux[{i}].{x}"));
                    if (err.Count == 0)
                    {
                        if (niveaux.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
                            erreurs.Add($"niveaux[{i}] : le code « {code} » est en double.");
                        niveaux.Add(new NiveauPositionnement(code!, nom!, partie!, description!));
                    }
                    i++;
                }
                if (niveaux.Count < 2) erreurs.Add("niveaux : il faut au moins deux niveaux.");
            }
            else erreurs.Add("niveaux : champ obligatoire (liste de niveaux, du plus facile au plus difficile).");

            int indiceDepart = niveaux.Count / 2;
            if (depart is not null)
            {
                indiceDepart = niveaux.FindIndex(x => string.Equals(x.Code, depart, StringComparison.OrdinalIgnoreCase));
                if (indiceDepart < 0) { erreurs.Add($"depart : le niveau « {depart} » n'existe pas."); indiceDepart = 0; }
            }

            var questions = new List<QuestionPositionnement>();
            var ids = new HashSet<string>();
            if (r.TryGetProperty("questions", out var tq) && tq.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var e in tq.EnumerateArray())
                {
                    var err = new List<string>();
                    string? qid = Texte(e, "id", err, true);
                    string? code = Texte(e, "niveau", err, true);
                    string? comp = Texte(e, "competence", err, true);
                    string? question = Texte(e, "question", err, true);
                    string? texte = Texte(e, "texte", err, false);
                    string? ecoute = Texte(e, "ecoute", err, false);
                    var choix = new List<string>();
                    if (e.TryGetProperty("choix", out var tc) && tc.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in tc.EnumerateArray())
                            if (c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } s) choix.Add(s);
                            else err.Add("choix : chaque choix doit être un texte non vide.");
                    }
                    else err.Add("choix : champ obligatoire (liste de textes, la bonne réponse en premier).");

                    int niveau = code is null ? -1 : niveaux.FindIndex(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (code is not null && niveau < 0) err.Add($"niveau : « {code} » n'est pas dans la liste des niveaux.");
                    Competence? competence = comp switch
                    {
                        "grammaire" => Competence.Grammaire,
                        "vocabulaire" => Competence.Vocabulaire,
                        "lecture" => Competence.Lecture,
                        "ecoute" => Competence.Ecoute,
                        null => null,
                        _ => null,
                    };
                    if (comp is not null && competence is null) err.Add($"competence : « {comp} » inconnue (grammaire, vocabulaire, lecture ou ecoute).");
                    if (competence == Competence.Ecoute && ecoute is null) err.Add("ecoute : obligatoire pour une question d'écoute (le texte lu à voix haute).");
                    if (competence == Competence.Lecture && texte is null) err.Add("texte : obligatoire pour une question de lecture.");
                    if (choix.Count is > 0 and < ChoixMin) err.Add($"choix : il faut au moins {ChoixMin} choix.");
                    if (choix.Distinct(StringComparer.Ordinal).Count() != choix.Count) err.Add("choix : deux choix identiques.");
                    if (qid is not null && !ids.Add(qid)) err.Add($"id : « {qid} » est en double.");

                    string ou = qid is null ? $"questions[{i}]" : $"questions[{i}] ({qid})";
                    erreurs.AddRange(err.Select(x => $"{ou}.{x}"));
                    if (err.Count == 0)
                        questions.Add(new QuestionPositionnement(qid!, niveau, competence!.Value, question!, choix, texte, ecoute));
                    i++;
                }
            }
            else erreurs.Add("questions : champ obligatoire (liste de questions).");

            for (int niv = 0; niv < niveaux.Count; niv++)
                if (erreurs.Count == 0 && questions.Count(q => q.Niveau == niv) < TestAdaptatif.TailleBloc)
                    erreurs.Add($"niveau {niveaux[niv].Code} : il faut au moins {TestAdaptatif.TailleBloc} questions.");

            if (erreurs.Count > 0) return new(null, erreurs);
            return new(new BanquePositionnement
            {
                Id = id!, Matiere = matiere!, Bloc = bloc, Titre = titre!, Consignes = consignes, LangueEcoute = langue,
                Depart = indiceDepart, Niveaux = niveaux, Questions = questions,
            }, []);
        }
        catch (JsonException e)
        {
            return new(null, [$"JSON invalide, ligne {e.LineNumber + 1} : {e.Message}"]);
        }
    }

    private static string? Texte(JsonElement objet, string nom, List<string> erreurs, bool obligatoire)
    {
        if (objet.ValueKind != JsonValueKind.Object)
        {
            if (obligatoire) erreurs.Add($"{nom} : l'élément doit être un objet {{ … }}.");
            return null;
        }
        if (!objet.TryGetProperty(nom, out var valeur) || valeur.ValueKind == JsonValueKind.Null)
        {
            if (obligatoire) erreurs.Add($"{nom} : champ obligatoire manquant.");
            return null;
        }
        if (valeur.ValueKind != JsonValueKind.String || valeur.GetString() is not { } s || s.Trim().Length == 0)
        {
            erreurs.Add($"{nom} : ce champ doit être un texte non vide.");
            return null;
        }
        return s;
    }
}
