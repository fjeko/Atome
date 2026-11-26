using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.App.Views;
using Revisions.Core.Mascotte;
using Revisions.Core.Positionnement;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Par où commence l'écran : le test, ou le choix direct d'un niveau.</summary>
public enum ModePositionnement { Test, Choix }

public sealed record ParametresPositionnement(BanquePositionnement Banque, ModePositionnement Mode);

/// <summary>Un niveau proposé dans « Je connais mon niveau ».</summary>
public sealed record ChoixNiveau(int Indice, string Code, string Nom, string Description)
{
    public string Titre => $"{Code} – {Nom}";
}

/// <summary>Une ligne du bilan par compétence (« Grammaire · 9 / 12 »).</summary>
public sealed record LigneCompetence(string Nom, string Score, double Part);

/// <summary>
/// Test de positionnement, en plein écran : consignes (et vérification du son), questions une à une,
/// puis résultat, appliqué aux acquis de la matière. Ou bien choix direct du niveau de départ.
/// Les réponses ne sont pas corrigées pendant le test : c'est un positionnement, pas un contrôle.
/// </summary>
public sealed partial class PositionnementViewModel : ObservableObject
{
    /// <summary>Nombre d'écoutes possibles par question d'écoute (la lecture automatique comprise).</summary>
    public const int EcoutesMax = 3;

    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotPositionnement _depot;
    private readonly ServiceVoix _voix;
    private readonly BanquePositionnement _banque;
    private TestAdaptatif? _test;
    private QuestionPositionnement? _question;
    private int[] _ordre = [];
    private int _ecoutes;
    private ResultatPositionnement? _resultat;

    public PositionnementViewModel(MainViewModel main, EtatApplication etat, DepotPositionnement depot, ServiceVoix voix, ParametresPositionnement parametres)
    {
        _main = main;
        _etat = etat;
        _depot = depot;
        _voix = voix;
        _volumeVoix = etat.Reglages.VolumeVoix;
        _voix.Volume = _volumeVoix;
        _banque = parametres.Banque;
        Niveaux = _banque.Niveaux.Select((n, i) => new ChoixNiveau(i, n.Code, n.Nom, n.Description)).ToList();
        if (parametres.Mode == ModePositionnement.Choix) AfficherChoix();
        else AfficherConsignes();
    }

    /// <summary>Volume de la voix (0 à 100), réglable pendant le test et retenu pour la prochaine fois.</summary>
    [ObservableProperty] private int _volumeVoix;

    partial void OnVolumeVoixChanged(int value)
    {
        _voix.Volume = value;
        _etat.Reglages.VolumeVoix = value;
        _etat.EnregistrerSansAppliquer();
    }

    public string Titre => _banque.Titre;
    public string Matiere => _banque.Matiere;
    public IReadOnlyList<ChoixNiveau> Niveaux { get; }

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private Humeur _humeur = Humeur.Contente;

    // ---- Étapes (une seule visible à la fois) ----
    [ObservableProperty] private bool _estConsignes;
    [ObservableProperty] private bool _estQuestion;
    [ObservableProperty] private bool _estResultat;
    [ObservableProperty] private bool _estChoix;

    private void Etape(bool consignes = false, bool question = false, bool resultat = false, bool choix = false)
    {
        EstConsignes = consignes; EstQuestion = question; EstResultat = resultat; EstChoix = choix;
        if (!question) _voix.Arreter();
    }

    // ---- Consignes ------------------------------------------------------------------------

    [ObservableProperty] private string _consignes = "";
    /// <summary>La banque a des questions d'écoute.</summary>
    public bool AEcoute => _banque.AEcoute && _banque.LangueEcoute is not null;
    /// <summary>Une voix de Windows existe pour la langue des questions d'écoute.</summary>
    [ObservableProperty] private bool _voixDisponible;
    [ObservableProperty] private string _etatVoix = "";
    /// <summary>Sans voix : l'étudiant a choisi de passer le test sans les questions d'écoute.</summary>
    [ObservableProperty] private bool _sansEcoute;

    private void AfficherConsignes()
    {
        Etape(consignes: true);
        Humeur = Humeur.Encourageante;
        Message = "Une vingtaine de questions, environ 15 minutes. Réponds sans aide : c'est ce qui rend le résultat juste.";
        Consignes = _banque.Consignes ??
            "Le test s'adapte à tes réponses : les questions deviennent plus difficiles ou plus faciles selon tes résultats. "
            + "Tu ne verras pas la correction pendant le test. Si tu ne sais pas, clique sur « Je ne sais pas » plutôt que de répondre au hasard.";
        VerifierVoix();
    }

