using CommunityToolkit.Mvvm.ComponentModel;
using Revisions.App.Services;
using Revisions.Core.Collections;

namespace Revisions.App.ViewModels;

/// <summary>
/// Une collection de packs dans les Réglages (ligne avec interrupteur) ou dans les filtres de la Bibliothèque (puce).
/// Cocher ou décocher appelle <paramref name="changer"/> ; si le changement est refusé, la liste est reconstruite par l'écran.
/// </summary>
public sealed partial class ChoixCollection : ObservableObject
{
    private readonly Func<ChoixCollection, Task> _changer;
    private readonly bool _initialise;

    public ChoixCollection(InfoCollection info, Func<ChoixCollection, Task> changer)
    {
        Cle = info.Cle;
        Nom = info.Nom;
        NbCours = info.NbCours;
        NbCartes = info.NbCartes;
        _visible = info.Visible;
        _changer = changer;
        _initialise = true;
    }

    public string Cle { get; }
    public string Nom { get; }
    public int NbCours { get; }
    public int NbCartes { get; }

    /// <summary>Compteurs affichés sous le nom (les cartes d'une collection pas encore chargée sont inconnues).</summary>
    public string Detail => NbCartes > 0
        ? $"{Texte.Pluriel(NbCours, "cours", "cours")}  ·  {Texte.Pluriel(NbCartes, "carte", "cartes")}"
        : Texte.Pluriel(NbCours, "cours", "cours");

    /// <summary>Afficher et réviser cette collection.</summary>
    [ObservableProperty] private bool _visible;

    partial void OnVisibleChanged(bool value)
    {
        if (_initialise) _ = _changer(this);
    }
}
