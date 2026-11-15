namespace Revisions.Core.Sessions;

/// <summary>
/// La file des cartes d'une session en cours.
/// Une carte ratée revient une fois, quelques cartes plus loin, pour être revue tout de suite.
/// </summary>
public sealed class SessionEnCours
{
    /// <summary>Nombre de cartes entre une carte ratée et son retour.</summary>
    public const int EcartRetour = 5;

    private readonly List<CarteAReviser> _file;
    private readonly HashSet<string> _dejaRevenues = [];
    private int _position;

    public SessionEnCours(IEnumerable<CarteAReviser> cartes) => _file = cartes.ToList();

    /// <summary>Carte à présenter, ou null quand la session est terminée.</summary>
    public CarteAReviser? Courante => _position < _file.Count ? _file[_position] : null;

    /// <summary>Vrai si la carte courante est un retour (déjà ratée dans cette session).</summary>
    public bool CouranteEstUnRetour => Courante is { } c && _dejaRevenues.Contains(c.Carte.Id);

    /// <summary>Nombre de cartes déjà traitées.</summary>
    public int Faites => _position;

    /// <summary>Nombre total de passages prévus (retours compris).</summary>
    public int Total => _file.Count;

    public bool EstTerminee => Courante is null;

    /// <summary>
    /// Passe à la carte suivante. Si la carte vient d'être ratée (et n'est pas déjà revenue),
    /// on la replace plus loin dans la file avec son nouvel état.
    /// </summary>
    public void Avancer(bool ratee, CarteAReviser? carteMiseAJour = null)
    {
        var carte = Courante ?? throw new InvalidOperationException("La session est terminée.");
        if (ratee && _dejaRevenues.Add(carte.Carte.Id))
        {
            int index = Math.Min(_file.Count, _position + 1 + EcartRetour);
            _file.Insert(index, carteMiseAJour ?? carte);
        }
        _position++;
    }
}
