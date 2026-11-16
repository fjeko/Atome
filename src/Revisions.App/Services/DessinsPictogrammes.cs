using Revisions.Core.Bibliotheque;

namespace Revisions.App.Services;

/// <summary>
/// Le tracé de chaque pictogramme de matière, au format des chemins WPF (« M » = aller à,
/// « L/H/V » = ligne, « C/Q » = courbe, « Z » = fermer). Tous sont dessinés au trait, sans
/// remplissage, sur une grille de 24 × 24 : ils prennent la couleur et la taille qu'on leur donne.
/// </summary>
public static class DessinsPictogrammes
{
    public static string Pour(Pictogramme pictogramme) => pictogramme switch
    {
        // Tour d'ordinateur : boîtier, deux baies, bouton de marche.
        Pictogramme.Ordinateur =>
            "M7,2.5 H17 V21.5 H7 Z M10,6.5 H14 M10,9.5 H14 M11.2,17 A0.8,0.8 0 1 0 12.8,17 A0.8,0.8 0 1 0 11.2,17",

        // Fenêtre de logiciel : cadre, barre de titre, chevrons < > du code.
        Pictogramme.Code =>
            "M3,4.5 H21 V19.5 H3 Z M3,8.5 H21 M9.5,11.5 L7,14 L9.5,16.5 M14.5,11.5 L17,14 L14.5,16.5",

        // Personne à capuche derrière un ordinateur portable.
        Pictogramme.Securite =>
            "M7.5,12.5 V8 C7.5,4.5 9.5,2.5 12,2.5 C14.5,2.5 16.5,4.5 16.5,8 V12.5 " +
            "M9.8,12.5 V9 C9.8,7.3 14.2,7.3 14.2,9 V12.5 " +
            "M7.5,11 C5,11.8 4,13.5 4,15.5 M16.5,11 C19,11.8 20,13.5 20,15.5 " +
            "M5.5,15.5 H18.5 V20.5 H5.5 Z M2.5,20.5 H21.5",

        // Drapeau sur sa hampe.
        Pictogramme.Langue =>
            "M6,2.5 V21.5 M6,4 H19 L16,8.5 L19,13 H6",

        // Matrice : deux crochets et quatre coefficients.
        Pictogramme.Maths =>
            "M7.5,3.5 H4.5 V20.5 H7.5 M16.5,3.5 H19.5 V20.5 H16.5 M9.3,9 H10.7 M13.3,9 H14.7 M9.3,15 H10.7 M13.3,15 H14.7",

        // Organigramme : début, test (losange), deux sorties.
        Pictogramme.Algorithme =>
            "M8.5,2.5 H15.5 V6.5 H8.5 Z M12,6.5 V9 M12,9 L16,12.5 L12,16 L8,12.5 Z M12,16 V21.5 M16,12.5 H20 V18",

        // Balance : mât, fléau, deux plateaux, socle.
        Pictogramme.Droit =>
            "M12,3 V21 M7.5,21 H16.5 M5,6.5 H19 M5,6.5 L2.5,13 H7.5 Z M19,6.5 L16.5,13 H21.5 Z",

        // Plume.
        Pictogramme.Lettres =>
            "M20,3.5 C12,4 7,9 6,17 M20,3.5 C20,10 16,15 8.5,15.5 M6,17 L4,21 M10.5,11 H15",

        // Livre ouvert.
        _ =>
            "M3,5 C6,4 9,4 12,6 C15,4 18,4 21,5 V19 C18,18 15,18 12,20 C9,18 6,18 3,19 Z M12,6 V20",
    };
}
