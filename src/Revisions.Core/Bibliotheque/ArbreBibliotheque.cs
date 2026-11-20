using System.Text.RegularExpressions;

namespace Revisions.Core.Bibliotheque;

/// <summary>Une séance dans la bibliothèque, avec ses compteurs (une ligne lue en base).</summary>
public sealed class ResumeSeance
{
    public string PackId { get; set; } = "";
    public string? Bloc { get; set; }
    public string Matiere { get; set; } = "";
    public string Partie { get; set; } = "";
    public string? Sequence { get; set; }
    public string Seance { get; set; } = "";
    public int? Ordre { get; set; }
    public bool Lu { get; set; }
    public int NbCartes { get; set; }
    public int NbMaitrisees { get; set; }
    public int NbDues { get; set; }
    public int NbNouvelles { get; set; }
    /// <summary>Dernier parcours « Lire puis contrôler » terminé (date « O »), ou null.</summary>
    public string? DateControle { get; set; }
    /// <summary>Score de ce contrôle, en %.</summary>
    public int? ScoreControle { get; set; }

    /// <summary>« Lue et contrôlée le 20/11 · 83 % », ou null si la séance n'a jamais été contrôlée.</summary>
    public string? TexteControle => DateControle is { Length: >= 10 } d
        && DateOnly.TryParseExact(d[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var jour)
        ? $"Lue et contrôlée le {jour:dd/MM} · {ScoreControle ?? 0} %"
        : null;
    /// <summary>Vrai pour un cours libre intégré à Atome (badge « Libre » dans la bibliothèque).</summary>
    public bool Libre => PackId.StartsWith(Revisions.Core.Packs.ContenuLibre.PrefixeId, StringComparison.Ordinal);
}

/// <summary>
/// Un niveau de la bibliothèque (une matière, une compétence, une séquence…).
/// Il contient des sous-niveaux, des séances, ou les deux.
/// </summary>
public sealed class NoeudBibliotheque
{
    /// <summary>Texte qui identifie le niveau parmi ses voisins (sert à retrouver un chemin).</summary>
    public required string Cle { get; init; }
    /// <summary>Titre complet, affiché sur la tuile.</summary>
    public required string Titre { get; init; }
    /// <summary>Titre court, pour le fil d'Ariane (« Bloc 1 », « B1.2 », « SQ1 »).</summary>
    public required string TitreCourt { get; init; }
    /// <summary>Petite mention au-dessus du titre (le bloc d'une matière), ou null.</summary>
    public string? Etiquette { get; init; }
    /// <summary>Pictogramme de la tuile (seulement pour les matières), ou null.</summary>
    public Pictogramme? Icone { get; init; }

    public List<NoeudBibliotheque> Enfants { get; } = [];
    public List<ResumeSeance> Seances { get; } = [];

    // Totaux de tout ce que contient le niveau (sous-niveaux compris).
    public int NbSeances { get; private set; }
    public int NbCartes { get; private set; }
    public int NbDues { get; private set; }
    public int NbNouvelles { get; private set; }
    public int NbMaitrisees { get; private set; }

    /// <summary>Part des cartes maîtrisées, entre 0 et 1.</summary>
    public double Maitrise => NbCartes == 0 ? 0 : (double)NbMaitrisees / NbCartes;

    /// <summary>Toutes les séances du niveau, sous-niveaux compris.</summary>
    public IEnumerable<ResumeSeance> ToutesLesSeances() => Seances.Concat(Enfants.SelectMany(e => e.ToutesLesSeances()));

    /// <summary>Matière du niveau (celle de sa première séance) : elle donne sa couleur à la tuile.</summary>
    public ResumeSeance? PremiereSeance => ToutesLesSeances().FirstOrDefault();

    /// <summary>Vrai si toutes les séances du niveau sont des cours libres (badge « Libre » sur la tuile).</summary>
    public bool ToutLibre => ToutesLesSeances().All(s => s.Libre) && PremiereSeance is not null;