    [RelayCommand]
    private void VerifierVoix()
    {
        if (!AEcoute) { VoixDisponible = false; EtatVoix = ""; return; }
        VoixDisponible = ServiceVoix.Disponible(_banque.LangueEcoute!);
        EtatVoix = VoixDisponible
            ? $"Certaines questions se font à l'écoute (voix « {ServiceVoix.NomVoix(_banque.LangueEcoute!)} »). Monte le son et clique sur « Tester le son »."
            : "Certaines questions se font à l'écoute, mais aucune voix anglaise n'est installée dans Windows. Pour l'ajouter : "
              + "Paramètres → Heure et langue → Voix → Ajouter des voix → English (United Kingdom), puis clique sur « Vérifier à nouveau ». "
              + "Tu peux aussi passer le test sans les questions d'écoute.";
    }

    [RelayCommand]
    private async Task TesterSonAsync()
    {
        if (_banque.LangueEcoute is { } langue && !await _voix.LireAsync("Hello! If you can hear this sentence, you're ready to start.", langue))
            EtatVoix = "La lecture n'a pas marché. Vérifie le son de ton PC, ou passe le test sans les questions d'écoute.";
    }

    [RelayCommand]
    private void Commencer() => Demarrer(ecoute: VoixDisponible);

    [RelayCommand]
    private void CommencerSansEcoute() => Demarrer(ecoute: false);

    private void Demarrer(bool ecoute)
    {
        SansEcoute = !ecoute;
        _test = new TestAdaptatif(_banque, new Random(), ecoute && AEcoute);
        Humeur = Humeur.Contente;
        Message = "C'est parti. Prends ton temps, mais ne reste pas bloqué : au-delà d'une minute, choisis « Je ne sais pas ».";
        QuestionSuivante();
    }

    // ---- Questions ------------------------------------------------------------------------

    [ObservableProperty] private string? _messageRendu;
    [ObservableProperty] private string _repere = "";
    [ObservableProperty] private double _avancement;
    [ObservableProperty] private bool _estEcoute;
    [ObservableProperty] private string _texteReecouter = "";
    [ObservableProperty] private bool _peutReecouter;

    private void QuestionSuivante()
    {
        if (_test is null) return;
        if (_test.Termine)
        {
            AfficherResultat(_test.Resultat());
            return;
        }
        _question = _test.Courante!;
        _ordre = Enumerable.Range(0, _question.Choix.Count).ToArray();
        Random.Shared.Shuffle(_ordre);
        EstEcoute = _question.Ecoute is not null;
        _ecoutes = 0;
        Repere = $"Question {_test.Numero}";
        Avancement = 100.0 * _test.Repondues / Math.Max(1, _test.TotalEstime);
        Etape(question: true);

        string markdown = _question.Question;
        if (_question.Texte is { } texte)
            markdown = string.Join("\n", texte.Split('\n').Select(l => "> " + l)) + "\n\n" + markdown;
        if (EstEcoute) markdown = "🎧 *Écoute, puis réponds.*\n\n" + markdown;
        var elements = new List<object> { new { genre = "question", markdown } };
        for (int position = 0; position < _ordre.Length; position++)
            elements.Add(new { genre = "choix", index = position, markdown = _question.Choix[_ordre[position]], etat = "actif" });
        MessageRendu = JsonSerializer.Serialize(new { elements, animation = "carte" });
        MettreAJourEcoute();
        if (EstEcoute) _ = ReecouterAsync();
    }

    private void MettreAJourEcoute()
    {
        int restantes = EcoutesMax - _ecoutes;
        PeutReecouter = EstEcoute && restantes > 0;
        TexteReecouter = _ecoutes == 0 ? "Écouter" : restantes > 0 ? $"Réécouter ({restantes})" : "Plus d'écoute";
    }

    [RelayCommand]
    private async Task ReecouterAsync()
    {
        if (_question?.Ecoute is not { } texte || _banque.LangueEcoute is not { } langue || _ecoutes >= EcoutesMax) return;
        _ecoutes++;
        MettreAJourEcoute();
        await _voix.LireAsync(texte, langue);
    }

    /// <summary>Clic sur un choix (position à l'écran).</summary>
    [RelayCommand]
    private void Choisir(int position)
    {
        if (_test is null || _question is null || !EstQuestion || position < 0 || position >= _ordre.Length) return;
        Repondre(_ordre[position] == 0);   // la bonne réponse est le premier choix du fichier
    }

    [RelayCommand]
    private void JeNeSaisPas()
    {
        if (_test is null || _question is null || !EstQuestion) return;
        Repondre(false);
    }

    private void Repondre(bool juste)
    {
        _voix.Arreter();
        _test!.Repondre(juste);
        _question = null;
        QuestionSuivante();
    }

    // ---- Résultat -------------------------------------------------------------------------

    [ObservableProperty] private string _titreResultat = "";
    [ObservableProperty] private string _texteResultat = "";
    [ObservableProperty] private string? _conseil;
    [ObservableProperty] private IReadOnlyList<LigneCompetence> _competences = [];
    [ObservableProperty] private string _boutonCommencer = "";

