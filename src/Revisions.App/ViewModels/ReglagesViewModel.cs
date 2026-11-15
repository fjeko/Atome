using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un choix de thème dans la liste déroulante.</summary>
public sealed record ChoixTheme(string Cle, string Libelle);

/// <summary>
/// Écran Réglages. Chaque modification est enregistrée tout de suite : pas de bouton « Enregistrer » à oublier.
/// </summary>
public partial class ReglagesViewModel : ObservableObject
{
    private readonly EtatApplication _etat;
    private readonly bool _initialise;

    [ObservableProperty] private double _objectifMinimal;
    [ObservableProperty] private double _plafondQuotidien;
    [ObservableProperty] private double _nouvellesParJour;
    [ObservableProperty] private double _tailleReprise;
    [ObservableProperty] private double _joursAvantReprise;
    /// <summary>Rétention visée, en pourcentage (80 à 95).</summary>
    [ObservableProperty] private double _retention;
    [ObservableProperty] private string _dossierPacks;
    [ObservableProperty] private string _dossierCours;
    [ObservableProperty] private ChoixTheme _theme;

    public IReadOnlyList<ChoixTheme> Themes { get; } =
    [
        new("systeme", "Comme Windows"),
        new("clair", "Clair"),
        new("sombre", "Sombre"),
    ];

    public string DossierDonnees => BaseDeDonnees.DossierDonnees;

    public ReglagesViewModel(EtatApplication etat)
    {
        _etat = etat;
        var r = etat.Reglages;
        _objectifMinimal = r.ObjectifMinimal;
        _plafondQuotidien = r.PlafondQuotidien;
        _nouvellesParJour = r.NouvellesParJour;
        _tailleReprise = r.TailleReprise;
        _joursAvantReprise = r.JoursAvantReprise;
        _retention = Math.Round(r.RetentionCible * 100);
        _dossierPacks = r.DossierPacks ?? "";
        _dossierCours = r.DossierCours ?? "";
        _theme = Themes.FirstOrDefault(t => t.Cle == r.Theme) ?? Themes[0];
        _initialise = true;
    }

    /// <summary>Appelée à chaque changement d'une propriété : on recopie tout dans les réglages et on enregistre.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_initialise) return;

        var r = _etat.Reglages;
        r.ObjectifMinimal = (int)ObjectifMinimal;
        r.PlafondQuotidien = (int)PlafondQuotidien;
        r.NouvellesParJour = (int)NouvellesParJour;
        r.TailleReprise = (int)TailleReprise;
        r.JoursAvantReprise = (int)JoursAvantReprise;
        r.RetentionCible = Retention / 100;
        r.DossierPacks = DossierPacks.Length == 0 ? null : DossierPacks;
        r.DossierCours = DossierCours.Length == 0 ? null : DossierCours;
        r.Theme = Theme.Cle;
        _etat.Enregistrer();
    }

    [RelayCommand]
    private void ChoisirDossierPacks()
    {
        if (ChoisirDossier("Dossier des packs", DossierPacks) is { } dossier) DossierPacks = dossier;
    }

    [RelayCommand]
    private void ChoisirDossierCours()
    {
        if (ChoisirDossier("Dossier « Cours » (celui qui contient le dossier cours/ avec les PDF)", DossierCours) is { } dossier) DossierCours = dossier;
    }

    private static string? ChoisirDossier(string titre, string actuel)
    {
        var dialogue = new OpenFolderDialog { Title = titre, InitialDirectory = actuel };
        return dialogue.ShowDialog() == true ? dialogue.FolderName : null;
    }

    [RelayCommand]
    private void OuvrirDossierDonnees() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DossierDonnees) { UseShellExecute = true });
}
