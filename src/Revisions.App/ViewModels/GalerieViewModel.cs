using CommunityToolkit.Mvvm.Input;
using Revisions.App.Controles;
using Revisions.App.Services;
using Revisions.Core.Apparence;
using Revisions.Core.Mascotte;

namespace Revisions.App.ViewModels;

/// <summary>Un maillon de fil d'Ariane d'exemple (mêmes noms de propriétés que dans la Bibliothèque).</summary>
public sealed record MaillonExemple(string Titre, int Profondeur, bool EstCourant);

/// <summary>Une tuile d'exemple : une matière, sa maîtrise et ses mentions.</summary>
public sealed record TuileExemple(string Matiere, string? Partie, string Titre, string Detail, double Progression, string? Mention, bool Cochee, bool Libre);

/// <summary>Une icône de l'interface et son nom.</summary>
public sealed record IconeExemple(string Nom, string Glyphe);

/// <summary>
/// Page « Galerie », réservée au développement : tous les composants du système de design dans tous leurs
/// états, en sombre et en clair côte à côte. Elle sert de référence et à repérer les régressions.
/// </summary>
public partial class GalerieViewModel
{
    public IReadOnlyList<JourDeSemaine> Semaine { get; } =
    [
        new("L", true, false, "lundi"), new("M", true, false, "mardi"), new("M", false, false, "mercredi"),
        new("J", true, true, "jeudi"), new("V", false, false, "vendredi"), new("S", false, false, "samedi"), new("D", false, false, "dimanche"),
    ];

    public IReadOnlyList<MaillonExemple> Fil { get; } =
        [new("Bibliothèque", 0, false), new("Bloc 1", 1, false), new("B1.2", 2, false), new("Thème 3", 3, true)];

    public IReadOnlyList<TuileExemple> Tuiles { get; } =
    [
        new("Bloc 1 – Support et mise à disposition de services informatiques", null, "Bloc 1 – Support", "54 séances", 0.42, "12 à revoir", false, false),
        new("Bloc 1 – Support", "B1.2 Partie I – SQ1 C#", "B1.2 – C#", "18 séances", 0.8, null, false, true),
        new("Bloc 2 – SLAM", null, "Bloc 2 – SLAM", "30 séances", 0.15, "3 à revoir", false, false),
        new("Bloc 3 – Cybersécurité", null, "Bloc 3 – Cybersécurité", "21 séances", 1, null, true, false),
        new("Mathématiques pour l'informatique", null, "Mathématiques", "16 séances", 0.6, null, false, true),
        new("Algorithmique appliquée", null, "Algorithmique", "12 séances", 0.05, "Nouveau", false, false),
        new("CEJM – Culture économique, juridique et managériale", null, "CEJM", "24 séances", 0.33, null, false, false),
        new("Anglais", null, "Anglais", "40 séances", 0.7, null, false, true),
        new("Physique appliquée", null, "Matière importée", "3 séances", -1, null, false, false),
    ];

    public IReadOnlyList<string> Tons { get; } = ["Info", "Succes", "Attention", "Erreur", .. Enum.GetNames<Teinte>()];

    public IReadOnlyList<Humeur> Humeurs { get; } = Enum.GetValues<Humeur>();

    public IReadOnlyList<IconeExemple> IconesInterface { get; } = typeof(Icones).GetFields()
        .Select(f => new IconeExemple(f.Name, (string)f.GetValue(null)!)).ToList();

    public IReadOnlyList<IconeExemple> IconesMatieres { get; } = typeof(ReperesMatieres).GetFields()
        .Where(f => f.IsLiteral).Select(f => new IconeExemple(f.Name.Replace("Icone", ""), (string)f.GetValue(null)!)).ToList();

    public Toast ExempleToast { get; } = new("Signalement enregistré, merci !", Icones.Coche);

    [RelayCommand]
    private static void Toast() => ServiceToast.Afficher("Prompt copié dans le presse-papiers");

    [RelayCommand]
    private static void Rien(object? _) { }
}
