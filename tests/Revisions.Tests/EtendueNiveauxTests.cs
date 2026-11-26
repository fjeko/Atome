using Revisions.Core.Bibliotheque;

namespace Revisions.Tests;

/// <summary>Étendue « Du A1 au C1 » affichée sur la tuile d'une langue.</summary>
public class EtendueNiveauxTests
{
    private static NoeudBibliotheque Matiere(params string[] enfants)
    {
        var n = new NoeudBibliotheque { Cle = "m", Titre = "Anglais", TitreCourt = "Anglais" };
        foreach (string e in enfants) n.Enfants.Add(new NoeudBibliotheque { Cle = e, Titre = e, TitreCourt = e });
        return n;
    }

    [Fact]
    public void Une_langue_affiche_ses_niveaux_extremes() =>
        Assert.Equal("Du A1 au C1", Matiere("A0 – Commencer ici", "B1 – Intermédiaire", "A1 – Débutant", "C1 – Avancé", "Méthode – Oral").EtendueNiveaux);

    [Fact]
    public void Une_matiere_ordinaire_n_en_a_pas() =>
        Assert.Null(Matiere("Algèbre", "Analyse").EtendueNiveaux);
}
