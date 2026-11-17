using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Revisions.Core.Modeles;
using Revisions.Core.Packs;

namespace Revisions.Data;

/// <summary>Ce qui est arrivé à un fichier lors d'un import.</summary>
public enum StatutImport
{
    Importe,
    MisAJour,
    Inchange,
    Refuse,
}

/// <summary>Compte rendu de l'import d'un fichier.</summary>
public sealed record ResultatImport(string Fichier, string? PackId, string? Seance, StatutImport Statut, IReadOnlyList<Probleme> Problemes);

/// <summary>
/// Importe les packs dans la base. Règle la plus importante : réimporter un pack ne fait
/// jamais perdre l'historique. Les cartes sont reconnues par leur identifiant ; celles qui ont
/// disparu du fichier sont archivées, jamais supprimées.
/// </summary>
public sealed class ImportateurPacks(BaseDeDonnees baseDeDonnees, TimeProvider horloge)
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Importe tous les fichiers .json d'un dossier (sous-dossiers compris).</summary>
    /// <param name="dossierPacks">Dossier à parcourir.</param>
    /// <param name="dossierCours">Dossier « Cours », pour vérifier que le document d'origine existe (facultatif).</param>
    public List<ResultatImport> ImporterDossier(string dossierPacks, string? dossierCours)
    {
        var resultats = new List<ResultatImport>();
        if (!Directory.Exists(dossierPacks)) return resultats;

        // Pour repérer deux fichiers qui portent le même identifiant de pack.
        var fichierParPack = new Dictionary<string, string>();
        // Packs déjà connus avec la taille et la date de leur fichier : un fichier inchangé n'est même pas relu.
        Dictionary<string, (string Id, string Seance, long Taille, string Modifie)> connus;
        using (var connexion = baseDeDonnees.Ouvrir())
            connus = connexion.Query<(string Chemin, string Id, string Seance, long? Taille, string? Modifie)>(
                    "SELECT chemin_fichier, id, seance, fichier_taille, fichier_modifie FROM pack WHERE chemin_fichier IS NOT NULL AND fichier_taille IS NOT NULL")
                .GroupBy(x => x.Chemin).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Seance, g.First().Taille!.Value, g.First().Modifie!));
        foreach (string fichier in Directory.EnumerateFiles(dossierPacks, "*.json", SearchOption.AllDirectories).Order())
        {
            var info = new FileInfo(fichier);
            if (connus.TryGetValue(fichier, out var c) && c.Taille == info.Length && c.Modifie == Horodatage(info))
            {
                fichierParPack.TryAdd(c.Id, fichier);
                resultats.Add(new ResultatImport(fichier, c.Id, c.Seance, StatutImport.Inchange, []));
                continue;
            }
            resultats.Add(ImporterFichier(fichier, dossierCours, fichierParPack));
        }
        return resultats;
    }

    private static string Horodatage(FileInfo info) => info.LastWriteTimeUtc.Ticks.ToString();

    /// <summary>Importe un fichier de pack.</summary>
    public ResultatImport ImporterFichier(string fichier, string? dossierCours, Dictionary<string, string>? fichierParPack = null)
    {
        byte[] octets;
        try
        {
            octets = File.ReadAllBytes(fichier);
        }
        catch (IOException e)
        {
            return Refuser(fichier, null, [new Probleme(Gravite.Erreur, "", "Lecture impossible : " + e.Message)]);
        }

        string empreinte = Convert.ToHexString(SHA256.HashData(octets));
        using var connexion = baseDeDonnees.Ouvrir();

        // Fichier déjà importé et inchangé : rien à faire (c'est le cas courant au démarrage).
        var connus = connexion.Query<(string Id, string Seance)>(
            "SELECT id, seance FROM pack WHERE empreinte = @empreinte", new { empreinte }).ToList();
        if (connus.Count > 0)
        {
            var p = connus[0];
            fichierParPack?.TryAdd(p.Id, fichier);
            Marquer(connexion, p.Id, fichier);
            return new ResultatImport(fichier, p.Id, p.Seance, StatutImport.Inchange, []);
        }

        var lecture = LecteurPack.Lire(Encoding.UTF8.GetString(octets));
        if (lecture.Pack is not { } pack)
            return Refuser(fichier, null, lecture.Problemes, connexion);

        var problemes = new List<Probleme>(lecture.Problemes);

        if (fichierParPack is not null && !fichierParPack.TryAdd(pack.Id, fichier))
        {
            problemes.Add(new Probleme(Gravite.Erreur, "id",
                $"Le pack « {pack.Id} » existe déjà dans le fichier « {fichierParPack[pack.Id]} »."));
            return Refuser(fichier, pack.Id, problemes, connexion);
        }

        // Un identifiant de carte ou de section ne peut pas appartenir à deux packs :
        // sinon l'historique de l'une serait attribué à l'autre.
        var idsCartes = pack.Cartes.Select(c => c.Id).ToList();
        var idsSections = pack.Sections.Select(s => s.Id).ToList();
        foreach (var (id, autre) in connexion.Query<(string, string)>(
                     "SELECT id, pack_id FROM carte WHERE pack_id <> @pack AND id IN @ids", new { pack = pack.Id, ids = idsCartes }))
            problemes.Add(new Probleme(Gravite.Erreur, "cartes", $"Identifiant déjà utilisé par le pack « {autre} ».", id));
        foreach (var (id, autre) in connexion.Query<(string, string)>(
                     "SELECT id, pack_id FROM section WHERE pack_id <> @pack AND id IN @ids", new { pack = pack.Id, ids = idsSections }))
            problemes.Add(new Probleme(Gravite.Erreur, "sections", $"Identifiant de section « {id} » déjà utilisé par le pack « {autre} »."));
        if (problemes.Any(x => x.Gravite == Gravite.Erreur))
            return Refuser(fichier, pack.Id, problemes, connexion);

        if (dossierCours is not null && !File.Exists(Path.Combine(dossierCours, pack.SourceFichier)))
            problemes.Add(new Probleme(Gravite.Avertissement, "source.fichier", $"Document d'origine introuvable : {pack.SourceFichier}"));

        // Tout ou rien : si une étape échoue, la transaction est annulée et la base reste intacte.
        string maintenant = horloge.GetUtcNow().ToString("O");
        using var transaction = connexion.BeginTransaction();
        bool existait = connexion.ExecuteScalar<long>("SELECT COUNT(*) FROM pack WHERE id = @Id", new { pack.Id }, transaction) > 0;
        Enregistrer(connexion, transaction, pack, fichier, empreinte, maintenant);
        Marquer(connexion, pack.Id, fichier, transaction);
        var statut = existait ? StatutImport.MisAJour : StatutImport.Importe;
        Journaliser(connexion, transaction, fichier, pack.Id, statut, problemes, maintenant);
        transaction.Commit();

        return new ResultatImport(fichier, pack.Id, pack.Seance, statut, problemes);
    }

    /// <summary>Retient la taille et la date du fichier, pour que le prochain scan puisse l'ignorer sans le relire.</summary>
    private static void Marquer(SqliteConnection connexion, string packId, string fichier, SqliteTransaction? transaction = null)
    {
        var info = new FileInfo(fichier);
        connexion.Execute("UPDATE pack SET fichier_taille = @taille, fichier_modifie = @modifie WHERE id = @packId AND chemin_fichier = @fichier",
            new { taille = info.Length, modifie = Horodatage(info), fichier, packId }, transaction);
    }

    private static void Enregistrer(SqliteConnection connexion, SqliteTransaction transaction, Pack pack,
        string fichier, string empreinte, string maintenant)
    {
        // Matière : créée si elle n'existe pas encore.
        connexion.Execute(
            """
            INSERT INTO matiere (nom, bloc) VALUES (@Matiere, @Bloc)
            ON CONFLICT(nom) DO UPDATE SET bloc = COALESCE(excluded.bloc, bloc)
            """, new { pack.Matiere, pack.Bloc }, transaction);
        long matiereId = connexion.ExecuteScalar<long>("SELECT id FROM matiere WHERE nom = @Matiere", new { pack.Matiere }, transaction);

        // Pack : inséré, ou mis à jour s'il existe (même identifiant).
        connexion.Execute(
            """
            INSERT INTO pack (id, matiere_id, partie, sequence, seance, ordre, source_fichier, source_pages, resume,
                              notions_json, format_version, chemin_fichier, empreinte, date_import, date_maj)
            VALUES (@Id, @matiereId, @Partie, @Sequence, @Seance, @Ordre, @SourceFichier, @SourcePages, @Resume,
                    @notions, @FormatVersion, @fichier, @empreinte, @maintenant, @maintenant)
            ON CONFLICT(id) DO UPDATE SET
              matiere_id = excluded.matiere_id, partie = excluded.partie, sequence = excluded.sequence,
              seance = excluded.seance, ordre = excluded.ordre, source_fichier = excluded.source_fichier,
              source_pages = excluded.source_pages, resume = excluded.resume, notions_json = excluded.notions_json,
              format_version = excluded.format_version, chemin_fichier = excluded.chemin_fichier,
              empreinte = excluded.empreinte, date_maj = excluded.date_maj
            """,
            new
            {
                pack.Id, matiereId, pack.Partie, pack.Sequence, pack.Seance, pack.Ordre, pack.SourceFichier,
                pack.SourcePages, pack.Resume, notions = JsonSerializer.Serialize(pack.Notions),
                pack.FormatVersion, fichier, empreinte, maintenant,
            }, transaction);

        // Sections : mises à jour ou ajoutées ; « archivee = 0 » réactive une section revenue.
        for (int i = 0; i < pack.Sections.Count; i++)
        {
            var s = pack.Sections[i];
            connexion.Execute(
                """
                INSERT INTO section (id, pack_id, position, titre, contenu, page_source, notions_json, archivee)
                VALUES (@Id, @pack, @position, @Titre, @Contenu, @PageSource, @notions, 0)
                ON CONFLICT(id) DO UPDATE SET
                  position = excluded.position, titre = excluded.titre, contenu = excluded.contenu,
                  page_source = excluded.page_source, notions_json = excluded.notions_json, archivee = 0
                """,
                new { s.Id, pack = pack.Id, position = i, s.Titre, s.Contenu, s.PageSource, notions = JsonSerializer.Serialize(s.Notions) },
                transaction);
        }

        // Cartes : le contenu est mis à jour, mais etat_carte et revision ne sont jamais touchés ici.
        for (int i = 0; i < pack.Cartes.Count; i++)
        {
            var c = pack.Cartes[i];
            connexion.Execute(
                """
                INSERT INTO carte (id, pack_id, section_id, position, type, difficulte, donnees_json, archivee, date_ajout)
                VALUES (@Id, @pack, @SectionId, @position, @type, @Difficulte, @donnees, 0, @maintenant)
                ON CONFLICT(id) DO UPDATE SET
                  section_id = excluded.section_id, position = excluded.position, type = excluded.type,
                  difficulte = excluded.difficulte, donnees_json = excluded.donnees_json, archivee = 0
                """,
                new
                {
                    c.Id, pack = pack.Id, c.SectionId, position = i, type = c.Type.ToString().ToLowerInvariant(),
                    c.Difficulte, donnees = JsonSerializer.Serialize(c.Contenu, OptionsJson), maintenant,
                }, transaction);
            // Une carte nouvelle reçoit un état « jamais vue » ; une carte connue garde le sien.
            connexion.Execute("INSERT OR IGNORE INTO etat_carte (carte_id) VALUES (@Id)", new { c.Id }, transaction);
        }

        // Ce qui a disparu du fichier est archivé (l'historique reste).
        connexion.Execute("UPDATE carte SET archivee = 1 WHERE pack_id = @pack AND id NOT IN @ids",
            new { pack = pack.Id, ids = pack.Cartes.Select(c => c.Id) }, transaction);
        connexion.Execute("UPDATE section SET archivee = 1 WHERE pack_id = @pack AND id NOT IN @ids",
            new { pack = pack.Id, ids = pack.Sections.Select(s => s.Id) }, transaction);
    }

    private ResultatImport Refuser(string fichier, string? packId, IReadOnlyList<Probleme> problemes, SqliteConnection? connexion = null)
    {
        if (connexion is not null)
            Journaliser(connexion, null, fichier, packId, StatutImport.Refuse, problemes, horloge.GetUtcNow().ToString("O"));
        return new ResultatImport(fichier, packId, null, StatutImport.Refuse, problemes);
    }

    private static void Journaliser(SqliteConnection connexion, SqliteTransaction? transaction, string fichier,
        string? packId, StatutImport statut, IReadOnlyList<Probleme> problemes, string maintenant)
    {
        connexion.Execute(
            "INSERT INTO journal_import (date_heure, fichier, pack_id, resultat, messages_json) VALUES (@maintenant, @fichier, @packId, @resultat, @messages)",
            new
            {
                maintenant, fichier, packId,
                resultat = statut switch { StatutImport.Importe => "importe", StatutImport.MisAJour => "mis_a_jour", _ => "refuse" },
                messages = JsonSerializer.Serialize(problemes.Select(p => p.ToString())),
            }, transaction);
    }
}
