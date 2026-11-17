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
    private double _dernierDefilement;   // dernière position connue de la page
    private bool _restauration;          // vrai pendant qu'on remet la page où elle était

    public VueReglages()
    {
        InitializeComponent();
        // Changer de thème recalcule tout l'affichage et ramène la page tout en haut :
        // on mémorise la position et on la remet juste après, pour pouvoir essayer les thèmes à la suite.
        Defilement.ScrollChanged += (_, e) => { if (!_restauration && e.VerticalChange != 0) _dernierDefilement = Defilement.VerticalOffset; };
        Loaded += (_, _) => { if (App.Services.GetService(typeof(Services.EtatApplication)) is Services.EtatApplication etat) etat.ThemeChange += Restaurer; };
        Unloaded += (_, _) => { if (App.Services.GetService(typeof(Services.EtatApplication)) is Services.EtatApplication etat) etat.ThemeChange -= Restaurer; };
    }

    private void Restaurer()
    {
        double position = _dernierDefilement;
        _restauration = true;
        Dispatcher.BeginInvoke(() =>
        {
            Defilement.ScrollToVerticalOffset(position);
            _dernierDefilement = position;
            Dispatcher.BeginInvoke(() => _restauration = false, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
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
