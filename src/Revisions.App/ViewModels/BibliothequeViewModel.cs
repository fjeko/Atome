using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revisions.App.Services;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Collections;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une séance dans la bibliothèque, avec ses textes prêts à afficher.</summary>
/// <param name="seance">La séance et ses compteurs.</param>
/// <param name="emplacement">Où elle se trouve (affiché dans les résultats de recherche), ou null.</param>
public sealed class LigneSeance(ResumeSeance seance, string? emplacement = null)
{
    public string PackId => seance.PackId;
    public string Titre => seance.Seance;
    public string? Emplacement => emplacement;
    /// <summary>Cours libre intégré à Atome : affiche le badge « Libre ».</summary>
    public bool EstLibre => seance.Libre;
    public bool ADesCartes => seance.NbCartes > 0;
    public double Maitrise => seance.NbCartes == 0 ? 0 : (double)seance.NbMaitrisees / seance.NbCartes;
    /// <summary>Matière et partie : donnent sa couleur à la ligne (système de design).</summary>
    public string Matiere => seance.Matiere;
    public string Partie => seance.Partie;

    public string Detail
    {
        get
        {
            var morceaux = new List<string> { Texte.Pluriel(seance.NbCartes, "carte", "cartes") };
            if (seance.NbDues > 0) morceaux.Add($"{seance.NbDues} à revoir");
            if (seance.NbNouvelles > 0) morceaux.Add($"{seance.NbNouvelles} à découvrir");
            if (seance.NbMaitrisees > 0) morceaux.Add(Texte.Pluriel(seance.NbMaitrisees, "maîtrisée", "maîtrisées"));
            return string.Join("  ·  ", morceaux);
        }
    }
}

/// <summary>Une tuile : un niveau de la bibliothèque dans lequel on peut entrer.</summary>
public sealed class TuileBibliotheque(NoeudBibliotheque noeud)
{
    public NoeudBibliotheque Noeud => noeud;
    public string Titre => noeud.Titre;
    public string? Etiquette => noeud.Etiquette;
    public string Detail => $"{Texte.Pluriel(noeud.NbSeances, "séance", "séances")}  ·  {Texte.Pluriel(noeud.NbCartes, "carte", "cartes")}";
    public string? Dues => noeud.NbDues > 0 ? $"{noeud.NbDues} à revoir" : null;
    public double Maitrise => noeud.Maitrise;
    public string InfoMaitrise => $"{Texte.Pluriel(noeud.NbMaitrisees, "carte maîtrisée", "cartes maîtrisées")} sur {noeud.NbCartes}";
    /// <summary>Matière (et partie) du niveau : donne sa couleur et son icône à la tuile.</summary>
    public string? Matiere => noeud.PremiereSeance?.Matiere;
    /// <summary>Partie, seulement sous la matière (à la racine, une matière garde sa couleur à elle).</summary>
    public string? Partie => noeud.Icone is null ? noeud.PremiereSeance?.Partie : null;
    /// <summary>Tout est maîtrisé : la tuile se coche.</summary>
    public bool Finie => noeud.NbCartes > 0 && noeud.NbMaitrisees >= noeud.NbCartes;
    public bool Libre => noeud.ToutLibre;
}

/// <summary>Un maillon du fil d'Ariane (« Bibliothèque › Bloc 1 › B1.2 »).</summary>
/// <param name="Titre">Titre court du niveau.</param>
/// <param name="Profondeur">0 pour la racine, 1 pour une matière, etc.</param>
/// <param name="EstCourant">Vrai pour le dernier maillon (le niveau affiché).</param>
public sealed record MaillonFil(string Titre, int Profondeur, bool EstCourant);

/// <summary>
/// Écran Bibliothèque (US1, US2) : on descend de tuile en tuile (matière, compétence, séquence)
/// jusqu'à la liste des séances. Le regroupement en niveaux est fait par
/// <see cref="ArbreBibliotheque"/>, dans le cœur métier.
/// </summary>
public partial class BibliothequeViewModel : ObservableObject
{
    /// <summary>Nombre maximal de résultats affichés pendant une recherche.</summary>
    private const int MaxResultats = 60;

    // Dernier endroit visité : on y revient en rouvrant la bibliothèque (par exemple après une lecture).
    private static List<string> _dernierChemin = [];

    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotBibliotheque _bibliotheque;
    private readonly DepotRevisions _revisions;
    private readonly ImportateurPacks _importateur;

    private List<ResumeSeance> _toutes = [];
    private NoeudBibliotheque _racine = ArbreBibliotheque.Construire([]);
    /// <summary>Niveaux traversés depuis la racine (vide = on est à la racine).</summary>
    private List<NoeudBibliotheque> _chemin = [];