    /// <summary>Trie les enfants et les séances, puis additionne les compteurs en remontant.</summary>
    internal void Finaliser()
    {
        Enfants.Sort((a, b) => ArbreBibliotheque.OrdreNaturel.Compare(a.Cle, b.Cle));
        Seances.Sort((a, b) =>
        {
            int parOrdre = (a.Ordre ?? int.MaxValue).CompareTo(b.Ordre ?? int.MaxValue);
            return parOrdre != 0 ? parOrdre : ArbreBibliotheque.OrdreNaturel.Compare(a.Seance, b.Seance);
        });

        foreach (var enfant in Enfants) enfant.Finaliser();
        NbSeances = Seances.Count + Enfants.Sum(e => e.NbSeances);
        NbCartes = Seances.Sum(s => s.NbCartes) + Enfants.Sum(e => e.NbCartes);
        NbDues = Seances.Sum(s => s.NbDues) + Enfants.Sum(e => e.NbDues);
        NbNouvelles = Seances.Sum(s => s.NbNouvelles) + Enfants.Sum(e => e.NbNouvelles);
        NbMaitrisees = Seances.Sum(s => s.NbMaitrisees) + Enfants.Sum(e => e.NbMaitrisees);
    }
}

/// <summary>
/// Range les séances en niveaux pour la bibliothèque en tuiles :
/// matière → compétence (B1.2…) → partie ou séquence (SQ1…) → séquence du pack → séances.
/// Un niveau qui n'existe pas pour une séance est simplement sauté : en maths, par exemple,
/// il n'y a pas de compétence, on passe de la matière aux séquences.
/// </summary>
public static partial class ArbreBibliotheque
{
    /// <summary>« B1.2 », « B1.2 Partie II »… au début du champ « partie » d'un pack.</summary>
    [GeneratedRegex(@"^(B\d+\.\d+(?:\s+Partie\s+[IVX\d]+)?)\s*(?:[–—-]\s*)?(\S.*)$")]
    private static partial Regex DebutCompetence();

