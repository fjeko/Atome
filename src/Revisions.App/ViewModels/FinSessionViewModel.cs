using CommunityToolkit.Mvvm.Input;

namespace Revisions.App.ViewModels;

/// <summary>Écran de fin de session : ce qui a été fait, sans jugement.</summary>
public partial class FinSessionViewModel(BilanSession bilan, MainViewModel main)
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

    [RelayCommand]
    private void Accueil() => main.AllerAccueil();

    [RelayCommand]
    private void RevoirNotion(CarteRatee carte) => main.RevoirLaNotion(carte.SectionId);
}
