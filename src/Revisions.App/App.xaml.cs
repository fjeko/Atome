using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Revisions.App.Services;
using Revisions.App.ViewModels;
using Revisions.Core.Planification;
using Revisions.Data;

namespace Revisions.App;

/// <summary>
/// Point de départ de l'application : prépare les services (injection de dépendances),
/// ouvre la fenêtre principale et attrape les erreurs imprévues.
/// </summary>
public partial class App : Application
{
    /// <summary>Fournisseur de services, pour les rares endroits qui ne peuvent pas recevoir leurs dépendances par constructeur.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

#if DEBUG
    /// <summary>
    /// Aide au développement : « Revisions.exe --ecran=lecture:maths-sq1-s03 » ouvre directement un écran.
    /// Valeurs : bibliotheque, statistiques, reglages, lecture:&lt;pack&gt;, session:&lt;pack&gt;.
    /// </summary>
    private static async void OuvrirEcranDeTest(MainViewModel principal, string[] arguments)
    {
        // « --touches=_,_,3,a » rejoue des touches dans la session (« _ » = Espace), pour les essais automatiques.
        if (arguments.FirstOrDefault(a => a.StartsWith("--touches=")) is { } touches)
        {
            _ = Current.Dispatcher.InvokeAsync(async () =>
            {
                foreach (string touche in touches["--touches=".Length..].Split(','))
                {
                    await Task.Delay(350);
                    if (principal.VueCourante is not SessionViewModel session) continue;
                    // « saisie:texte » remplit la zone de réponse écrite.
                    if (touche.StartsWith("saisie:")) session.ReponseSaisie = touche["saisie:".Length..];
                    else session.Touche(touche == "_" ? " " : touche);
                }
            });
        }
        await Task.Yield();

        // « --hauteur=1700 » agrandit la fenêtre (pour capturer un écran long en entier).
        if (arguments.FirstOrDefault(a => a.StartsWith("--hauteur=")) is { } hauteur)
            Current.MainWindow!.Height = double.Parse(hauteur["--hauteur=".Length..]);

        // « --theme=ocean » et « --accent=#D8498F » (ou « --accent=auto ») changent l'apparence au vol.
        var etat = Services.GetRequiredService<EtatApplication>();
        string? theme = arguments.FirstOrDefault(a => a.StartsWith("--theme="))?["--theme=".Length..];
        string? accent = arguments.FirstOrDefault(a => a.StartsWith("--accent="))?["--accent=".Length..];
        if (theme is not null || accent is not null)
        {
            if (theme is not null) etat.Reglages.Theme = theme;
            if (accent is not null) etat.Reglages.CouleurAccent = accent == "auto" ? null : accent;
            etat.Enregistrer();
        }

        string? ecran = arguments.FirstOrDefault(a => a.StartsWith("--ecran="))?["--ecran=".Length..];
        if (ecran is null) return;
        string[] morceaux = ecran.Split(':', 2);
        switch (morceaux[0])
        {
            case "bibliotheque":
                // « bibliotheque:0,1 » descend dans les tuiles ; « bibliotheque:?matrice » lance une recherche.
                principal.AllerBibliotheque();
                if (morceaux.Length > 1 && principal.VueCourante is BibliothequeViewModel bibliotheque)
                {
                    if (morceaux[1].StartsWith('?')) bibliotheque.Recherche = morceaux[1][1..];
                    else bibliotheque.DescendreParIndices(morceaux[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse));
                }
                break;
            case "statistiques": principal.AllerStatistiques(); break;
            case "reglages": principal.AllerReglages(); break;
            case "aide":
                principal.AllerAide();
                if (morceaux.Length > 1 && principal.VueCourante is AideViewModel aide) aide.Index = int.Parse(morceaux[1]);
                break;
            case "lecture": principal.OuvrirLecture(morceaux[1]); break;
            case "pdf":
                ActivatorUtilities.CreateInstance<LectureViewModel>(Services, new ParametresLecture(morceaux[1], null, false)).VoirPdfCommand.Execute(null);
                break;
            case "notion": principal.RevoirLaNotion(morceaux[1]); break;
            case "session":
                principal.DemarrerSession(Services.GetRequiredService<DepotRevisions>().CartesDuPack(morceaux[1]), "seance");
                break;
        }
    }
#endif

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une erreur imprévue ne ferme pas l'application : on la note et on prévient simplement.
        DispatcherUnhandledException += (_, args) =>
        {
            Journal.Erreur("Interface", args.Exception);
            MessageBox.Show(
                "Une erreur inattendue s'est produite. Tes révisions déjà enregistrées ne sont pas perdues.\n\n" + args.Exception.Message,
                "Révisions BTS SIO", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new BaseDeDonnees(BaseDeDonnees.CheminParDefaut));
            services.AddSingleton<DepotBibliotheque>();
            services.AddSingleton<DepotRevisions>();
            services.AddSingleton<DepotProfil>();
            services.AddSingleton<ImportateurPacks>();
            services.AddSingleton(new Fsrs());
            services.AddSingleton<Planificateur>();
            services.AddSingleton<EtatApplication>();
            services.AddSingleton<MainViewModel>();
            Services = services.BuildServiceProvider();

            Services.GetRequiredService<EtatApplication>().Initialiser();

            var principal = Services.GetRequiredService<MainViewModel>();
            MainWindow = new MainWindow { DataContext = principal };
            MainWindow.Show();
            await principal.DemarrerAsync();
#if DEBUG
            OuvrirEcranDeTest(principal, e.Args);
#endif
        }
        catch (Exception exception)
        {
            Journal.Erreur("Démarrage", exception);
            MessageBox.Show("L'application n'a pas pu démarrer :\n\n" + exception.Message + "\n\nDétails dans " + Journal.Chemin,
                "Révisions BTS SIO", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
