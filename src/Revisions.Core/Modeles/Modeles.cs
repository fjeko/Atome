namespace Revisions.Core.Modeles;

/// <summary>Note donnée par l'étudiant après avoir vu la réponse (les 4 boutons de la session).</summary>
public enum Note
{
    Rate = 1,
    Difficile = 2,
    Bien = 3,
    Facile = 4,
}

/// <summary>Types de cartes du format de pack version 1.</summary>
public enum TypeCarte
{
    Question,
    Qcm,
    Exercice,
}

/// <summary>Un pack = une séance de cours : ses sections (le cours) et ses cartes (la révision).</summary>
public sealed record Pack
{
    public required string Id { get; init; }
    public required int FormatVersion { get; init; }
    public required string Matiere { get; init; }
    public string? Bloc { get; init; }
    public required string Partie { get; init; }
    public string? Sequence { get; init; }
    public required string Seance { get; init; }
    public int? Ordre { get; init; }
    /// <summary>Chemin du document d'origine, relatif au dossier « Cours ».</summary>
    public required string SourceFichier { get; init; }
    public int? SourcePages { get; init; }
    public required string Resume { get; init; }
    public required IReadOnlyList<string> Notions { get; init; }
    public required IReadOnlyList<Section> Sections { get; init; }
    public required IReadOnlyList<Carte> Cartes { get; init; }
}

/// <summary>Une partie courte du cours, écrite en Markdown + LaTeX.</summary>
public sealed record Section(
    string Id,
    string Titre,
    string Contenu,
    int? PageSource,
    IReadOnlyList<string> Notions);

/// <summary>Une carte de révision. Son contenu dépend de son type (voir <see cref="ContenuCarte"/>).</summary>
public sealed record Carte(
    string Id,
    string SectionId,
    TypeCarte Type,
    int Difficulte,
    ContenuCarte Contenu);

/// <summary>
/// Champs propres à chaque type de carte. Seuls ceux du type concerné sont remplis :
/// question → Recto, Verso ; qcm → Question, Choix, BonneReponse, Explication ;
/// exercice → Enonce, Etapes, Reponse.
/// Cet objet est enregistré tel quel en JSON dans la base (colonne donnees_json).
/// </summary>
public sealed record ContenuCarte
{
    public string? Recto { get; init; }
    public string? Verso { get; init; }
    public string? Question { get; init; }
    public List<string>? Choix { get; init; }
    public int? BonneReponse { get; init; }
    public string? Explication { get; init; }
    public string? Enonce { get; init; }
    public List<string>? Etapes { get; init; }
    public string? Reponse { get; init; }
}

/// <summary>
/// Ce que l'application sait de la mémorisation d'une carte (état de l'algorithme FSRS).
/// Une carte jamais vue a une stabilité nulle (null).
/// </summary>
public sealed record EtatCarte
{
    public required string CarteId { get; init; }
    /// <summary>Stabilité S en jours : durée au bout de laquelle on a encore 90 % de chances de se souvenir.</summary>
    public double? Stabilite { get; init; }
    /// <summary>Difficulté D de la carte pour cet étudiant, entre 1 (facile) et 10 (difficile).</summary>
    public double? DifficulteFsrs { get; init; }
    /// <summary>Jour à partir duquel la carte est à revoir.</summary>
    public DateOnly? Echeance { get; init; }
    public DateOnly? DerniereRevision { get; init; }
    public int Repetitions { get; init; }
    public int Oublis { get; init; }

    public bool EstNouvelle => Stabilite is null;
}

/// <summary>Réglages choisis par l'étudiant (écran Réglages).</summary>
public sealed class Reglages
{
    /// <summary>Nombre de cartes qui suffit pour « réussir » sa journée (US9).</summary>
    public int ObjectifMinimal { get; set; } = 5;
    /// <summary>Nombre maximal de cartes proposées par jour (R3).</summary>
    public int PlafondQuotidien { get; set; } = 40;
    /// <summary>Nombre maximal de nouvelles cartes par jour (R4).</summary>
    public int NouvellesParJour { get; set; } = 10;
    /// <summary>Taille de la session en mode reprise (R2).</summary>
    public int TailleReprise { get; set; } = 10;
    /// <summary>Nombre de jours d'absence qui déclenche le mode reprise (R2).</summary>
    public int JoursAvantReprise { get; set; } = 7;
    /// <summary>Probabilité de bonne réponse visée par la planification (0,80 à 0,95).</summary>
    public double RetentionCible { get; set; } = 0.9;
    public string? DossierPacks { get; set; }
    public string? DossierCours { get; set; }
    /// <summary>Clé du thème choisi : « systeme », « clair », « sombre », « ocean »… (voir Apparence.Themes).</summary>
    public string Theme { get; set; } = "systeme";
    /// <summary>Couleur d'accent choisie, au format « #RRGGBB », ou null pour celle du thème.</summary>
    public string? CouleurAccent { get; set; }
}
