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
            _file.Insert(PlaceDuRetour(carte.Matiere), carteMiseAJour ?? carte);
        _position++;
    }

    /// <summary>Écart minimal et maximal autour de <see cref="EcartRetour"/> pour rester dans un paquet de la même matière.</summary>
    public const int EcartMin = 3, EcartMax = 10;

    /// <summary>
    /// Où replacer une carte ratée : environ <see cref="EcartRetour"/> cartes plus loin, en restant si possible
    /// juste après une carte de la même matière (donc dans un de ses paquets). On cherche la place la plus proche
    /// de l'écart habituel, entre <see cref="EcartMin"/> et <see cref="EcartMax"/> cartes plus loin.
    /// </summary>
    private int PlaceDuRetour(string matiere)
    {
        int debut = _position + 1;
        int habituel = Math.Min(_file.Count, debut + EcartRetour);
        int meilleur = -1;
        for (int index = Math.Min(_file.Count, debut + EcartMin); index <= Math.Min(_file.Count, debut + EcartMax); index++)
        {
            // Juste après une carte de la même matière (la carte d'avant n'est pas celle qui vient d'être ratée).
            bool memeMatiere = index - 1 > _position && _file[index - 1].Matiere == matiere;
            if (memeMatiere && (meilleur < 0 || Math.Abs(index - habituel) < Math.Abs(meilleur - habituel))) meilleur = index;
        }
        return meilleur >= 0 ? meilleur : habituel;
    }
}
