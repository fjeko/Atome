using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.Core.Calculatrice;

namespace Revisions.App.ViewModels;

/// <summary>Où en est la calculatrice sur l'écran affiché.</summary>
public enum EtatCalculatrice
{
    /// <summary>Pas un exercice de maths : ni bouton ni panneau.</summary>
    Indisponible,
    /// <summary>Exercice de maths : la calculatrice sert.</summary>
    Disponible,
    /// <summary>Exercice de maths dont le but est de calculer à la main : panneau grisé, avec une explication.</summary>
    Desactivee,
}

/// <summary>Une ligne de l'historique : ce qui a été tapé et le résultat (ou l'erreur).</summary>
public partial class EntreeHistorique(string expression, Valeur? valeur, string? erreur) : ObservableObject
{
    public string Expression { get; } = expression;
    public Valeur? Valeur { get; } = valeur;
    public string? Erreur { get; } = erreur;

    /// <summary>« = 1/2 » ou le message d'erreur ; réécrit quand on bascule fraction / décimal.</summary>
    [ObservableProperty] private string _resume = "";

    public void Actualiser(bool decimale) =>
        Resume = Erreur is not null ? "Erreur : " + Erreur : "= " + Formatage.Texte(Valeur!, decimale);
}

/// <summary>Une case de l'éditeur de matrice (texte tapé : « 3 », « -1/2 », « 0,25 »).</summary>
public partial class CelluleMatrice : ObservableObject
{
    [ObservableProperty] private string _texte = "";
}

/// <summary>Un nom de matrice proposé dans la liste : « A » ou « A (2×3) » quand elle est définie.</summary>
public sealed partial class NomMatrice(string nom) : ObservableObject
{
    public string Nom { get; } = nom;
    [ObservableProperty] private string _etiquette = nom;
}

/// <summary>
/// La calculatrice (panneau latéral). Une seule instance pour toute la session : l'historique, Ans et les matrices
/// définies restent en mémoire quand on change de carte ou de TD. Toute la logique est ici ; la vue ne gère que le focus.
/// </summary>
public partial class CalculatriceViewModel : ObservableObject
{
    private readonly Calculateur _calc = new();

    // ---- Disponibilité ---------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible), nameof(Active), nameof(Desactivee), nameof(PeutCopier))]
    private EtatCalculatrice _etat = EtatCalculatrice.Indisponible;

    /// <summary>Explication affichée quand la calculatrice est grisée.</summary>
    [ObservableProperty] private string? _raison;

    /// <summary>Le panneau est ouvert (il ne s'affiche que si la calculatrice est <see cref="Visible"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanneauAffiche))]
    private bool _estOuverte;

    public bool Visible => Etat != EtatCalculatrice.Indisponible;
    public bool Active => Etat == EtatCalculatrice.Disponible;
    public bool Desactivee => Etat == EtatCalculatrice.Desactivee;
    public bool PanneauAffiche => EstOuverte && Visible;

    /// <summary>Appelé par l'écran affiché (carte, exercice de TD) pour dire si la calculatrice est permise.</summary>
    public void Definir(EtatCalculatrice etat, string? raison = null)
    {
        Etat = etat;
        Raison = raison;
        if (etat == EtatCalculatrice.Indisponible) CopierVersReponse = null;
    }

    partial void OnEstOuverteChanged(bool value) => OnPropertyChanged(nameof(PanneauAffiche));
    partial void OnEtatChanged(EtatCalculatrice value) => OnPropertyChanged(nameof(PanneauAffiche));

    [RelayCommand]
    private void Basculer() => EstOuverte = !EstOuverte;

    [RelayCommand]
    public void Fermer() => EstOuverte = false;

    // ---- Saisie et historique --------------------------------------------------------------

    [ObservableProperty] private string _saisie = "";

    /// <summary>Message d'erreur de la dernière saisie, en français.</summary>
    [ObservableProperty] private string? _erreur;

    /// <summary>Résultat affiché en gros : fractions exactes, ou décimal (virgule) quand l'interrupteur est actif.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Decimale), nameof(LibelleMode))]
    private bool _modeDecimal;

    public bool Decimale => ModeDecimal;
    public string LibelleMode => ModeDecimal ? "Valeurs décimales" : "Valeurs exactes";

    public ObservableCollection<EntreeHistorique> Historique { get; } = [];

    // Position dans l'historique avec ↑ / ↓ (-1 = la ligne en cours de frappe).
    private int _positionHistorique = -1;
    private string _brouillon = "";

    [ObservableProperty] private Valeur? _dernier;

    partial void OnModeDecimalChanged(bool value)
    {
        foreach (var e in Historique) e.Actualiser(value);
        MettreAJourResultat();
    }

