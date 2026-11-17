using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.Core.Packs;
using Revisions.Core.TdGroupe;

namespace Revisions.App.ViewModels;

/// <summary>État d'une case après la vérification : vert si juste, orange sinon (jamais de rouge : R1).</summary>
public enum EtatCase { Neutre, Juste, AReprendre }

/// <summary>Une case de saisie (un coefficient de matrice, ou le champ d'un nombre).</summary>
public partial class CaseTdViewModel(int index, int colonnes, int? attendue) : ObservableObject
{
    /// <summary>Position dans la grille, ligne après ligne (sert à la navigation avec les flèches).</summary>
    public int Index { get; } = index;

    /// <summary>Nombre de colonnes de la grille (flèches haut et bas : on saute d'une ligne entière).</summary>
    public int Colonnes { get; } = colonnes;

    /// <summary>Texte saisi.</summary>
    [ObservableProperty] private string _valeur = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TexteAttendu))]
    private EtatCase _etat;

    /// <summary>On ne modifie plus la case une fois la correction affichée.</summary>
    [ObservableProperty] private bool _modifiable = true;

    /// <summary>Bonne valeur, montrée sous une case à reprendre.</summary>
    public string? TexteAttendu => Etat == EtatCase.AReprendre && attendue is { } v ? $"→ {v}" : null;
}

/// <summary>Une proposition d'un choix (« Oui », « 2 × 3 », réponse d'un QCM…).</summary>
public partial class ChoixTdViewModel(QuestionTdViewModel question, int index, string texte) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>Lettre affichée : A, B, C…</summary>
    public string Lettre { get; } = ((char)('A' + index)).ToString();

    /// <summary>Texte simple (les formules LaTeX sont converties en caractères ordinaires).</summary>
    public string Texte { get; } = TexteSimple.Extrait(texte, 80);

    [ObservableProperty] private bool _estChoisi;

    /// <summary>Après la vérification : la bonne proposition en vert, un mauvais choix en orange.</summary>
    [ObservableProperty] private EtatCase _etat;

    [RelayCommand]
    private void Choisir() => question.Choisir(Index);
}

/// <summary>
/// Une sous-question a), b), c) et sa zone de réponse : grille pour une matrice, champ pour un nombre,
/// boutons pour un choix, ou « J'avais juste / Pas tout à fait » pour une réponse rédigée.
/// </summary>
public partial class QuestionTdViewModel : ObservableObject
{
    public QuestionTd Question { get; }

    public string Lettre => Question.Lettre + ")";
    public string Libelle => Question.Libelle;

    public bool EstMatrice => Question.Attendu is ReponseMatrice;
    public bool EstNombre => Question.Attendu is ReponseNombre;
    public bool EstChoix => Question.Attendu is ReponseChoix;
    public bool EstLibre => Question.Attendu is ReponseLibre;

    /// <summary>Saisie dans des cases : la grille d'une matrice, ou la case unique d'un nombre.</summary>
    public bool EstGrille => EstMatrice || EstNombre;

    /// <summary>Dimension de la grille (1 × 1 pour un nombre).</summary>
    public int Lignes { get; }
    public int Colonnes { get; }

    public ObservableCollection<CaseTdViewModel> Cases { get; } = [];
    public IReadOnlyList<ChoixTdViewModel> Choix { get; } = [];

    /// <summary>Résultat, une fois vérifiée (null avant ; pour une réponse rédigée, ce que l'étudiant a dit).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bilan))]
    private bool? _juste;

    /// <summary>Choix modifiables tant que la correction n'est pas affichée.</summary>
    [ObservableProperty] private bool _modifiable = true;

    /// <summary>Petit texte après la vérification.</summary>
    public string? Bilan => Juste switch
    {
        true => "Juste !",
        false when EstLibre => "Pas grave : la correction est là pour ça.",
        false => "À reprendre avec la correction.",
        null => null,
    };

    /// <summary>Levé quand l'étudiant évalue lui-même une réponse rédigée.</summary>
    public event Action? AutoEvaluee;

    public QuestionTdViewModel(QuestionTd question)
    {
        Question = question;
        switch (question.Attendu)
        {
            case ReponseMatrice m:
                Lignes = m.Valeur.Lignes;
                Colonnes = m.Valeur.Colonnes;
                int k = 0;
                foreach (int valeur in m.Valeur.Coefficients()) Cases.Add(new CaseTdViewModel(k++, Colonnes, valeur));
                break;
            case ReponseNombre n:
                Lignes = Colonnes = 1;
                Cases.Add(new CaseTdViewModel(0, 1, n.Valeur));
                break;
            case ReponseChoix c:
                Choix = c.Choix.Select((texte, i) => new ChoixTdViewModel(this, i, texte)).ToList();
                break;
        }
    }

    /// <summary>Choisit une proposition (un seul choix possible).</summary>
    public void Choisir(int index)
    {
        if (!Modifiable) return;
        foreach (var choix in Choix) choix.EstChoisi = choix.Index == index;
    }

    /// <summary>Vérifie la saisie et colore les cases. Ne fait rien pour une réponse rédigée.</summary>
    public void Verifier()
    {
        Modifiable = false;
        foreach (var c in Cases) c.Modifiable = false;
        ResultatVerification? resultat = Question.Attendu switch
        {
            ReponseMatrice m => VerificationSaisie.Matrice(m.Valeur, Cases.Select(c => (string?)c.Valeur).ToList()),
            ReponseNombre n => VerificationSaisie.Nombre(n.Valeur, Cases[0].Valeur),
            ReponseChoix c => VerificationSaisie.Choix(c.Bonne, Choix.FirstOrDefault(x => x.EstChoisi)?.Index),
            _ => null,
        };
        if (resultat is null) return;

        for (int i = 0; i < Cases.Count; i++) Cases[i].Etat = resultat.Cases[i] ? EtatCase.Juste : EtatCase.AReprendre;
        if (Question.Attendu is ReponseChoix choix)
            foreach (var x in Choix)
                x.Etat = x.Index == choix.Bonne ? EtatCase.Juste : x.EstChoisi ? EtatCase.AReprendre : EtatCase.Neutre;
        Juste = resultat.Juste;
    }

    /// <summary>Réponse rédigée : l'étudiant dit lui-même s'il avait juste, après avoir vu la correction.</summary>
    [RelayCommand]
    private void AutoEvaluer(string juste)
    {
        if (!EstLibre) return;
        Juste = juste == "oui";
        AutoEvaluee?.Invoke();
    }
}
