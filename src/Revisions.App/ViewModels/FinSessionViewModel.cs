using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;

namespace Revisions.App.ViewModels;

/// <summary>Écran de fin de session : ce qui a été fait, sans jugement.</summary>
public partial class FinSessionViewModel(BilanSession bilan, MainViewModel main, ServiceSignalement signalements) : ObservableObject
{
    public string Titre => bilan.NiveauApres > bilan.NiveauAvant
        ? $"Niveau {bilan.NiveauApres} atteint !"
        : "Session terminée";

    public string Cartes => bilan.CartesRevues == 1 ? "1 carte" : $"{bilan.CartesRevues} cartes";

    public string Reussite => $"{Math.Round(100.0 * bilan.Reussies / Math.Max(1, bilan.CartesRevues))} %";

    public string Xp => $"+{bilan.XpGagnee} XP";

    public string? Objectif => bilan.ObjectifAtteintPendantLaSession ? "Objectif du jour atteint pendant cette session." : null;

    public IReadOnlyList<CarteRatee> Ratees => bilan.Ratees;

    public bool ADesRatees => bilan.Ratees.Count > 0;

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
