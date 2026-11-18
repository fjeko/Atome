using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une page de l'aide.</summary>
/// <param name="Titre">Titre affiché dans le sommaire (première ligne « # … » du fichier).</param>
/// <param name="Markdown">Texte de la page, sans la ligne de titre.</param>
/// <param name="Prompt">Texte à copier pour créer un pack (seulement sur la page concernée).</param>
public sealed record PageAide(string Titre, string Markdown, string? Prompt);

/// <summary>
/// Écran Aide. Les pages sont des fichiers Markdown du dossier Aide/, incorporés dans l'exécutable
/// et affichés avec la même zone de rendu que les cours.
/// </summary>
public partial class AideViewModel : ObservableObject
{
    /// <summary>Page qui contient le prompt à copier.</summary>
    private const string PagePrompt = "4-creer-un-pack.md";

    public IReadOnlyList<PageAide> Pages { get; }

    /// <summary>Page affichée (liée à la sélection du sommaire).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PromptDisponible))]
    private int _index;

    [ObservableProperty]
    private string? _messageRendu;

    /// <summary>Petit message après la copie (« Prompt copié »).</summary>
    [ObservableProperty]
    private string? _confirmation;

    public bool PromptDisponible => Index >= 0 && Index < Pages.Count && Pages[Index].Prompt is not null;

    public AideViewModel(EtatApplication etat)
    {
        Pages = ChargerPages(etat);
        Rendre();
    }

    partial void OnIndexChanged(int value)
    {
        Confirmation = null;
        if (value >= 0 && value < Pages.Count) Rendre();
    }

    private void Rendre()
    {
        var page = Pages[Index];
        var elements = new List<object>
        {
            new { genre = "titre", markdown = page.Titre },
            new { genre = "texte", markdown = page.Markdown },
        };
        if (page.Prompt is not null) elements.Add(new { genre = "prompt", markdown = page.Prompt });
        MessageRendu = JsonSerializer.Serialize(new { elements });
    }

    /// <summary>Ouvre le Discord de la communauté dans le navigateur.</summary>
    [RelayCommand]
    private void OuvrirDiscord()
    {
        if (!Liens.OuvrirDiscord()) Confirmation = "Impossible d'ouvrir le navigateur.";
    }

    /// <summary>Copie le prompt dans le presse-papiers, prêt à être collé dans une conversation avec Claude.</summary>
    [RelayCommand]
    private void CopierPrompt()
    {
        if (Pages[Index].Prompt is not { } prompt) return;
        try
        {
            Clipboard.SetText(prompt);
            Confirmation = "Prompt copié : colle-le dans ta conversation avec Claude.";
            ServiceToast.Afficher("Prompt copié dans le presse-papiers");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Le presse-papiers de Windows est parfois occupé par un autre programme.
            Confirmation = "Le presse-papiers est occupé, réessaie dans un instant.";
        }
    }

    // ---- Chargement des pages --------------------------------------------------------------

    private static List<PageAide> ChargerPages(EtatApplication etat)
    {
        var assemblage = Assembly.GetExecutingAssembly();
        string prompt = ExtrairePrompt(Lire(assemblage, "Aide/prompt-type.md"));

        return assemblage.GetManifestResourceNames()
            .Where(nom => nom.StartsWith("Aide/") && nom != "Aide/prompt-type.md")
            .Order(Core.Ordre.TriNaturel.Comparateur) // « 10-… » après « 9-… »
            .Select(nom =>
            {
                // Les fichiers contiennent des repères remplacés par les vrais dossiers de cet ordinateur.
                string texte = Lire(assemblage, nom)
                    .Replace("{{DOSSIER_PACKS}}", etat.Reglages.DossierPacks ?? "(non défini : choisis-le dans Réglages)")
                    .Replace("{{DOSSIER_DONNEES}}", BaseDeDonnees.DossierDonnees);
                var (titre, corps) = SeparerTitre(texte);
                return new PageAide(titre, corps, nom.EndsWith(PagePrompt) ? prompt : null);
            })
            .ToList();
    }

    private static string Lire(Assembly assemblage, string nom)
    {
        using var flux = assemblage.GetManifestResourceStream(nom)
                         ?? throw new InvalidOperationException($"Page d'aide introuvable : {nom}");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    /// <summary>Sépare la première ligne « # Titre » du reste de la page.</summary>
    private static (string Titre, string Corps) SeparerTitre(string texte)
    {
        texte = texte.ReplaceLineEndings("\n").TrimStart();
        int fin = texte.IndexOf('\n');
        if (!texte.StartsWith("# ") || fin < 0) return ("Aide", texte);
        return (texte[2..fin].Trim(), texte[(fin + 1)..]);
    }

    /// <summary>
    /// Dans docs/prompt-type.md, le prompt à coller est tout ce qui suit la première ligne « --- »
    /// (avant, ce sont des explications pour l'humain).
    /// </summary>
    private static string ExtrairePrompt(string fichier)
    {
        fichier = fichier.ReplaceLineEndings("\n");
        int separateur = fichier.IndexOf("\n---\n", StringComparison.Ordinal);
        return (separateur < 0 ? fichier : fichier[(separateur + 5)..]).Trim();
    }
}
