using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Revisions.Core.Modeles;

namespace Revisions.Core.Signalements;

/// <summary>Ce qu'on signale : une carte de révision ou une section de cours.</summary>
public enum CibleSignalement
{
    Carte,
    Section,
}

/// <summary>Pourquoi on signale (les choix de la petite fenêtre).</summary>
public enum MotifSignalement
{
    ReponseFausse,
    QuestionPasClaire,
    Faute,
    Affichage,
    Autre,
}

/// <summary>Codes et libellés des cibles et des motifs. Les codes sont ceux du fichier exporté (docs/FORMAT-SIGNALEMENTS.md).</summary>
public static class VocabulaireSignalement
{
    public static string Code(CibleSignalement cible) => cible == CibleSignalement.Carte ? "carte" : "section";

    public static CibleSignalement CibleDepuis(string code) => code == "carte" ? CibleSignalement.Carte : CibleSignalement.Section;

    public static string Code(MotifSignalement motif) => motif switch
    {
        MotifSignalement.ReponseFausse => "reponse_fausse",
        MotifSignalement.QuestionPasClaire => "question_pas_claire",
        MotifSignalement.Faute => "faute",
        MotifSignalement.Affichage => "affichage",
        _ => "autre",
    };

    public static MotifSignalement MotifDepuis(string code) => code switch
    {
        "reponse_fausse" => MotifSignalement.ReponseFausse,
        "question_pas_claire" => MotifSignalement.QuestionPasClaire,
        "faute" => MotifSignalement.Faute,
        "affichage" => MotifSignalement.Affichage,
        _ => MotifSignalement.Autre,
    };

    public static string Libelle(MotifSignalement motif) => motif switch
    {
        MotifSignalement.ReponseFausse => "Réponse fausse",
        MotifSignalement.QuestionPasClaire => "Question pas claire",
        MotifSignalement.Faute => "Faute ou coquille",
        MotifSignalement.Affichage => "Formule ou code mal affiché",
        _ => "Autre",
    };
}

/// <summary>
/// Un signalement tel qu'il est enregistré. Il garde le texte vu par l'étudiant au moment du signalement
/// et l'empreinte du pack : si le pack a changé depuis, on le voit.
/// </summary>
public sealed record Signalement
{
    public required string Id { get; init; }
    public required DateTimeOffset DateHeure { get; init; }
    public required CibleSignalement Cible { get; init; }
    public required string CibleId { get; init; }
    public required string PackId { get; init; }
    public required MotifSignalement Motif { get; init; }
    public string? Commentaire { get; init; }
    /// <summary>Titre de la section (vide pour une carte).</summary>
    public string? Titre { get; init; }
    /// <summary>Recto de la carte, ou contenu de la section.</summary>
    public required string TexteRecto { get; init; }
    /// <summary>Verso de la carte (vide pour une section).</summary>
    public string? TexteVerso { get; init; }
    /// <summary>SHA-256 du fichier de pack au moment du signalement.</summary>
    public string? PackEmpreinte { get; init; }
    /// <summary>Date de dernière mise à jour du pack dans l'application.</summary>
    public string? PackDateMaj { get; init; }
    public string? Matiere { get; init; }
    public string? Seance { get; init; }
    /// <summary>Document d'origine (relatif au dossier Cours), pour vérifier dans le PDF.</summary>
    public string? SourceFichier { get; init; }
    public int? PageSource { get; init; }
    public bool Exporte { get; init; }
}

/// <summary>Texte d'une carte tel qu'on le copie dans un signalement : recto et verso en Markdown brut.</summary>
public static class TexteCarte
{
    /// <summary>Ce que l'étudiant voit d'abord (question, énoncé, ou question + choix du QCM).</summary>
    public static string Recto(Carte carte)
    {
        var c = carte.Contenu;
        switch (carte.Type)
        {
            case TypeCarte.Qcm:
                var sb = new StringBuilder(c.Question);
                for (int i = 0; i < (c.Choix?.Count ?? 0); i++)
                    sb.Append($"\n{(char)('A' + i)}. {c.Choix![i]}");
                return sb.ToString();
            case TypeCarte.Exercice:
                return c.Enonce ?? "";
            default:
                return c.Recto ?? "";
        }
    }

    /// <summary>La réponse (avec les étapes d'un exercice, ou la bonne lettre et l'explication d'un QCM).</summary>
    public static string Verso(Carte carte)
    {
        var c = carte.Contenu;
        switch (carte.Type)
        {
            case TypeCarte.Qcm:
                string bonne = c.BonneReponse is int i && c.Choix is { } choix && i >= 0 && i < choix.Count
                    ? $"Bonne réponse : {(char)('A' + i)}. {choix[i]}" : "";
                return $"{bonne}\n{c.Explication}".Trim();
            case TypeCarte.Exercice:
                var etapes = c.Etapes is { Count: > 0 }
                    ? string.Join("\n", c.Etapes.Select((e, n) => $"Étape {n + 1} : {e}")) + "\n"
                    : "";
                return (etapes + c.Reponse).Trim();
            default:
                return c.Verso ?? "";
        }
    }
}

/// <summary>Création du fichier « signalements-….json » (contrat : docs/FORMAT-SIGNALEMENTS.md à la racine de Cours).</summary>
public static class ExportSignalements
{
    public const int VersionFormat = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // accents et symboles lisibles dans le fichier
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Contenu JSON du fichier d'export pour les signalements donnés (les plus anciens d'abord).</summary>
    public static string Creer(IEnumerable<Signalement> signalements, string? auteur, DateTimeOffset dateExport, string versionAtome)
    {
        var fichier = new
        {
            formatVersion = VersionFormat,
            auteur = string.IsNullOrWhiteSpace(auteur) ? null : auteur.Trim(),
            dateExport = dateExport.ToString("O"),
            versionAtome,
            signalements = signalements.OrderBy(s => s.DateHeure).Select(s => new
            {
                id = s.Id,
                date = s.DateHeure.ToString("O"),
                type = VocabulaireSignalement.Code(s.Cible),
                cibleId = s.CibleId,
                packId = s.PackId,
                motif = VocabulaireSignalement.Code(s.Motif),
                commentaire = string.IsNullOrWhiteSpace(s.Commentaire) ? null : s.Commentaire.Trim(),
                affiche = new
                {
                    titre = s.Titre,
                    recto = s.Cible == CibleSignalement.Carte ? s.TexteRecto : null,
                    verso = s.TexteVerso,
                    contenu = s.Cible == CibleSignalement.Section ? s.TexteRecto : null,
                },
                pack = new
                {
                    empreinte = s.PackEmpreinte,
                    dateMaj = s.PackDateMaj,
                    matiere = s.Matiere,
                    seance = s.Seance,
                    sourceFichier = s.SourceFichier,
                    pageSource = s.PageSource,
                },
            }).ToList(),
        };
        return JsonSerializer.Serialize(fichier, Options);
    }

    /// <summary>« signalements-Prénom-2026-11-17.json » (sans le prénom s'il est vide ; caractères interdits retirés).</summary>
    public static string NomFichier(string? auteur, DateOnly jour)
    {
        var interdits = Path.GetInvalidFileNameChars();
        string propre = new string((auteur ?? "").Trim().Where(c => !interdits.Contains(c)).ToArray())
            .Replace(' ', '-');
        if (propre.Length > 40) propre = propre[..40];
        return propre.Length == 0 ? $"signalements-{jour:yyyy-MM-dd}.json" : $"signalements-{propre}-{jour:yyyy-MM-dd}.json";
    }
}
