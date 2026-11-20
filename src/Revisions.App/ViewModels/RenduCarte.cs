using Revisions.Core.Modeles;

namespace Revisions.App.ViewModels;

/// <summary>
/// Contenu d'une carte pour la zone de rendu (Markdown + LaTeX), commun à la session
/// et au parcours « Lire puis contrôler » : une carte s'affiche partout de la même façon.
/// </summary>
public static class RenduCarte
{
    /// <param name="ordreChoix">Ordre d'affichage des choix d'un QCM (index dans le pack).</param>
    /// <param name="choixFait">Index du choix fait dans le pack, ou null.</param>
    /// <param name="saisie">Réponse écrite (carte « question »), comparée à la bonne réponse une fois révélée.</param>
    public static List<object> Elements(Carte carte, int[] ordreChoix, int? choixFait, bool reponseVisible, int etapesRevelees, string saisie)
    {
        var c = carte.Contenu;
        var elements = new List<object>();
        switch (carte.Type)
        {
            case TypeCarte.Question:
                elements.Add(new { genre = "question", markdown = c.Recto });
                if (reponseVisible && !string.IsNullOrWhiteSpace(saisie))
                {
                    // Une réponse a été écrite : on l'affiche à côté de la bonne pour comparer.
                    elements.Add(new { genre = "comparaison", saisie = saisie.Trim(), markdown = c.Verso });
                }
                else if (reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = "Réponse" });
                    elements.Add(new { genre = "reponse", markdown = c.Verso });
                }
                break;

            case TypeCarte.Qcm:
                elements.Add(new { genre = "question", markdown = c.Question });
                for (int position = 0; position < ordreChoix.Length; position++)
                {
                    int i = ordreChoix[position]; // index du choix dans le pack
                    // Avant le choix : cliquable. Après : le bon en vert, le mauvais choisi en rouge.
                    string etat = choixFait is null ? "actif"
                        : i == c.BonneReponse ? "bon"
                        : i == choixFait ? "mauvais"
                        : "neutre";
                    elements.Add(new { genre = "choix", index = position, markdown = c.Choix![i], etat });
                }
                if (reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = choixFait == c.BonneReponse ? "Bonne réponse !" : "Explication" });
                    elements.Add(new { genre = "explication", markdown = c.Explication });
                }
                break;

            case TypeCarte.Exercice:
                elements.Add(new { genre = "question", markdown = c.Enonce });
                for (int i = 0; i < etapesRevelees; i++)
                    elements.Add(new { genre = "etape", index = i + 1, markdown = c.Etapes![i] });
                if (reponseVisible)
                {
                    elements.Add(new { genre = "etiquette", markdown = "Réponse" });
                    elements.Add(new { genre = "reponse", markdown = c.Reponse });
                }
                break;
        }
        return elements;
    }

    /// <summary>Libellé du bouton principal avant la réponse : « Voir la réponse » ou « Étape suivante ».</summary>
    public static string TexteReveler(Carte carte, int etapesRevelees) =>
        carte.Type == TypeCarte.Exercice && etapesRevelees < (carte.Contenu.Etapes?.Count ?? 0)
            ? (etapesRevelees == 0 ? "Voir la première étape" : "Étape suivante")
            : "Voir la réponse";

    /// <summary>Délai avant la prochaine révision, en clair (« demain », « 6 j », « 2 mois »).</summary>
    public static string Delai(int jours) => jours switch
    {
        1 => "demain",
        < 30 => $"{jours} j",
        < 365 => $"{Math.Round(jours / 30.0)} mois",
        _ => $"{jours / 365.0:0.#} an(s)",
    };
}
