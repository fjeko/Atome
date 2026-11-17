using System.Globalization;
using Dapper;
using Revisions.Core.Apparence;
using Revisions.Core.MisesAJour;
using Revisions.Core.Modeles;
using Revisions.Core.Planification;

namespace Revisions.Data;

/// <summary>Réussite d'une matière sur une période.</summary>
public sealed class ReussiteMatiere
{
    public string Matiere { get; set; } = "";
    public int Total { get; set; }
    public int Reussies { get; set; }
    public double Taux => Total == 0 ? 0 : (double)Reussies / Total;
}

/// <summary>Avancement d'une matière : combien de cartes vues et maîtrisées.</summary>
public sealed class MaitriseMatiere
{
    public string Matiere { get; set; } = "";
    public int Cartes { get; set; }
    public int Vues { get; set; }
    public int Maitrisees { get; set; }
}

/// <summary>Une section fragile : là où une session ciblée serait la plus utile.</summary>
public sealed record PointFaible(string SectionId, string Titre, string Matiere, string Seance, int Cartes, double RappelMoyen, int Oublis);

/// <summary>Profil (XP), réglages et chiffres pour l'accueil et les statistiques.</summary>
public sealed class DepotProfil(BaseDeDonnees baseDeDonnees) : IStockageCleValeur
{
    // ---- XP ------------------------------------------------------------------------------

