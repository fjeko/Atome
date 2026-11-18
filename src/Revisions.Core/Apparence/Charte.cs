namespace Revisions.Core.Apparence;

/// <summary>Trois nuances d'une même couleur : un fond pâle (tuile, bandeau), un trait (barre, icône, pastille) et un texte lisible sur le fond pâle.</summary>
public sealed record Nuances(Couleur Pale, Couleur Trait, Couleur Texte);

/// <summary>Une paire « texte sur fond » à vérifier, avec le contraste minimal exigé (4,5 pour du texte, 3 pour un élément graphique).</summary>
public sealed record PaireContraste(string Nom, Couleur Devant, Couleur Fond, double Minimum)
{
    public double Contraste => Couleur.Contraste(Devant, Fond);
    public bool Conforme => Contraste >= Minimum;
}

/// <summary>
/// Toutes les couleurs du système de design pour un thème (cahier de design, section 1) : neutres, accent,
/// couleurs de sens (succès, attention, erreur, information) et couleurs des matières.
/// Calculée dans le cœur métier, sans WPF : les contrastes WCAG sont ainsi vérifiés par les tests.
/// </summary>
/// <param name="Fond">Fond de la page (sous les cartes). Avec l'effet Mica, c'est sa couleur de repli.</param>
/// <param name="Surface">Fond des cartes.</param>
/// <param name="Surelevee">Fond des éléments posés au-dessus (dialogues, bandeaux, menus).</param>
/// <param name="AccentTrait">Accent assez contrasté pour un élément graphique (barre de progression, anneau).</param>
/// <param name="AccentDoux">Accent en fond pâle (élément sélectionné, bouton « Facile », information).</param>
public sealed record Charte(
    bool Sombre,
    Couleur Fond,
    Couleur Surface,
    Couleur Surelevee,
    Couleur Bordure,
    Couleur Texte,
    Couleur TexteSecondaire,
    Couleur TexteDiscret,
    Couleur Accent,
    Couleur SurAccent,
    Couleur AccentTexte,
    Couleur AccentTrait,
    Nuances AccentDoux,
    Nuances Succes,
    Nuances Attention,
    Nuances Erreur,
    Nuances Facile,
    IReadOnlyDictionary<Teinte, Nuances> Matieres)
{
    /// <summary>Information = accent, pour rester dans la couleur choisie.</summary>
    public Nuances Info => AccentDoux;

    /// <summary>Nuances d'une matière, d'après son nom (et sa partie).</summary>
    public Nuances PourMatiere(string? matiere, string? partie = null) =>
        Matieres[ReperesMatieres.Pour(matiere, partie).Teinte];

    // Teinte (degrés) et saturation de base de chaque couleur.
    private static readonly IReadOnlyDictionary<Teinte, (double T, double S)> Bases = new Dictionary<Teinte, (double, double)>
    {
        [Teinte.Bleu] = (210, 0.75),
        [Teinte.Violet] = (268, 0.62),
        [Teinte.Indigo] = (234, 0.58),
        [Teinte.Corail] = (6, 0.78),
        [Teinte.Orange] = (26, 0.85),
        [Teinte.Ocre] = (43, 0.80),
        [Teinte.Vert] = (148, 0.55),
        [Teinte.Rose] = (330, 0.65),
        [Teinte.Gris] = (220, 0.08),
        [Teinte.Sarcelle] = (176, 0.60),
        [Teinte.Cyan] = (194, 0.70),
        [Teinte.Brun] = (24, 0.38),
        [Teinte.Lime] = (84, 0.55),
    };

    // Neutres des thèmes sans fond imposé (« Comme Windows », « Clair », « Sombre »).
    private static readonly Couleur FondClair = Couleur.Parse("#F3F3F5"), FondSombre = Couleur.Parse("#1B1B1F");

    /// <summary>Calcule la charte complète à partir de la palette du thème (et de l'accent) choisis.</summary>
    public static Charte Calculer(Palette p)
    {
        bool sombre = p.Sombre;
        Couleur surface = p.Feuille;
        Couleur fond = p.Fond ?? (sombre ? FondSombre : FondClair);
        Couleur surelevee = sombre ? surface.Melanger(p.Texte, 0.05) : surface.Melanger(Couleur.Blanc, 0.6);
        Couleur bordure = surface.Melanger(p.Texte, sombre ? 0.13 : 0.12);
        Couleur[] fonds = [fond, surface, surelevee];

        Couleur texte = Assurer(p.Texte, fonds, 7);
        Couleur secondaire = Assurer(texte.Melanger(surface, 0.27), fonds, 4.5);
        Couleur discret = Assurer(texte.Melanger(surface, 0.42), fonds, 4.5);

        Couleur accent = p.Accent;
        Couleur accentTexte = Assurer(p.AccentTexte, fonds, 4.5);
        Couleur accentTrait = Assurer(accent, fonds, 3);
        Couleur accentPale = surface.Melanger(accent, sombre ? 0.20 : 0.13);
        var accentDoux = new Nuances(accentPale, Assurer(accent, [accentPale, surface], 3), Assurer(accentTexte, [accentPale], 4.5));

        Nuances Nuancer(double t, double s) => Charte.Nuancer(t, s, sombre, fonds, texte);

        return new Charte(
            Sombre: sombre,
            Fond: fond,
            Surface: surface,
            Surelevee: surelevee,
            Bordure: bordure,
            Texte: texte,
            TexteSecondaire: secondaire,
            TexteDiscret: discret,
            Accent: accent,
            SurAccent: p.TexteSurAccent,
            AccentTexte: accentTexte,
            AccentTrait: accentTrait,
            AccentDoux: accentDoux,
            Succes: Nuancer(145, 0.55),
            Attention: Nuancer(38, 0.90),
            Erreur: Nuancer(356, 0.62),
            Facile: TeinteProche(accent, [356, 38, 145]) ? Nuancer(Bases[Teinte.Bleu].T, Bases[Teinte.Bleu].S) : accentDoux,
            Matieres: Bases.ToDictionary(b => b.Key, b => Nuancer(b.Value.T, b.Value.S)));
    }


    /// <summary>
    /// Vrai si l'accent ressemble à l'une des couleurs de sens (rouge, ambre, vert) ou s'il est presque gris :
    /// le bouton « Facile » prend alors le bleu, pour ne jamais se confondre avec « Raté », « Difficile » ou « Bien ».
    /// </summary>
    private static bool TeinteProche(Couleur accent, double[] teintes)
    {
        var (t, s) = accent.Tsl;
        if (s < 0.15) return true;
        return teintes.Any(autre => Math.Min(Math.Abs(t - autre), 360 - Math.Abs(t - autre)) < 30);
    }
    /// <summary>
    /// Fabrique les trois nuances d'une couleur. En clair : fond très pâle, trait moyen, texte foncé.
    /// En sombre : fond profond et peu saturé, trait lumineux, texte très clair. Chaque nuance est ensuite
    /// ajustée si besoin pour garantir les contrastes WCAG (le texte principal reste lisible sur le fond pâle).
    /// </summary>
    private static Nuances Nuancer(double t, double s, bool sombre, Couleur[] fonds, Couleur textePrincipal)
    {
        Couleur pale = sombre ? Couleur.DepuisTsl(t, s * 0.34, 0.19) : Couleur.DepuisTsl(t, s * 0.85, 0.93);
        // Le texte principal posé sur le fond pâle (titre d'une tuile) doit rester parfaitement lisible.
        if (Couleur.Contraste(textePrincipal, pale) < 7)
            pale = pale.Melanger(sombre ? Couleur.Noir : Couleur.Blanc, 0.3);
        Couleur trait = sombre ? Couleur.DepuisTsl(t, s * 0.80, 0.64) : Couleur.DepuisTsl(t, s * 0.85, 0.44);
        trait = Assurer(trait, [.. fonds, pale], 3);
        Couleur texte = sombre ? Couleur.DepuisTsl(t, s * 0.70, 0.84) : Couleur.DepuisTsl(t, s * 0.80, 0.27);
        texte = Assurer(texte, [pale, .. fonds], 4.5);
        return new Nuances(pale, trait, texte);
    }

    /// <summary>
    /// Ajuste une couleur jusqu'à ce qu'elle soit assez contrastée sur chacun des fonds donnés
    /// (tous clairs ou tous foncés : ceux d'un même thème).
    /// </summary>
    private static Couleur Assurer(Couleur couleur, Couleur[] fonds, double minimum)
    {
        Couleur resultat = couleur;
        for (int i = 0; i < 4; i++)
            foreach (Couleur fond in fonds)
                resultat = resultat.AjusterPourContraste(fond, minimum);
        return resultat;
    }

    /// <summary>Toutes les paires texte / fond du système de design, pour le test de contraste WCAG AA.</summary>
    public IEnumerable<PaireContraste> Paires()
    {
        var fonds = new[] { ("fond", Fond), ("surface", Surface), ("surélevée", Surelevee) };
        foreach (var (nomFond, fond) in fonds)
        {
            yield return new($"texte sur {nomFond}", Texte, fond, 4.5);
            yield return new($"texte secondaire sur {nomFond}", TexteSecondaire, fond, 4.5);
            yield return new($"texte discret sur {nomFond}", TexteDiscret, fond, 4.5);
            yield return new($"accent (texte) sur {nomFond}", AccentTexte, fond, 4.5);
            yield return new($"accent (trait) sur {nomFond}", AccentTrait, fond, 3);
        }
        yield return new("texte sur bouton accent", SurAccent, Accent, 4.5);

        var nuances = new List<(string, Nuances)>
        {
            ("accent doux", AccentDoux), ("succès", Succes), ("attention", Attention), ("erreur", Erreur), ("facile", Facile),
        };
        nuances.AddRange(Matieres.Select(m => ($"matière {m.Key}", m.Value)));
        foreach (var (nom, n) in nuances)
        {
            yield return new($"{nom} : texte sur pâle", n.Texte, n.Pale, 4.5);
            yield return new($"{nom} : texte principal sur pâle", Texte, n.Pale, 4.5);
            yield return new($"{nom} : trait sur pâle", n.Trait, n.Pale, 3);
            yield return new($"{nom} : trait sur surface", n.Trait, Surface, 3);
            yield return new($"{nom} : texte sur surface", n.Texte, Surface, 4.5);
        }
    }
}
