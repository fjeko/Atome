using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Bibliotheque;
using Revisions.Core.Sessions;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Un niveau de la bibliothèque (matière, compétence, partie…) dans l'arbre « Une matière ou une partie ».</summary>
public sealed partial class NoeudChoix : ObservableObject
{
    private readonly Action<NoeudChoix> _choisi;

    public NoeudBibliotheque Noeud { get; }
    public string Titre => Noeud.Etiquette is { } e ? $"{e} · {Noeud.Titre}" : Noeud.Titre;
    public string Detail { get; }
    public IReadOnlyList<NoeudChoix> Enfants { get; }

    /// <summary>Lié à la sélection de l'arbre (IsSelected) : choisir un niveau recalcule la session proposée.</summary>
    [ObservableProperty] private bool _estChoisi;

    public NoeudChoix(NoeudBibliotheque noeud, Action<NoeudChoix> choisi)
    {
        Noeud = noeud;
        _choisi = choisi;
        Detail = $"{noeud.NbDues} à revoir · {noeud.NbNouvelles} nouvelles";
        // Les séances elles-mêmes ne sont pas dans l'arbre : on choisit une matière ou une partie.
        Enfants = noeud.Enfants.Where(e => e.NbCartes > 0).Select(e => new NoeudChoix(e, choisi)).ToList();
    }

    partial void OnEstChoisiChanged(bool value)
    {
        if (value) _choisi(this);
    }
}

/// <summary>Une séance listée sous un choix (points faibles, lectures récentes).</summary>
public sealed record LigneSeanceChoix(string Titre, string Detail);