    public int XpTotal()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT xp_total FROM profil WHERE id = 1");
    }

    /// <summary>Ajoute de l'XP (jamais de valeur négative : l'XP ne baisse pas).</summary>
    public void AjouterXp(int xp)
    {
        if (xp <= 0) return;
        using var connexion = baseDeDonnees.Ouvrir();
        connexion.Execute("UPDATE profil SET xp_total = xp_total + @xp WHERE id = 1", new { xp });
    }

    // ---- Réglages (table clé / valeur) -----------------------------------------------------

    public string? LireValeur(string cle)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<string?>("SELECT valeur FROM reglage WHERE cle = @cle", new { cle });
    }

    public void EcrireValeur(string cle, string? valeur)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        if (valeur is null)
            connexion.Execute("DELETE FROM reglage WHERE cle = @cle", new { cle });
        else
            connexion.Execute(
                "INSERT INTO reglage (cle, valeur) VALUES (@cle, @valeur) ON CONFLICT(cle) DO UPDATE SET valeur = excluded.valeur",
                new { cle, valeur });
    }

    public Reglages ChargerReglages()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var valeurs = connexion.Query<(string Cle, string Valeur)>("SELECT cle, valeur FROM reglage")
            .ToDictionary(x => x.Cle, x => x.Valeur);
        var defaut = new Reglages();

        int Entier(string cle, int parDefaut, int min, int max) =>
            valeurs.TryGetValue(cle, out var v) && int.TryParse(v, out int n) ? Math.Clamp(n, min, max) : parDefaut;

        return new Reglages
        {
            ObjectifMinimal = Entier("objectif_minimal", defaut.ObjectifMinimal, 1, 100),
            PlafondQuotidien = Entier("plafond_quotidien", defaut.PlafondQuotidien, 5, 500),
            NouvellesParJour = Entier("nouvelles_par_jour", defaut.NouvellesParJour, 0, 100),
            TailleReprise = Entier("taille_reprise", defaut.TailleReprise, 3, 100),
            JoursAvantReprise = Entier("jours_avant_reprise", defaut.JoursAvantReprise, 2, 60),
            RetentionCible = valeurs.TryGetValue("retention_cible", out var r)
                             && double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out double retention)
                ? Math.Clamp(retention, 0.80, 0.95)
                : defaut.RetentionCible,
            DossierPacks = valeurs.GetValueOrDefault("dossier_packs"),
            DossierCours = valeurs.GetValueOrDefault("dossier_cours"),
            CoursLibres = valeurs.GetValueOrDefault("cours_libres") switch { "1" => true, "0" => false, _ => null },
            // Une valeur abîmée ou inconnue retombe sur un choix sûr plutôt que de faire planter l'affichage.
            // Jamais choisi (premier lancement) : Crépuscule.
            Theme = valeurs.TryGetValue("theme", out var cleTheme) ? Themes.Trouver(cleTheme).Cle : "crepuscule",
            CouleurAccent = Couleur.TryParse(valeurs.GetValueOrDefault("couleur_accent"), out var accent) ? accent.Hex : null,
            // Activé tant qu'on ne l'a pas explicitement désactivé.
            VerifierMisesAJour = valeurs.GetValueOrDefault("verifier_mises_a_jour") != "0",
            // Désactivé tant qu'on ne l'a pas explicitement activé.
            NouvellesNonLues = valeurs.GetValueOrDefault("nouvelles_non_lues") == "1",
        };
    }

    public void EnregistrerReglages(Reglages r)
    {
        EcrireValeur("objectif_minimal", r.ObjectifMinimal.ToString());
        EcrireValeur("plafond_quotidien", r.PlafondQuotidien.ToString());
        EcrireValeur("nouvelles_par_jour", r.NouvellesParJour.ToString());
        EcrireValeur("taille_reprise", r.TailleReprise.ToString());
        EcrireValeur("jours_avant_reprise", r.JoursAvantReprise.ToString());
        EcrireValeur("retention_cible", r.RetentionCible.ToString(CultureInfo.InvariantCulture));
        EcrireValeur("dossier_packs", r.DossierPacks);
        EcrireValeur("dossier_cours", r.DossierCours);
        EcrireValeur("nouvelles_non_lues", r.NouvellesNonLues ? "1" : "0");
        EcrireValeur("theme", r.Theme);
        EcrireValeur("couleur_accent", r.CouleurAccent);
        EcrireValeur("verifier_mises_a_jour", r.VerifierMisesAJour ? "1" : "0");
    }

    /// <summary>Jour où le mode reprise a commencé, ou null s'il n'est pas actif.</summary>
    public DateOnly? DebutReprise
    {
        get => BaseDeDonnees.Jour(LireValeur("reprise_debut"));
        set => EcrireValeur("reprise_debut", value is { } j ? BaseDeDonnees.Iso(j) : null);
    }

    /// <summary>Dernier jour où le bonus d'objectif a été donné (une fois par jour au plus).</summary>
    public DateOnly? JourBonusObjectif
    {
        get => BaseDeDonnees.Jour(LireValeur("objectif_bonus_jour"));
        set => EcrireValeur("objectif_bonus_jour", value is { } j ? BaseDeDonnees.Iso(j) : null);
    }

    /// <summary>Dernier jour où l'application a été ouverte (sert au message de la mascotte).</summary>
    public DateOnly? DernierJourOuverture
    {
        get => BaseDeDonnees.Jour(LireValeur("derniere_ouverture"));
        set => EcrireValeur("derniere_ouverture", value is { } j ? BaseDeDonnees.Iso(j) : null);
    }

    // ---- Activité ------------------------------------------------------------------------

    /// <summary>Tous les jours où au moins une carte a été révisée.</summary>
    public List<DateOnly> JoursActifs()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<string>("SELECT DISTINCT jour FROM revision ORDER BY jour")
            .Select(j => BaseDeDonnees.Jour(j)!.Value).ToList();
    }

    /// <summary>Nombre de réponses données chaque jour depuis une date (pour le graphique de régularité).</summary>
    public Dictionary<DateOnly, int> ReponsesParJour(DateOnly depuis)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<(string Jour, int Nombre)>(
                "SELECT jour, COUNT(*) FROM revision WHERE jour >= @depuis GROUP BY jour", new { depuis = BaseDeDonnees.Iso(depuis) })
            .ToDictionary(x => BaseDeDonnees.Jour(x.Jour)!.Value, x => x.Nombre);
    }

    /// <summary>Nombre de cartes différentes révisées ce jour-là.</summary>
    public int CartesFaites(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(DISTINCT carte_id) FROM revision WHERE jour = @jour",
            new { jour = BaseDeDonnees.Iso(jour) });
    }

    /// <summary>Nombre de nouvelles cartes découvertes ce jour-là.</summary>
    public int NouvellesVues(DateOnly jour)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.ExecuteScalar<int>("SELECT COUNT(*) FROM revision WHERE jour = @jour AND stabilite_avant IS NULL",
            new { jour = BaseDeDonnees.Iso(jour) });
    }

    /// <summary>Durée médiane d'une réponse, en secondes, sur les 200 dernières (25 s tant qu'il n'y a pas d'historique).</summary>
    public double SecondesParCarte()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var durees = connexion.Query<int>(
            "SELECT duree_ms FROM revision WHERE duree_ms IS NOT NULL ORDER BY id DESC LIMIT 200").Order().ToList();
        // Médiane des derniers temps de réponse, environ 20 s par carte sans historique (voir EstimationDuree).
        return Revisions.Core.Sessions.EstimationDuree.SecondesParCarte(durees);
    }

    // ---- Statistiques --------------------------------------------------------------------

    /// <summary>Taux de bonnes réponses par matière depuis une date (hors première découverte d'une carte).</summary>
    public List<ReussiteMatiere> ReussiteParMatiere(DateOnly depuis)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<ReussiteMatiere>(
            """
            SELECT m.nom AS Matiere, COUNT(*) AS Total, SUM(r.note >= 2) AS Reussies
            FROM revision r
            JOIN carte c ON c.id = r.carte_id
            JOIN pack p ON p.id = c.pack_id
            LEFT JOIN collection col ON col.cle = p.collection
            JOIN matiere m ON m.id = p.matiere_id
            WHERE r.jour >= @depuis AND r.stabilite_avant IS NOT NULL AND COALESCE(col.masque, 0) = 0
            GROUP BY m.nom ORDER BY m.nom
            """, new { depuis = BaseDeDonnees.Iso(depuis) }).ToList();
    }

    public List<MaitriseMatiere> MaitriseParMatiere()
    {
        using var connexion = baseDeDonnees.Ouvrir();
        return connexion.Query<MaitriseMatiere>(
            """
            SELECT m.nom AS Matiere, COUNT(*) AS Cartes,
                   SUM(e.stabilite IS NOT NULL) AS Vues, COALESCE(SUM(e.stabilite >= 21), 0) AS Maitrisees
            FROM carte c
            JOIN pack p ON p.id = c.pack_id
            JOIN matiere m ON m.id = p.matiere_id
            JOIN etat_carte e ON e.carte_id = c.id
            WHERE c.archivee = 0
            GROUP BY m.nom ORDER BY m.nom
            """).ToList();
    }

    /// <summary>
    /// Les sections les plus fragiles : celles dont les cartes déjà vues ont la plus faible
    /// probabilité de rappel aujourd'hui (au moins 2 cartes vues pour que ce soit parlant).
    /// </summary>
    public List<PointFaible> PointsFaibles(DateOnly jour, int maximum)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        var lignes = connexion.Query<(string SectionId, string Titre, string Matiere, string Seance, double Stabilite, string Derniere, long Oublis)>(
            """
            SELECT s.id, s.titre, m.nom, p.seance, e.stabilite, e.derniere_revision, e.oublis
            FROM carte c
            JOIN section s ON s.id = c.section_id
            JOIN pack p ON p.id = c.pack_id
            JOIN matiere m ON m.id = p.matiere_id
            JOIN etat_carte e ON e.carte_id = c.id
            WHERE c.archivee = 0 AND e.stabilite IS NOT NULL
            """);

        return lignes
            .GroupBy(l => l.SectionId)
            .Where(g => g.Count() >= 2)
            .Select(g =>
            {
                var premiere = g.First();
                double rappel = g.Average(l =>
                    Fsrs.Retrievabilite(jour.DayNumber - BaseDeDonnees.Jour(l.Derniere)!.Value.DayNumber, l.Stabilite));
                return new PointFaible(g.Key, premiere.Titre, premiere.Matiere, premiere.Seance, g.Count(), rappel, (int)g.Sum(l => l.Oublis));
            })
            .Where(p => p.RappelMoyen < 0.9 || p.Oublis > 0)
            .OrderBy(p => p.RappelMoyen)
            .Take(maximum)
            .ToList();
    }
}
