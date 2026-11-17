using System.Windows;
using Revisions.App.ViewModels;
using Revisions.App.Views;
using Revisions.Core.Signalements;
using Revisions.Data;

namespace Revisions.App.Services;

/// <summary>
/// Ouvre la petite fenêtre de signalement et enregistre le résultat. Les écrans (session, lecture,
/// fin de session) n'ont qu'à appeler ce service : ils ne connaissent ni la fenêtre ni la base.
/// </summary>
public sealed class ServiceSignalement(DepotSignalements depot, EtatApplication etat)
{
    /// <summary>Texte de remerciement affiché par les écrans après un signalement.</summary>
    public const string Merci = "Merci ! Ton signalement aidera à corriger le cours.";

    /// <summary>Signale une carte. Renvoie vrai si un signalement a été enregistré (faux si l'étudiant a annulé).</summary>
    public bool SignalerCarte(string carteId, string packId, string description, string recto, string verso) =>
        Signaler(CibleSignalement.Carte, carteId, packId, "Carte · " + description, null, recto, verso);

    /// <summary>Signale une section de cours.</summary>
    public bool SignalerSection(string sectionId, string packId, string titre, string contenu) =>
        Signaler(CibleSignalement.Section, sectionId, packId, "Section · " + titre, titre, contenu, null);

    private bool Signaler(CibleSignalement cible, string cibleId, string packId, string description,
        string? titre, string recto, string? verso)
    {
        var modele = new SignalementViewModel(description);
        // La fenêtre active peut être la fenêtre de lecture (« Revoir la notion ») : on s'y attache.
        var proprietaire = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        var fenetre = new FenetreSignalement { DataContext = modele, Owner = proprietaire };
        if (fenetre.ShowDialog() != true || modele.MotifChoisi is not { } motif) return false;

        depot.Enregistrer(cible, cibleId, packId, motif, modele.Commentaire, titre, recto, verso, etat.Maintenant);
        return true;
    }
}
