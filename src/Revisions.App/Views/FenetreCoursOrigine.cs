using System.IO;
using System.Text.Json;
using System.Windows;
using Revisions.App.Controles;
using Revisions.App.Services;

namespace Revisions.App.Views;

/// <summary>
/// Fenêtre qui affiche proprement un cours d'origine au format Markdown (.md), avec le même rendu
/// que la lecture (formules comprises). Évite de dépendre du programme que Windows associe aux .md.
/// </summary>
public sealed class FenetreCoursOrigine : Window
{
    public FenetreCoursOrigine(string chemin)
    {
        Title = Path.GetFileNameWithoutExtension(chemin);
        Width = 1000;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string texte = File.ReadAllText(chemin);
        Content = new VueMarkdown
        {
            Margin = new Thickness(8),
            Message = JsonSerializer.Serialize(new { elements = new[] { new { genre = "texte", markdown = texte } } }),
        };
        ServiceTheme.Habiller(this);
    }
}
