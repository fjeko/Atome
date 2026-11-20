namespace Revisions.Core.Sessions;

/// <summary>
/// Carte « coriace » : une carte qui résiste, ratée plusieurs fois de suite. Atome le remarque pour
/// proposer de l'aide, jamais pour faire la leçon : relire le passage, signaler la carte ou la mettre de côté.
/// </summary>
public static class CartesCoriaces
{
    /// <summary>Nombre de notes récentes regardées.</summary>
    public const int Fenetre = 4;
    /// <summary>Nombre de « Raté » parmi ces notes à partir duquel la carte est coriace.</summary>
    public const int Seuil = 3;

    /// <param name="notesRecentes">Notes de la carte (1 = Raté … 4 = Facile), de la plus récente à la plus ancienne.</param>
    public static bool EstCoriace(IEnumerable<int> notesRecentes) =>
        notesRecentes.Take(Fenetre).Count(n => n == 1) >= Seuil;
}
