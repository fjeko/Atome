using Revisions.Core.Mascotte;

namespace Revisions.App.Views;

/// <summary>
/// Proposition du premier lancement (aucun pack) : charger les cours libres livrés avec Atome ?
/// Un dialogue d'Atome, avec la mascotte. <see cref="FenetreDialogue.Reponse"/> vaut « oui » ou « non ».
/// </summary>
public sealed class FenetreCoursLibres(int nombreDeCours) : FenetreDialogue(
    "Bienvenue dans Atome",
    $"Charger les cours libres BTS SIO SLAM – 1re année ? ({nombreDeCours} cours, licence libre)\n\n" +
    "Ce sont des cours originaux livrés avec Atome, avec leurs cartes de révision. Tu pourras les masquer ou les remettre " +
    "à tout moment dans Réglages → Mes collections.",
    [new ChoixDialogue("Plus tard", "non", Annulation: true), new ChoixDialogue("Oui, les charger", "oui", Principal: true)],
    Humeur.Contente);
