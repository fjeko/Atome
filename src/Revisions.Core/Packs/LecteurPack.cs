using System.Text.Json;
using System.Text.RegularExpressions;
using Revisions.Core.Modeles;

namespace Revisions.Core.Packs;

/// <summary>Gravité d'un problème trouvé dans un pack.</summary>
public enum Gravite
{
    /// <summary>Le pack est refusé.</summary>
    Erreur,
    /// <summary>Le pack est importé, mais quelque chose mérite d'être signalé.</summary>
    Avertissement,
}

/// <summary>Un problème précis : où (chemin du champ, carte) et quoi (message en clair).</summary>
public sealed record Probleme(Gravite Gravite, string Chemin, string Message, string? CarteId = null)
{
    public override string ToString() =>
        (CarteId is null ? "" : $"[carte {CarteId}] ") + (Chemin.Length == 0 ? "" : Chemin + " : ") + Message;
}

/// <summary>Résultat de la lecture d'un fichier de pack.</summary>
public sealed class ResultatLecture
{
    /// <summary>Le pack lu, ou null s'il a été refusé.</summary>
    public Pack? Pack { get; init; }
    public List<Probleme> Problemes { get; init; } = [];
    public bool EstValide => Pack is not null;
    public IEnumerable<Probleme> Erreurs => Problemes.Where(p => p.Gravite == Gravite.Erreur);
    public IEnumerable<Probleme> Avertissements => Problemes.Where(p => p.Gravite == Gravite.Avertissement);
}

/// <summary>
/// Lit et valide un pack au format décrit dans packs/FORMAT.md.
/// On parcourt le JSON « à la main » (JsonDocument) plutôt que de le désérialiser d'un coup :
/// c'est ce qui permet de donner des messages d'erreur précis (champ, carte).
/// </summary>
public static partial class LecteurPack
{
    /// <summary>Version du format la plus récente que cette application sait lire.</summary>
    public const int VersionMax = 1;

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex IdPackValide();

    /// <summary>Lit le texte JSON d'un pack. Ne lève jamais d'exception : tout est dans le résultat.</summary>
    public static ResultatLecture Lire(string json)
    {
        var problemes = new List<Probleme>();
        void Erreur(string chemin, string message, string? carte = null) =>
            problemes.Add(new Probleme(Gravite.Erreur, chemin, message, carte));
        void Avertir(string chemin, string message, string? carte = null) =>
            problemes.Add(new Probleme(Gravite.Avertissement, chemin, message, carte));

        // Le format demande de l'UTF-8 sans BOM : on tolère le BOM, mais on le signale.
        if (json.Length > 0 && json[0] == '﻿')
        {
            json = json[1..];
            Avertir("", "Le fichier commence par un BOM UTF-8 (toléré).");
        }

        JsonDocument document;
        try
        {
            // Options par défaut = JSON strict : ni commentaires, ni virgule finale.
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            Erreur("", $"JSON invalide, ligne {e.LineNumber + 1}, position {e.BytePositionInLine + 1} : {e.Message}");
            return new ResultatLecture { Problemes = problemes };
        }

        using (document)
        {
            var racine = document.RootElement;
            if (racine.ValueKind != JsonValueKind.Object)
            {
                Erreur("", "Le pack doit être un objet JSON { … }.");
                return new ResultatLecture { Problemes = problemes };
            }

            // --- Version du format -------------------------------------------------------
            int? version = Entier(racine, "formatVersion", "formatVersion", true, Erreur);
            if (version is > VersionMax)
            {
                Erreur("formatVersion", $"Ce pack est au format {version} : il demande une version plus récente de l'application (format {VersionMax} au maximum).");
                return new ResultatLecture { Problemes = problemes };
            }
            if (version is < 1) Erreur("formatVersion", "La version doit valoir 1.");

            // --- En-tête -----------------------------------------------------------------
            string? id = Texte(racine, "id", "id", true, Erreur);
            if (id is not null && !IdPackValide().IsMatch(id))
                Erreur("id", $"« {id} » : seuls les minuscules, les chiffres et les tirets sont autorisés.");
            string? matiere = Texte(racine, "matiere", "matiere", true, Erreur);
            string? bloc = Texte(racine, "bloc", "bloc", false, Erreur);
            string? partie = Texte(racine, "partie", "partie", true, Erreur);
            string? sequence = Texte(racine, "sequence", "sequence", false, Erreur);
            string? seance = Texte(racine, "seance", "seance", true, Erreur);
            int? ordre = Entier(racine, "ordre", "ordre", false, Erreur);
            string? resume = Texte(racine, "resume", "resume", true, Erreur);
            List<string>? notions = ListeTextes(racine, "notions", "notions", true, Erreur);

            string? sourceFichier = null;
            int? sourcePages = null;
            if (racine.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                sourceFichier = Texte(source, "fichier", "source.fichier", true, Erreur);
                sourcePages = Entier(source, "pages", "source.pages", false, Erreur);
            }
            else
            {
                Erreur("source", "Champ obligatoire manquant (objet avec « fichier »).");
            }

            // --- Sections ----------------------------------------------------------------
            var sections = new List<Section>();
            var idsSections = new HashSet<string>();
            if (racine.TryGetProperty("sections", out var jsonSections) && jsonSections.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var s in jsonSections.EnumerateArray())
                {
                    string chemin = $"sections[{i++}]";
                    if (s.ValueKind != JsonValueKind.Object) { Erreur(chemin, "Une section doit être un objet."); continue; }
                    string? sId = Texte(s, "id", chemin + ".id", true, Erreur);
                    string? titre = Texte(s, "titre", chemin + ".titre", true, Erreur);
                    string? contenu = Texte(s, "contenu", chemin + ".contenu", true, Erreur);
                    int? page = Entier(s, "pageSource", chemin + ".pageSource", false, Erreur);
                    var sNotions = ListeTextes(s, "notions", chemin + ".notions", false, Erreur) ?? [];
                    if (sId is not null && !idsSections.Add(sId))
                        Erreur(chemin + ".id", $"Identifiant de section en double : « {sId} ».");
                    if (page is not null && sourcePages is not null && page > sourcePages)
                        Avertir(chemin + ".pageSource", $"Page {page} au-delà du nombre de pages du document ({sourcePages}).");
                    if (sId is not null && titre is not null && contenu is not null)
                        sections.Add(new Section(sId, titre, contenu, page, sNotions));
                }
            }
            else
            {
                Erreur("sections", "Champ obligatoire manquant (liste de sections).");
            }

            // --- Cartes ------------------------------------------------------------------
            var cartes = new List<Carte>();
            var idsCartes = new HashSet<string>();
            if (racine.TryGetProperty("cartes", out var jsonCartes) && jsonCartes.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var c in jsonCartes.EnumerateArray())
                {
                    string chemin = $"cartes[{i++}]";
                    if (c.ValueKind != JsonValueKind.Object) { Erreur(chemin, "Une carte doit être un objet."); continue; }
                    var carte = LireCarte(c, chemin, idsSections, idsCartes, Erreur);
                    if (carte is not null) cartes.Add(carte);
                }
            }
            else
            {
                Erreur("cartes", "Champ obligatoire manquant (liste de cartes).");
            }