    [RelayCommand]
    public void Calculer()
    {
        string texte = Saisie.Trim();
        if (texte.Length == 0) return;
        try
        {
            var valeur = _calc.Evaluer(texte);
            var entree = new EntreeHistorique(texte, valeur, null);
            entree.Actualiser(ModeDecimal);
            Historique.Insert(0, entree);
            Dernier = valeur;
            Erreur = null;
            Saisie = "";
            Retour = null;
        }
        catch (ErreurCalcul e)
        {
            Erreur = e.Message;
        }
        catch (OverflowException)
        {
            Erreur = "Ce calcul donne un nombre beaucoup trop grand.";
        }
        _positionHistorique = -1;
        MettreAJourResultat();
    }

    /// <summary>Flèche haut (+1) ou bas (-1) : remonte ou redescend dans les calculs déjà faits.</summary>
    public void NaviguerHistorique(int sens)
    {
        if (Historique.Count == 0) return;
        if (_positionHistorique == -1 && sens > 0) _brouillon = Saisie;
        int nouvelle = Math.Clamp(_positionHistorique + sens, -1, Historique.Count - 1);
        if (nouvelle == _positionHistorique) return;
        _positionHistorique = nouvelle;
        Saisie = nouvelle == -1 ? _brouillon : Historique[nouvelle].Expression;
        Erreur = null;
    }

    /// <summary>Un clic sur une ligne de l'historique la remet dans la saisie.</summary>
    [RelayCommand]
    private void Reprendre(EntreeHistorique? entree)
    {
        if (entree is null) return;
        Saisie = entree.Expression;
        Erreur = null;
    }

    [RelayCommand]
    private void ViderHistorique()
    {
        Historique.Clear();
        Dernier = null;
        Erreur = null;
        Retour = null;
        MettreAJourResultat();
    }

    // ---- Affichage du dernier résultat ------------------------------------------------------

    /// <summary>Le résultat est une matrice : on l'affiche en grille.</summary>
    [ObservableProperty] private bool _resultatEstMatrice;
    [ObservableProperty] private bool _resultatEstNombre;
    [ObservableProperty] private string _resultatTexte = "";
    [ObservableProperty] private bool _resultatApproche;
    [ObservableProperty] private int _resultatColonnes = 1;
    [ObservableProperty] private IReadOnlyList<string> _resultatCases = [];
    [ObservableProperty] private string? _resultatTaille;

    private void MettreAJourResultat()
    {
        switch (Dernier)
        {
            case Nombre n:
                ResultatEstNombre = true;
                ResultatEstMatrice = false;
                ResultatTexte = n.Texte(ModeDecimal);
                ResultatApproche = !n.EstExact;
                ResultatCases = [];
                ResultatTaille = null;
                break;
            case ValeurMatrice m:
                var mat = m.Matrice;
                ResultatEstMatrice = true;
                ResultatEstNombre = false;
                ResultatColonnes = mat.Colonnes;
                ResultatCases = Enumerable.Range(0, mat.Lignes * mat.Colonnes)
                    .Select(k => Formatage.Coefficient(mat[k / mat.Colonnes, k % mat.Colonnes], ModeDecimal)).ToList();
                ResultatTaille = $"Matrice {mat.Taille}";
                ResultatApproche = false;
                ResultatTexte = "";
                break;
            default:
                ResultatEstNombre = ResultatEstMatrice = ResultatApproche = false;
                ResultatTexte = "";
                ResultatCases = [];
                ResultatTaille = null;
                break;
        }
        OnPropertyChanged(nameof(APasDeResultat));
        OnPropertyChanged(nameof(PeutCopier));
    }

    public bool APasDeResultat => Dernier is null;

    // ---- Copier dans la réponse du TD ------------------------------------------------------

    /// <summary>
    /// Posé par le TD quand une grille de réponse est affichée : reçoit le dernier résultat et renvoie null si c'est copié,
    /// ou une explication en français (tailles différentes, fractions…).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeutCopier))]
    private Func<Valeur, string?>? _copierVersReponse;

    public bool PeutCopier => CopierVersReponse is not null && Active && Dernier is not null;

    /// <summary>Petit message sous le résultat (« Copié dans la réponse ») ou explication.</summary>
    [ObservableProperty] private string? _retour;

    [RelayCommand]
    private void CopierDansReponse()
    {
        if (Dernier is null || CopierVersReponse is null) return;
        Retour = CopierVersReponse(Dernier) ?? "Copié dans la grille de réponse.";
    }

    // ---- Éditeur de matrices ----------------------------------------------------------------

    public IReadOnlyList<int> Tailles { get; } = [1, 2, 3, 4, 5];

    [ObservableProperty] private bool _editeurOuvert;

    /// <summary>Onglet « Calcul » (l'autre onglet est « Matrices »).</summary>
    public bool OngletCalcul
    {
        get => !EditeurOuvert;
        set { if (value) EditeurOuvert = false; }
    }

    partial void OnEditeurOuvertChanged(bool value) => OnPropertyChanged(nameof(OngletCalcul));