    [ObservableProperty] private IReadOnlyList<MaillonFil> _fil = [];
    [ObservableProperty] private string _titreNiveau = "";
    [ObservableProperty] private IReadOnlyList<TuileBibliotheque> _tuiles = [];
    [ObservableProperty] private IReadOnlyList<LigneSeance> _seances = [];
    [ObservableProperty] private bool _peutRevenir;
    /// <summary>Petit texte sous les résultats d'une recherche (nombre trouvé, liste tronquée…).</summary>
    [ObservableProperty] private string? _infoRecherche;

    /// <summary>Texte de recherche : remplace les tuiles par la liste des séances trouvées.</summary>
    [ObservableProperty] private string _recherche = "";

    /// <summary>Import du contenu d'un camarade (bouton mis en avant quand la bibliothèque est vide).</summary>
    public PartageViewModel Partage { get; }

    [ObservableProperty] private bool _estVide;
    [ObservableProperty] private bool _importEnCours;

    /// <summary>Filtres rapides : une puce par collection (mêmes réglages que « Mes collections » dans les Réglages).</summary>
    [ObservableProperty] private IReadOnlyList<ChoixCollection> _mesCollections = [];
    /// <summary>Explication quand on essaie de décocher la dernière collection.</summary>
    [ObservableProperty] private string? _messageCollections;
    /// <summary>Vrai s'il y a au moins deux collections (sinon les puces n'ont pas d'intérêt).</summary>
    [ObservableProperty] private bool _afficherFiltres;

    private async Task ChangerCollectionAsync(ChoixCollection puce)
    {
        MessageCollections = await _main.BasculerCollectionAsync(puce.Cle, puce.Visible);
        Charger();
    }

    public BibliothequeViewModel(MainViewModel main, EtatApplication etat, DepotBibliotheque bibliotheque,
        DepotRevisions revisions, ImportateurPacks importateur, PartageViewModel partage)
    {
        Partage = partage;
        _main = main;
        _etat = etat;
        _bibliotheque = bibliotheque;
        _revisions = revisions;
        _importateur = importateur;
        Charger();
    }

    partial void OnRechercheChanged(string value) => Afficher();

    /// <summary>Relit les séances en base et reconstruit l'arbre, en restant au même endroit.</summary>
    private void Charger()
    {
        var collections = _main.ListerCollections();
        MesCollections = collections.Select(i => new ChoixCollection(i, ChangerCollectionAsync)).ToList();
        AfficherFiltres = collections.Count > 1;
        _toutes = _bibliotheque.ListerSeances(_etat.Jour);
        _racine = ArbreBibliotheque.Construire(_toutes);
        _chemin = ArbreBibliotheque.Suivre(_racine, _dernierChemin);
        // Bibliothèque vide : soit il n'y a aucun cours, soit ils sont tous dans des collections masquées.
        bool vide = _toutes.Count == 0;
        bool existent = _bibliotheque.CompterPacks() > 0;   // en base, pas les cours libres seulement proposés
        EstVide = vide && !existent;
        CollectionsToutesMasquees = vide && existent;
        Afficher();
    }

    /// <summary>Des cours existent, mais toutes leurs collections sont masquées.</summary>
    [ObservableProperty] private bool _collectionsToutesMasquees;

    /// <summary>Bouton « Afficher mes collections » : ouvre les Réglages, où chaque collection a son interrupteur.</summary>
    [RelayCommand]
    private void AfficherMesCollections() => _main.AllerReglages();

    /// <summary>Met à jour ce qui est affiché : les résultats d'une recherche, ou le niveau courant.</summary>
    private void Afficher()
    {
        string filtre = Recherche.Trim();
        if (filtre.Length > 0)
        {
            AfficherRecherche(filtre);
            return;
        }

        var niveau = _chemin.Count > 0 ? _chemin[^1] : _racine;
        _dernierChemin = _chemin.Select(n => n.Cle).ToList();

        TitreNiveau = _chemin.Count > 0 ? niveau.Titre : "";
        Tuiles = niveau.Enfants.Select(n => new TuileBibliotheque(n)).ToList();
        Seances = niveau.Seances.Select(s => new LigneSeance(s)).ToList();
        PeutRevenir = _chemin.Count > 0;
        InfoRecherche = null;
        Fil = _chemin.Select(n => n.TitreCourt).Prepend("Bibliothèque")
            .Select((titre, i) => new MaillonFil(titre, i, i == _chemin.Count))
            .ToList();
    }

