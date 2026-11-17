using System.Net.Http;
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
    /// <summary>Horloge d'essai : l'heure du système, décalée d'une durée fixe.</summary>
    private sealed class HorlogeDecalee(TimeSpan decalage) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + decalage;
    }

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
            case "td":
                // « td » ouvre l'écran ; « td:MAT1-2-8-48217 » rejoint ce TD ; « td:CODE:demo » remplit en plus
                // quelques cases (justes et fausses) et vérifie : pour capturer l'écran de correction.
                principal.AllerTdGroupe();
                if (morceaux.Length > 1 && principal.VueCourante is TdGroupeViewModel td) td.DemonstrationDev(morceaux[1]);
                break;
            case "reglages": principal.AllerReglages(); break;
            case "planning": principal.AllerPlanning(); break;
            case "aide":
                principal.AllerAide();
                if (morceaux.Length > 1 && principal.VueCourante is AideViewModel aide) aide.Index = int.Parse(morceaux[1]);
                break;
            case "lecture": principal.OuvrirLecture(morceaux[1]); break;
            case "pdf":
                ActivatorUtilities.CreateInstance<LectureViewModel>(Services, new ParametresLecture(morceaux[1], null, false)).VoirPdfCommand.Execute(null);
                break;
            case "notion": principal.RevoirLaNotion(morceaux[1]); break;
            case "rapport": principal.AnnoncerImport(principal.ImporterDossier(), afficherRapportSiRien: true); break;
            case "session":
                principal.DemarrerSession(Services.GetRequiredService<DepotRevisions>().CartesDuPack(morceaux[1]), "seance");
                break;
        }
    }
#endif

    // ---- Instance unique -----------------------------------------------------------------------
    // Un Mutex nommé empêche de lancer Atome deux fois (deux instances sur la même base se gêneraient).
    // Le second lancement réveille la première fenêtre via un EventWaitHandle nommé, puis s'arrête sans bruit.
    private const string NomMutex = "Atome-instance-unique-RevisionsBTS";
    private const string NomSignal = "Atome-instance-unique-RevisionsBTS-signal";
    private Mutex? _mutex;
    private EventWaitHandle? _signal;

    /// <summary>Vrai si on est la première instance ; sinon, prévient l'autre de passer au premier plan.</summary>
    private bool PrendreLaPlace()
    {
        _mutex = new Mutex(initiallyOwned: true, NomMutex, out bool premiere);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, NomSignal);
        if (!premiere)
        {
            _signal.Set();
            return false;
        }
        var fil = new Thread(() =>
        {
            while (_signal.WaitOne())
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is not { } fenetre) return;
                    if (fenetre.WindowState == WindowState.Minimized) fenetre.WindowState = WindowState.Normal;
                    fenetre.Activate();
                    fenetre.Topmost = true; // astuce classique : force le passage au premier plan
                    fenetre.Topmost = false;
                    fenetre.Focus();
                });
        }) { IsBackground = true };
        fil.Start();
        return true;
    }

    /// <summary>
    /// Rend la place d'instance unique juste avant de lancer l'installateur d'une mise à jour :
    /// l'Atome relancé à la fin de l'installation doit pouvoir démarrer (et l'installateur, qui guette
    /// ce Mutex, sait ainsi qu'Atome est fermé).
    /// </summary>
    internal void LibererInstanceUnique()
    {
        try { _mutex?.ReleaseMutex(); }
        catch (ApplicationException) { } // pas propriétaire : rien à rendre
        _mutex?.Dispose();
        _mutex = null;
    }

    /// <summary>Reprend la place d'instance unique si l'installateur n'a finalement pas pu être lancé.</summary>
    internal void ReprendreInstanceUnique() => _mutex ??= new Mutex(initiallyOwned: true, NomMutex, out _);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!PrendreLaPlace()) { Shutdown(); return; }
        Journal.Etape("Lancement");

        // Une erreur imprévue ne ferme pas l'application : on la note et on prévient simplement.
        DispatcherUnhandledException += (_, args) =>
        {
            Journal.Erreur("Interface", args.Exception);
            MessageBox.Show(
                "Une erreur inattendue s'est produite. Tes révisions déjà enregistrées ne sont pas perdues.\n\n" + args.Exception.Message,
                "Atome", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        try
        {
            var services = new ServiceCollection();
#if DEBUG
            // Essais : REVISIONS_DECALAGE_JOURS=10 fait croire à l'application qu'on est 10 jours plus tard
            // (pour voir des cartes à revoir et le mode reprise sans attendre).
            services.AddSingleton<TimeProvider>(
                int.TryParse(Environment.GetEnvironmentVariable("REVISIONS_DECALAGE_JOURS"), out int jours)
                    ? new HorlogeDecalee(TimeSpan.FromDays(jours))
                    : TimeProvider.System);
#else
            services.AddSingleton(TimeProvider.System);
#endif
            services.AddSingleton(new BaseDeDonnees(BaseDeDonnees.CheminParDefaut));
            Journal.Etape("Base prête (migrations)");
            services.AddSingleton<DepotBibliotheque>();
            services.AddSingleton<DepotRevisions>();
            services.AddSingleton<DepotProfil>();
            services.AddSingleton<DepotAcquis>();
            services.AddSingleton<DepotPlanning>();
            services.AddSingleton<DepotSignalements>();
            services.AddSingleton<ServiceSignalement>();
            services.AddSingleton<SelectionDuJour>();
            services.AddSingleton<ImportateurPacks>();
            services.AddSingleton<DepotCoursLibres>();
            services.AddSingleton(new Fsrs());
            services.AddSingleton<Planificateur>();
            services.AddSingleton<EtatApplication>();
            // Mises à jour : un seul client HTTP pour toute l'application (les délais sont gérés requête par requête).
            services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(8),
            }) { Timeout = Timeout.InfiniteTimeSpan });
            services.AddSingleton(s => new Revisions.Core.MisesAJour.ServiceMiseAJour(
                s.GetRequiredService<HttpClient>(), s.GetRequiredService<DepotProfil>(),
                s.GetRequiredService<TimeProvider>(), InfosApplication.Version));
            services.AddSingleton<ServiceInstallation>();
            // TD en groupe : générateurs de TD (matrices, séance d'un pack).
            services.AddSingleton(s => ServiceTdGroupe.CreerCatalogue(s.GetRequiredService<DepotBibliotheque>(),
                s.GetRequiredService<DepotRevisions>(), s.GetRequiredService<EtatApplication>()));
            services.AddSingleton<MainViewModel>();
            services.AddTransient<PartageViewModel>();
            Services = services.BuildServiceProvider();

            Services.GetRequiredService<EtatApplication>().Initialiser();
            Journal.Etape("Services et réglages prêts");

            var principal = Services.GetRequiredService<MainViewModel>();
            MainWindow = new MainWindow { DataContext = principal };
            Journal.Etape("Fenêtre créée");
            MainWindow.Show();
            Journal.Etape("Fenêtre affichée");
            await principal.DemarrerAsync(); // lance aussi la vérification des mises à jour, en tâche de fond
#if DEBUG
            OuvrirEcranDeTest(principal, e.Args);
#endif
        }
        catch (Exception exception)
        {
            Journal.Erreur("Démarrage", exception);
            MessageBox.Show("L'application n'a pas pu démarrer :\n\n" + exception.Message + "\n\nDétails dans " + Journal.Chemin,
                "Atome", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
