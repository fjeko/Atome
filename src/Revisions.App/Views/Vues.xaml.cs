using System.Windows;
using System.Windows.Controls;

namespace Revisions.App.Views;

// Code-behind des vues. Il est volontairement vide : toute la logique est dans les ViewModels (MVVM).
// Chaque classe ne fait que charger son fichier XAML.

public partial class VueAccueil : UserControl
{
    public VueAccueil() => InitializeComponent();
}

public partial class VueBibliotheque : UserControl
{
    public VueBibliotheque() => InitializeComponent();
}

public partial class VueLecture : UserControl
{
    public VueLecture() => InitializeComponent();
}

public partial class VueFinSession : UserControl
{
    public VueFinSession() => InitializeComponent();
}

public partial class VueStatistiques : UserControl
{
    public VueStatistiques() => InitializeComponent();
}


public partial class VueAide : UserControl
{
    public VueAide() => InitializeComponent();
}

public partial class VueReglages : UserControl
{
    public VueReglages() => InitializeComponent();
}

public partial class FenetreLecture : Window
{
    public FenetreLecture()
    {
        InitializeComponent();
        Services.ServiceTheme.Habiller(this);
    }

    private void Fermer(object sender, RoutedEventArgs e) => Close();
}

public partial class FenetreRapport : Window
{
    public FenetreRapport()
    {
        InitializeComponent();
        Services.ServiceTheme.Habiller(this);
    }

    private void Fermer(object sender, RoutedEventArgs e) => Close();
}