    /// <summary>« Thème 3 », « SQ1 », « Séquence 10 »… au début d'un titre.</summary>
    [GeneratedRegex(@"^(Thème\s+\d+|SQ\s?\d+|Séquence\s+\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DebutSequence();

    /// <summary>Le code « B1.2 » au début d'une compétence (pour le fil d'Ariane).</summary>
    [GeneratedRegex(@"^B\d+\.\d+")]
    private static partial Regex CodeCompetence();

    /// <summary>Séparateur compétence › thème dans le champ « partie » des cours libres.</summary>
    private const string SeparateurTheme = " › ";

    /// <summary>Tri « humain » : « Séquence 2 » avant « Séquence 10 », « Partie II » avant « Partie IX » (voir <see cref="Ordre.TriNaturel"/>).</summary>
    public static IComparer<string> OrdreNaturel { get; } = Ordre.TriNaturel.Comparateur;

    /// <summary>Construit l'arbre complet. La racine représente toute la bibliothèque.</summary>
    public static NoeudBibliotheque Construire(IEnumerable<ResumeSeance> seances)
    {
        var racine = new NoeudBibliotheque { Cle = "", Titre = "Bibliothèque", TitreCourt = "Bibliothèque" };
        foreach (var seance in seances)
        {
            var noeud = racine;
            foreach (var niveau in Niveaux(seance))
            {
                var enfant = noeud.Enfants.FirstOrDefault(e => e.Cle == niveau.Cle);
                if (enfant is null)
                {
                    enfant = niveau;
                    noeud.Enfants.Add(enfant);
                }
                noeud = enfant;
            }
            noeud.Seances.Add(seance);
        }
        racine.Finaliser();
        return racine;
    }

    /// <summary>
    /// Sépare la compétence du reste dans le champ « partie » :
    /// « B1.2 Partie I – SQ1 Corrections… » → (« B1.2 Partie I », « SQ1 Corrections… »).
    /// Sans compétence reconnue, la partie est rendue telle quelle.
    /// </summary>
    public static (string? Competence, string Reste) DecouperPartie(string partie)
    {
        // Format des cours libres : « B1.1 – Gérer le patrimoine informatique › Thème 1 – Inventaire… ».
        int separateur = partie.IndexOf(SeparateurTheme, StringComparison.Ordinal);
        if (separateur > 0 && CodeCompetence().IsMatch(partie))
            return (partie[..separateur].Trim(), partie[(separateur + SeparateurTheme.Length)..].Trim());

        var correspondance = DebutCompetence().Match(partie.Trim());
        return correspondance.Success
            ? (Regex.Replace(correspondance.Groups[1].Value, @"\s+", " "), correspondance.Groups[2].Value.Trim())
            : (null, partie.Trim());
    }

    /// <summary>Clés des niveaux traversés puis de la séance elle-même (« § » + identifiant du pack) : sert à enregistrer les acquis.</summary>
    public static List<string> Chemin(ResumeSeance s) => [.. Niveaux(s).Select(n => n.Cle), "§" + s.PackId];

    /// <summary>Les niveaux à traverser pour atteindre une séance (les niveaux absents sont omis).</summary>
    private static IEnumerable<NoeudBibliotheque> Niveaux(ResumeSeance s)
    {
        // Matière. Si son nom commence par son bloc (« Bloc 1 – Support… »), on ne le répète pas.
        string titreMatiere = s.Matiere;
        string courtMatiere = s.Matiere;
        if (!string.IsNullOrWhiteSpace(s.Bloc) && s.Matiere.StartsWith(s.Bloc, StringComparison.CurrentCultureIgnoreCase))
        {
            string reste = s.Matiere[s.Bloc.Length..].TrimStart(' ', '–', '—', '-', ':');
            if (reste.Length > 0)
            {
                titreMatiere = reste;
                courtMatiere = s.Bloc;
            }
        }
        // Le bloc fait partie de la clé pour que les matières restent rangées par bloc.
        yield return new NoeudBibliotheque
        {
            Cle = $"{s.Bloc} {s.Matiere}".Trim(),
            Titre = titreMatiere,
            TitreCourt = courtMatiere,
            Etiquette = string.IsNullOrWhiteSpace(s.Bloc) ? null : s.Bloc,
            Icone = Pictogrammes.PourMatiere(s.Matiere),
        };

        // Compétence puis partie, toutes deux tirées du champ « partie ».
        var (competence, partie) = DecouperPartie(s.Partie);
        if (competence is not null)
            yield return new NoeudBibliotheque { Cle = competence, Titre = competence, TitreCourt = CompetenceCourte(competence) };
        if (partie.Length > 0)
            yield return new NoeudBibliotheque { Cle = partie, Titre = partie, TitreCourt = Court(partie) };

        // Séquence du pack, quand elle est renseignée.
        if (!string.IsNullOrWhiteSpace(s.Sequence))
            yield return new NoeudBibliotheque { Cle = s.Sequence, Titre = s.Sequence, TitreCourt = Court(s.Sequence) };
    }

    /// <summary>« B1.1 – Gérer le patrimoine informatique » → « B1.1 » ; « B1.2 Partie I » reste tel quel.</summary>
    private static string CompetenceCourte(string competence)
    {
        var code = CodeCompetence().Match(competence);
        return code.Success && competence.Contains('–') ? code.Value : competence;
    }

    /// <summary>Version courte d'un titre pour le fil d'Ariane : « Thème 2 – Graphes » → « Thème 2 » ; « SQ1 – Calcul matriciel » → « SQ1 ».</summary>
    private static string Court(string titre)
    {
        var correspondance = DebutSequence().Match(titre);
        return correspondance.Success ? correspondance.Value : titre;
    }

    /// <summary>
    /// Suit un chemin de clés depuis la racine et renvoie les niveaux traversés.
    /// Si un niveau n'existe plus (pack retiré), on s'arrête au dernier trouvé.
    /// </summary>
    public static List<NoeudBibliotheque> Suivre(NoeudBibliotheque racine, IEnumerable<string> cles)
    {
        var chemin = new List<NoeudBibliotheque>();
        var noeud = racine;
        foreach (string cle in cles)
        {
            var suivant = noeud.Enfants.FirstOrDefault(e => e.Cle == cle);
            if (suivant is null) break;
            chemin.Add(suivant);
            noeud = suivant;
        }
        return chemin;
    }
}
