using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Views;

/// <summary>
/// Le petit personnage de l'accueil. Expressions possibles (propriété <see cref="Expression"/>) :
/// « content », « clin » (clin d'œil), « joie » (yeux fermés, grand sourire) et « malin » (clin d'œil en coin).
/// </summary>
public partial class Mascotte : UserControl
{
    public static readonly DependencyProperty ExpressionProperty = DependencyProperty.Register(
        nameof(Expression), typeof(string), typeof(Mascotte),
        new PropertyMetadata("content", (o, _) => ((Mascotte)o).Appliquer()));

    public string Expression
    {
        get => (string)GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    public Mascotte()
    {
        InitializeComponent();
        Appliquer();
    }

    /// <summary>Montre les yeux et la bouche qui correspondent à l'expression.</summary>
    private void Appliquer()
    {
        // Pendant InitializeComponent, les éléments nommés n'existent pas encore.
        if (OeilG is null) return;
        string e = Expression;
        bool joie = e == "joie", clin = e is "clin" or "malin";
        OeilG.Visibility = joie ? Visibility.Collapsed : Visibility.Visible;
        OeilD.Visibility = joie || clin ? Visibility.Collapsed : Visibility.Visible;
        ArcG.Visibility = joie ? Visibility.Visible : Visibility.Collapsed;
        ArcD.Visibility = joie || clin ? Visibility.Visible : Visibility.Collapsed;
        Sourire.Visibility = e is "joie" or "malin" ? Visibility.Collapsed : Visibility.Visible;
        Rire.Visibility = joie ? Visibility.Visible : Visibility.Collapsed;
        EnCoin.Visibility = e == "malin" ? Visibility.Visible : Visibility.Collapsed;
    }
}
