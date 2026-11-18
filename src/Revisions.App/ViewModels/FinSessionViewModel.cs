using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.Mascotte;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Écran de fin de session : ce qui a été fait, sans jugement.</summary>
public partial class FinSessionViewModel(BilanSession bilan, MainViewModel main, ServiceSignalement signalements, DepotProfil profil) : ObservableObject
{
    public string Titre => NiveauGagne ? $"Niveau {bilan.NiveauApres} atteint !" : "Session terminée";

    public string Cartes => bilan.CartesRevues == 1 ? "1 carte" : $"{bilan.CartesRevues} cartes";

    public string Reussite => $"{Math.Round(100.0 * TauxReussite)} %";

    /// <summary>Part des cartes réussies au premier passage (0 à 1), pour l'anneau.</summary>
    public double TauxReussite => (double)bilan.Reussies / Math.Max(1, bilan.CartesRevues);

    public string Xp => $"+{bilan.XpGagnee} XP";

    public string? Objectif => bilan.ObjectifAtteintPendantLaSession ? "Objectif du jour atteint pendant cette session." : null;

    public IReadOnlyList<CarteRatee> Ratees => bilan.Ratees;

    public bool ADesRatees => bilan.Ratees.Count > 0;

    /// <summary>Un niveau a été gagné pendant la session : petite célébration (et son « niveau », si activé).</summary>
    public bool NiveauGagne => bilan.NiveauApres > bilan.NiveauAvant;

    // ---- Niveau et XP après la session (compteur) ----
    private readonly int _xpTotal = profil.XpTotal();
    public int Niveau => Niveaux.NiveauPour(_xpTotal);
    public double ProgressionNiveau => Niveaux.Progression(_xpTotal);
    public string XpTotal => $"{_xpTotal} XP  ·  niveau suivant à {Niveaux.XpPourNiveau(Niveau + 1)}";

    // ---- Mascotte : fière, avec un message de la banque ----
    private readonly MessageMascotte _message = BanqueMessages.Tirer(
        bilan.NiveauApres > bilan.NiveauAvant ? BanqueMessages.NiveauGagne : BanqueMessages.FinSession,
        valeurs: new Dictionary<string, string> { ["niveau"] = bilan.NiveauApres.ToString() });
    public string MessageMascotte => _message.Texte;
    public Humeur HumeurMascotte => _message.Humeur;

    /// <summary>Remerciement affiché après un signalement.</summary>
    [ObservableProperty] private string? _messageSignalement;

    [RelayCommand]
    private void Accueil() => main.AllerAccueil();

    [RelayCommand]
    private void RevoirNotion(CarteRatee carte) => main.RevoirLaNotion(carte.SectionId);

    /// <summary>Signale une erreur dans une carte ratée (drapeau de la liste « À revoir »).</summary>
    [RelayCommand]
    private void Signaler(CarteRatee carte)
    {
        if (signalements.SignalerCarte(carte.CarteId, carte.PackId, carte.Seance, carte.Recto, carte.Verso))
            MessageSignalement = ServiceSignalement.Merci;
    }
}
