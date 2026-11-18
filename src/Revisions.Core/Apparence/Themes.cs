namespace Revisions.Core.Apparence;

/// <summary>Un thème proposé dans les réglages.</summary>
/// <param name="Cle">Nom technique enregistré dans les réglages.</param>
/// <param name="Nom">Nom affiché.</param>
/// <param name="Sombre">Vrai : sombre ; faux : clair ; null : suit le réglage de Windows.</param>
/// <param name="Accent">Couleur d'accent du thème, ou null pour celle de Windows.</param>
/// <param name="Fond">Couleur de fond des fenêtres, ou null pour le fond habituel de Windows.</param>
/// <param name="Feuille">Couleur de fond des zones de lecture (cours, cartes), ou null pour le blanc / gris foncé habituel.</param>
public sealed record ThemeApp(string Cle, string Nom, bool? Sombre, Couleur? Accent = null, Couleur? Fond = null, Couleur? Feuille = null);

/// <summary>Toutes les couleurs finales à appliquer, une fois le thème et l'accent choisis.</summary>
/// <param name="Sombre">Base sombre ou claire.</param>
/// <param name="AccentImpose">Vrai si l'accent vient du thème ou de l'utilisateur (sinon : accent de Windows).</param>
/// <param name="Accent">Couleur d'accent (boutons principaux, sélection, barres).</param>
/// <param name="TexteSurAccent">Noir ou blanc, lisible sur l'accent.</param>
/// <param name="AccentTexte">Variante de l'accent lisible comme texte ou lien sur la feuille.</param>
/// <param name="Fond">Fond des fenêtres, ou null pour celui de Windows.</param>
/// <param name="Feuille">Fond des zones de lecture.</param>
/// <param name="Texte">Texte principal.</param>
/// <param name="Discret">Texte secondaire.</param>
/// <param name="Bord">Traits et bordures.</param>
/// <param name="FondDoux">Fond des encadrés, du code et des en-têtes de tableau.</param>
public sealed record Palette(
    bool Sombre,
    bool AccentImpose,
    Couleur Accent,
    Couleur TexteSurAccent,
    Couleur AccentTexte,
    Couleur? Fond,
    Couleur Feuille,
    Couleur Texte,
    Couleur Discret,
    Couleur Bord,
    Couleur FondDoux);

/// <summary>Liste des thèmes et calcul de la palette finale.</summary>
public static class Themes
{
    /// <summary>Thème du premier lancement : sombre (choix de Florian, cahier de design). Un thème déjà choisi est toujours gardé.</summary>
    public const string CleParDefaut = "sombre";

    /// <summary>Les trois thèmes neutres, puis six thèmes colorés tout faits.</summary>
    public static readonly IReadOnlyList<ThemeApp> Tous =
    [
        new("systeme", "Comme Windows", null),
        new("clair", "Clair", false),
        new("sombre", "Sombre", true),
        new("ocean", "Océan", true, Couleur.Parse("#4CC2FF"), Couleur.Parse("#0E1A2B"), Couleur.Parse("#132338")),
        new("foret", "Forêt", true, Couleur.Parse("#6CCB5F"), Couleur.Parse("#111D16"), Couleur.Parse("#17271D")),
        new("crepuscule", "Crépuscule", true, Couleur.Parse("#B69CFF"), Couleur.Parse("#19142B"), Couleur.Parse("#211B38")),
        new("sable", "Sable", false, Couleur.Parse("#9A5B13"), Couleur.Parse("#F3EBDD"), Couleur.Parse("#FBF6EC")),
        new("menthe", "Menthe", false, Couleur.Parse("#0F7B5F"), Couleur.Parse("#E8F3EE"), Couleur.Parse("#F6FBF8")),
        new("lavande", "Lavande", false, Couleur.Parse("#6B4FBB"), Couleur.Parse("#EDE9F7"), Couleur.Parse("#F8F6FD")),
    ];

    /// <summary>Couleurs d'accent proposées en un clic (un champ libre « #RRGGBB » existe aussi).</summary>
    public static readonly IReadOnlyList<(string Nom, Couleur Couleur)> Accents =
    [
        ("Bleu", Couleur.Parse("#2F7DE1")),
        ("Turquoise", Couleur.Parse("#0E9AA7")),
        ("Vert", Couleur.Parse("#2E9E5B")),
        ("Jaune", Couleur.Parse("#D9A300")),
        ("Orange", Couleur.Parse("#E8731C")),
        ("Rouge", Couleur.Parse("#D64545")),
        ("Rose", Couleur.Parse("#D8498F")),
        ("Violet", Couleur.Parse("#8B5CF6")),
        ("Indigo", Couleur.Parse("#5B6CE0")),
        ("Gris", Couleur.Parse("#6B7785")),
    ];

    // Couleurs de base des thèmes neutres (les mêmes que dans Web/app.css).
    private static readonly Couleur FeuilleClaire = Couleur.Blanc, FeuilleSombre = Couleur.Parse("#26262B");
    private static readonly Couleur TexteClair = Couleur.Parse("#1B1B1B"), TexteSombre = Couleur.Parse("#F2F2F2");

    /// <summary>Thème correspondant à une clé ; une clé inconnue donne le thème par défaut.</summary>
    public static ThemeApp Trouver(string? cle) =>
        Tous.FirstOrDefault(t => t.Cle == cle) ?? Tous[0];

    /// <summary>Calcule la palette finale.</summary>
    /// <param name="theme">Thème choisi.</param>
    /// <param name="accentPerso">Couleur d'accent choisie par l'utilisateur (prioritaire), ou null.</param>
    /// <param name="systemeSombre">Windows est-il en mode sombre ? (pour le thème « Comme Windows »)</param>
    /// <param name="accentSysteme">Couleur d'accent de Windows.</param>
    public static Palette Resoudre(ThemeApp theme, Couleur? accentPerso, bool systemeSombre, Couleur accentSysteme)
    {
        bool sombre = theme.Sombre ?? systemeSombre;
        Couleur feuille = theme.Feuille ?? (sombre ? FeuilleSombre : FeuilleClaire);
        Couleur texte = sombre ? TexteSombre : TexteClair;
        Couleur accent = accentPerso ?? theme.Accent ?? accentSysteme;

        return new Palette(
            Sombre: sombre,
            AccentImpose: accentPerso is not null || theme.Accent is not null,
            Accent: accent,
            TexteSurAccent: accent.TexteLisible,
            // Sur la feuille, l'accent sert de texte (liens) : on garantit le contraste « texte normal ».
            AccentTexte: accent.AjusterPourContraste(feuille, 4.5),
            Fond: theme.Fond,
            Feuille: feuille,
            Texte: texte,
            // Les nuances sont des mélanges entre le texte et la feuille : elles restent
            // cohérentes quel que soit le thème.
            Discret: texte.Melanger(feuille, 0.35),
            Bord: feuille.Melanger(texte, 0.16),
            FondDoux: feuille.Melanger(texte, 0.06));
    }
}
