using Revisions.Core.Modeles;
using static System.FormattableString;

namespace Revisions.Core.TdGroupe;

/// <summary>Ce dont le TD « séance » a besoin : le titre de la séance, ses cartes et les titres de ses sections.</summary>
public sealed record ContenuSeance(string Titre, IReadOnlyList<Carte> Cartes, IReadOnlyDictionary<string, string> TitresSections);

/// <summary>
/// TD « à partir d'une séance » : choisit et ordonne, avec la graine, les cartes exercice et QCM d'un pack.
/// Le code contient l'identifiant du pack ; tout le monde obtient le même TD s'il a le même contenu
/// (l'empreinte, deux mots, permet de le vérifier à l'oral).
/// </summary>
/// <param name="charger">Charge le contenu d'un pack importé (null s'il n'est pas dans la bibliothèque).</param>
public sealed class GenerateurSeance(Func<string, ContenuSeance?> charger) : IGenerateurTd
{
    public string Prefixe => "SEA";
    public string Nom => "Une séance de la bibliothèque";
    public int VersionActuelle => 1;
    public IReadOnlyCollection<int> VersionsPrisesEnCharge { get; } = [1];

    /// <summary>Noms des niveaux : la difficulté maximale des cartes retenues.</summary>
    public static IReadOnlyList<string> NomsNiveaux { get; } = ["Cartes faciles", "Faciles et moyennes", "Toutes les cartes"];

    public Td Generer(ParametresTd p)
    {
        if (p.PackId is not { Length: > 0 } packId)
            throw new TdImpossibleException("Ce code de séance ne contient pas d'identifiant de pack.");
        var contenu = charger(packId)
            ?? throw new TdImpossibleException($"La séance « {packId} » n'est pas dans ta bibliothèque. Importe le même pack que ton camarade, puis réessaie.");

        // Ordre de départ fixe (par identifiant, comparaison ordinale) : il ne doit dépendre ni de la base ni de la langue.
        var candidates = contenu.Cartes
            .Where(c => c.Type is TypeCarte.Exercice or TypeCarte.Qcm && c.Difficulte <= p.Niveau)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
            throw new TdImpossibleException("Cette séance n'a pas de carte « exercice » ou « QCM » à ce niveau. Choisis un niveau plus élevé ou une autre séance.");

        var r = new GenerateurAleatoire(p.GraineInterne);
        r.Melanger(candidates);
        var exercices = candidates.Take(p.Nombre).Select(c => Convertir(c, r, contenu)).ToList();

        return new Td(p, CodeTd.Ecrire(p), contenu.Titre, exercices, EmpreinteTd.Calculer(exercices));
    }

    internal static ExerciceTd Convertir(Carte carte, GenerateurAleatoire r, ContenuSeance contenu)
    {
        var c = carte.Contenu;
        string section = contenu.TitresSections.GetValueOrDefault(carte.SectionId, "");
        string rappel = section.Length > 0 ? $"Notion du cours : **{section}**." : "Relis la section du cours correspondante.";

        if (carte.Type == TypeCarte.Qcm)
        {
            // Choix mélangés avec la graine : dans les packs, la bonne réponse est souvent la première.
            var ordre = Enumerable.Range(0, c.Choix!.Count).ToList();
            r.Melanger(ordre);
            var choix = ordre.Select(i => c.Choix[i]).ToList();
            int bonne = ordre.IndexOf(c.BonneReponse ?? 0);
            return new ExerciceTd("QCM", c.Question ?? "",
                [new QuestionTd("a", "Choisis la bonne réponse.", "réponse", new ReponseChoix(choix, bonne),
                    [Invariant($"Bonne réponse : **{(char)('A' + bonne)}**. {choix[bonne]}"), .. string.IsNullOrWhiteSpace(c.Explication) ? Array.Empty<string>() : [c.Explication!]])],
                rappel, carte.SectionId);
        }

        // Exercice : réponse rédigée, la correction est faite de ses étapes puis de la réponse.
        var etapes = (c.Etapes ?? []).ToList();
        etapes.Add("**Réponse :** " + c.Reponse);
        string indice = etapes.Count > 1 ? rappel + "\n\nPour démarrer : " + etapes[0] : rappel;
        return new ExerciceTd("Exercice", c.Enonce ?? "",
            [new QuestionTd("a", "Cherche, puis compare avec la correction.", "réponse", new ReponseLibre(c.Reponse ?? ""), etapes)],
            indice, carte.SectionId);
    }
}
