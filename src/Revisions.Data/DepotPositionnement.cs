using System.Text;
using System.Text.Json;
using Dapper;
using Revisions.Core.Acquis;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Positionnement;

namespace Revisions.Data;

/// <summary>Niveau enregistré pour une matière, par le test ou par le choix de l'étudiant.</summary>
/// <param name="Depart">Code du niveau par lequel le cours commence (« B1 »).</param>
/// <param name="Atteint">Code du plus haut niveau validé au test, ou null (débutant, ou niveau choisi sans test).</param>
/// <param name="ParTest">Vrai si le niveau vient du test, faux s'il a été choisi.</param>
/// <param name="Date">Date du positionnement (« yyyy-MM-dd »).</param>
public sealed record NiveauEnregistre(string Depart, string? Atteint, bool ParTest, string Date);

/// <summary>
/// Tests de positionnement : banques de questions (fichiers « *.positionnement.json » rangés avec les packs),
/// niveau enregistré par matière, et application de ce niveau aux acquis.
/// </summary>
public sealed class DepotPositionnement(BaseDeDonnees baseDeDonnees, DepotAcquis acquis)
{
    private const string Prefixe = "positionnement§";

    /// <summary>Lit toutes les banques valides trouvées dans les dossiers donnés (sous-dossiers compris).</summary>
    /// <param name="erreurs">Reçoit, pour chaque fichier refusé, son chemin et ses erreurs.</param>
    public static List<BanquePositionnement> ChargerBanques(IEnumerable<string?> dossiers, List<(string Fichier, IReadOnlyList<string> Erreurs)>? erreurs = null)
    {
        var banques = new List<BanquePositionnement>();
        foreach (string? dossier in dossiers)
        {
            if (dossier is null || !Directory.Exists(dossier)) continue;
            IEnumerable<string> fichiers;
            try { fichiers = Directory.EnumerateFiles(dossier, "*" + LecteurBanque.Extension, SearchOption.AllDirectories).Order().ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string fichier in fichiers)
            {
                LectureBanque lecture;
                try { lecture = LecteurBanque.Lire(File.ReadAllText(fichier, Encoding.UTF8)); }
                catch (IOException e) { lecture = new LectureBanque(null, ["Lecture impossible : " + e.Message]); }
                if (lecture.Banque is { } b)
                {
                    // Deux fichiers avec le même identifiant : le premier trouvé gagne.
                    if (banques.All(x => x.Id != b.Id)) banques.Add(b);
                }
                else erreurs?.Add((fichier, lecture.Erreurs));
            }
        }
        return banques;
    }

    /// <summary>Niveau enregistré pour cette banque, ou null si l'étudiant ne s'est pas encore positionné.</summary>
    public NiveauEnregistre? Lire(string banqueId)
    {
        using var connexion = baseDeDonnees.Ouvrir();
        string? json = connexion.ExecuteScalar<string?>("SELECT valeur FROM reglage WHERE cle = @cle", new { cle = Prefixe + banqueId });
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<NiveauEnregistre>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Enregistre le niveau de départ et l'applique aux acquis : les niveaux en dessous sont « révisés »
    /// (solides, ou corrects pour celui juste en dessous) et reviennent en rappels espacés ; le niveau de départ
    /// et ceux au-dessus repartent de « pas vu ». Les autres parties de la matière (mode d'emploi, méthode…) ne bougent pas.
    /// Les cartes qui ont déjà un historique gardent leur état.
    /// </summary>
    public void Enregistrer(BanquePositionnement banque, int depart, int? atteint, bool parTest, DateOnly jour)
    {
        depart = Math.Clamp(depart, 0, banque.Niveaux.Count - 1);
        var niveau = new NiveauEnregistre(banque.Niveaux[depart].Code,
            atteint is >= 0 ? banque.Niveaux[atteint.Value].Code : null, parTest, jour.ToString("yyyy-MM-dd"));
        using (var connexion = baseDeDonnees.Ouvrir())
            connexion.Execute("INSERT OR REPLACE INTO reglage (cle, valeur) VALUES (@cle, @valeur)",
                new { cle = Prefixe + banque.Id, valeur = JsonSerializer.Serialize(niveau) });

        foreach (var (chemin, avancement, comprehension) in PlanAcquis(banque, depart))
            acquis.Definir(chemin, avancement, comprehension);
        acquis.Recalculer(jour);
    }

    /// <summary>Réglages d'acquis à écrire pour un niveau de départ (chemin de la partie, avancement, compréhension).</summary>
    public static List<(string Chemin, Avancement? Avancement, Comprehension? Comprehension)> PlanAcquis(BanquePositionnement banque, int depart)
    {
        var plan = new List<(string, Avancement?, Comprehension?)>();
        for (int i = 0; i < banque.Niveaux.Count; i++)
        {
            // Même découpage que la bibliothèque : la clé de la partie, sans l'éventuel code de compétence.
            var (competence, reste) = ArbreBibliotheque.DecouperPartie(banque.Niveaux[i].Partie);
            var cles = new List<string> { banque.CleMatiere };
            if (competence is not null) cles.Add(competence);
            if (reste.Length > 0) cles.Add(reste);
            string chemin = CalculAcquis.Chemin(cles);
            if (i < depart)
                plan.Add((chemin, Avancement.Revise, i == depart - 1 ? Comprehension.Correct : Comprehension.Solide));
            else
                plan.Add((chemin, null, null));
        }
        return plan;
    }
}
