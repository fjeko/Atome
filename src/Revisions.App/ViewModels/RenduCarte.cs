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
    /// <param name="saisieLatex">Matrice remplie dans la grille, en LaTeX (affichée comme la réponse), ou null.</param>
    /// <param name="code">La carte demande du code : la saisie s'affiche en police code, sans retouche (indentation gardée).</param>
    public static List<object> Elements(Carte carte, int[] ordreChoix, int? choixFait, bool reponseVisible, int etapesRevelees, string saisie,
        string? saisieLatex = null, bool code = false)
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
                    elements.Add(new { genre = "comparaison", saisie = code ? saisie.TrimEnd() : saisie.Trim(), markdown = c.Verso, code });
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
                // On écrit son résultat (ou on répond de tête), puis on voit directement la réponse.
                // La correction étape par étape n'apparaît que si le résultat écrit est différent (ou s'il n'y en a pas).
                elements.Add(new { genre = "question", markdown = c.Enonce });
                if (!reponseVisible) break;
                // Du code ne se compare pas au caractère près : la correction détaillée reste toujours affichée.
                bool memeResultat = !code && ComparaisonReponse.MemeResultat(saisie, c.Reponse);
                if (!string.IsNullOrWhiteSpace(saisie))
                {
                    if (memeResultat) elements.Add(new { genre = "etiquette", markdown = Felicitation(carte.Id) });
                    elements.Add(new { genre = "comparaison", saisie = code ? saisie.TrimEnd() : saisie.Trim(), saisieMarkdown = saisieLatex,
                        markdown = c.Reponse, code });
                }
                else
                {
                    elements.Add(new { genre = "etiquette", markdown = "Réponse" });
                    elements.Add(new { genre = "reponse", markdown = c.Reponse });
                }
                if (!memeResultat && c.Etapes is { Count: > 0 })
                {
                    elements.Add(new { genre = "etiquette", markdown = "Correction détaillée" });
                    for (int i = 0; i < c.Etapes.Count; i++)
                        elements.Add(new { genre = "etape", index = i + 1, markdown = c.Etapes[i] });
                }
                break;
        }
        return elements;
    }

    private static readonly string[] Felicitations =
    [
        "Même résultat que toi !",
        "Bravo, c'est exactement ça !",
        "Pile juste !",
        "Bien joué, résultat exact !",
        "Parfait, tu as trouvé !",
        "Excellent, rien à redire !",
        "Calcul impeccable !",
        "Yes ! C'est la bonne réponse.",
        "Tout juste, continue comme ça !",
        "Superbe, c'est gagné !",
    ];

    /// <summary>
    /// Message de félicitation pour un résultat juste, tiré au hasard. Le tirage est fixé par carte (pendant
    /// que l'application est ouverte) pour que le message ne change pas si l'écran se redessine.
    /// </summary>
    private static string Felicitation(string carteId) =>
        Felicitations[(carteId.GetHashCode() & int.MaxValue) % Felicitations.Length];

    /// <summary>Délai avant la prochaine révision, en clair (« demain », « 6 j », « 2 mois »).</summary>
    public static string Delai(int jours) => jours switch
    {
        1 => "demain",
        < 30 => $"{jours} j",
        < 365 => $"{Math.Round(jours / 30.0)} mois",
        _ => $"{jours / 365.0:0.#} an(s)",
    };
}
