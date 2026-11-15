using Revisions.Core.Modeles;

namespace Revisions.Core.Planification;

/// <summary>Les deux nombres que FSRS retient pour chaque carte.</summary>
/// <param name="Stabilite">S, en jours : au bout de S jours, on a 90 % de chances de se souvenir.</param>
/// <param name="Difficulte">D, entre 1 et 10 : plus c'est haut, plus la stabilité grandit lentement.</param>
public readonly record struct EtatMemoire(double Stabilite, double Difficulte);

/// <summary>
/// Algorithme de répétition espacée FSRS (version 5), avec ses paramètres par défaut.
/// Référence : https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm
///
/// Idée générale : la mémoire d'une carte est décrite par sa stabilité S et sa difficulté D.
/// La probabilité de se souvenir (« retrievabilité » R) baisse avec le temps, d'autant plus
/// lentement que S est grand. Chaque réponse met à jour S et D ; la prochaine révision est
/// placée au moment où R retombe à la rétention visée (90 % par défaut).
///
/// Avantage sur SM-2 pour un rythme irrégulier : le calcul utilise le temps réellement écoulé.
/// Une bonne réponse après une longue pause augmente beaucoup la stabilité, au lieu d'être ignorée.
/// </summary>
public sealed class Fsrs
{
    /// <summary>Forme de la courbe d'oubli (constantes de FSRS-4.5 et 5).</summary>
    private const double Decroissance = -0.5;
    private const double Facteur = 19.0 / 81.0;

    public const int IntervalleMax = 3650; // 10 ans
    private const double StabiliteMin = 0.01;

    /// <summary>Les 19 paramètres par défaut de FSRS-5 (w0 à w18).</summary>
    public static readonly double[] ParametresParDefaut =
    [
        0.40255, 1.18385, 3.173, 15.69105,   // w0-w3 : stabilité initiale selon la première note
        7.1949, 0.5345,                      // w4-w5 : difficulté initiale
        1.4604, 0.0046,                      // w6-w7 : évolution de la difficulté
        1.54575, 0.1192, 1.01925,            // w8-w10 : gain de stabilité après un succès
        1.9395, 0.11, 0.29605, 2.2698,       // w11-w14 : stabilité après un oubli
        0.2315, 2.9898,                      // w15-w16 : pénalité « Difficile », bonus « Facile »
        0.51655, 0.6621,                     // w17-w18 : révision le même jour
    ];

    private readonly double[] _w;

    public Fsrs(double[]? parametres = null)
    {
        _w = parametres ?? ParametresParDefaut;
        if (_w.Length != 19) throw new ArgumentException("FSRS-5 attend 19 paramètres.", nameof(parametres));
    }

    /// <summary>Probabilité de se souvenir après <paramref name="jours"/> jours, pour une stabilité donnée.</summary>
    public static double Retrievabilite(double jours, double stabilite) =>
        Math.Pow(1 + Facteur * Math.Max(0, jours) / stabilite, Decroissance);

    /// <summary>Nombre de jours à attendre pour que R retombe à la rétention visée (entre 1 jour et 10 ans).</summary>
    public static int Intervalle(double stabilite, double retention)
    {
        double jours = stabilite / Facteur * (Math.Pow(retention, 1 / Decroissance) - 1);
        return (int)Math.Clamp(Math.Round(jours), 1, IntervalleMax);
    }

    /// <summary>État après la toute première réponse à une carte.</summary>
    public EtatMemoire PremiereReponse(Note note) =>
        new(Math.Max(StabiliteMin, _w[(int)note - 1]), DifficulteInitiale(note));

    /// <summary>
    /// État après une nouvelle réponse.
    /// </summary>
    /// <param name="etat">État avant la réponse.</param>
    /// <param name="joursEcoules">Jours écoulés depuis la révision précédente (0 = même jour).</param>
    /// <param name="note">Note donnée.</param>
    public EtatMemoire ReponseSuivante(EtatMemoire etat, double joursEcoules, Note note)
    {
        double s = etat.Stabilite, d = etat.Difficulte;
        int g = (int)note;
        double nouvelleStabilite;

        if (joursEcoules < 1)
        {
            // Révision le même jour (carte ratée qui revient dans la session).
            nouvelleStabilite = s * Math.Exp(_w[17] * (g - 3 + _w[18]));
        }
        else
        {
            double r = Retrievabilite(joursEcoules, s);
            if (note == Note.Rate)
            {
                // Oubli : la stabilité repart d'une petite valeur, jamais plus haute qu'avant.
                double apresOubli = _w[11] * Math.Pow(d, -_w[12]) * (Math.Pow(s + 1, _w[13]) - 1) * Math.Exp(_w[14] * (1 - r));
                nouvelleStabilite = Math.Min(s, apresOubli);
            }
            else
            {
                // Succès : la stabilité grandit. Le gain est plus fort si la carte est facile (D bas),
                // si la stabilité était petite, et si on était proche de l'oubli (R bas).
                double modificateur = note == Note.Difficile ? _w[15] : note == Note.Facile ? _w[16] : 1;
                double gain = Math.Exp(_w[8]) * (11 - d) * Math.Pow(s, -_w[9]) * (Math.Exp(_w[10] * (1 - r)) - 1) * modificateur;
                nouvelleStabilite = s * (gain + 1);
            }
        }

        return new EtatMemoire(Math.Max(StabiliteMin, nouvelleStabilite), DifficulteSuivante(d, g));
    }

    private double DifficulteInitiale(Note note) =>
        Math.Clamp(_w[4] - Math.Exp(_w[5] * ((int)note - 1)) + 1, 1, 10);

    private double DifficulteSuivante(double d, int g)
    {
        double variation = -_w[6] * (g - 3);                     // Raté : +, Facile : −
        double amortie = d + variation * (10 - d) / 9;           // plus D est haut, moins il monte
        double cible = DifficulteInitiale(Note.Facile);          // retour lent vers une valeur moyenne
        return Math.Clamp(_w[7] * cible + (1 - _w[7]) * amortie, 1, 10);
    }
}
