using Dapper;
using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;

namespace Revisions.Data;

/// <summary>
/// Acquis déclarés par l'étudiant (ce qu'il a déjà vu ou révisé hors de l'application)
/// et leur effet sur les cartes qui n'ont encore aucun historique.
/// </summary>
public sealed class DepotAcquis(BaseDeDonnees baseDeDonnees, DepotBibliotheque bibliotheque)
{
    private const string CleQuestionnaireVu = "questionnaire_vu";

    /// <summary>Tous les réglages, par chemin de niveau.</summary>
    public Dictionary<string, ReglageAcquis> Charger()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Chemin, string? Avancement, string? Comprehension)>(
                "SELECT chemin, avancement, comprehension FROM acquis")
            .ToDictionary(l => l.Chemin, l => new ReglageAcquis(LireAvancement(l.Avancement), LireComprehension(l.Comprehension)));
    }

    /// <summary>Règle (ou efface, si les deux valeurs sont null) l'acquis d'un niveau. Ne recalcule pas les cartes.</summary>
    public void Definir(string chemin, Avancement? avancement, Comprehension? comprehension)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        if (avancement is null && comprehension is null)
        {
            connexion.Execute("DELETE FROM acquis WHERE chemin = @chemin", new { chemin });
            return;
        }
        connexion.Execute(
            """
            INSERT INTO acquis (chemin, avancement, comprehension, date_maj) VALUES (@chemin, @av, @co, @date)
            ON CONFLICT(chemin) DO UPDATE SET avancement = @av, comprehension = @co, date_maj = @date
            """,
            new { chemin, av = EcrireAvancement(avancement), co = EcrireComprehension(comprehension), date = DateTime.Now.ToString("O") });
    }

    /// <summary>Vrai si le questionnaire de premier lancement doit être proposé.</summary>
    public bool QuestionnaireAProposer()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        bool vu = connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM reglage WHERE cle = @cle", new { cle = CleQuestionnaireVu }) > 0;
        bool profil = connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM acquis") > 0;
        return !vu && !profil;
    }

    /// <summary>Note que le questionnaire a été fait ou repoussé (« Plus tard ») : il ne s'affichera plus tout seul.</summary>
    public void MarquerQuestionnaireVu()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("INSERT OR REPLACE INTO reglage (cle, valeur) VALUES (@cle, '1')", new { cle = CleQuestionnaireVu });
    }

    /// <summary>
    /// Applique les acquis aux cartes qui n'ont encore AUCUNE révision enregistrée.
    /// Une carte qui a un vrai historique garde son état FSRS. Peut être relancé à volonté.
    /// </summary>
    /// <returns>Nombre de cartes modifiées.</returns>
    public int Recalculer(DateOnly jour, IReadOnlyList<ResumeSeance>? seances = null)
    {
        seances ??= bibliotheque.ListerSeances(jour);
        var reglages = Charger();
        var acquisParPack = seances.ToDictionary(s => s.PackId, s => CalculAcquis.Resoudre(reglages, ArbreBibliotheque.Chemin(s)));

        using var connexion = baseDeDonnees.Ouvrir();
        using var transaction = connexion.BeginTransaction();
        var cartes = connexion.Query<LigneCarte>(
            """
            SELECT c.id AS CarteId, c.pack_id AS PackId, e.stabilite AS Stabilite, e.echeance AS Echeance, e.prioritaire AS Prioritaire
            FROM carte c JOIN etat_carte e ON e.carte_id = c.id
            WHERE c.archivee = 0 AND NOT EXISTS (SELECT 1 FROM revision r WHERE r.carte_id = c.id)
            """, transaction: transaction).ToList();

        int modifiees = 0;
        foreach (var carte in cartes)
        {
            if (!acquisParPack.TryGetValue(carte.PackId, out var acquis)) continue;
            var depart = CalculAcquis.EtatInitial(acquis, carte.CarteId, jour);
            string? echeance = depart.Echeance is { } e ? BaseDeDonnees.Iso(e) : null;
            // On n'écrit que ce qui change.
            if (carte.Stabilite == depart.Stabilite && carte.Echeance == echeance && (carte.Prioritaire != 0) == depart.Prioritaire) continue;

            connexion.Execute(
                """
                UPDATE etat_carte SET stabilite = @s, difficulte_fsrs = @d, echeance = @e, derniere_revision = @r,
                                      repetitions = 0, oublis = 0, prioritaire = @p
                WHERE carte_id = @id
                """,
                new
                {
                    s = depart.Stabilite, d = depart.Difficulte, e = echeance,
                    r = depart.DerniereRevision is { } r ? BaseDeDonnees.Iso(r) : null,
                    p = depart.Prioritaire ? 1 : 0, id = carte.CarteId,
                }, transaction);
            modifiees++;
        }
        transaction.Commit();
        return modifiees;
    }

    private sealed class LigneCarte
    {
        public string CarteId { get; set; } = "";
        public string PackId { get; set; } = "";
        public double? Stabilite { get; set; }
        public string? Echeance { get; set; }
        public long Prioritaire { get; set; }
    }

    private static string? EcrireAvancement(Avancement? a) => a switch
    {
        Avancement.PasVu => "pas_vu", Avancement.Vu => "vu", Avancement.Revise => "revise", _ => null,
    };

    private static Avancement? LireAvancement(string? t) => t switch
    {
        "pas_vu" => Avancement.PasVu, "vu" => Avancement.Vu, "revise" => Avancement.Revise, _ => null,
    };

    private static string? EcrireComprehension(Comprehension? c) => c switch
    {
        Comprehension.Decouverte => "decouverte", Comprehension.Fragile => "fragile",
        Comprehension.Correct => "correct", Comprehension.Solide => "solide", _ => null,
    };

    private static Comprehension? LireComprehension(string? t) => t switch
    {
        "decouverte" => Comprehension.Decouverte, "fragile" => Comprehension.Fragile,
        "correct" => Comprehension.Correct, "solide" => Comprehension.Solide, _ => null,
    };
}
