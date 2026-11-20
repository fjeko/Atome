using System.Text.Json;
using Dapper;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;
using Revisions.Core.Sessions;

namespace Revisions.Data;

/// <summary>Une carte mise de côté, avec de quoi la reconnaître dans la liste.</summary>
public sealed record CarteMiseDeCote(string CarteId, string Matiere, string Seance, string Extrait);

/// <summary>Cartes à réviser, enregistrement des réponses et sessions.</summary>
public sealed class DepotRevisions(BaseDeDonnees baseDeDonnees)
{
    // Partie commune à toutes les requêtes qui chargent des cartes avec leur état.
    private const string SelectCartes =
        """
        SELECT c.id AS Id, c.section_id AS SectionId, c.type AS Type, c.difficulte AS Difficulte, c.donnees_json AS Donnees,
               c.pack_id AS PackId, m.nom AS Matiere, p.seance AS Seance, p.partie AS Partie,
               e.stabilite AS Stabilite, e.difficulte_fsrs AS DifficulteFsrs, e.echeance AS Echeance,
               e.derniere_revision AS DerniereRevision, e.repetitions AS Repetitions, e.oublis AS Oublis
        FROM carte c
        JOIN pack p ON p.id = c.pack_id
        JOIN matiere m ON m.id = p.matiere_id
        JOIN etat_carte e ON e.carte_id = c.id
        WHERE c.archivee = 0 AND e.suspendue = 0
        """;

    /// <summary>Cartes déjà vues dont l'échéance est arrivée.</summary>
    public List<CarteAReviser> CartesDues(DateOnly jour) =>
        Charger(SelectCartes + " AND e.stabilite IS NOT NULL AND e.echeance <= @jour", new { jour = BaseDeDonnees.Iso(jour) });

