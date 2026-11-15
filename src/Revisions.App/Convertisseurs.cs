using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Revisions.App;

/// <summary>Affiche un élément seulement si la valeur est « vraie » : booléen vrai, texte non vide, objet non nul.</summary>
/// <remarks>Avec le paramètre « inverse », la logique est retournée.</remarks>
public sealed class VisibleSiConvertisseur : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool vrai = value switch
        {
            bool b => b,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "inverse") vrai = !vrai;
        return vrai ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
