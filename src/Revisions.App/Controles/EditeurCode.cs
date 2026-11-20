using System.Windows.Controls;
using System.Windows.Input;
using Revisions.Core.Modeles;

namespace Revisions.App.Controles;

/// <summary>
/// Transforme une zone de texte en petit éditeur de code, le temps d'une carte qui demande du code :
/// Entrée garde l'indentation, Tab indente de 4 espaces, Maj+Tab désindente. Les règles elles-mêmes sont
/// dans le Core (EditionCode, testé) ; ici on ne fait que les appliquer au TextBox.
/// On passe par SelectedText pour que Ctrl+Z annule ces modifications comme une frappe normale.
/// </summary>
public static class EditeurCode
{
    /// <summary>Traite la touche si elle concerne l'édition de code. Renvoie vrai si elle est consommée.</summary>
    public static bool Traiter(TextBox zone, KeyEventArgs e)
    {
        var mod = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Enter when mod == ModifierKeys.None || mod == ModifierKeys.Shift:
                Inserer(zone, EditionCode.NouvelleLigne(zone.Text, zone.SelectionStart));
                return true;

            case Key.Tab when mod == ModifierKeys.None && !zone.SelectedText.Contains('\n'):
                Inserer(zone, EditionCode.Retrait);
                return true;

            case Key.Tab when mod == ModifierKeys.None || mod == ModifierKeys.Shift:
                // Plusieurs lignes sélectionnées (ou Maj+Tab) : on décale les lignes entières.
                var (debut, fin) = EditionCode.Lignes(zone.Text, zone.SelectionStart, zone.SelectionLength);
                string bloc = zone.Text[debut..fin];
                string nouveau = mod == ModifierKeys.Shift ? EditionCode.Desindenter(bloc) : EditionCode.Indenter(bloc);
                int curseur = zone.SelectionStart, longueur = zone.SelectionLength;
                zone.Select(debut, fin - debut);
                zone.SelectedText = nouveau;
                if (longueur > 0) zone.Select(debut, nouveau.Length); // le bloc reste sélectionné pour enchaîner
                else zone.Select(Math.Clamp(curseur + (nouveau.Length - bloc.Length), debut, debut + nouveau.Length), 0);
                return true;
        }
        return false;
    }

    private static void Inserer(TextBox zone, string texte)
    {
        zone.SelectedText = texte;
        zone.Select(zone.SelectionStart + texte.Length, 0);
    }
}
