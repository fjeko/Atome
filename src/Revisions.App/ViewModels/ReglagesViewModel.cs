using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Apparence;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un thème dans la grille de choix, avec les couleurs de son aperçu.</summary>
public sealed partial class ChoixTheme(ThemeApp theme, string fond, string feuille, string accent, string texte) : ObservableObject
{
    public string Cle => theme.Cle;
    public string Nom => theme.Nom;
    public string Fond => fond;
    public string Feuille => feuille;
    public string Accent => accent;
    public string Texte => texte;

    /// <summary>Vrai pour le thème en cours (cadre mis en avant).</summary>
    [ObservableProperty] private bool _estChoisi;
}

/// <summary>Une pastille de couleur d'accent. <see cref="Hex"/> vaut null pour « celle du thème ».</summary>
public sealed partial class ChoixAccent(string nom, string? hex) : ObservableObject
{
    public string Nom => nom;
    public string? Hex => hex;
    public bool EstAutomatique => hex is null;

    [ObservableProperty] private bool _estChoisi;
}

/// <summary>
/// Écran Réglages. Chaque modification est enregistrée et appliquée tout de suite :
/// pas de bouton « Enregistrer » à oublier.
/// </summary>
public partial class ReglagesViewModel : ObservableObject
{
    private readonly EtatApplication _etat;
    private readonly bool _initialise;
    private string _cleTheme;
    private string? _couleurAccent;

    [ObservableProperty] private double _objectifMinimal;
    [ObservableProperty] private double _plafondQuotidien;
    [ObservableProperty] private double _nouvellesParJour;
    [ObservableProperty] private double _tailleReprise;
    [ObservableProperty] private double _joursAvantReprise;
    /// <summary>Rétention visée, en pourcentage (80 à 95).</summary>
    [ObservableProperty] private double _retention;
    [ObservableProperty] private string _dossierPacks;
    [ObservableProperty] private string _dossierCours;

    /// <summary>Champ libre pour une couleur d'accent au format #RRGGBB.</summary>
    [ObservableProperty] private string _accentLibre;
    /// <summary>Message affiché si le champ libre ne contient pas une couleur valide.</summary>
    [ObservableProperty] private string? _erreurAccent;

    public IReadOnlyList<ChoixTheme> Themes { get; }
    public IReadOnlyList<ChoixAccent> Accents { get; }

    public string DossierDonnees => BaseDeDonnees.DossierDonnees;

    /// <summary>Partage du contenu avec un camarade (export et import).</summary>
    public PartageViewModel Partage { get; }

    public ReglagesViewModel(EtatApplication etat, PartageViewModel partage)
    {
        _etat = etat;
        Partage = partage;
        _ = partage.EstimerAsync(); // taille des documents, calculée en arrière-plan
        var r = etat.Reglages;
#pragma warning disable MVVMTK0034 // on remplit les champs directement : rien à enregistrer pendant la construction
        _objectifMinimal = r.ObjectifMinimal;
        _plafondQuotidien = r.PlafondQuotidien;
        _nouvellesParJour = r.NouvellesParJour;
        _tailleReprise = r.TailleReprise;
        _joursAvantReprise = r.JoursAvantReprise;
        _retention = Math.Round(r.RetentionCible * 100);
        _dossierPacks = r.DossierPacks ?? "";
        _dossierCours = r.DossierCours ?? "";
        _accentLibre = r.CouleurAccent ?? "";
#pragma warning restore MVVMTK0034
        _cleTheme = r.Theme;
        _couleurAccent = r.CouleurAccent;

        // Aperçu de chaque thème : on calcule sa palette comme si on l'appliquait.
        bool systemeSombre = etat.Palette.Sombre;
        Couleur accentWindows = ServiceTheme.AccentWindows();
        Themes = Core.Apparence.Themes.Tous.Select(t =>
        {
            var p = Core.Apparence.Themes.Resoudre(t, null, systemeSombre, accentWindows);
            var fond = p.Fond ?? (p.Sombre ? Couleur.Parse("#202020") : Couleur.Parse("#F3F3F3"));
            var feuille = t.Feuille ?? (p.Sombre ? Couleur.Parse("#2D2D2D") : Couleur.Blanc);
            return new ChoixTheme(t, fond.Hex, feuille.Hex, p.Accent.Hex, p.Texte.Hex) { EstChoisi = t.Cle == _cleTheme };
        }).ToList();

        Accents = Core.Apparence.Themes.Accents
            .Select(a => new ChoixAccent(a.Nom, a.Couleur.Hex))
            .Prepend(new ChoixAccent("Celle du thème", null))
            .ToList();
        MarquerAccent();
        _initialise = true;
    }

    /// <summary>Appelée à chaque changement d'une propriété : on recopie tout dans les réglages et on enregistre.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Le champ libre et son message d'erreur ne s'enregistrent pas tels quels (voir OnAccentLibreChanged).
        if (!_initialise || e.PropertyName is nameof(AccentLibre) or nameof(ErreurAccent)) return;
        Enregistrer();
    }

    private void Enregistrer()
    {
        var r = _etat.Reglages;
        r.ObjectifMinimal = (int)ObjectifMinimal;
        r.PlafondQuotidien = (int)PlafondQuotidien;
        r.NouvellesParJour = (int)NouvellesParJour;
        r.TailleReprise = (int)TailleReprise;
        r.JoursAvantReprise = (int)JoursAvantReprise;
        r.RetentionCible = Retention / 100;
        r.DossierPacks = DossierPacks.Length == 0 ? null : DossierPacks;
        r.DossierCours = DossierCours.Length == 0 ? null : DossierCours;
        r.Theme = _cleTheme;
        r.CouleurAccent = _couleurAccent;
        _etat.Enregistrer(); // enregistre et applique le thème immédiatement
    }

    // ---- Personnalisation ------------------------------------------------------------------

    [RelayCommand]
    private void ChoisirTheme(ChoixTheme choix)
    {
        _cleTheme = choix.Cle;
        foreach (var theme in Themes) theme.EstChoisi = theme == choix;
        Enregistrer();
    }

    [RelayCommand]
    private void ChoisirAccent(ChoixAccent choix)
    {
        _couleurAccent = choix.Hex;
        AccentLibre = choix.Hex ?? "";
        ErreurAccent = null;
        MarquerAccent();
        Enregistrer();
    }

    /// <summary>Champ libre : dès que le texte est une couleur valide, elle est appliquée.</summary>
    partial void OnAccentLibreChanged(string value)
    {
        if (!_initialise) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            ErreurAccent = null;
            return; // champ vidé : on garde la couleur en cours
        }
        if (!Couleur.TryParse(value, out var couleur))
        {
            ErreurAccent = "Écris une couleur comme #2F7DE1 (un # puis 6 chiffres ou lettres de A à F).";
            return;
        }
        ErreurAccent = null;
        if (couleur.Hex == _couleurAccent) return;
        _couleurAccent = couleur.Hex;
        MarquerAccent();
        Enregistrer();
    }

    private void MarquerAccent()
    {
        foreach (var accent in Accents)
            accent.EstChoisi = string.Equals(accent.Hex, _couleurAccent, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Dossiers --------------------------------------------------------------------------

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
