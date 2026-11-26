namespace Revisions.Core.Positionnement;

/// <summary>Bilan d'un niveau pendant le test.</summary>
/// <param name="Acquis">Vrai si le niveau est validé, faux s'il ne l'est pas, null s'il n'a pas été testé.</param>
public sealed record BilanNiveau(int Niveau, int Justes, int Total, bool? Acquis);

/// <summary>Réussite sur une compétence, toutes questions confondues.</summary>
public sealed record BilanCompetence(Competence Competence, int Justes, int Total)
{
    public double Taux => Total == 0 ? 0 : (double)Justes / Total;
}

/// <summary>Résultat final du test de positionnement.</summary>
/// <param name="NiveauAtteint">Indice du plus haut niveau validé, ou -1 si aucun (débutant).</param>
/// <param name="NiveauDepart">Indice du niveau par lequel commencer le cours.</param>
/// <param name="ToutAcquis">Vrai si même le dernier niveau est validé.</param>
/// <param name="PointFort">Compétence nettement au-dessus des autres, ou null.</param>
/// <param name="PointFaible">Compétence nettement en dessous des autres, ou null.</param>
public sealed record ResultatPositionnement(int NiveauAtteint, int NiveauDepart, bool ToutAcquis, int NbQuestions,
    IReadOnlyList<BilanNiveau> ParNiveau, IReadOnlyList<BilanCompetence> ParCompetence,
    Competence? PointFort, Competence? PointFaible);

/// <summary>
/// Test adaptatif par blocs. On commence au niveau de départ de la banque, par un bloc de
/// <see cref="TailleBloc"/> questions (grammaire, vocabulaire, lecture et écoute mélangées) :
/// <list type="bullet">
/// <item>presque tout juste (≥ 5 / 6) : le niveau est validé, on monte ;</item>
/// <item>beaucoup d'erreurs (≤ 2 / 6) : il ne l'est pas, on descend ;</item>
/// <item>entre les deux : un second bloc au même niveau tranche (≥ 8 / 12 pour valider).</item>
/// </list>
/// Le test s'arrête dès qu'un niveau validé est juste en dessous d'un niveau non validé (ou aux extrémités).
/// Le cours commence au premier niveau non validé. Les réponses ne sont jamais montrées pendant le test.
/// </summary>
public sealed class TestAdaptatif
{
    public const int TailleBloc = 6;
    /// <summary>Justes sur un bloc pour valider directement.</summary>
    public const int SeuilValide = 5;
    /// <summary>Justes sur un bloc à partir duquel (et en dessous) le niveau n'est pas validé directement.</summary>
    public const int SeuilEchec = 2;
    /// <summary>Sur deux blocs, il faut au moins deux tiers de bonnes réponses pour valider (8 / 12).</summary>
    private static bool DeuxTiers(int justes, int total) => total > 0 && justes * 3 >= total * 2;
    /// <summary>Écart (en part de bonnes réponses) qui fait d'une compétence un point fort ou un point faible.</summary>
    public const double EcartNotable = 0.2;

    /// <summary>Composition d'un bloc : deux de grammaire, deux de vocabulaire, une de lecture, une d'écoute.</summary>
    private static readonly Competence[] Composition =
        [Competence.Grammaire, Competence.Grammaire, Competence.Vocabulaire, Competence.Vocabulaire, Competence.Lecture, Competence.Ecoute];

    private readonly BanquePositionnement _banque;
    private readonly Random _hasard;
    private readonly bool _ecoute;
    private readonly HashSet<string> _posees = [];
    private readonly bool?[] _decision;
    private readonly int[] _justesNiveau;
    private readonly int[] _totalNiveau;
    private readonly List<(QuestionPositionnement Question, bool Juste)> _reponses = [];

    private readonly Queue<QuestionPositionnement> _bloc = new();
    private int _niveau;
    private int _blocsAuNiveau;
    private int _justesBloc;
    private int _totalBloc;

    /// <param name="ecoutePossible">Faux s'il n'y a aucune voix pour lire les questions d'écoute : elles sont remplacées.</param>
    public TestAdaptatif(BanquePositionnement banque, Random hasard, bool ecoutePossible)
    {
        _banque = banque;
        _hasard = hasard;
        _ecoute = ecoutePossible;
        _decision = new bool?[banque.Niveaux.Count];
        _justesNiveau = new int[banque.Niveaux.Count];
        _totalNiveau = new int[banque.Niveaux.Count];
        _niveau = Math.Clamp(banque.Depart, 0, banque.Niveaux.Count - 1);
        if (!PreparerBloc()) Termine = true;
    }

    /// <summary>Question à poser, ou null si le test est terminé.</summary>
    public QuestionPositionnement? Courante => Termine ? null : _bloc.Peek();

    public bool Termine { get; private set; }

    /// <summary>Numéro de la question courante (à partir de 1).</summary>
    public int Numero => _reponses.Count + 1;

    /// <summary>Nombre de questions déjà répondues.</summary>
    public int Repondues => _reponses.Count;

    /// <summary>Niveau en cours de test (indice).</summary>
    public int NiveauCourant => _niveau;

    /// <summary>
    /// Estimation du nombre total de questions, pour la barre d'avancement : il reste au moins
    /// le bloc en cours ; tant que le test n'a pas trouvé sa frontière, on compte un bloc de plus.
    /// </summary>
    public int TotalEstime => Termine ? _reponses.Count : _reponses.Count + _bloc.Count + (FrontiereTrouvee() ? 0 : TailleBloc);

