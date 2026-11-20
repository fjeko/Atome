using System.Text.Json;
using Dapper;
using Revisions.Core.Parcours;

namespace Revisions.Data;

/// <summary>Parcours « Lire puis contrôler » : reprise d'un parcours en cours et dernier résultat de chaque séance.</summary>
public sealed class DepotParcours(BaseDeDonnees baseDeDonnees)
{
    /// <summary>Parcours en cours de la séance, ou null (aucun, ou déjà terminé).</summary>
    public EtatParcours? Charger(string packId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        string? json = connexion.ExecuteScalar<string?>("SELECT etat_json FROM parcours_lecture WHERE pack_id = @packId", new { packId });
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<EtatParcours>(json); }
        catch (JsonException) { return null; } // état illisible : on repart de zéro plutôt que de bloquer
    }

    /// <summary>Enregistre l'avancement (appelé après chaque action : on peut quitter à tout moment).</summary>
    public void Enregistrer(EtatParcours etat)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute(
            """
            INSERT INTO parcours_lecture (pack_id, etat_json) VALUES (@PackId, @json)
            ON CONFLICT (pack_id) DO UPDATE SET etat_json = excluded.etat_json
            """, new { etat.PackId, json = JsonSerializer.Serialize(etat) });
    }

    /// <summary>Oublie le parcours en cours (« Recommencer »), sans toucher au dernier résultat.</summary>
    public void Abandonner(string packId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("UPDATE parcours_lecture SET etat_json = NULL WHERE pack_id = @packId", new { packId });
    }

    /// <summary>Marque la séance « lue et contrôlée » avec la date et le score, et clôt le parcours en cours.</summary>
    public void Terminer(string packId, DateTimeOffset maintenant, int score)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute(
            """
            INSERT INTO parcours_lecture (pack_id, etat_json, date_controle, score) VALUES (@packId, NULL, @date, @score)
            ON CONFLICT (pack_id) DO UPDATE SET etat_json = NULL, date_controle = excluded.date_controle, score = excluded.score
            """, new { packId, date = maintenant.ToString("O"), score });
    }
}
