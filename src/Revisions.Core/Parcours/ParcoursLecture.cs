using Revisions.Core.Modeles;

namespace Revisions.Core.Parcours;

/// <summary>Les trois temps du parcours « Lire puis contrôler ».</summary>
public enum PhaseParcours
{
    /// <summary>Le cours section par section, avec 1 ou 2 questions flash après chaque section.</summary>
    Lecture,
    /// <summary>Le contrôle final : cours masqué, questions mélangées sur toute la séance.</summary>
    Controle,
    /// <summary>Les résultats : score par section.</summary>
    Resultats,
}

/// <summary>Une réponse donnée pendant le parcours.</summary>
/// <param name="CarteId">Carte posée.</param>
/// <param name="SectionId">Section de la carte (pour le score par section).</param>
/// <param name="Controle">Vrai pour une question du contrôle final, faux pour une question flash.</param>
/// <param name="Note">Note donnée (1 = Raté … 4 = Facile), la même que celle envoyée à FSRS.</param>
public sealed record ReponseParcours(string CarteId, string SectionId, bool Controle, int Note)
{
    public bool Reussie => Note > (int)Modeles.Note.Rate;
}

/// <summary>Score d'une section sur l'écran de résultats.</summary>
public sealed record ScoreSection(string SectionId, string Titre, int Reussies, int Total)
{
    public bool Ratee => Total > 0 && Reussies < Total;
}

/// <summary>
/// État complet d'un parcours. Il est enregistré en JSON après chaque action : on peut quitter
/// et reprendre exactement là où on en était (même section, même question, mêmes cartes tirées).
/// </summary>
public sealed record EtatParcours
{
    public required string PackId { get; init; }
    public PhaseParcours Phase { get; set; } = PhaseParcours.Lecture;
    /// <summary>Section affichée pendant la lecture.</summary>
    public int IndexSection { get; set; }
    /// <summary>Question flash en cours après la section (-1 = on lit encore la section).</summary>
    public int IndexFlash { get; set; } = -1;
    /// <summary>Question du contrôle en cours.</summary>
    public int IndexControle { get; set; }
    /// <summary>Questions flash tirées pour chaque section (identifiants de cartes, 0 à 2 par section).</summary>
    public Dictionary<string, List<string>> Flash { get; init; } = new();
    /// <summary>Questions du contrôle final, déjà mélangées.</summary>
    public List<string> Controle { get; init; } = [];
    public List<ReponseParcours> Reponses { get; init; } = [];
}

/// <summary>
/// Règles du parcours « Lire puis contrôler » (fiche A1), sans base ni écran : tout est testable.
/// Le contenu vient uniquement des packs existants : les cartes sont déjà reliées à leur section.
/// </summary>
public static class ParcoursLecture
{
    public const int FlashParSection = 2;
    public const int ControleMin = 8;
    public const int ControleMax = 12;

    /// <summary>
    /// Tire les questions du parcours.
    /// Flash : 1 question par section (2 si la section a au moins 4 cartes), aucune si elle n'a pas de carte.
    /// Contrôle : 8 à 12 questions (moins si la séance a moins de cartes), d'abord les cartes non vues pendant
    /// la lecture, complétées si besoin par des cartes flash ; l'ensemble est mélangé.
    /// </summary>
    public static EtatParcours Composer(string packId, IReadOnlyList<Section> sections, IReadOnlyList<Carte> cartes, Random hasard)
    {
        var flash = new Dictionary<string, List<string>>();
        var parSection = cartes.GroupBy(c => c.SectionId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var section in sections)
        {
            if (!parSection.TryGetValue(section.Id, out var liste) || liste.Count == 0) continue;
            int combien = liste.Count >= 4 ? FlashParSection : 1;
            var melange = liste.ToArray();
            hasard.Shuffle(melange);
            flash[section.Id] = melange.Take(combien).Select(c => c.Id).ToList();
        }

        // Seules les cartes rattachées à une section de la séance comptent (une carte orpheline n'aurait pas de score).
        var idsSections = sections.Select(s => s.Id).ToHashSet();
        var utilisables = cartes.Where(c => idsSections.Contains(c.SectionId)).ToList();
        var dejaVues = flash.Values.SelectMany(v => v).ToHashSet();

        int cible = Math.Min(utilisables.Count, Math.Clamp(utilisables.Count, ControleMin, ControleMax));
        var nonVues = utilisables.Where(c => !dejaVues.Contains(c.Id)).ToArray();
        var vues = utilisables.Where(c => dejaVues.Contains(c.Id)).ToArray();
        hasard.Shuffle(nonVues);
        hasard.Shuffle(vues);
        var controle = nonVues.Concat(vues).Take(cible).Select(c => c.Id).ToArray();
        hasard.Shuffle(controle);

        return new EtatParcours { PackId = packId, Flash = flash, Controle = [.. controle] };
    }

