using System.IO;
using Revisions.Core.Packs;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une ligne du rapport d'import : un fichier, ce qui lui est arrivé, et les problèmes trouvés.</summary>
public sealed record LigneRapport(string Fichier, string Statut, bool EstRefuse, IReadOnlyList<string> Problemes);

/// <summary>Rapport affiché après un import : les refus d'abord, puis le reste.</summary>
public sealed class RapportImportViewModel
{
    public string Resume { get; }
    public IReadOnlyList<LigneRapport> Lignes { get; }

    public RapportImportViewModel(IReadOnlyList<ResultatImport> resultats)
    {
        Lignes = resultats
            .Where(r => r.Statut != StatutImport.Inchange || r.Problemes.Count > 0)
            .OrderBy(r => r.Statut == StatutImport.Refuse ? 0 : 1)
            .Select(r => new LigneRapport(
                r.Seance is null ? Path.GetFileName(r.Fichier) : $"{r.Seance}  ({Path.GetFileName(r.Fichier)})",
                r.Statut switch
                {
                    StatutImport.Importe => "Importé",
                    StatutImport.MisAJour => "Mis à jour",
                    StatutImport.Refuse => "Refusé",
                    _ => "Inchangé",
                },
                r.Statut == StatutImport.Refuse,
                r.Problemes
                    .OrderBy(p => p.Gravite)
                    .Select(p => (p.Gravite == Gravite.Erreur ? "Erreur – " : "À noter – ") + p)
                    .ToList()))
            .ToList();

        int inchanges = resultats.Count(r => r.Statut == StatutImport.Inchange);
        Resume = $"{Texte.Pluriel(resultats.Count, "fichier examiné", "fichiers examinés")}, dont {inchanges} déjà à jour.";
    }
}