            // --- Couverture des notions (avertissements seulement) -----------------------
            if (notions is not null)
            {
                // Une notion est couverte par une section si celle-ci la cite,
                // et par une carte si une carte pointe vers une telle section.
                var sectionsAvecCarte = cartes.Select(c => c.SectionId).ToHashSet();
                foreach (string notion in notions)
                {
                    var sectionsDeLaNotion = sections.Where(s => s.Notions.Contains(notion)).ToList();
                    if (sectionsDeLaNotion.Count == 0)
                        Avertir("notions", $"La notion « {notion} » n'est citée par aucune section.");
                    else if (!sectionsDeLaNotion.Any(s => sectionsAvecCarte.Contains(s.Id)))
                        Avertir("notions", $"La notion « {notion} » n'a aucune carte.");
                }
            }

            if (problemes.Any(p => p.Gravite == Gravite.Erreur))
                return new ResultatLecture { Problemes = problemes };

            var pack = new Pack
            {
                Id = id!,
                FormatVersion = version!.Value,
                Matiere = matiere!,
                Bloc = bloc,
                Partie = partie!,
                Sequence = sequence,
                Seance = seance!,
                Ordre = ordre,
                SourceFichier = sourceFichier!,
                SourcePages = sourcePages,
                Resume = resume!,
                Notions = notions!,
                Sections = sections,
                Cartes = cartes,
            };
            return new ResultatLecture { Pack = pack, Problemes = problemes };
        }
    }

    /// <summary>Lit une carte et vérifie les champs propres à son type. Renvoie null si elle est invalide.</summary>
    private static Carte? LireCarte(JsonElement c, string chemin, HashSet<string> idsSections,
        HashSet<string> idsCartes, Action<string, string, string?> erreurBrute)
    {
        string? id = Texte(c, "id", chemin + ".id", true, (ch, m, _) => erreurBrute(ch, m, null));
        // À partir d'ici, chaque message cite l'identifiant de la carte.
        bool ok = true;
        void ErreurLocale(string ch, string m, string? x = null) { ok = false; erreurBrute(ch, m, id); }

        if (id is null) ok = false;
        else if (!idsCartes.Add(id)) ErreurLocale(chemin + ".id", $"Identifiant de carte en double : « {id} ».");

        string? section = Texte(c, "section", chemin + ".section", true, ErreurLocale);
        if (section is not null && !idsSections.Contains(section))
            ErreurLocale(chemin + ".section", $"La section « {section} » n'existe pas dans ce pack.");

        int? difficulte = Entier(c, "difficulte", chemin + ".difficulte", true, ErreurLocale);
        if (difficulte is < 1 or > 3)
            ErreurLocale(chemin + ".difficulte", "La difficulté doit valoir 1, 2 ou 3.");

        string? typeTexte = Texte(c, "type", chemin + ".type", true, ErreurLocale);
        TypeCarte type = default;
        ContenuCarte? contenu = null;
        switch (typeTexte)
        {
            case null:
                break;
            case "question":
                type = TypeCarte.Question;
                contenu = new ContenuCarte
                {
                    Recto = Texte(c, "recto", chemin + ".recto", true, ErreurLocale),
                    Verso = Texte(c, "verso", chemin + ".verso", true, ErreurLocale),
                };
                break;
            case "qcm":
                type = TypeCarte.Qcm;
                var choix = ListeTextes(c, "choix", chemin + ".choix", true, ErreurLocale);
                int? bonne = Entier(c, "bonneReponse", chemin + ".bonneReponse", true, ErreurLocale);
                if (choix is not null && choix.Count is < 2 or > 5)
                    ErreurLocale(chemin + ".choix", $"Un QCM a entre 2 et 5 choix ({choix!.Count} ici).");
                if (choix is not null && bonne is not null && (bonne < 0 || bonne >= choix.Count))
                    ErreurLocale(chemin + ".bonneReponse", $"L'index {bonne} ne correspond à aucun choix (de 0 à {choix.Count - 1}).");
                contenu = new ContenuCarte
                {
                    Question = Texte(c, "question", chemin + ".question", true, ErreurLocale),
                    Choix = choix,
                    BonneReponse = bonne,
                    Explication = Texte(c, "explication", chemin + ".explication", true, ErreurLocale),
                };
                break;
            case "exercice":
                type = TypeCarte.Exercice;
                var etapes = ListeTextes(c, "etapes", chemin + ".etapes", true, ErreurLocale);
                if (etapes is { Count: 0 })
                    ErreurLocale(chemin + ".etapes", "La correction doit contenir au moins une étape.");
                contenu = new ContenuCarte
                {
                    Enonce = Texte(c, "enonce", chemin + ".enonce", true, ErreurLocale),
                    Etapes = etapes,
                    Reponse = Texte(c, "reponse", chemin + ".reponse", true, ErreurLocale),
                };
                break;
            default:
                ErreurLocale(chemin + ".type", $"Type de carte inconnu : « {typeTexte} » (attendu : question, qcm ou exercice).");
                break;
        }

        return ok && contenu is not null
            ? new Carte(id!, section!, type, difficulte!.Value, contenu)
            : null;
    }

    // ------------------------------------------------------------------------------------
    // Petites fonctions de lecture d'un champ, avec contrôle du type.
    // ------------------------------------------------------------------------------------

    /// <summary>Lit un champ texte. Un champ facultatif peut être absent ou null.</summary>
    private static string? Texte(JsonElement objet, string nom, string chemin, bool obligatoire,
        Action<string, string, string?> erreur)
    {
        if (!objet.TryGetProperty(nom, out var valeur) || valeur.ValueKind == JsonValueKind.Null)
        {
            if (obligatoire) erreur(chemin, "Champ obligatoire manquant.", null);
            return null;
        }
        if (valeur.ValueKind != JsonValueKind.String)
        {
            erreur(chemin, "Ce champ doit être un texte entre guillemets.", null);
            return null;
        }
        string texte = valeur.GetString()!;
        if (obligatoire && string.IsNullOrWhiteSpace(texte))
        {
            erreur(chemin, "Ce champ obligatoire est vide.", null);
            return null;
        }
        return texte;
    }

    /// <summary>Lit un champ entier.</summary>
    private static int? Entier(JsonElement objet, string nom, string chemin, bool obligatoire,
        Action<string, string, string?> erreur)
    {
        if (!objet.TryGetProperty(nom, out var valeur) || valeur.ValueKind == JsonValueKind.Null)
        {
            if (obligatoire) erreur(chemin, "Champ obligatoire manquant.", null);
            return null;
        }
        if (valeur.ValueKind != JsonValueKind.Number || !valeur.TryGetInt32(out int entier))
        {
            erreur(chemin, "Ce champ doit être un nombre entier.", null);
            return null;
        }
        return entier;
    }

    /// <summary>Lit une liste de textes.</summary>
    private static List<string>? ListeTextes(JsonElement objet, string nom, string chemin, bool obligatoire,
        Action<string, string, string?> erreur)
    {
        if (!objet.TryGetProperty(nom, out var valeur) || valeur.ValueKind == JsonValueKind.Null)
        {
            if (obligatoire) erreur(chemin, "Champ obligatoire manquant.", null);
            return null;
        }
        if (valeur.ValueKind != JsonValueKind.Array)
        {
            erreur(chemin, "Ce champ doit être une liste [ … ].", null);
            return null;
        }
        var liste = new List<string>();
        foreach (var element in valeur.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                erreur(chemin, "La liste ne doit contenir que des textes.", null);
                return null;
            }
            liste.Add(element.GetString()!);
        }
        return liste;
    }
}