    private void AfficherRecherche(string filtre)
    {
        bool Contient(string? texte) => texte is not null && texte.Contains(filtre, StringComparison.CurrentCultureIgnoreCase);
        var trouvees = _toutes.Where(s => Contient(s.Seance) || Contient(s.Partie) || Contient(s.Matiere) || Contient(s.Sequence)).ToList();

        TitreNiveau = $"Recherche : « {filtre} »";
        Tuiles = [];
        Seances = trouvees.Take(MaxResultats).Select(s => new LigneSeance(s, $"{s.Matiere}  ›  {s.Partie}")).ToList();
        PeutRevenir = false;
        Fil = [new MaillonFil("Bibliothèque", 0, true)]; // un clic dessus quitte la recherche
        InfoRecherche = trouvees.Count switch
        {
            0 => "Aucune séance ne correspond.",
            > MaxResultats => $"{trouvees.Count} séances trouvées, les {MaxResultats} premières sont affichées : précise ta recherche pour voir les autres.",
            _ => Texte.Pluriel(trouvees.Count, "séance trouvée", "séances trouvées") + ".",
        };
    }

    // ---- Navigation --------------------------------------------------------------------------

    /// <summary>Entre dans une tuile.</summary>
    [RelayCommand]
    private void Ouvrir(TuileBibliotheque tuile)
    {
        _chemin.Add(tuile.Noeud);
        Afficher();
    }

    /// <summary>Remonte d'un niveau.</summary>
    [RelayCommand]
    private void Retour()
    {
        if (_chemin.Count == 0) return;
        _chemin.RemoveAt(_chemin.Count - 1);
        Afficher();
    }

    /// <summary>Clic sur un maillon du fil d'Ariane : on revient à ce niveau (et on quitte la recherche).</summary>
    [RelayCommand]
    private void AllerA(MaillonFil maillon)
    {
        _chemin = _chemin.Take(maillon.Profondeur).ToList();
        if (Recherche.Length > 0) Recherche = ""; // déclenche Afficher()
        else Afficher();
    }

#if DEBUG
    /// <summary>Aide aux essais : descend dans l'arbre en suivant des numéros de tuile (0 = la première).</summary>
    public void DescendreParIndices(IEnumerable<int> indices)
    {
        _chemin = [];
        var noeud = _racine;
        foreach (int indice in indices)
        {
            if (indice >= noeud.Enfants.Count) break;
            noeud = noeud.Enfants[indice];
            _chemin.Add(noeud);
        }
        Afficher();
    }
#endif

    // ---- Actions sur une séance ------------------------------------------------------------

    [RelayCommand]
    private void Lire(LigneSeance seance) => _main.OuvrirLecture(seance.PackId);

    /// <summary>Révise toutes les cartes d'une séance, dans l'ordre du cours (US12).</summary>
    [RelayCommand]
    private void Reviser(LigneSeance seance) => _main.DemarrerSession(_revisions.CartesDuPack(seance.PackId), "seance");

    // ---- Import ------------------------------------------------------------------------------

    /// <summary>Import manuel d'un ou plusieurs fichiers de pack.</summary>
    [RelayCommand]
    private async Task ImporterAsync()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Importer des packs",
            Filter = "Packs et contenu Atome (*.json;*.atome)|*.json;*.atome|Packs (*.json)|*.json|Contenu Atome (*.atome)|*.atome",
            Multiselect = true,
            InitialDirectory = _etat.Reglages.DossierPacks ?? "",
        };
        if (dialogue.ShowDialog() != true) return;

        // Les .atome suivent le même flux que « Importer du contenu » des Réglages ; les .json, l'import de packs habituel.
        // Si les deux sont choisis, les packs sont importés d'abord, puis chaque .atome.
        var (packs, atomes) = SeparerFichiers(dialogue.FileNames);
        if (packs.Count > 0)
            await ImporterAsync(() => packs.Select(f => _importateur.ImporterFichier(f, _etat.Reglages.DossierCours, null, Collections.Manuel, Collections.NomManuel, reafficherSiMasquee: true)).ToList());
        foreach (string atome in atomes)
        {
            await Partage.ImporterFichierAsync(atome);
            Charger();
        }
    }

    /// <summary>Sépare les fichiers choisis : packs (.json et autres) d'un côté, contenus .atome de l'autre.</summary>
    internal static (List<string> Packs, List<string> Atomes) SeparerFichiers(IEnumerable<string> fichiers)
    {
        var atomes = fichiers.Where(f => string.Equals(Path.GetExtension(f), ".atome", StringComparison.OrdinalIgnoreCase)).ToList();
        var packs = fichiers.Except(atomes).ToList();
        return (packs, atomes);
    }

    /// <summary>Relit le dossier des packs (utile quand de nouveaux packs viennent d'y être ajoutés).</summary>
    [RelayCommand]
    private Task ActualiserAsync() => ImporterAsync(_main.ImporterDossier);

    private async Task ImporterAsync(Func<List<ResultatImport>> import)
    {
        ImportEnCours = true;
        try
        {
            var resultats = await Task.Run(import);
            Charger();
            _main.AnnoncerImport(resultats, afficherRapportSiRien: true);
        }
        finally
        {
            ImportEnCours = false;
        }
    }
}
