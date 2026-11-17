using Revisions.Core.Bibliotheque;
using Revisions.Core.Ordre;

namespace Revisions.Tests;

/// <summary>Tri naturel (nombres, chiffres romains), ordre du cours et tourniquet des matières.</summary>
public class OrdreTests
{
    // ---- Tri naturel ------------------------------------------------------------------------

    [Theory]
    [InlineData("SQ2", "SQ10")]
    [InlineData("B1.2", "B1.10")]
    [InlineData("Séance 9", "Séance 10")]
    [InlineData("S01", "S2")]
    [InlineData("abc", "ABD")]               // sans tenir compte de la casse
    [InlineData("Écrire", "Fichier")]        // accents à la française
    [InlineData("SQ1", "SQ1 Titre")]         // le plus court d'abord
    public void Tri_naturel_des_nombres_et_des_lettres(string avant, string apres)
    {
        Assert.True(TriNaturel.Comparer(avant, apres) < 0);
        Assert.True(TriNaturel.Comparer(apres, avant) > 0);
    }

    [Theory]
    [InlineData("I", 1)]
    [InlineData("II", 2)]
    [InlineData("III", 3)]
    [InlineData("IV", 4)]
    [InlineData("V", 5)]
    [InlineData("VI", 6)]
    [InlineData("IX", 9)]
    [InlineData("X", 10)]
    [InlineData("XIV", 14)]
    [InlineData("XXXIX", 39)]
    public void Chiffres_romains_valides(string romain, int valeur) => Assert.Equal(valeur, TriNaturel.Romain(romain));

    [Theory]
    [InlineData("")]
    [InlineData("IIII")]
    [InlineData("VX")]
    [InlineData("IL")]
    [InlineData("CV")]
    [InlineData("ii")]
    [InlineData("Ix")]
    public void Ce_qui_n_est_pas_un_chiffre_romain_est_refuse(string texte) => Assert.Null(TriNaturel.Romain(texte));

    [Theory]
    [InlineData("B1.2 Partie II – SQ4", "B1.2 Partie 2 – SQ4")]
    [InlineData("Partie IX", "Partie 9")]
    [InlineData("CV et réseaux sociaux", "CV et réseaux sociaux")]   // « CV » n'est pas un nombre
    [InlineData("VISITE", "VISITE")]                                 // un mot qui commence par V reste un mot
    [InlineData("SQ1 Titre", "SQ1 Titre")]
    public void Normaliser_ne_remplace_que_les_mots_romains(string texte, string attendu) =>
        Assert.Equal(attendu, TriNaturel.Normaliser(texte));

    [Fact]
    public void Les_parties_en_chiffres_romains_sont_triees_par_valeur()
    {
        var parties = new[] { "Partie X", "Partie IX", "Partie II", "Partie V", "Partie IV", "Partie I" };
        var triees = parties.OrderBy(p => p, TriNaturel.Comparateur).ToList();
        Assert.Equal(["Partie I", "Partie II", "Partie IV", "Partie V", "Partie IX", "Partie X"], triees);
    }

    // ---- Ordre du cours ----------------------------------------------------------------------

    private static ResumeSeance S(string id, string partie, int? ordre, string matiere = "Bloc 1 – Support", string? bloc = "Bloc 1", string? seance = null) =>
        new() { PackId = id, Matiere = matiere, Bloc = bloc, Partie = partie, Ordre = ordre, Seance = seance ?? id };

    [Fact]
    public void L_ordre_de_seance_ne_passe_pas_avant_la_partie()
    {
        // Le défaut corrigé : « ordre » repart de 1 dans chaque partie, la SQ5 séance 1 ne doit pas passer avant la SQ1 séance 2.
        var seances = new[]
        {
            S("sq5-s1", "B1.1 – SQ5 Sauvegardes", 1),
            S("sq1-s2", "B1.1 – SQ1 Patrimoine", 2),
            S("sq1-s1", "B1.1 – SQ1 Patrimoine", 1),
            S("sq10-s1", "B1.1 – SQ10 Autre", 1),
        };
        var triees = seances.OrderBy(s => s, OrdreDuCours.Instance).Select(s => s.PackId).ToList();
        Assert.Equal(["sq1-s1", "sq1-s2", "sq5-s1", "sq10-s1"], triees);
    }