    private void AfficherResultat(ResultatPositionnement r)
    {
        _resultat = r;
        _depot.Enregistrer(_banque, r.NiveauDepart, r.NiveauAtteint, parTest: true, _etat.Jour);
        Etape(resultat: true);
        Humeur = Humeur.Fiere;
        var depart = _banque.Niveaux[r.NiveauDepart];
        if (r.NiveauAtteint < 0)
        {
            TitreResultat = "On part des bases";
            TexteResultat = $"Le test conseille de commencer au niveau {depart.Code} ({depart.Nom}). Aucun souci : le cours est fait pour partir de zéro.";
            Message = "Bravo d'avoir joué le jeu ! On construit tout pas à pas.";
        }
        else
        {
            var atteint = _banque.Niveaux[r.NiveauAtteint];
            TitreResultat = $"Ton niveau : {atteint.Code} ({atteint.Nom})";
            TexteResultat = r.ToutAcquis
                ? $"Tu valides même le dernier niveau. Continue avec les unités {depart.Code} pour consolider, et son Entraînement."
                : $"Tu commences au niveau {depart.Code} ({depart.Nom}). Ce qui est en dessous est compté comme acquis : "
                  + "ses cartes reviendront en simples rappels, étalés sur plusieurs semaines.";
            Message = "Bien joué ! Tes révisions sont calées sur ton niveau.";
        }
        Competences = r.ParCompetence
            .Select(b => new LigneCompetence(TestAdaptatif.Nom(b.Competence), $"{b.Justes} / {b.Total}", b.Taux))
            .ToList();
        var morceaux = new List<string>();
        if (r.PointFaible is { } faible) morceaux.Add($"À travailler en priorité : {TestAdaptatif.Nom(faible).ToLowerInvariant()}.");
        if (r.PointFort is { } fort) morceaux.Add($"Point fort : {TestAdaptatif.Nom(fort).ToLowerInvariant()}.");
        if (SansEcoute && AEcoute) morceaux.Add("Le test a été passé sans les questions d'écoute : la compréhension orale n'est pas mesurée.");
        morceaux.Add("C'est une estimation : si le niveau te paraît trop facile ou trop difficile après quelques séances, change-le depuis la bibliothèque.");
        Conseil = string.Join("\n", morceaux);
        BoutonCommencer = $"Commencer en {depart.Code}";
    }

    /// <summary>Va dans la bibliothèque, directement dans la partie du niveau de départ.</summary>
    [RelayCommand]
    private void AllerAuCours()
    {
        int depart = _resultat?.NiveauDepart ?? _choisi ?? 0;
        var (competence, reste) = Revisions.Core.Bibliotheque.ArbreBibliotheque.DecouperPartie(_banque.Niveaux[depart].Partie);
        var cles = new List<string> { _banque.CleMatiere };
        if (competence is not null) cles.Add(competence);
        if (reste.Length > 0) cles.Add(reste);
        BibliothequeViewModel.PositionnerSur(cles);
        _main.AllerBibliotheque();
    }

    // ---- Choix direct du niveau -------------------------------------------------------------

    private int? _choisi;

    [RelayCommand]
    private void AfficherChoix()
    {
        Etape(choix: true);
        Humeur = Humeur.Contente;
        Message = "Par où veux-tu commencer ? Tout ce qui est en dessous sera compté comme acquis.";
    }

    [RelayCommand]
    private void ChoisirNiveau(ChoixNiveau niveau)
    {
        _choisi = niveau.Indice;
        _resultat = null;
        _depot.Enregistrer(_banque, niveau.Indice, atteint: null, parTest: false, _etat.Jour);
        AllerAuCours();
    }

    /// <summary>Depuis les consignes ou le choix : passer au test.</summary>
    [RelayCommand]
    private void PasserLeTest() => AfficherConsignes();

    // ---- Quitter --------------------------------------------------------------------------

    [RelayCommand]
    private void Quitter()
    {
        if (EstQuestion && _test is { Repondues: > 0 }
            && !ServiceDialogue.Confirmer("Arrêter le test ?", "Tes réponses ne seront pas gardées : il faudra recommencer le test depuis le début.", "Arrêter", "Continuer le test"))
            return;
        _voix.Arreter();
        _main.AllerBibliotheque();
    }

    /// <summary>Raccourcis : A à E (choix), Espace (réécouter), Échap (quitter).</summary>
    [RelayCommand]
    public void Touche(string touche)
    {
        switch (touche)
        {
            case "Escape": Quitter(); break;
            case "a" or "b" or "c" or "d" or "e" when EstQuestion: Choisir(touche[0] - 'a'); break;
            case " " when EstQuestion && PeutReecouter: _ = ReecouterAsync(); break;
        }
    }
}