    /// <summary>Enregistre la réponse à la question courante (« je ne sais pas » compte comme une erreur).</summary>
    public void Repondre(bool juste)
    {
        if (Termine) throw new InvalidOperationException("Le test est terminé.");
        var q = _bloc.Dequeue();
        _reponses.Add((q, juste));
        _totalBloc++;
        _totalNiveau[_niveau]++;
        if (juste) { _justesBloc++; _justesNiveau[_niveau]++; }
        if (_bloc.Count == 0) FinirBloc();
    }

    private void FinirBloc()
    {
        _blocsAuNiveau++;
        bool? decision;
        if (_blocsAuNiveau == 1)
        {
            // Un bloc incomplet (banque trop petite) se juge à la proportion.
            int total = Math.Max(1, _totalBloc);
            decision = _justesBloc * TailleBloc >= SeuilValide * total ? true
                : _justesBloc * TailleBloc <= SeuilEchec * total ? false
                : null;
        }
        else decision = DeuxTiers(_justesNiveau[_niveau], _totalNiveau[_niveau]);

        _justesBloc = _totalBloc = 0;
        if (decision is null)
        {
            // Incertain : un second bloc au même niveau, s'il reste des questions. Sinon on tranche à la proportion.
            if (PreparerBloc()) return;
            decision = DeuxTiers(_justesNiveau[_niveau], _totalNiveau[_niveau]);
        }
        Decider(decision.Value);
    }

    private void Decider(bool acquis)
    {
        _decision[_niveau] = acquis;
        int suivant = acquis ? _niveau + 1 : _niveau - 1;
        bool fin = suivant < 0 || suivant >= _decision.Length || _decision[suivant] is not null;
        if (!fin)
        {
            _niveau = suivant;
            _blocsAuNiveau = 0;
            if (PreparerBloc()) return;
        }
        Termine = true;
    }

    /// <summary>Vrai si le niveau courant suffira à conclure (son voisin dans le sens à suivre est déjà jugé, ou n'existe pas).</summary>
    private bool FrontiereTrouvee()
    {
        bool hautJuge = _niveau + 1 >= _decision.Length || _decision[_niveau + 1] is not null;
        bool basJuge = _niveau - 1 < 0 || _decision[_niveau - 1] is not null;
        return hautJuge && basJuge;
    }

    /// <summary>Tire un bloc au niveau courant ; faux s'il ne reste aucune question à ce niveau.</summary>
    private bool PreparerBloc()
    {
        var disponibles = _banque.Questions
            .Where(q => q.Niveau == _niveau && !_posees.Contains(q.Id) && (_ecoute || q.Competence != Competence.Ecoute))
            .OrderBy(_ => _hasard.Next())
            .ToList();
        if (disponibles.Count == 0) return false;

        var bloc = new List<QuestionPositionnement>();
        foreach (var competence in Composition)
        {
            var q = disponibles.FirstOrDefault(x => x.Competence == competence)
                    // Plus de question de ce type (ou pas d'écoute possible) : n'importe quelle autre.
                    ?? disponibles.FirstOrDefault();
            if (q is null) break;
            bloc.Add(q);
            disponibles.Remove(q);
        }
        // Mélangé, mais jamais deux questions d'écoute à la suite (il n'y en a qu'une par bloc).
        foreach (var q in bloc.OrderBy(_ => _hasard.Next()))
        {
            _posees.Add(q.Id);
            _bloc.Enqueue(q);
        }
        return true;
    }

    /// <summary>Résultat du test (à appeler une fois <see cref="Termine"/> vrai).</summary>
    public ResultatPositionnement Resultat()
    {
        if (!Termine) throw new InvalidOperationException("Le test n'est pas terminé.");
        int atteint = -1;
        for (int i = 0; i < _decision.Length; i++)
            if (_decision[i] == true) atteint = i;
        // Le plus haut niveau validé sous le premier échec : un échec plus bas l'emporte (cas d'une banque trop petite).
        int premierEchec = Array.FindIndex(_decision, d => d == false);
        if (premierEchec >= 0 && atteint > premierEchec) atteint = premierEchec - 1;

        int nb = _decision.Length;
        bool tout = atteint == nb - 1;
        int depart = tout ? nb - 1 : atteint + 1;

        var parNiveau = Enumerable.Range(0, nb).Select(i => new BilanNiveau(i, _justesNiveau[i], _totalNiveau[i], _decision[i])).ToList();
        var parCompetence = Enum.GetValues<Competence>()
            .Select(c => new BilanCompetence(c, _reponses.Count(r => r.Question.Competence == c && r.Juste), _reponses.Count(r => r.Question.Competence == c)))
            .Where(b => b.Total > 0)
            .ToList();

        // Point fort / faible : seulement sur des compétences assez interrogées, et seulement si l'écart est net.
        double global = _reponses.Count == 0 ? 0 : (double)_reponses.Count(r => r.Juste) / _reponses.Count;
        var fiables = parCompetence.Where(b => b.Total >= 3).ToList();
        var faible = fiables.OrderBy(b => b.Taux).FirstOrDefault(b => b.Taux <= global - EcartNotable);
        var fort = fiables.OrderByDescending(b => b.Taux).FirstOrDefault(b => b.Taux >= global + EcartNotable);
        return new ResultatPositionnement(atteint, depart, tout, _reponses.Count, parNiveau, parCompetence, fort?.Competence, faible?.Competence);
    }

    /// <summary>Nom d'une compétence, en clair.</summary>
    public static string Nom(Competence c) => c switch
    {
        Competence.Grammaire => "Grammaire",
        Competence.Vocabulaire => "Vocabulaire",
        Competence.Lecture => "Compréhension écrite",
        _ => "Compréhension orale",
    };
}