    [ObservableProperty] private int _lignesEditeur = 2;
    [ObservableProperty] private int _colonnesEditeur = 2;
    [ObservableProperty] private string _nomEdite = "A";
    [ObservableProperty] private string? _messageEditeur;
    [ObservableProperty] private bool _erreurEditeur;

    public ObservableCollection<CelluleMatrice> Cellules { get; } = [];

    /// <summary>Liste des noms A–Z avec la taille des matrices déjà définies.</summary>
    public ObservableCollection<NomMatrice> Noms { get; } = [];

    /// <summary>Matrices définies, pour le rappel sous l'éditeur (« A 2×2 · B 3×1 »).</summary>
    [ObservableProperty] private string _resumeMatrices = "Aucune matrice définie pour le moment.";

    public CalculatriceViewModel()
    {
        foreach (string nom in Calculateur.NomsPossibles) Noms.Add(new NomMatrice(nom));
        ReconstruireGrille(2, 2);
        ActualiserNoms();
    }

    partial void OnLignesEditeurChanged(int value) => ReconstruireGrille(value, ColonnesEditeur);
    partial void OnColonnesEditeurChanged(int value) => ReconstruireGrille(LignesEditeur, value);

    partial void OnNomEditeChanged(string value)
    {
        if (!_calc.Matrices.TryGetValue(value, out var m))
        {
            MessageEditeur = null;
            return;
        }
        // On recharge la matrice définie : taille et coefficients.
        _chargement = true;
        LignesEditeur = m.Lignes;
        ColonnesEditeur = m.Colonnes;
        _chargement = false;
        Cellules.Clear();
        for (int i = 0; i < m.Lignes; i++)
        for (int j = 0; j < m.Colonnes; j++)
            Cellules.Add(new CelluleMatrice { Texte = m[i, j].EnFraction() });
        _anciennesColonnes = m.Colonnes;
        OnPropertyChanged(nameof(ColonnesGrille));
        MessageEditeur = null;
    }

    private bool _chargement;

    /// <summary>Change la taille de la grille en gardant ce qui était déjà tapé aux mêmes positions.</summary>
    private void ReconstruireGrille(int lignes, int colonnes)
    {
        if (_chargement) return;
        var anciennes = Cellules.ToList();
        int anciennesColonnes = _anciennesColonnes;
        Cellules.Clear();
        for (int i = 0; i < lignes; i++)
        for (int j = 0; j < colonnes; j++)
        {
            int k = i * anciennesColonnes + j;
            bool existait = j < anciennesColonnes && k < anciennes.Count;
            Cellules.Add(new CelluleMatrice { Texte = existait ? anciennes[k].Texte : "0" });
        }
        _anciennesColonnes = colonnes;
        OnPropertyChanged(nameof(ColonnesGrille));
    }

    private int _anciennesColonnes = 2;

    /// <summary>Nombre de colonnes de la grille affichée (la vue en a besoin pour ranger les cases).</summary>
    public int ColonnesGrille => ColonnesEditeur;

    [RelayCommand]
    private void Enregistrer()
    {
        try
        {
            var m = new MatriceRationnelle(LignesEditeur, ColonnesEditeur, (i, j) => LireCase(i, j));
            _calc.DefinirMatrice(NomEdite, m);
            ErreurEditeur = false;
            MessageEditeur = $"Matrice {NomEdite} ({m.Taille}) enregistrée. Écris maintenant par exemple {NomEdite}^-1 ou det({NomEdite}).";
            ActualiserNoms();
        }
        catch (ErreurCalcul e)
        {
            ErreurEditeur = true;
            MessageEditeur = e.Message;
        }
    }

    [RelayCommand]
    private void Supprimer()
    {
        _calc.SupprimerMatrice(NomEdite);
        ErreurEditeur = false;
        MessageEditeur = $"Matrice {NomEdite} supprimée.";
        ActualiserNoms();
    }

    private Rationnel LireCase(int i, int j)
    {
        string texte = Cellules[i * ColonnesEditeur + j].Texte.Trim();
        if (texte.Length == 0) return Rationnel.Zero;
        try
        {
            if (new Calculateur().Evaluer(texte) is Nombre { Exact: { } r }) return r;
        }
        catch (ErreurCalcul)
        {
            // le message ci-dessous suffit
        }
        throw new ErreurCalcul($"La case ligne {i + 1}, colonne {j + 1} (« {texte} ») doit contenir un nombre exact : 3, -1/2 ou 0,25.");
    }

    private void ActualiserNoms()
    {
        // On met les étiquettes à jour sur place : la liste ne change pas, la sélection reste.
        foreach (var n in Noms)
            n.Etiquette = _calc.Matrices.TryGetValue(n.Nom, out var m) ? $"{n.Nom} ({m.Taille})" : n.Nom;
        ResumeMatrices = _calc.Matrices.Count == 0
            ? "Aucune matrice définie pour le moment."
            : string.Join("  ·  ", _calc.Matrices.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value.Taille}"));
    }
}
