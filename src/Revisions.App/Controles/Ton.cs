using System.Windows;
using System.Windows.Media;
using Revisions.Core.Apparence;

namespace Revisions.App.Controles;

/// <summary>
/// Donne une couleur (un « ton ») à un élément et à tout ce qu'il contient : une couleur de sens
/// (« Succes », « Attention », « Erreur », « Info ») ou celle d'une matière.
/// </summary>
/// <remarks>
/// On pose <c>ds:Ton.Nom="Succes"</c> ou <c>ds:Ton.Matiere="{Binding Matiere}"</c> sur un conteneur ;
/// ses descendants lisent ensuite les trois nuances héritées <see cref="PaleProperty"/>, <see cref="TraitProperty"/>
/// et <see cref="TexteProperty"/>, ainsi que l'icône de la matière. Les nuances sont des références dynamiques
/// vers les ressources « Ton.Bleu.Pale »… : elles suivent le thème, y compris dans la Galerie (clair et sombre côte à côte).
/// </remarks>
public static class Ton
{
    // ---- Entrées --------------------------------------------------------------------------------

    /// <summary>Nom du ton : Info, Succes, Attention, Erreur, ou une teinte de matière (Bleu, Violet…).</summary>
    public static readonly DependencyProperty NomProperty = DependencyProperty.RegisterAttached(
        "Nom", typeof(string), typeof(Ton), new PropertyMetadata(null, (o, e) => Appliquer(o, e.NewValue as string)));

    /// <summary>Nom de la matière : sa teinte et son icône sont choisies par le Core (ReperesMatieres).</summary>
    public static readonly DependencyProperty MatiereProperty = DependencyProperty.RegisterAttached(
        "Matiere", typeof(string), typeof(Ton), new PropertyMetadata(null, (o, _) => AppliquerMatiere(o)));

    /// <summary>Partie de la matière (facultatif) : distingue par exemple le C# du bloc 1.</summary>
    public static readonly DependencyProperty PartieProperty = DependencyProperty.RegisterAttached(
        "Partie", typeof(string), typeof(Ton), new PropertyMetadata(null, (o, _) => AppliquerMatiere(o)));

    public static string? GetNom(DependencyObject o) => (string?)o.GetValue(NomProperty);
    public static void SetNom(DependencyObject o, string? v) => o.SetValue(NomProperty, v);
    public static string? GetMatiere(DependencyObject o) => (string?)o.GetValue(MatiereProperty);
    public static void SetMatiere(DependencyObject o, string? v) => o.SetValue(MatiereProperty, v);
    public static string? GetPartie(DependencyObject o) => (string?)o.GetValue(PartieProperty);
    public static void SetPartie(DependencyObject o, string? v) => o.SetValue(PartieProperty, v);

    // ---- Sorties (héritées par les descendants) --------------------------------------------------

    public static readonly DependencyProperty PaleProperty = DependencyProperty.RegisterAttached(
        "Pale", typeof(Brush), typeof(Ton), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty TraitProperty = DependencyProperty.RegisterAttached(
        "Trait", typeof(Brush), typeof(Ton), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty TexteProperty = DependencyProperty.RegisterAttached(
        "Texte", typeof(Brush), typeof(Ton), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty IconeProperty = DependencyProperty.RegisterAttached(
        "Icone", typeof(string), typeof(Ton), new FrameworkPropertyMetadata(ReperesMatieres.IconeLivre, FrameworkPropertyMetadataOptions.Inherits));

    public static Brush GetPale(DependencyObject o) => (Brush)o.GetValue(PaleProperty);
    public static void SetPale(DependencyObject o, Brush v) => o.SetValue(PaleProperty, v);
    public static Brush GetTrait(DependencyObject o) => (Brush)o.GetValue(TraitProperty);
    public static void SetTrait(DependencyObject o, Brush v) => o.SetValue(TraitProperty, v);
    public static Brush GetTexte(DependencyObject o) => (Brush)o.GetValue(TexteProperty);
    public static void SetTexte(DependencyObject o, Brush v) => o.SetValue(TexteProperty, v);
    public static string GetIcone(DependencyObject o) => (string)o.GetValue(IconeProperty);
    public static void SetIcone(DependencyObject o, string v) => o.SetValue(IconeProperty, v);

    private static void AppliquerMatiere(DependencyObject o)
    {
        var repere = ReperesMatieres.Pour(GetMatiere(o), GetPartie(o));
        o.SetValue(IconeProperty, repere.Icone);
        Appliquer(o, repere.Teinte.ToString());
    }

    private static void Appliquer(DependencyObject o, string? nom)
    {
        if (o is not FrameworkElement element || string.IsNullOrEmpty(nom)) return;
        element.SetResourceReference(PaleProperty, $"Ton.{nom}.Pale");
        element.SetResourceReference(TraitProperty, $"Ton.{nom}.Trait");
        element.SetResourceReference(TexteProperty, $"Ton.{nom}.Texte");
    }
}

/// <summary>Rayon des coins, posé par les styles et lu par les modèles de contrôle (boutons, tuiles).</summary>
public static class Forme
{
    public static readonly DependencyProperty RayonProperty = DependencyProperty.RegisterAttached(
        "Rayon", typeof(CornerRadius), typeof(Forme), new PropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetRayon(DependencyObject o) => (CornerRadius)o.GetValue(RayonProperty);
    public static void SetRayon(DependencyObject o, CornerRadius v) => o.SetValue(RayonProperty, v);

    /// <summary>Icône d'un bouton (caractère de Segoe Fluent Icons), affichée avant son texte.</summary>
    public static readonly DependencyProperty IconeProperty = DependencyProperty.RegisterAttached(
        "Icone", typeof(string), typeof(Forme), new PropertyMetadata(null));

    public static string? GetIcone(DependencyObject o) => (string?)o.GetValue(IconeProperty);
    public static void SetIcone(DependencyObject o, string? v) => o.SetValue(IconeProperty, v);

    /// <summary>Petit texte d'aide affiché à droite d'un bouton (raccourci clavier : « 1 », « Espace »).</summary>
    public static readonly DependencyProperty RaccourciProperty = DependencyProperty.RegisterAttached(
        "Raccourci", typeof(string), typeof(Forme), new PropertyMetadata(null));

    public static string? GetRaccourci(DependencyObject o) => (string?)o.GetValue(RaccourciProperty);
    public static void SetRaccourci(DependencyObject o, string? v) => o.SetValue(RaccourciProperty, v);
}
