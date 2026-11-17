using System.Globalization;
using Dapper;
using Revisions.Core.Signalements;

namespace Revisions.Data;

/// <summary>Signalements d'erreurs dans le contenu (cartes et sections), enregistrés en local.</summary>
public sealed class DepotSignalements(BaseDeDonnees baseDeDonnees)
{
    /// <summary>Enregistre un signalement ; les informations du pack (empreinte, source…) sont lues dans la base.</summary>
    /// <param name="titre">Titre de la section (null pour une carte).</param>
    /// <param name="recto">Recto de la carte, ou contenu de la section : tel qu'affiché.</param>
    /// <param name="verso">Verso de la carte (null pour une section).</param>
    public Signalement Enregistrer(CibleSignalement cible, string cibleId, string packId, MotifSignalement motif,
        string? commentaire, string? titre, string recto, string? verso, DateTimeOffset maintenant)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var pack = connexion.QueryFirstOrDefault<InfosPack>(
            """
            SELECT p.empreinte AS Empreinte, p.date_maj AS DateMaj, m.nom AS Matiere, p.seance AS Seance, p.source_fichier AS Source
            FROM pack p JOIN matiere m ON m.id = p.matiere_id WHERE p.id = @packId
            """, new { packId }) ?? new InfosPack();
        int? page = cible == CibleSignalement.Section
            ? connexion.ExecuteScalar<int?>("SELECT page_source FROM section WHERE id = @cibleId", new { cibleId })
            : connexion.ExecuteScalar<int?>(
                "SELECT s.page_source FROM carte c JOIN section s ON s.id = c.section_id WHERE c.id = @cibleId", new { cibleId });

        var signalement = new Signalement
        {
            Id = Guid.NewGuid().ToString(),
            DateHeure = maintenant,
            Cible = cible,
            CibleId = cibleId,
            PackId = packId,
            Motif = motif,
            Commentaire = string.IsNullOrWhiteSpace(commentaire) ? null : commentaire.Trim(),
            Titre = titre,
            TexteRecto = recto,
            TexteVerso = verso,
            PackEmpreinte = pack.Empreinte,
            PackDateMaj = pack.DateMaj,
            Matiere = pack.Matiere,
            Seance = pack.Seance,
            SourceFichier = pack.Source,
            PageSource = page,
        };
        connexion.Execute(
            """
            INSERT INTO signalement (id, date_heure, cible, cible_id, pack_id, motif, commentaire, titre, texte_recto, texte_verso,
                                     pack_empreinte, pack_date_maj, matiere, seance, source_fichier, page_source, etat)
            VALUES (@Id, @Date, @Cible, @CibleId, @PackId, @Motif, @Commentaire, @Titre, @TexteRecto, @TexteVerso,
                    @PackEmpreinte, @PackDateMaj, @Matiere, @Seance, @SourceFichier, @PageSource, 'nouveau')
            """,
            new
            {
                signalement.Id,
                Date = maintenant.ToString("O"),
                Cible = VocabulaireSignalement.Code(cible),
                signalement.CibleId,
                signalement.PackId,
                Motif = VocabulaireSignalement.Code(motif),
                signalement.Commentaire,
                signalement.Titre,
                signalement.TexteRecto,
                signalement.TexteVerso,
                signalement.PackEmpreinte,
                signalement.PackDateMaj,
                signalement.Matiere,
                signalement.Seance,
                signalement.SourceFichier,
                signalement.PageSource,
            });
        return signalement;
    }

    /// <summary>Tous les signalements, du plus récent au plus ancien.</summary>
    public List<Signalement> Lister()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<Ligne>(
            """
            SELECT id AS Id, date_heure AS DateHeure, cible AS Cible, cible_id AS CibleId, pack_id AS PackId, motif AS Motif,
                   commentaire AS Commentaire, titre AS Titre, texte_recto AS TexteRecto, texte_verso AS TexteVerso,
                   pack_empreinte AS PackEmpreinte, pack_date_maj AS PackDateMaj, matiere AS Matiere, seance AS Seance,
                   source_fichier AS SourceFichier, page_source AS PageSource, etat AS Etat
            FROM signalement ORDER BY date_heure DESC
            """).Select(l => new Signalement
            {
                Id = l.Id,
                DateHeure = DateTimeOffset.Parse(l.DateHeure, CultureInfo.InvariantCulture),
                Cible = VocabulaireSignalement.CibleDepuis(l.Cible),
                CibleId = l.CibleId,
                PackId = l.PackId,
                Motif = VocabulaireSignalement.MotifDepuis(l.Motif),
                Commentaire = l.Commentaire,
                Titre = l.Titre,
                TexteRecto = l.TexteRecto,
                TexteVerso = l.TexteVerso,
                PackEmpreinte = l.PackEmpreinte,
                PackDateMaj = l.PackDateMaj,
                Matiere = l.Matiere,
                Seance = l.Seance,
                SourceFichier = l.SourceFichier,
                PageSource = l.PageSource,
                Exporte = l.Etat == "exporte",
            }).ToList();
    }

    /// <summary>Supprime un signalement (il ne sera plus exporté).</summary>
    public void Supprimer(string id)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("DELETE FROM signalement WHERE id = @id", new { id });
    }

    /// <summary>Passe les signalements donnés à l'état « exporté ».</summary>
    public void MarquerExportes(IEnumerable<string> ids)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        foreach (string id in ids)
            connexion.Execute("UPDATE signalement SET etat = 'exporte' WHERE id = @id", new { id }, transaction);
        transaction.Commit();
    }

    /// <summary>Nombre de signalements pas encore exportés.</summary>
    public int CompterNouveaux()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM signalement WHERE etat = 'nouveau'");
    }

    private sealed class InfosPack
    {
        public string? Empreinte { get; set; }
        public string? DateMaj { get; set; }
        public string? Matiere { get; set; }
        public string? Seance { get; set; }
        public string? Source { get; set; }
    }

    private sealed class Ligne
    {
        public string Id { get; set; } = "";
        public string DateHeure { get; set; } = "";
        public string Cible { get; set; } = "";
        public string CibleId { get; set; } = "";
        public string PackId { get; set; } = "";
        public string Motif { get; set; } = "";
        public string? Commentaire { get; set; }
        public string? Titre { get; set; }
        public string TexteRecto { get; set; } = "";
        public string? TexteVerso { get; set; }
        public string? PackEmpreinte { get; set; }
        public string? PackDateMaj { get; set; }
        public string? Matiere { get; set; }
        public string? Seance { get; set; }
        public string? SourceFichier { get; set; }
        public int? PageSource { get; set; }
        public string Etat { get; set; } = "";
    }
}
