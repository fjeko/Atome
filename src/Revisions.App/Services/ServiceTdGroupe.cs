using Revisions.Core.TdGroupe;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>
/// Crée le catalogue des TD en groupe. Ajouter un nouveau type de TD (graphes, ordonnancement…) :
/// écrire son générateur dans Revisions.Core/TdGroupe, puis l'ajouter à la liste ci-dessous
/// (et un thème dans TdGroupeViewModel).
/// </summary>
public static class ServiceTdGroupe
{
    public static CatalogueTd CreerCatalogue(DepotBibliotheque bibliotheque, DepotRevisions revisions, EtatApplication etat) =>
        new([
            new GenerateurMatrices(), new GenerateurInverse(), new GenerateurGraphes(), new GenerateurOrdonnancement(),
            new GenerateurIpv4(), new GenerateurDroitsLinux(), new GenerateurSql(),
            new GenerateurSeance(id => ChargerSeance(id, bibliotheque, revisions, etat)),
        ]);

    /// <summary>
    /// Contenu d'une séance pour un TD « séance ». L'identifiant du code est en minuscules :
    /// on retrouve le pack sans tenir compte des majuscules.
    /// </summary>
    private static ContenuSeance? ChargerSeance(string packId, DepotBibliotheque bibliotheque, DepotRevisions revisions, EtatApplication etat)
    {
        string? vraiId = bibliotheque.ListerSeances(etat.Jour)
            .FirstOrDefault(s => string.Equals(s.PackId, packId, StringComparison.OrdinalIgnoreCase))?.PackId;
        if (vraiId is null || bibliotheque.ChargerSeance(vraiId) is not { } seance) return null;
        var cartes = revisions.CartesDuPack(vraiId).Select(c => c.Carte).ToList();
        return new ContenuSeance(seance.Seance, cartes, seance.Sections.ToDictionary(s => s.Id, s => s.Titre));
    }
}
