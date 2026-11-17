using CommunityToolkit.Mvvm.ComponentModel;
using Revisions.Core.Signalements;

namespace Revisions.App.ViewModels;

/// <summary>Un motif proposé dans la petite fenêtre « Signaler une erreur ».</summary>
public sealed partial class ChoixMotif(MotifSignalement motif) : ObservableObject
{
    public MotifSignalement Motif => motif;
    public string Libelle => VocabulaireSignalement.Libelle(motif);

    [ObservableProperty] private bool _estChoisi;
}

/// <summary>
/// Petite fenêtre de signalement : un motif (obligatoire, pour que « Envoyer » ne parte pas par erreur)
/// et un commentaire facultatif. Rien n'est enregistré ici : la fenêtre se contente de rassembler la saisie.
/// </summary>
public sealed partial class SignalementViewModel : ObservableObject
{
    public IReadOnlyList<ChoixMotif> Motifs { get; }

    /// <summary>Ce qu'on signale, en une ligne (« Carte · Matrices… »).</summary>
    public string Cible { get; }

    [ObservableProperty] private string _commentaire = "";

    public SignalementViewModel(string cible)
    {
        Cible = cible;
        Motifs = Enum.GetValues<MotifSignalement>().Select(m => new ChoixMotif(m)).ToList();
        // Quand un motif est coché, le bouton « Envoyer » doit se mettre à jour.
        foreach (var motif in Motifs)
            motif.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PeutEnvoyer));
    }

    public bool PeutEnvoyer => Motifs.Any(m => m.EstChoisi);

    public MotifSignalement? MotifChoisi => Motifs.FirstOrDefault(m => m.EstChoisi)?.Motif;
}
