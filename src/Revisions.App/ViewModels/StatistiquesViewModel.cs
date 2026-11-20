using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.Core.Gamification;
using Revisions.Core.Temps;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Une barre du graphique de régularité : une semaine.</summary>
/// <param name="Etiquette">Date du lundi (jj/MM).</param>
/// <param name="Reponses">Nombre de réponses dans la semaine.</param>
/// <param name="Hauteur">Hauteur de la barre en pixels.</param>
public sealed record BarreSemaine(string Etiquette, int Reponses, double Hauteur, bool Active);

/// <summary>Une ligne « matière » avec une barre de progression.</summary>
public sealed record LigneMatiere(string Matiere, string Detail, double Valeur);

/// <summary>Une section fragile, avec le texte prêt à afficher.</summary>
public sealed record LignePointFaible(PointFaible Point)
{
    public string Titre => Point.Titre;
    public string Detail => $"{Point.Matiere}  ·  {Point.Seance}  ·  rappel estimé {Math.Round(Point.RappelMoyen * 100)} %"
                            + (Point.Oublis > 0 ? $"  ·  {Texte.Pluriel(Point.Oublis, "oubli", "oublis")}" : "");
}

/// <summary>Écran Statistiques : régularité, réussite et points faibles (US10).</summary>
public partial class StatistiquesViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private const int NombreDeSemaines = 12;
    private const double HauteurMax = 110;

    private readonly MainViewModel _main;
    private readonly DepotRevisions _revisions;

    public IReadOnlyList<BarreSemaine> Semaines { get; }
    public string Regularite { get; }
    public IReadOnlyList<LigneMatiere> Reussites { get; }
    public IReadOnlyList<LigneMatiere> Maitrises { get; }
    public IReadOnlyList<LignePointFaible> PointsFaibles { get; }
    public bool AucuneDonnee { get; }
    public bool AucunPointFaible => PointsFaibles.Count == 0;

    /// <summary>Calendrier de régularité : 12 colonnes (semaines) de 7 cases (jours).</summary>
    public IReadOnlyList<IReadOnlyList<JourCalendrier>> Calendrier { get; }
    /// <summary>Cartes révisées par jour, sur les 30 derniers jours (courbe), et le texte de chaque point.</summary>
    public IReadOnlyList<int> Courbe { get; }
    public IReadOnlyList<string> EtiquettesCourbe { get; }
    public string ResumeCourbe { get; }

    public StatistiquesViewModel(MainViewModel main, EtatApplication etat, DepotProfil profil, DepotRevisions revisions)
    {
        _main = main;
        _revisions = revisions;
        var jour = etat.Jour;

        // ---- Régularité : 12 dernières semaines ----
        var lundi = JourDeRevision.LundiDe(jour);
        var premierLundi = lundi.AddDays(-7 * (NombreDeSemaines - 1));
        var parJour = profil.ReponsesParJour(premierLundi);
        var totaux = Enumerable.Range(0, NombreDeSemaines)
            .Select(i => premierLundi.AddDays(7 * i))
            .Select(debut => (Debut: debut, Total: Enumerable.Range(0, 7).Sum(j => parJour.GetValueOrDefault(debut.AddDays(j)))))
            .ToList();
        int maximum = Math.Max(1, totaux.Max(t => t.Total));
        Semaines = totaux
            .Select(t => new BarreSemaine(t.Debut.ToString("dd/MM"), t.Total,
                t.Total == 0 ? 3 : Math.Max(8, HauteurMax * t.Total / maximum), t.Total > 0))
            .ToList();
        int actives = totaux.Count(t => t.Total > 0);
        Regularite = $"{Texte.Pluriel(actives, "semaine active", "semaines actives")} sur les {NombreDeSemaines} dernières";
        AucuneDonnee = actives == 0;

        // ---- Calendrier et courbe sur 30 jours (mêmes données que la régularité) ----
        Calendrier = Core.Gamification.Calendrier.Construire(premierLundi, NombreDeSemaines, parJour, jour);
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var trente = Enumerable.Range(0, 30).Select(i => jour.AddDays(i - 29)).ToList();
        Courbe = trente.Select(j => parJour.GetValueOrDefault(j)).ToList();
        EtiquettesCourbe = trente.Select(j => $"{j.ToString("dddd d MMMM", fr)} : {Texte.Pluriel(parJour.GetValueOrDefault(j), "carte", "cartes")}").ToList();
        int total = Courbe.Sum();
        ResumeCourbe = $"{Texte.Pluriel(total, "carte révisée", "cartes révisées")} en 30 jours, sur {Texte.Pluriel(Courbe.Count(n => n > 0), "jour", "jours")}";

        // ---- Réussite par matière : 30 derniers jours ----
        Reussites = profil.ReussiteParMatiere(jour.AddDays(-30))
            .Select(r => new LigneMatiere(r.Matiere, $"{Math.Round(r.Taux * 100)} %  ({r.Reussies} / {r.Total})", r.Taux))
            .ToList();

        // ---- Avancement par matière ----
        Maitrises = profil.MaitriseParMatiere()
            .Select(m => new LigneMatiere(m.Matiere,
                $"{Texte.Pluriel(m.Maitrisees, "maîtrisée", "maîtrisées")}, {Texte.Pluriel(m.Vues, "vue", "vues")} sur {m.Cartes}",
                m.Cartes == 0 ? 0 : (double)m.Maitrisees / m.Cartes))
            .ToList();

        // ---- Points faibles ----
        PointsFaibles = profil.PointsFaibles(jour, 5).Select(p => new LignePointFaible(p)).ToList();
        foreach (var c in revisions.CartesMisesDeCote()) MisesDeCote.Add(c);
        _aDesMisesDeCote = MisesDeCote.Count > 0;
    }

    /// <summary>Cartes mises de côté depuis une session (« Mettre de côté ») : on peut les remettre en jeu.</summary>
    public System.Collections.ObjectModel.ObservableCollection<CarteMiseDeCote> MisesDeCote { get; } = [];
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _aDesMisesDeCote;

    [RelayCommand]
    private void Reactiver(CarteMiseDeCote carte)
    {
        _revisions.DefinirSuspendue(carte.CarteId, false);
        MisesDeCote.Remove(carte);
        ADesMisesDeCote = MisesDeCote.Count > 0;
    }

    /// <summary>Session ciblée sur une section fragile.</summary>
    [RelayCommand]
    private void Travailler(LignePointFaible ligne) =>
        _main.DemarrerSession(_revisions.CartesDeLaSection(ligne.Point.SectionId), "point_faible");
}
