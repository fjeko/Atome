using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Views;

/// <summary>Semaine : 7 pastilles (voir AccueilViewModel.JourDeSemaine).</summary>
public partial class Semaine : UserControl
{
    public static readonly DependencyProperty JoursProperty = DependencyProperty.Register(nameof(Jours), typeof(IEnumerable), typeof(Semaine));

    public IEnumerable? Jours { get => (IEnumerable?)GetValue(JoursProperty); set => SetValue(JoursProperty, value); }

    public Semaine() => InitializeComponent();
}