    /// <summary>Questions flash de la section en cours de lecture.</summary>
    public static IReadOnlyList<string> FlashDe(EtatParcours etat, IReadOnlyList<Section> sections) =>
        etat.IndexSection < sections.Count && etat.Flash.TryGetValue(sections[etat.IndexSection].Id, out var l) ? l : [];

    /// <summary>
    /// Avance d'un pas après la lecture d'une section ou une réponse flash :
    /// question flash suivante, sinon section suivante, sinon contrôle (ou résultats s'il n'y a aucune question).
    /// </summary>
    public static void AvancerLecture(EtatParcours etat, IReadOnlyList<Section> sections)
    {
        var flash = FlashDe(etat, sections);
        if (etat.IndexFlash + 1 < flash.Count)
        {
            etat.IndexFlash++;
            return;
        }
        etat.IndexFlash = -1;
        if (etat.IndexSection + 1 < sections.Count)
        {
            etat.IndexSection++;
            return;
        }
        etat.Phase = etat.Controle.Count > 0 ? PhaseParcours.Controle : PhaseParcours.Resultats;
        etat.IndexControle = 0;
    }

    /// <summary>Enregistre une réponse et passe à la suite.</summary>
    public static void Repondre(EtatParcours etat, IReadOnlyList<Section> sections, string carteId, string sectionId, Note note)
    {
        bool controle = etat.Phase == PhaseParcours.Controle;
        etat.Reponses.Add(new ReponseParcours(carteId, sectionId, controle, (int)note));
        if (!controle)
        {
            AvancerLecture(etat, sections);
            return;
        }
        etat.IndexControle++;
        if (etat.IndexControle >= etat.Controle.Count) etat.Phase = PhaseParcours.Resultats;
    }

    /// <summary>
    /// Score par section : les réponses du contrôle final, ou à défaut celles des questions flash
    /// (une section sans question au contrôle garde ainsi un score). Les sections sans aucune réponse sont omises.
    /// </summary>
    public static List<ScoreSection> Scores(EtatParcours etat, IReadOnlyList<Section> sections)
    {
        var resultat = new List<ScoreSection>();
        foreach (var section in sections)
        {
            var reponses = etat.Reponses.Where(r => r.SectionId == section.Id && r.Controle).ToList();
            if (reponses.Count == 0) reponses = etat.Reponses.Where(r => r.SectionId == section.Id && !r.Controle).ToList();
            if (reponses.Count == 0) continue;
            resultat.Add(new ScoreSection(section.Id, section.Titre, reponses.Count(r => r.Reussie), reponses.Count));
        }
        return resultat;
    }

    /// <summary>Score global en pourcentage : le contrôle final, ou les questions flash s'il n'y a pas eu de contrôle.</summary>
    public static int ScoreGlobal(EtatParcours etat)
    {
        var reponses = etat.Reponses.Where(r => r.Controle).ToList();
        if (reponses.Count == 0) reponses = etat.Reponses;
        return reponses.Count == 0 ? 0 : (int)Math.Round(100.0 * reponses.Count(r => r.Reussie) / reponses.Count);
    }

    /// <summary>Phrase de la mascotte sur l'écran de résultats : elle encourage toujours, jamais de reproche.</summary>
    public static string Encouragement(int score) => score switch
    {
        >= 90 => "Superbe ! Ce cours est bien en place.",
        >= 70 => "Beau travail ! Encore quelques points à consolider.",
        >= 40 => "Bon début : relire les sections en orange va tout éclairer.",
        _ => "C'est normal après une première lecture : les révisions vont faire le reste.",
    };
}
