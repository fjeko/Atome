using System.Text.Json;
using Dapper;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Modeles;

namespace Revisions.Data;

/// <summary>Une séance complète pour l'écran de lecture.</summary>
public sealed record SeanceALire(
    string PackId,
    string Matiere,
    string Partie,
    string Seance,
    string Resume,
    IReadOnlyList<string> Notions,
    string SourceFichier,
    IReadOnlyList<Section> Sections);

/// <summary>Lecture des cours : liste des séances et contenu d'une séance.</summary>
public sealed class DepotBibliotheque(BaseDeDonnees baseDeDonnees)
{
    /// <summary>Nombre de packs en base, visibles ou non (sert à distinguer « rien d'importé » de « tout est masqué »).</summary>
    public int CompterPacks()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM pack");
    }

    /// <summary>Toutes les séances, triées pour l'arbre bloc &gt; matière &gt; partie &gt; séance.</summary>
    public List<ResumeSeance> ListerSeances(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<ResumeSeance>(
            """
            SELECT p.id AS PackId, m.bloc AS Bloc, m.nom AS Matiere, p.partie AS Partie, p.sequence AS Sequence,
                   p.seance AS Seance, p.ordre AS Ordre, (p.date_lecture IS NOT NULL) AS Lu,
                   COUNT(c.id) AS NbCartes,
                   COALESCE(SUM(e.stabilite >= 21), 0) AS NbMaitrisees,
                   COALESCE(SUM(e.stabilite IS NOT NULL AND e.echeance <= @jour), 0) AS NbDues,
                   COALESCE(SUM(c.id IS NOT NULL AND e.stabilite IS NULL), 0) AS NbNouvelles
            FROM pack p
            JOIN matiere m ON m.id = p.matiere_id
            LEFT JOIN collection col ON col.cle = p.collection   -- visibilité : uniquement la collection
            LEFT JOIN carte c ON c.pack_id = p.id AND c.archivee = 0
            LEFT JOIN etat_carte e ON e.carte_id = c.id
            WHERE COALESCE(col.masque, 0) = 0
            GROUP BY p.id
            ORDER BY m.bloc, m.nom, p.partie, p.sequence, p.ordre, p.seance
            """, new { jour = BaseDeDonnees.Iso(jour) }).ToList();
    }

    /// <summary>Charge une séance avec ses sections (non archivées), ou null si elle n'existe pas.</summary>
    public SeanceALire? ChargerSeance(string packId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var pack = connexion.QueryFirstOrDefault<LignePack>(
            """
            SELECT p.id AS Id, m.nom AS Matiere, p.partie AS Partie, p.seance AS Seance, p.resume AS Resume,
                   p.notions_json AS Notions, p.source_fichier AS SourceFichier
            FROM pack p JOIN matiere m ON m.id = p.matiere_id WHERE p.id = @packId
            """, new { packId });
        if (pack is null) return null;

        var sections = connexion.Query<LigneSection>(
                """
                SELECT id AS Id, titre AS Titre, contenu AS Contenu, page_source AS PageSource, notions_json AS Notions
                FROM section WHERE pack_id = @packId AND archivee = 0 ORDER BY position
                """, new { packId })
            .Select(s => new Section(s.Id, s.Titre, s.Contenu, s.PageSource, Liste(s.Notions)))
            .ToList();

        return new SeanceALire(pack.Id, pack.Matiere, pack.Partie, pack.Seance, pack.Resume, Liste(pack.Notions), pack.SourceFichier, sections);
    }

    /// <summary>Identifiant du pack qui contient une section (pour « Revoir la notion »).</summary>
    public string? PackDeLaSection(string sectionId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<string?>("SELECT pack_id FROM section WHERE id = @sectionId", new { sectionId });
    }

    /// <summary>
    /// Note qu'une séance a été ouverte en lecture. Critère retenu pour « séance lue » : l'ouverture de l'écran de lecture,
    /// qui affiche tout de suite la première section (simple et prévisible ; pas de suivi du défilement).
    /// Ses nouvelles cartes sont alors débloquées, et elle compte pour « Ce que je viens de lire » pendant 7 jours.
    /// </summary>
    public void MarquerLue(string packId, DateTimeOffset maintenant)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("UPDATE pack SET date_lecture = COALESCE(date_lecture, @date), derniere_lecture = @date WHERE id = @packId",
            new { packId, date = maintenant.ToString("O") });
    }

    /// <summary>Jour de la dernière ouverture en lecture de chaque séance lue.</summary>
    public Dictionary<string, DateOnly> DernieresLectures()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Id, string Date)>("SELECT id, derniere_lecture FROM pack WHERE derniere_lecture IS NOT NULL")
            // Date complète au format « O » (heure locale) : les 10 premiers caractères donnent le jour.
            .Select(l => (l.Id, Jour: l.Date.Length >= 10
                && DateOnly.TryParseExact(l.Date[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var j) ? j : (DateOnly?)null))
            .Where(l => l.Jour is not null)
            .ToDictionary(l => l.Id, l => l.Jour!.Value);
    }

    private static List<string> Liste(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private sealed class LignePack
    {
        public string Id { get; set; } = "";
        public string Matiere { get; set; } = "";
        public string Partie { get; set; } = "";
        public string Seance { get; set; } = "";
        public string Resume { get; set; } = "";
        public string? Notions { get; set; }
        public string SourceFichier { get; set; } = "";
    }

    private sealed class LigneSection
    {
        public string Id { get; set; } = "";
        public string Titre { get; set; } = "";
        public string Contenu { get; set; } = "";
        public int? PageSource { get; set; }
        public string? Notions { get; set; }
    }
}
