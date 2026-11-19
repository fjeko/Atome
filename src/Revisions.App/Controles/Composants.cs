using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Controles;

/// <summary>
/// TuileMatiere : grande tuile cliquable aux couleurs d'une matière (bibliothèque, choix de révision).
/// La couleur vient de <c>ds:Ton.Matiere</c> posé sur la tuile ; l'apparence est dans Themes/Composants.xaml.
/// </summary>
public class TuileMatiere : Button
{
    public static readonly DependencyProperty TitreProperty = DependencyProperty.Register(nameof(Titre), typeof(string), typeof(TuileMatiere));
    /// <summary>Petite mention au-dessus du titre (« Bloc 1 »), ou rien.</summary>
    public static readonly DependencyProperty EtiquetteProperty = DependencyProperty.Register(nameof(Etiquette), typeof(string), typeof(TuileMatiere));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(TuileMatiere));
    /// <summary>Maîtrise de 0 à 1, affichée dans l'anneau ; une valeur négative masque l'anneau.</summary>
    public static readonly DependencyProperty ProgressionProperty = DependencyProperty.Register(nameof(Progression), typeof(double), typeof(TuileMatiere), new PropertyMetadata(-1.0));
    /// <summary>Petite mention en bas de la tuile (« 12 à revoir »), ou rien.</summary>
    public static readonly DependencyProperty MentionProperty = DependencyProperty.Register(nameof(Mention), typeof(string), typeof(TuileMatiere));
    /// <summary>La tuile se coche quand sa matière (ou séance) est finie.</summary>
    public static readonly DependencyProperty CocheeProperty = DependencyProperty.Register(nameof(Cochee), typeof(bool), typeof(TuileMatiere));
    /// <summary>Affiche le badge « Libre » (cours libres intégrés).</summary>
    public static readonly DependencyProperty LibreProperty = DependencyProperty.Register(nameof(Libre), typeof(bool), typeof(TuileMatiere));

    public string? Titre { get => (string?)GetValue(TitreProperty); set => SetValue(TitreProperty, value); }
    public string? Etiquette { get => (string?)GetValue(EtiquetteProperty); set => SetValue(EtiquetteProperty, value); }
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double Progression { get => (double)GetValue(ProgressionProperty); set => SetValue(ProgressionProperty, value); }
    public string? Mention { get => (string?)GetValue(MentionProperty); set => SetValue(MentionProperty, value); }
    public bool Cochee { get => (bool)GetValue(CocheeProperty); set => SetValue(CocheeProperty, value); }
    public bool Libre { get => (bool)GetValue(LibreProperty); set => SetValue(LibreProperty, value); }
}

/// <summary>
/// Bandeau : message posé au-dessus du contenu (information, mise à jour, reprise après une pause).
/// Sa couleur vient de <c>ds:Ton.Nom</c> ; <see cref="Actions"/> reçoit les boutons, à droite ; le contenu
/// (facultatif) s'affiche sous le texte.
/// </summary>
public class Bandeau : ContentControl
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(nameof(Icone), typeof(string), typeof(Bandeau), new PropertyMetadata(Icones.Info));
    public static readonly DependencyProperty TitreProperty = DependencyProperty.Register(nameof(Titre), typeof(string), typeof(Bandeau));
    public static readonly DependencyProperty TexteProperty = DependencyProperty.Register(nameof(Texte), typeof(string), typeof(Bandeau));
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(nameof(Actions), typeof(object), typeof(Bandeau));

    public string? Icone { get => (string?)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }
    public string? Titre { get => (string?)GetValue(TitreProperty); set => SetValue(TitreProperty, value); }
    public string? Texte { get => (string?)GetValue(TexteProperty); set => SetValue(TexteProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    static Bandeau() => FocusableProperty.OverrideMetadata(typeof(Bandeau), new FrameworkPropertyMetadata(false));
}

/// <summary>
/// Ligne de réglage uniforme (écran Réglages) : icône facultative, libellé, phrase d'aide en petit,
/// et le contrôle (le contenu) aligné à droite. Avec <see cref="Dessous"/>, le contrôle passe sous le texte,
/// sur toute la largeur (grilles de tuiles, listes). L'apparence est dans Themes/Composants.xaml.
/// </summary>
public class LigneReglage : ContentControl
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(nameof(Icone), typeof(string), typeof(LigneReglage));
    public static readonly DependencyProperty LibelleProperty = DependencyProperty.Register(nameof(Libelle), typeof(string), typeof(LigneReglage));
    public static readonly DependencyProperty AideProperty = DependencyProperty.Register(nameof(Aide), typeof(string), typeof(LigneReglage));
    public static readonly DependencyProperty DessousProperty = DependencyProperty.Register(nameof(Dessous), typeof(bool), typeof(LigneReglage));

    public string? Icone { get => (string?)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }
    public string? Libelle { get => (string?)GetValue(LibelleProperty); set => SetValue(LibelleProperty, value); }
    public string? Aide { get => (string?)GetValue(AideProperty); set => SetValue(AideProperty, value); }
    public bool Dessous { get => (bool)GetValue(DessousProperty); set => SetValue(DessousProperty, value); }

    // Le contrôle contenu reçoit le focus, pas la ligne : un seul arrêt au clavier par réglage.
    static LigneReglage() => FocusableProperty.OverrideMetadata(typeof(LigneReglage), new FrameworkPropertyMetadata(false));
}

/// <summary>Icônes de Segoe Fluent Icons utilisées par l'interface (une seule liste, pour rester cohérent).</summary>
public static class Icones
{
    public const string Accueil = "";
    public const string Bibliotheque = "";
    public const string Groupe = "";
    /// <summary>Une personne seule (U+E77B, « Contact ») : TD seul ; trois superposées font « À plusieurs ».</summary>
    public const string Personne = "";
    public const string Acquis = "";
    public const string Planning = "";
    public const string Statistiques = "";
    public const string Reglages = "";
    public const string Aide = "";
    public const string Galerie = "";
    public const string Menu = "";
    public const string Retour = "";
    public const string Suivant = "";
    public const string Chevron = "";
    public const string Fermer = "";
    public const string Coche = "";
    public const string Info = "";
    public const string Attention = "";
    public const string Telecharger = "";
    public const string Drapeau = "";
    public const string Livre = "";
    public const string Lire = ""; // livre : revoir la notion dans le cours
    public const string Pdf = "";
    public const string Recherche = "";
    public const string Jouer = "";
    public const string Pause = "";
    public const string PleinEcran = "";
    public const string Etoile = "";
    public const string Feu = "";
    public const string Calendrier = "";
    public const string Horloge = "";
    public const string Ampoule = "";
    public const string Coeur = "";
    public const string Ajouter = "";
    public const string Importer = "";
    public const string Exporter = "";
    public const string Copier = "";
    public const string Actualiser = "";
    public const string Filtre = "";
    public const string Cible = "";
    public const string Ouvrir = "";
    public const string Sommaire = "";
    public const string Clavier = "";
    public const string Dossier = "";
    public const string Partager = "";
    public const string ChevronBas = "";
    public const string ChevronHaut = "";
    public const string Bulle = "";
    /// <summary>Calculatrice (U+E8EF).</summary>
    public const string Calculatrice = "";
}
