using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revisions.App.Services;
using Revisions.App.Views;
using Revisions.Core.Modeles;
using Revisions.Data;

namespace Revisions.App.ViewModels;

/// <summary>Ce qu'il faut pour ouvrir l'écran de lecture.</summary>
/// <param name="PackId">Séance à lire.</param>
/// <param name="SectionId">Section à afficher d'abord (null = la première).</param>
/// <param name="DansUneFenetre">Vrai pour « Revoir la notion » : fenêtre à part, sans bouton de révision.</param>
public sealed record ParametresLecture(string PackId, string? SectionId, bool DansUneFenetre);

/// <summary>Écran Lecture : le cours d'une séance, une section par page (US3), et l'accès au PDF (US8).</summary>
public partial class LectureViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly EtatApplication _etat;
    private readonly DepotRevisions _revisions;
    private readonly SeanceALire _seance;
    private readonly ServiceSignalement _signalements;

    public string Matiere => _seance.Matiere;
    public string Titre => _seance.Seance;
    public IReadOnlyList<Section> Sections => _seance.Sections;
    public bool DansUneFenetre { get; }
    public bool PeutReviser { get; }

    /// <summary>Les cours libres n'ont pas de document d'origine : le bouton PDF est alors masqué.</summary>
    public bool ADocumentSource => _seance.SourceFichier.Length > 0;

    /// <summary>Libellé du bouton : « Voir le PDF » pour un PDF, « Voir le cours d'origine » sinon.</summary>
    public string LibelleSource => Path.GetExtension(_seance.SourceFichier).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "Voir le PDF" : "Voir le cours d'origine";

    /// <summary>Section affichée (liée à la sélection du sommaire).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position), nameof(Progression))]
    [NotifyCanExecuteChangedFor(nameof(PrecedentCommand), nameof(SuivantCommand))]
    private int _index;

    /// <summary>Message JSON envoyé à la zone de rendu.</summary>
    [ObservableProperty]
    private string? _messageRendu;

    public string Position => Sections.Count == 0 ? "" : $"{Index + 1} / {Sections.Count}";

    /// <summary>Progression de lecture dans la séance (0 à 1) : la barre au-dessus du texte.</summary>
    public double Progression => Sections.Count == 0 ? 0 : (Index + 1.0) / Sections.Count;

    /// <summary>Taille du texte (réglage général, 90 à 150 %) : « A- » et « A+ » de la lecture la changent partout.</summary>
    public string TailleTexte => $"{_etat.Reglages.TailleTexte} %";

    [RelayCommand]
    private void AgrandirTexte() => ChangerTaille(+10);

    [RelayCommand]
    private void RetrecirTexte() => ChangerTaille(-10);

    private void ChangerTaille(int pas)
    {
        int nouvelle = Math.Clamp(_etat.Reglages.TailleTexte + pas, Reglages.TailleTexteMin, Reglages.TailleTexteMax);
        if (nouvelle == _etat.Reglages.TailleTexte) return;
        _etat.Reglages.TailleTexte = nouvelle;
        _etat.Enregistrer(); // réapplique les tailles partout, contenu web compris
        OnPropertyChanged(nameof(TailleTexte));
    }

    public LectureViewModel(ParametresLecture parametres, MainViewModel main, EtatApplication etat,
        DepotBibliotheque bibliotheque, DepotRevisions revisions, ServiceSignalement signalements)
    {
        _signalements = signalements;
        _main = main;
        _etat = etat;
        _revisions = revisions;
        _seance = bibliotheque.ChargerSeance(parametres.PackId)
                  ?? throw new InvalidOperationException($"Séance introuvable : {parametres.PackId}");
        DansUneFenetre = parametres.DansUneFenetre;
        PeutReviser = !DansUneFenetre;

        bibliotheque.MarquerLue(parametres.PackId, etat.Maintenant);

        int depart = parametres.SectionId is null ? 0 : _seance.Sections.ToList().FindIndex(s => s.Id == parametres.SectionId);
        Index = Math.Max(0, depart);
        Rendre();
    }

    /// <summary>Remerciement affiché près du drapeau après un signalement (effacé au changement de section).</summary>
    [ObservableProperty]
    private string? _messageSignalement;

    /// <summary>Signale une erreur dans la section affichée.</summary>
    [RelayCommand]
    private void Signaler()
    {
        if (Sections.Count == 0) return;
        var section = Sections[Index];
        if (_signalements.SignalerSection(section.Id, _seance.PackId, section.Titre, section.Contenu))
            MessageSignalement = ServiceSignalement.Merci;
    }

    partial void OnIndexChanged(int value)
    {
        MessageSignalement = null;
        // La liste du sommaire met -1 quand rien n'est sélectionné : on l'ignore.
        if (value >= 0 && value < Sections.Count) Rendre();
    }

    /// <summary>Prépare le contenu de la page : résumé et notions sur la première, puis la section.</summary>
    private void Rendre()
    {
        if (Sections.Count == 0)
        {
            MessageRendu = JsonSerializer.Serialize(new { elements = new[] { new { genre = "texte", markdown = "Cette séance n'a pas de contenu." } } });
            return;
        }

        var section = Sections[Index];
        var elements = new List<object>();
        if (Index == 0 && !DansUneFenetre)
        {
            elements.Add(new { genre = "resume", markdown = _seance.Resume });
            if (_seance.Notions.Count > 0)
                elements.Add(new { genre = "notions", markdown = "**Notions :** " + string.Join(" · ", _seance.Notions) });
        }
        elements.Add(new { genre = "titre", markdown = section.Titre });
        elements.Add(new { genre = "texte", markdown = section.Contenu });
        MessageRendu = JsonSerializer.Serialize(new { elements, ecoute = Revisions.Core.Voix.ChoixVoix.LanguePourPack(_seance.PackId, _seance.Matiere) });
    }

    [RelayCommand(CanExecute = nameof(PeutReculer))]
    private void Precedent() => Index--;

    private bool PeutReculer() => Index > 0;

    [RelayCommand(CanExecute = nameof(PeutAvancer))]
    private void Suivant() => Index++;

    private bool PeutAvancer() => Index < Sections.Count - 1;

    [RelayCommand]
    private void Retour() => _main.AllerBibliotheque();

    /// <summary>Lance une session sur les cartes de cette séance (US12).</summary>
    [RelayCommand]
    private void Reviser() => _main.DemarrerSession(_revisions.CartesDuPack(_seance.PackId), "seance");

    /// <summary>Parcours guidé « Lire puis contrôler » sur cette séance.</summary>
    [RelayCommand]
    private void LireEtControler() => _main.OuvrirParcours(_seance.PackId);

    /// <summary>Flèches gauche/droite relayées par la zone de contenu.</summary>
    [RelayCommand]
    private void Touche(string touche)
    {
        if (touche == "ArrowRight" && PeutAvancer()) Suivant();
        else if (touche == "ArrowLeft" && PeutReculer()) Precedent();
    }

    /// <summary>Ouvre le document d'origine, à la page de la section affichée quand elle est connue.</summary>
    [RelayCommand]
    private void VoirPdf()
    {
        string? dossierCours = _etat.Reglages.DossierCours;
        string chemin = dossierCours is null ? "" : Path.Combine(dossierCours, _seance.SourceFichier);
        if (!File.Exists(chemin))
        {
            ServiceDialogue.Informer("Document introuvable",
                $"Le document d'origine est introuvable :\n{_seance.SourceFichier}\n\nVérifie le dossier « Cours » dans les réglages.");
            return;
        }

        int? page = Sections.Count > 0 ? Sections[Index].PageSource : null;
        if (Path.GetExtension(chemin).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            new FenetrePdf(dossierCours!, _seance.SourceFichier, page, _etat.EstSombre) { Owner = Application.Current.MainWindow }.Show();
        }
        else if (Path.GetExtension(chemin).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            // Cours en Markdown : affiché dans une fenêtre d'Atome, avec le même rendu que la lecture.
            new FenetreCoursOrigine(chemin) { Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow }.Show();
        }
        else
        {
            // Autre type de fichier (.docx…) : on laisse Windows l'ouvrir avec le programme habituel.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chemin) { UseShellExecute = true });
        }
    }
}