    /// <summary>Nombre de cartes dues (pour l'accueil, sans charger leur contenu).</summary>
    public int NombreDeDues(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM etat_carte e JOIN carte c ON c.id = e.carte_id
            WHERE c.archivee = 0 AND e.suspendue = 0 AND e.stabilite IS NOT NULL AND e.echeance <= @jour
            """, new { jour = BaseDeDonnees.Iso(jour) });
    }

    /// <summary>
    /// Toutes les nouvelles cartes, sans leur contenu. L'ordre de découverte est décidé ensuite
    /// par <see cref="ChoixNouvelles"/> (ordre du cours, tourniquet des matières, planning).
    /// </summary>
    public List<CandidateNouvelle> CandidatesNouvelles()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Id, string PackId, long Position, long Prioritaire)>(
                """
                SELECT c.id, c.pack_id, c.position, e.prioritaire
                FROM carte c JOIN etat_carte e ON e.carte_id = c.id
                WHERE c.archivee = 0 AND e.suspendue = 0 AND e.stabilite IS NULL
                """)
            .Select(l => new CandidateNouvelle(l.Id, l.PackId, (int)l.Position, l.Prioritaire != 0))
            .ToList();
    }

    /// <summary>Cartes déjà vues qui ne sont pas encore dues (candidates à l'avancement avant un devoir), sans leur contenu.</summary>
    public List<CarteVue> CartesVuesNonDues(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Id, string PackId, double Stabilite, string Derniere, string Echeance)>(
                """
                SELECT c.id, c.pack_id, e.stabilite, e.derniere_revision, e.echeance
                FROM carte c JOIN etat_carte e ON e.carte_id = c.id
                WHERE c.archivee = 0 AND e.suspendue = 0 AND e.stabilite IS NOT NULL AND e.derniere_revision IS NOT NULL AND e.echeance > @jour
                """, new { jour = BaseDeDonnees.Iso(jour) })
            .Select(l => new CarteVue(l.Id, l.PackId, l.Stabilite, BaseDeDonnees.Jour(l.Derniere)!.Value, BaseDeDonnees.Jour(l.Echeance)!.Value))
            .ToList();
    }

    /// <summary>
    /// Toutes les cartes déjà vues, sans leur contenu, avec leurs oublis FSRS et leur nombre de « Raté » dans l'historique
    /// (de quoi composer n'importe quel type de session sans relire la base).
    /// </summary>
    public List<CarteVue> CartesConnues(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Id, string PackId, double Stabilite, string Derniere, string Echeance, long Oublis, long Ratees)>(
                """
                SELECT c.id, c.pack_id, e.stabilite,
                       COALESCE(e.derniere_revision, e.echeance, @jour), COALESCE(e.echeance, @jour), e.oublis,
                       (SELECT COUNT(*) FROM revision r WHERE r.carte_id = c.id AND r.note = 1)
                FROM carte c JOIN etat_carte e ON e.carte_id = c.id
                WHERE c.archivee = 0 AND e.suspendue = 0 AND e.stabilite IS NOT NULL
                """, new { jour = BaseDeDonnees.Iso(jour) })
            .Select(l => new CarteVue(l.Id, l.PackId, l.Stabilite, BaseDeDonnees.Jour(l.Derniere)!.Value,
                BaseDeDonnees.Jour(l.Echeance)!.Value, (int)l.Oublis, (int)l.Ratees))
            .ToList();
    }

    /// <summary>
    /// Réponses par séance depuis une date, hors première découverte d'une carte
    /// (une réponse est « réussie » si elle n'est pas « Raté »).
    /// </summary>
    public Dictionary<string, ReussiteSeance> ReussiteParSeance(DateOnly depuis)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string PackId, long Reponses, long Reussies)>(
                """
                SELECT c.pack_id, COUNT(*), SUM(r.note > 1)
                FROM revision r JOIN carte c ON c.id = r.carte_id
                WHERE c.archivee = 0 AND r.stabilite_avant IS NOT NULL AND r.jour >= @depuis
                GROUP BY c.pack_id
                """, new { depuis = BaseDeDonnees.Iso(depuis) })
            .ToDictionary(l => l.PackId, l => new ReussiteSeance((int)l.Reponses, (int)l.Reussies));
    }

    /// <summary>Charge les cartes demandées, dans l'ordre de la liste.</summary>
    public List<CarteAReviser> ChargerCartes(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return [];
        var parId = Charger(SelectCartes + " AND c.id IN @ids", new { ids }).ToDictionary(c => c.Carte.Id);
        return ids.Where(parId.ContainsKey).Select(id => parId[id]).ToList();
    }

    /// <summary>Toutes les cartes d'une séance, dans l'ordre du cours (US12).</summary>
    public List<CarteAReviser> CartesDuPack(string packId) =>
        Charger(SelectCartes + " AND c.pack_id = @packId ORDER BY c.position", new { packId });

    /// <summary>Cartes « exercice » d'une matière, dans l'ordre du cours (TD solo, exercices rédigés).</summary>
    public List<CarteAReviser> CartesExerciceDeLaMatiere(string matiere) =>
        Charger(SelectCartes + " AND c.type = 'exercice' AND m.nom = @matiere ORDER BY p.ordre, p.id, c.position", new { matiere });

    /// <summary>Cartes déjà vues d'une section (session « point faible »).</summary>
    public List<CarteAReviser> CartesDeLaSection(string sectionId) =>
        Charger(SelectCartes + " AND c.section_id = @sectionId ORDER BY c.position", new { sectionId });

    private List<CarteAReviser> Charger(string sql, object parametres)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<LigneCarte>(sql, parametres).Select(l => l.VersCarte()).ToList();
    }

    /// <summary>Les dernières notes d'une carte (1 = Raté … 4 = Facile), de la plus récente à la plus ancienne.</summary>
    public List<int> DernieresNotes(string carteId, int nombre)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<int>("SELECT note FROM revision WHERE carte_id = @carteId ORDER BY id DESC LIMIT @nombre",
            new { carteId, nombre }).ToList();
    }

    /// <summary>Met une carte de côté (jamais proposée ni révisée) ou la remet en jeu. Son historique et son état FSRS sont gardés.</summary>
    public void DefinirSuspendue(string carteId, bool suspendue)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("UPDATE etat_carte SET suspendue = @s WHERE carte_id = @carteId", new { s = suspendue ? 1 : 0, carteId });
    }

    /// <summary>Les cartes mises de côté, de quoi les retrouver et les réactiver (Statistiques).</summary>
    public List<CarteMiseDeCote> CartesMisesDeCote()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Id, string Matiere, string Seance, string Donnees)>(
                """
                SELECT c.id, m.nom, p.seance, c.donnees_json
                FROM carte c
                JOIN pack p ON p.id = c.pack_id
                JOIN matiere m ON m.id = p.matiere_id
                JOIN etat_carte e ON e.carte_id = c.id
                WHERE c.archivee = 0 AND e.suspendue = 1
                ORDER BY m.nom, p.seance, c.position
                """)
            .Select(l =>
            {
                var contenu = JsonSerializer.Deserialize<ContenuCarte>(l.Donnees) ?? new ContenuCarte();
                return new CarteMiseDeCote(l.Id, l.Matiere, l.Seance,
                    Core.Packs.TexteSimple.Extrait(contenu.Recto ?? contenu.Question ?? contenu.Enonce, 110));
            })
            .ToList();
    }

    /// <summary>Ouvre une session et renvoie son numéro.</summary>
    public long CreerSession(string mode, DateOnly jour, DateTimeOffset maintenant)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<long>(
            "INSERT INTO session_revision (debut, mode, jour) VALUES (@debut, @mode, @jour); SELECT last_insert_rowid();",
            new { debut = maintenant.ToString("O"), mode, jour = BaseDeDonnees.Iso(jour) });
    }

    public void TerminerSession(long sessionId, DateTimeOffset maintenant)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("UPDATE session_revision SET fin = @fin WHERE id = @sessionId",
            new { fin = maintenant.ToString("O"), sessionId });
    }

    /// <summary>
    /// Enregistre une réponse : une ligne d'historique, le nouvel état de la carte et l'XP gagnée.
    /// Les trois écritures sont dans la même transaction.
    /// </summary>
    public void EnregistrerReponse(long sessionId, EtatCarte avant, ResultatPlanification resultat, Note note,
        DateOnly jour, DateTimeOffset maintenant, int dureeMs, bool etaitDue, int xp)
    {
        var apres = resultat.NouvelEtat;
        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();

        connexion.Execute(
            """
            INSERT INTO revision (carte_id, session_id, date_heure, jour, note, duree_ms, etait_due, retrievabilite,
                                  stabilite_avant, stabilite_apres, difficulte_avant, difficulte_apres, xp)
            VALUES (@carte, @sessionId, @date, @jour, @note, @dureeMs, @etaitDue, @r, @sAvant, @sApres, @dAvant, @dApres, @xp)
            """,
            new
            {
                carte = avant.CarteId, sessionId, date = maintenant.ToString("O"), jour = BaseDeDonnees.Iso(jour),
                note = (int)note, dureeMs, etaitDue, r = resultat.Retrievabilite,
                sAvant = avant.Stabilite, sApres = apres.Stabilite, dAvant = avant.DifficulteFsrs, dApres = apres.DifficulteFsrs, xp,
            }, transaction);

        connexion.Execute(
            """
            UPDATE etat_carte SET stabilite = @Stabilite, difficulte_fsrs = @DifficulteFsrs, echeance = @echeance,
                                  derniere_revision = @derniere, repetitions = @Repetitions, oublis = @Oublis
            WHERE carte_id = @CarteId
            """,
            new
            {
                apres.Stabilite, apres.DifficulteFsrs, echeance = BaseDeDonnees.Iso(apres.Echeance!.Value),
                derniere = BaseDeDonnees.Iso(apres.DerniereRevision!.Value), apres.Repetitions, apres.Oublis, apres.CarteId,
            }, transaction);

        if (xp > 0)
            connexion.Execute("UPDATE profil SET xp_total = xp_total + @xp WHERE id = 1", new { xp }, transaction);

        transaction.Commit();
    }

    /// <summary>Ligne lue en base, convertie ensuite en objets du cœur métier.</summary>
    private sealed class LigneCarte
    {
        public string Id { get; set; } = "";
        public string SectionId { get; set; } = "";
        public string Type { get; set; } = "";
        public int Difficulte { get; set; }
        public string Donnees { get; set; } = "";
        public string PackId { get; set; } = "";
        public string Matiere { get; set; } = "";
        public string Seance { get; set; } = "";
        public string Partie { get; set; } = "";
        public double? Stabilite { get; set; }
        public double? DifficulteFsrs { get; set; }
        public string? Echeance { get; set; }
        public string? DerniereRevision { get; set; }
        public int Repetitions { get; set; }
        public int Oublis { get; set; }

        public CarteAReviser VersCarte()
        {
            var type = Type switch
            {
                "qcm" => TypeCarte.Qcm,
                "exercice" => TypeCarte.Exercice,
                _ => TypeCarte.Question,
            };
            var contenu = JsonSerializer.Deserialize<ContenuCarte>(Donnees) ?? new ContenuCarte();
            var etat = new EtatCarte
            {
                CarteId = Id,
                Stabilite = Stabilite,
                DifficulteFsrs = DifficulteFsrs,
                Echeance = BaseDeDonnees.Jour(Echeance),
                DerniereRevision = BaseDeDonnees.Jour(DerniereRevision),
                Repetitions = Repetitions,
                Oublis = Oublis,
            };
            return new CarteAReviser(new Carte(Id, SectionId, type, Difficulte, contenu), etat, PackId, Matiere, Seance, Partie);
        }
    }
}