/// <summary>
/// Écran « Autre révision » : chaque choix affiche son nombre de cartes et une durée estimée.
/// Les collections masquées, le plafond du jour, la limite de nouvelles cartes et le mode reprise s'appliquent à tous.
/// </summary>
public partial class ChoixRevisionViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly JourneeDeRevision _journee;
    private readonly SessionComposee _duJour, _pointsFaibles, _lecture, _devoir;

    /// <summary>Remarque sur les limites du jour (plafond atteint, mode reprise), ou null.</summary>
    public string? Remarque { get; }

    public string ResumeDuJour { get; }
    public bool PeutDuJour => _duJour.Cartes.Count > 0;

    // ---- Une matière ou une partie ----
    public IReadOnlyList<NoeudChoix> Arbre { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeutPerimetre))]
    [NotifyCanExecuteChangedFor(nameof(LancerPerimetreCommand))]
    private SessionComposee? _perimetre;
    [ObservableProperty] private string _resumePerimetre = "Choisis une matière ou une partie dans la liste.";
    public bool PeutPerimetre => Perimetre is { Cartes.Count: > 0 };

    // ---- Mes points faibles ----
    public string ResumePointsFaibles { get; }
    public bool PeutPointsFaibles => _pointsFaibles.Cartes.Count > 0;
    public IReadOnlyList<LigneSeanceChoix> SeancesFaibles { get; }

    // ---- Ce que je viens de lire ----
    public string ResumeLecture { get; }
    public bool PeutLecture => _lecture.Cartes.Count > 0;
    public IReadOnlyList<LigneSeanceChoix> SeancesLues { get; }

    // ---- Préparer un devoir ----
    public bool PlanningPresent { get; }
    public string ResumeDevoir { get; }
    public string? NomDevoir { get; }
    public bool PeutDevoir => _devoir.Cartes.Count > 0;

    public ChoixRevisionViewModel(MainViewModel main, EtatApplication etat, SelectionDuJour selection, DepotProfil profil)
    {
        _main = main;
        _journee = JourneeDeRevision.Preparer(etat, selection, profil);
        var plan = _journee.Plan;
        var donnees = _journee.Journee.Donnees;

        Remarque = plan.PlafondAtteint
            ? "Tu as atteint ton plafond du jour : ces révisions seront de nouveau proposées demain."
            : plan.ModeReprise
                ? $"On reprend en douceur : au plus {plan.MaxCartes} cartes aujourd'hui, sans nouvelle carte."
                : plan.MaxNouvelles == 0 && etat.Reglages.NouvellesParJour > 0
                    ? "Tu as déjà découvert tes nouvelles cartes du jour : les choix ci-dessous proposent seulement des cartes à revoir. Les suivantes arrivent demain (limite réglable dans les Réglages)."
                    : null;

        _duJour = _journee.Composer(DemandeSession.DuJour);
        ResumeDuJour = _journee.Resume(_duJour);

        var racine = ArbreBibliotheque.Construire(donnees.Seances);
        Arbre = racine.Enfants.Where(e => e.NbCartes > 0).Select(e => new NoeudChoix(e, Choisir)).ToList();

        _pointsFaibles = _journee.Composer(DemandeSession.PointsFaibles);
        ResumePointsFaibles = _pointsFaibles.Cartes.Count > 0 ? _journee.Resume(_pointsFaibles) : "Aucun point faible repéré pour l'instant.";
        SeancesFaibles = _pointsFaibles.SeancesFaibles.Take(8)
            .Select(s => new LigneSeanceChoix($"{s.Seance.Matiere} › {s.Seance.Seance}", DetailFaible(s)))
            .ToList();

        _lecture = _journee.Composer(DemandeSession.LectureRecente);
        var recents = CompositeurSession.PacksLusRecemment(donnees);
        SeancesLues = donnees.Seances.Where(s => recents.Contains(s.PackId))
            .OrderByDescending(s => donnees.DernieresLectures[s.PackId])
            .Take(8)
            .Select(s => new LigneSeanceChoix($"{s.Matiere} › {s.Seance}", "Lu " + Motifs.IlYa(donnees.DernieresLectures[s.PackId], donnees.Jour)))
            .ToList();
        ResumeLecture = recents.Count == 0
            ? "Tu n'as pas ouvert de cours dans Atome ces 7 derniers jours."
            : _journee.Resume(_lecture);

        PlanningPresent = donnees.Planning is not null;
        _devoir = _journee.Composer(DemandeSession.Devoir);
        NomDevoir = _devoir.Devoir is { } d ? Libelles.Echeance(d) : null;
        ResumeDevoir = !PlanningPresent
            ? "Importe le planning du CNED (écran Planning) pour utiliser ce choix."
            : _devoir.Devoir is null ? "Aucun devoir à venir dans ton planning." : _journee.Resume(_devoir);
    }

    private static string DetailFaible(SeanceFaible s)
    {
        var morceaux = new List<string> { s.NbCartes == 1 ? "1 carte" : $"{s.NbCartes} cartes" };
        if (s.FoisRatees > 0) morceaux.Add(s.FoisRatees == 1 ? "1 « Raté »" : $"{s.FoisRatees} « Raté »");
        if (s.Taux is { } t) morceaux.Add($"réussite {Motifs.Pourcentage(t)}");
        return string.Join(" · ", morceaux);
    }

    /// <summary>Un niveau vient d'être choisi dans l'arbre : on recompose la session « matière ou partie ».</summary>
    private void Choisir(NoeudChoix noeud)
    {
        Perimetre = _journee.Composer(DemandeSession.Perimetre(CompositeurSession.PacksDe(noeud.Noeud)));
        ResumePerimetre = $"{noeud.Noeud.Titre} : " + (Perimetre.Cartes.Count > 0
            ? _journee.Resume(Perimetre)
            : Perimetre.DebloquerUtile
                ? "rien à revoir pour l'instant. Lis un de ses cours pour débloquer ses nouvelles cartes."
                : "rien à revoir pour l'instant.");
    }

    private void Lancer(SessionComposee session) =>
        _main.DemarrerSession(_journee.Journee.Charger(session), _journee.Mode(session.Type));

    [RelayCommand]
    private void LancerDuJour() => Lancer(_duJour);

    [RelayCommand(CanExecute = nameof(PeutPerimetre))]
    private void LancerPerimetre() => Lancer(Perimetre!);

    [RelayCommand]
    private void LancerPointsFaibles() => Lancer(_pointsFaibles);

    [RelayCommand]
    private void LancerLecture() => Lancer(_lecture);

    [RelayCommand]
    private void LancerDevoir() => Lancer(_devoir);

    [RelayCommand]
    private void Retour() => _main.AllerAccueil();
}
