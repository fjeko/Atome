using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Controles;

namespace Revisions.App.ViewModels;

/// <summary>Une section de l'écran Réglages (entrée de la navigation de gauche).</summary>
public sealed record SectionReglages(string Cle, string Titre, string Icone);

/// <summary>Un réglage retrouvable par la recherche : son libellé, ses mots-clés et la section qui le contient.</summary>
public sealed record EntreeReglage(string Libelle, string Mots, SectionReglages Section)
{
    /// <summary>Texte lu par les lecteurs d'écran : « Libellé, dans la section X ».</summary>
    public string Description => $"{Libelle}, dans {Section.Titre}";
}

/// <summary>
/// Navigation de l'écran Réglages : sections, recherche, largeur de la fenêtre.
/// C'est de l'état d'interface seulement (rien n'est enregistré) ; la section choisie est gardée
/// pendant la session, pour retrouver l'écran là où on l'a laissé.
/// </summary>
public sealed partial class NavigationReglagesViewModel : ObservableObject
{
    /// <summary>Dernière section ouverte pendant cette session de l'application.</summary>
    private static string _derniereSection = "revision";

    public IReadOnlyList<SectionReglages> Sections { get; }
    private readonly IReadOnlyList<EntreeReglage> _entrees;

    [ObservableProperty] private SectionReglages _sectionChoisie;
    [ObservableProperty] private string _recherche = "";
    [ObservableProperty] private IReadOnlyList<EntreeReglage> _resultats = [];
    /// <summary>Vrai sous 1100 px : la navigation passe au-dessus du contenu.</summary>
    [ObservableProperty] private bool _compacte;

    public bool RechercheActive => Recherche.Trim().Length > 0;
    public bool AucunResultat => RechercheActive && Resultats.Count == 0;

    public NavigationReglagesViewModel()
    {
        var revision = new SectionReglages("revision", "Révision", Icones.Cible);
        var apparence = new SectionReglages("apparence", "Apparence", Icones.Galerie);
        var collections = new SectionReglages("collections", "Collections", Icones.Bibliotheque);
        var planning = new SectionReglages("planning", "Planning", Icones.Planning);
        var donnees = new SectionReglages("donnees", "Données", Icones.Dossier);
        var partage = new SectionReglages("partage", "Partage", Icones.Partager);
        var maj = new SectionReglages("maj", "Mises à jour", Icones.Telecharger);
        var apropos = new SectionReglages("apropos", "À propos", Icones.Info);
        Sections = [revision, apparence, collections, planning, donnees, partage, maj, apropos];

        _entrees =
        [
            new("Objectif minimal du jour", "cartes journée série", revision),
            new("Plafond quotidien", "limite maximum cartes arrêter", revision),
            new("Nouvelles cartes par jour", "nouveautés quota", revision),
            new("Cartes de cours non lus", "nouvelles lu ouvert vu", revision),
            new("Jours d'absence avant le mode reprise", "pause avancé", revision),
            new("Taille de la session de reprise", "pause retour avancé", revision),
            new("Réussite visée", "rétention fsrs mémoire pourcentage avancé", revision),
            new("Thème", "sombre clair couleurs fond", apparence),
            new("Couleur d'accent", "teinte pastille hexadécimale rrggbb code avancé", apparence),
            new("Taille du texte", "police zoom lisibilité agrandir", apparence),
            new("Réduire les animations", "mouvement transitions fondu", apparence),
            new("Sons", "audio bruit son", apparence),
            new("Mascotte", "atome personnage masquer", apparence),
            new("Mes collections", "afficher masquer cours libres packs", collections),
            new("Planning CNED", "dates échéances frise", planning),
            new("Mes signalements", "erreurs drapeau corriger", donnees),
            new("Dossier des packs", "chemin répertoire avancé", donnees),
            new("Dossier des cours", "chemin répertoire pdf avancé", donnees),
            new("Dossier des données", "base sauvegardes chemin avancé", donnees),
            new("Partager avec un camarade", "exporter importer fichier atome pdf", partage),
            new("Mises à jour au démarrage", "version github internet automatique", maj),
            new("Vérifier maintenant", "version nouvelle release", maj),
            new("À propos d'Atome", "licence mit projet version", apropos),
        ];

        _sectionChoisie = Sections.FirstOrDefault(s => s.Cle == _derniereSection) ?? revision;
    }

    partial void OnSectionChoisieChanged(SectionReglages value) => _derniereSection = value.Cle;

    partial void OnRechercheChanged(string value)
    {
        var mots = Normaliser(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Resultats = mots.Length == 0
            ? []
            : _entrees.Where(e =>
                {
                    string texte = Normaliser(e.Libelle + " " + e.Mots + " " + e.Section.Titre);
                    return mots.All(texte.Contains);
                }).ToList();
        OnPropertyChanged(nameof(RechercheActive));
        OnPropertyChanged(nameof(AucunResultat));
    }

    /// <summary>Un clic (ou Entrée) sur un résultat ouvre sa section et referme la recherche.</summary>
    [RelayCommand]
    private void OuvrirResultat(EntreeReglage? entree)
    {
        if (entree is null) return;
        SectionChoisie = entree.Section;
        Recherche = "";
    }

    /// <summary>Minuscules sans accents : « Réussite » et « reussite » se trouvent mutuellement.</summary>
    private static string Normaliser(string texte)
    {
        var sb = new StringBuilder();
        foreach (char c in texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
