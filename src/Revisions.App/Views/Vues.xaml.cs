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
    public VueFinSession()
    {
        InitializeComponent();
        // Son de fin de session (si les sons sont activés) ; niveau gagné : petite célébration du compteur et son « niveau ».
        Loaded += (_, _) =>
        {
            if (DataContext is not ViewModels.FinSessionViewModel fin) return;
            Services.ServiceSons.Jouer(fin.NiveauGagne ? Services.ServiceSons.Son.Niveau : Services.ServiceSons.Son.FinSession);
            if (fin.NiveauGagne) Services.Mouvement.Celebrer(Compteur);
        };
    }
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
        Loaded += (_, _) => Suivre(true);
        Unloaded += (_, _) => Suivre(false);
    }

    // On remonte en haut de page quand on change de section.
    private void Suivre(bool suivre)
    {
        if (DataContext is ViewModels.ReglagesViewModel vm)
        {
            vm.Navigation.PropertyChanged -= NavigationChange;
            if (suivre) vm.Navigation.PropertyChanged += NavigationChange;
        }
    }

    private void NavigationChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.NavigationReglagesViewModel.SectionChoisie)) Defilement.ScrollToTop();
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

public partial class FenetreSignalement : Window
{
    public FenetreSignalement()
    {
        InitializeComponent();
        Services.ServiceTheme.Habiller(this);
    }

    /// <summary>« Envoyer » ferme la fenêtre avec le résultat « oui » ; le service lit alors la saisie.</summary>
    private void Envoyer(object sender, RoutedEventArgs e) => DialogResult = true;
}

public partial class VueMesSignalements : UserControl
{
    public VueMesSignalements() => InitializeComponent();
}

public partial class VueAcquis : UserControl
{
    public VueAcquis() => InitializeComponent();
}

public partial class VueQuestionnaire : UserControl
{
    public VueQuestionnaire() => InitializeComponent();
}

public partial class VuePlanning : UserControl
{
    public VuePlanning() => InitializeComponent();
}

public partial class VueChoixRevision : UserControl
{
    public VueChoixRevision() => InitializeComponent();
}