    [Fact]
    public void Partie_I_avant_partie_II_puis_competence_suivante()
    {
        var seances = new[]
        {
            S("b13", "B1.3 – SQ1 Site web", 1),
            S("p2-sq4", "B1.2 Partie II – SQ4 Permissions", 1),
            S("p2-sq1", "B1.2 Partie II – SQ1 Postes", 3),
            S("p1-sq3", "B1.2 Partie I – SQ3 Objet", 1),
            S("b11", "B1.1 – SQ1 Patrimoine", 9),
        };
        var triees = seances.OrderBy(s => s, OrdreDuCours.Instance).Select(s => s.PackId).ToList();
        Assert.Equal(["b11", "p1-sq3", "p2-sq1", "p2-sq4", "b13"], triees);
    }

    [Fact]
    public void Sans_ordre_la_seance_passe_apres_et_le_titre_departage()
    {
        var seances = new[]
        {
            S("c", "SQ1", null, seance: "Séance 10"),
            S("b", "SQ1", null, seance: "Séance 2"),
            S("a", "SQ1", 1, seance: "Séance 99"),
        };
        Assert.Equal(["a", "b", "c"], seances.OrderBy(s => s, OrdreDuCours.Instance).Select(s => s.PackId));
    }

    [Fact]
    public void Les_matieres_sont_rangees_par_bloc()
    {
        var seances = new[]
        {
            S("b2", "SQ1", 1, "Bloc 2 – SLAM", "Bloc 2"),
            S("b10", "SQ1", 1, "Bloc 10 – Autre", "Bloc 10"),
            S("b1", "SQ1", 1, "Bloc 1 – Support", "Bloc 1"),
        };
        Assert.Equal(["b1", "b2", "b10"], seances.OrderBy(s => s, OrdreDuCours.Instance).Select(s => s.PackId));
    }

    // ---- Tourniquet --------------------------------------------------------------------------

    [Fact]
    public void Le_tourniquet_alterne_les_matieres_et_garde_l_ordre_interne()
    {
        var cartes = new[] { "M1", "M2", "M3", "A1", "B1", "B2" };
        var resultat = Tourniquet.Entrelacer(cartes, c => c[..1]);
        Assert.Equal(["M1", "A1", "B1", "M2", "B2", "M3"], resultat);
    }

    [Fact]
    public void Le_tourniquet_garde_tous_les_elements_une_seule_fois()
    {
        var cartes = Enumerable.Range(0, 50).Select(i => $"{"XYZ"[i % 3]}{i}").ToList();
        var resultat = Tourniquet.Entrelacer(cartes, c => c[..1]);
        Assert.Equal(cartes.Order(), resultat.Order());
    }

    [Fact]
    public void Tourniquet_vide_ou_d_une_seule_matiere()
    {
        Assert.Empty(Tourniquet.Entrelacer(Array.Empty<string>(), c => c));
        Assert.Equal(["a", "b", "c"], Tourniquet.Entrelacer(new[] { "a", "b", "c" }, _ => "seule"));
    }

    [Fact]
    public void Les_matieres_passent_dans_l_ordre_ou_elles_apparaissent()
    {
        // La liste est déjà triée (ordre du cours, ou date de période) : la première matière vue commence chaque tour.
        var resultat = Tourniquet.Entrelacer(new[] { "Bloc 10|x", "Bloc 2|y", "Bloc 10|w", "Bloc 1|z" }, c => c.Split('|')[0]);
        Assert.Equal(["Bloc 10|x", "Bloc 2|y", "Bloc 1|z", "Bloc 10|w"], resultat);
    }
}
