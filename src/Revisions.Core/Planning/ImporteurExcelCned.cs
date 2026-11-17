using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Revisions.Core.Temps;

namespace Revisions.Core.Planning;

/// <summary>Résultat de la conversion d'un Excel « Proposition de planning » du CNED.</summary>
/// <param name="Json">Le planning au format de planning/FORMAT.md (prêt pour l'import de planning), ou null en cas d'erreur.</param>
/// <param name="Planning">Le même planning, lu et validé par <see cref="LecteurPlanning"/>.</param>
/// <param name="Erreurs">Ce qui empêche l'import.</param>
/// <param name="Avertissements">Points à vérifier : fins déduites, libellés non reconnus, correction de coquille…</param>
public sealed record ResultatExcelCned(
    string? Json,
    PlanningCned? Planning,
    IReadOnlyList<string> Erreurs,
    IReadOnlyList<string> Avertissements)
{
    public bool Valide => Planning is not null;
}

/// <summary>
/// Convertit la grille Excel « Proposition de planning » du CNED en planning Atome.
/// <para>
/// Structure de la grille : une seule feuille ; une ligne de dates (le lundi de chaque semaine, en numéro de série
/// Excel) ; une cellule fusionnée en colonne A par matière, sur 2 ou 3 lignes ; sur ces lignes, des cellules de période
/// (souvent fusionnées sur plusieurs semaines) et une ligne de codes QZ1, DV1… placés dans la colonne de leur semaine.
/// </para>
/// <para>
/// Ce que la grille ne dit pas est déduit et signalé : une période non fusionnée dure jusqu'à la veille de la période
/// suivante de la même ligne, ou jusqu'à la semaine qui précède le devoir suivant (<c>approx</c>).
/// </para>
/// </summary>
public static partial class ImporteurExcelCned
{
    /// <summary>Premier jour des numéros de série Excel (le jour 1 est le 1899-12-31, mais Excel compte un 29/02/1900 qui n'existe pas).</summary>
    private static readonly DateOnly OrigineExcel = new(1899, 12, 30);

    private const int ColonneNom = 1;       // A : matière
    private const int ColonneDevoirs = 2;   // B : nombre de devoirs
    private const int PremiereSemaine = 3;  // C : première colonne de semaine

    /// <summary>Séquences de CEJM couvertes par chaque thème (confirmé par l'étudiant le 17/11/2026).</summary>
    private static readonly Dictionary<int, (int Debut, int Fin)> ThemesCejm = new()
    {
        [1] = (1, 6), [2] = (7, 9), [3] = (10, 14),
    };

    /// <summary>« Thème 4 » et « Thème 5 » du fichier du CNED sont les thèmes 2 et 3 (« Thème 2 » et « Thème 3 » restent valables).</summary>
    private static readonly Dictionary<int, int> NumeroThemeCejm = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 2, [5] = 3 };

    // ---- Point d'entrée -------------------------------------------------------------------------

    /// <summary>
    /// Convertit un classeur .xlsx. <paramref name="nomFichier"/> donne l'id du planning
    /// (« Proposition-planning-1ASISR-2026-2027.xlsx » → « cned-1a-sisr-2026-2027 »).
    /// </summary>
    public static ResultatExcelCned Convertir(Stream xlsx, string nomFichier, DateOnly aujourdhui)
    {
        FeuilleXlsx feuille;
        try { feuille = LecteurXlsx.Lire(xlsx); }
        catch (InvalidDataException e) { return Echec(e.Message); }

        var avertissements = new List<string>();
        var semaines = LireSemaines(feuille, avertissements);
        if (semaines.Count == 0)
            return Echec("La ligne des dates (les lundis des semaines) est introuvable : ce fichier n'a pas la mise en page de la proposition de planning du CNED.");

        var periodes = new List<PeriodeExcel>();
        var devoirs = new List<DevoirExcel>();
        (DateOnly? Possible, DateOnly? Souhaitable) stage = (null, null);
        LireMatieres(feuille, semaines, periodes, devoirs, ref stage, avertissements);

        if (periodes.Count == 0 && devoirs.Count == 0)
            return Echec("Aucune matière n'a été reconnue dans ce fichier : ce n'est pas la grille de la proposition de planning du CNED.");

        string id = IdDepuisNomDeFichier(nomFichier);
        string json = EcrireJson(id, TitreDepuisId(id, nomFichier), $"{Path.GetFileName(nomFichier)} (CNED), importé le {aujourdhui:dd/MM/yyyy}",
            periodes, devoirs, stage);

        var lecture = LecteurPlanning.Lire(json);
        if (lecture.Planning is null) return new ResultatExcelCned(null, null, lecture.Erreurs, avertissements);
        return new ResultatExcelCned(json, lecture.Planning, [], [.. avertissements, .. lecture.Avertissements]);

        static ResultatExcelCned Echec(string message) => new(null, null, [message], []);
    }

    // ---- Identifiant et titre -------------------------------------------------------------------

    /// <summary>
    /// Id du planning déduit du nom du fichier : « Proposition-planning-1ASISR-2026-2027(1).xlsx » → « cned-1a-sisr-2026-2027 ».
    /// Réimporter une nouvelle version du même fichier garde donc le même id (et les dates personnelles).
    /// </summary>
    public static string IdDepuisNomDeFichier(string nomFichier)
    {
        string nom = Path.GetFileNameWithoutExtension(nomFichier);
        nom = CopieWindows().Replace(nom, "");                     // « (1) » ajouté par le navigateur
        string s = SansAccents(nom).ToLowerInvariant();
        s = DebutProposition().Replace(s, "");                     // « proposition-planning- »
        s = AnneeOption().Replace(s, "$1$2-$3");                   // 1aslam → 1a-slam
        s = NonAlphanumerique().Replace(s, "-").Trim('-');
        if (s.Length == 0) s = "planning";
        return s.StartsWith("cned-", StringComparison.Ordinal) ? s : "cned-" + s;
    }

    private static string TitreDepuisId(string id, string nomFichier)
    {
        var m = IdAnneeOption().Match(id);
        if (!m.Success) return "Proposition de planning CNED – " + Path.GetFileNameWithoutExtension(nomFichier);
        int annee = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        string rang = annee == 1 ? "1re" : annee + "e";
        return $"Proposition de planning CNED – {rang} année BTS SIO {m.Groups[2].Value.ToUpperInvariant()} {m.Groups[3].Value}-{m.Groups[4].Value}";
    }

    // ---- Lecture de la grille -------------------------------------------------------------------

    /// <summary>Colonne → lundi de la semaine, lu sur la ligne qui contient le plus de numéros de série de dates.</summary>
    private static Dictionary<int, DateOnly> LireSemaines(FeuilleXlsx feuille, List<string> avertissements)
    {
        var parLigne = feuille.Cellules
            .Where(c => c.Colonne >= PremiereSemaine && c.Ligne <= 10 && c.Nombre is >= 36000 and <= 80000)
            .GroupBy(c => c.Ligne).OrderByDescending(g => g.Count()).FirstOrDefault();
        var semaines = new Dictionary<int, DateOnly>();
        if (parLigne is null || parLigne.Count() < 4) return semaines;

        foreach (var c in parLigne)
        {
            var jour = OrigineExcel.AddDays((int)c.Nombre!.Value);
            if (jour.DayOfWeek != DayOfWeek.Monday)
            {
                avertissements.Add($"La date de la colonne {LecteurXlsx.NomColonne(c.Colonne)} ({jour:dd/MM/yyyy}) n'est pas un lundi : elle est ramenée au lundi.");
                jour = JourDeRevision.LundiDe(jour);
            }
            semaines[c.Colonne] = jour;
        }
        return semaines;
    }

    /// <summary>Genre d'une matière : sert à choisir la façon de lire ses libellés.</summary>
    private enum Genre { Bloc, Culture, Anglais, Maths, Algo, Cejm, Ateliers, Stage, Inconnu }

    private sealed record PeriodeExcel(string Matiere, string Libelle, DateOnly Debut, DateOnly Fin, bool Approx, bool Continu, List<string> Cibles);
    private sealed record DevoirExcel(string Matiere, string Code, bool Note, DateOnly Semaine, string? Remarque);
    private sealed record CellulePeriode(int Ligne, int Colonne, int ColonneFin, bool Fusionnee, string Texte);

    private static void LireMatieres(FeuilleXlsx feuille, Dictionary<int, DateOnly> semaines,
        List<PeriodeExcel> periodes, List<DevoirExcel> devoirs, ref (DateOnly? Possible, DateOnly? Souhaitable) stage, List<string> avertissements)
    {
        int ligne = 1;
        bool themeCejmRenumerote = false;
        while (ligne <= feuille.DerniereLigne)
        {
            var nomCellule = feuille.Cellule(ligne, ColonneNom);
            var fusion = feuille.FusionDepuis(ligne, ColonneNom);
            int derniere = fusion?.Ligne2 ?? ligne;
            int ligneCourante = ligne;
            ligne = Math.Max(derniere, ligne) + 1;
            if (nomCellule is null || nomCellule.Nombre is not null) continue;
            if (fusion is not null && fusion.Colonne2 > ColonneDevoirs) continue;   // bandeau ou note en bas de page

            string nomBrut = Nettoyer(nomCellule.Texte);
            string n = Normaliser(nomBrut);
            if (n.StartsWith("matieres") || n.StartsWith("nombre de devoirs") || n.StartsWith("qz=") || n.StartsWith("dv=") || n.StartsWith('*'))
                continue;

            // Cellules de la matière : tout ce qui est à droite de la colonne B sur ses lignes.
            var periodesDeLaMatiere = new List<CellulePeriode>();
            var codes = new List<(int Colonne, string Code)>();
            for (int l = ligneCourante; l <= derniere; l++)
                foreach (var c in feuille.Cellules.Where(c => c.Ligne == l && c.Colonne >= PremiereSemaine).OrderBy(c => c.Colonne))
                {
                    string texte = Nettoyer(c.Texte);
                    if (CodeDevoir().IsMatch(texte)) { codes.Add((c.Colonne, texte.Replace(" ", "").ToUpperInvariant())); continue; }
                    var f = feuille.FusionDepuis(l, c.Colonne);
                    periodesDeLaMatiere.Add(new CellulePeriode(l, c.Colonne, f?.Colonne2 ?? c.Colonne, f is not null && f.Colonne2 > c.Colonne, texte));
                }

            var (genre, matiere) = Identifier(n, nomBrut);
            if (genre == Genre.Stage)
            {
                foreach (var p in periodesDeLaMatiere)
                {
                    string t = Normaliser(p.Texte);
                    if (!semaines.TryGetValue(p.Colonne, out var debut)) continue;
                    if (t.Contains("possible")) stage.Possible = debut;
                    else if (t.Contains("souhaitable")) stage.Souhaitable = debut;
                }
                continue;
            }
            if (periodesDeLaMatiere.Count == 0 && codes.Count == 0) continue;
            if (genre == Genre.Inconnu)
                avertissements.Add($"Matière « {matiere} » non reconnue : ses périodes sont affichées, sans cartes.");
            bool sisr = n.Contains("sisr");

            foreach (var p in periodesDeLaMatiere.OrderBy(p => p.Colonne).ThenBy(p => p.Ligne))
            {
                if (!semaines.TryGetValue(p.Colonne, out var debut))
                {
                    avertissements.Add($"« {matiere} – {p.Texte} » : pas de date pour la colonne {LecteurXlsx.NomColonne(p.Colonne)}, période ignorée.");
                    continue;
                }
                var (fin, approx) = FinDePeriode(p, periodesDeLaMatiere, codes, semaines);
                var lib = LireLibelle(genre, p.Texte, sisr);
                if (lib.Avertissement is not null) avertissements.Add($"« {matiere} – {p.Texte} » : {lib.Avertissement}");
                if (lib.ThemeRenumerote) themeCejmRenumerote = true;
                if (approx)
                    avertissements.Add($"« {matiere} – {lib.Libelle} » : la fin ({fin:dd/MM/yyyy}) est déduite (cellule non fusionnée), à confirmer.");
                periodes.Add(new PeriodeExcel(matiere, lib.Libelle, debut, fin, approx, lib.Continu, lib.Cibles));
            }

            // Libellés en double dans une matière : on les distingue (le couple matière + libellé doit être unique).
            foreach (var groupe in periodes.Where(p => p.Matiere == matiere).GroupBy(p => p.Libelle).Where(g => g.Count() > 1).ToList())
            {
                int k = 0;
                foreach (var p in groupe)
                {
                    if (++k == 1) continue;
                    periodes[periodes.IndexOf(p)] = p with { Libelle = $"{p.Libelle} ({k})" };
                }
            }

            LireDevoirs(matiere, codes, semaines, devoirs, avertissements,
                feuille.Cellule(ligneCourante, ColonneDevoirs) is { Nombre: { } attendus } ? (int)attendus : null);
        }

        if (themeCejmRenumerote)
            avertissements.Add("CEJM : « Thème 4 » et « Thème 5 » du fichier sont lus comme les thèmes 2 et 3 (séquences 7 à 9 et 10 à 14).");
    }

    /// <summary>
    /// Fin d'une période. Cellule fusionnée : le dimanche de sa dernière semaine. Sinon, fin déduite : la veille de la
    /// période suivante de la même ligne, ou la veille du devoir suivant ; <c>approx</c> si cela dépasse sa propre semaine.
    /// </summary>
    private static (DateOnly Fin, bool Approx) FinDePeriode(CellulePeriode p, List<CellulePeriode> toutes,
        List<(int Colonne, string Code)> codes, Dictionary<int, DateOnly> semaines)
    {
        var propreFin = semaines[p.Colonne].AddDays(6);
        if (p.Fusionnee)
        {
            if (semaines.TryGetValue(p.ColonneFin, out var derniere)) return (derniere.AddDays(6), false);
            // La fusion dépasse les dates connues : on prend la dernière semaine connue.
            var derniereConnue = semaines.Where(s => s.Key <= p.ColonneFin).MaxBy(s => s.Key).Value;
            return (derniereConnue.AddDays(6), true);
        }

        var suivantes = toutes.Where(x => x.Ligne == p.Ligne && x.Colonne > p.Colonne).Select(x => x.Colonne).ToList();
        if (suivantes.Count > 0 && semaines.TryGetValue(suivantes.Min(), out var debutSuivante))
            return (debutSuivante.AddDays(-1), debutSuivante.AddDays(-1) > propreFin);

        var devoirsApres = codes.Where(c => c.Colonne > p.Colonne && semaines.ContainsKey(c.Colonne)).Select(c => c.Colonne).ToList();
        if (devoirsApres.Count > 0)
        {
            var veilleDevoir = semaines[devoirsApres.Min()].AddDays(-1);
            return veilleDevoir >= propreFin ? (veilleDevoir, veilleDevoir > propreFin) : (propreFin, true);
        }
        return (propreFin, true);
    }

    /// <summary>
    /// Devoirs d'une matière. Si la numérotation d'un type a un trou (« DV1, DV3 » : coquille du CNED),
    /// elle est reprise dans l'ordre (« DV1, DV2 ») et signalée.
    /// </summary>
    private static void LireDevoirs(string matiere, List<(int Colonne, string Code)> codes, Dictionary<int, DateOnly> semaines,
        List<DevoirExcel> devoirs, List<string> avertissements, int? attendus)
    {
        var ajoutes = new List<DevoirExcel>();
        foreach (string type in new[] { "QZ", "DV" })
        {
            var duType = codes.Where(c => c.Code.StartsWith(type, StringComparison.Ordinal) && semaines.ContainsKey(c.Colonne))
                .OrderBy(c => c.Colonne).ToList();
            bool trou = duType.Select(c => int.Parse(c.Code[2..], CultureInfo.InvariantCulture)).SequenceEqual(Enumerable.Range(1, duType.Count)) == false;
            for (int i = 0; i < duType.Count; i++)
            {
                string code = type + (i + 1).ToString(CultureInfo.InvariantCulture);
                string? remarque = null;
                if (trou && duType[i].Code != code)
                {
                    remarque = $"Noté « {duType[i].Code} » dans le fichier du CNED, probablement une coquille.";
                    avertissements.Add($"{matiere} : « {duType[i].Code} » est lu comme {code} (numérotation avec un trou dans le fichier).");
                }
                ajoutes.Add(new DevoirExcel(matiere, code, type == "DV", semaines[duType[i].Colonne], remarque));
            }
        }
        devoirs.AddRange(ajoutes.OrderBy(d => d.Semaine).ThenBy(d => d.Code));

        int notes = ajoutes.Count(d => d.Note);
        if (attendus is { } a && a != notes)
            avertissements.Add($"{matiere} : le fichier annonce {a} devoir(s) noté(s) et en contient {notes}.");
    }

    // ---- Matières et libellés -------------------------------------------------------------------

    /// <summary>Reconnaît la matière à partir du texte de la colonne A et lui donne son nom dans le planning.</summary>
    private static (Genre Genre, string Nom) Identifier(string n, string brut)
    {
        var bloc = BlocCompetences().Match(n);
        if (bloc.Success) return (Genre.Bloc, "Bloc " + bloc.Groups[1].Value);
        if (n.StartsWith("stage")) return (Genre.Stage, "Stage");
        if (n.Contains("culture economique") || n.Contains("cejm")) return (Genre.Cejm, "CEJM");
        if (n.Contains("culture generale")) return (Genre.Culture, "Bloc 4 – Culture générale");
        if (n.Contains("anglais")) return (Genre.Anglais, "Anglais");
        if (n.Contains("algorithmique")) return (Genre.Algo, "Algorithmique");
        if (n.Contains("mathematiques")) return (Genre.Maths, "Mathématiques");
        if (n.Contains("atelier")) return (Genre.Ateliers, "Ateliers de professionnalisation");
        return (Genre.Inconnu, brut);
    }

    private sealed record LibelleLu(string Libelle, List<string> Cibles, bool Continu = false, string? Avertissement = null, bool ThemeRenumerote = false);

    /// <summary>
    /// Libellé affiché et préfixes d'id de packs d'une période.
    /// « B1.2-PARTIE II-SQ1-3 » → « B1.2 Partie II – SQ1 à SQ3 » et b1-b12p2-sq1, sq2, sq3.
    /// Ce qui n'a pas de pack (culture générale, ateliers, option SISR) donne une liste vide : la période s'affiche, sans cartes.
    /// </summary>
    private static LibelleLu LireLibelle(Genre genre, string texte, bool sisr)
    {
        string propre = Nettoyer(texte);
        switch (genre)
        {
            case Genre.Bloc:
            {
                var m = CodeBloc().Match(propre);
                if (!m.Success) return new LibelleLu(propre, [], Avertissement: "libellé non reconnu, période sans cartes.");
                string bloc = m.Groups[1].Value, comp = m.Groups[2].Value;
                string code = $"B{bloc}.{comp}";
                string prefixe = $"b{bloc}-b{bloc}{comp}";
                string libelle = code;

                var partie = PartieRomaine().Match(propre);
                if (partie.Success)
                {
                    string romain = partie.Groups[1].Value.ToUpperInvariant();
                    int numero = romain.All(ch => ch == 'I') ? romain.Length : int.TryParse(romain, out int num) ? num : 0;
                    if (numero > 0)
                    {
                        prefixe += "p" + numero.ToString(CultureInfo.InvariantCulture);
                        libelle += " Partie " + romain;
                    }
                }

                var sequences = Sequences(propre, out string texteSq);
                if (texteSq.Length > 0) libelle += " – " + texteSq;
                if (sisr) return new LibelleLu(libelle + " – SISR", []);   // pas de cours en option SISR
                return new LibelleLu(libelle, sequences.Count == 0 ? [prefixe] : sequences.Select(k => $"{prefixe}-sq{k}").ToList());
            }
            case Genre.Culture or Genre.Ateliers:
                // « Module 1 - Activité 1 », « Module 1 -Activité 2 » → « Module 1 Activité 2 »
                return new LibelleLu(ModuleTiret().Replace(propre, "$1 "), []);
            case Genre.Anglais:
            {
                var h = HeuresAnglais().Match(propre);
                return h.Success
                    ? new LibelleLu($"Altissia B2 ({h.Groups[1].Value} h, en continu)", ["ang"], Continu: true)
                    : new LibelleLu(propre, ["ang"]);
            }
            case Genre.Maths or Genre.Algo:
            {
                var m = SequenceNumero().Match(propre);
                if (!m.Success) return new LibelleLu(propre, [], Avertissement: "séquence non reconnue, période sans cartes.");
                string numero = m.Groups[1].Value;
                return new LibelleLu($"Séquence {numero}", [$"{(genre == Genre.Maths ? "maths" : "algo")}-sq{numero}"]);
            }
            case Genre.Cejm:
            {
                var m = ThemeNumero().Match(propre);
                if (!m.Success || !NumeroThemeCejm.TryGetValue(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), out int theme))
                    return new LibelleLu(propre, [], Avertissement: "thème non reconnu, période sans cartes.");
                var (debut, fin) = ThemesCejm[theme];
                var cibles = Enumerable.Range(debut, fin - debut + 1).Select(k => $"cejm-sq{k:00}").ToList();
                return new LibelleLu($"Thème {theme} (séquences {debut} à {fin})", cibles,
                    ThemeRenumerote: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) != theme);
            }
            default:
                return new LibelleLu(propre, []);
        }
    }

    /// <summary>« SQ1-3 » → 1, 2, 3 ; « SQ1 et SQ2 » → 1, 2 ; « SQ4 » → 4. Renvoie aussi le texte affiché (« SQ1 à SQ3 »).</summary>
    private static List<int> Sequences(string texte, out string affiche)
    {
        var m = PlageSequences().Match(texte);
        affiche = "";
        if (!m.Success) return [];
        int a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (!m.Groups[3].Success) { affiche = $"SQ{a}"; return [a]; }
        int b = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        bool liste = m.Groups[2].Value.Equals("et", StringComparison.OrdinalIgnoreCase);
        if (b < a) (a, b) = (b, a);
        if (liste)
        {
            affiche = $"SQ{a} et SQ{b}";
            return a == b ? [a] : [a, b];
        }
        affiche = b == a + 1 ? $"SQ{a} et SQ{b}" : $"SQ{a} à SQ{b}";
        return Enumerable.Range(a, b - a + 1).ToList();
    }

    // ---- Écriture du JSON -----------------------------------------------------------------------

    private static string EcrireJson(string id, string titre, string source, List<PeriodeExcel> periodes, List<DevoirExcel> devoirs,
        (DateOnly? Possible, DateOnly? Souhaitable) stage)
    {
        using var flux = new MemoryStream();
        using (var w = new Utf8JsonWriter(flux, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", 1);
            w.WriteString("id", id);
            w.WriteString("titre", titre);
            w.WriteString("source", source);
            w.WriteStartArray("remarques");
            w.WriteStringValue("Planning produit par l'import direct de l'Excel du CNED. Il sert de repère, jamais de reproche.");
            w.WriteStringValue("approx = true : fin de période déduite (cellule non fusionnée dans le fichier Excel).");
            w.WriteEndArray();

            w.WriteStartArray("periodes");
            foreach (var p in periodes.OrderBy(p => MatiereOrdre(periodes, p.Matiere)).ThenBy(p => p.Debut).ThenBy(p => p.Fin))
            {
                w.WriteStartObject();
                w.WriteString("matiere", p.Matiere);
                w.WriteString("libelle", p.Libelle);
                w.WriteString("debut", Iso(p.Debut));
                w.WriteString("fin", Iso(p.Fin));
                if (p.Approx) w.WriteBoolean("approx", true);
                if (p.Continu) w.WriteBoolean("continu", true);
                w.WriteStartArray("cible");
                foreach (string c in p.Cibles) w.WriteStringValue(c);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("devoirs");
            foreach (var d in devoirs.OrderBy(d => MatiereOrdre(periodes, d.Matiere, devoirs)))
            {
                w.WriteStartObject();
                w.WriteString("matiere", d.Matiere);
                w.WriteString("code", d.Code);
                w.WriteBoolean("note", d.Note);
                w.WriteString("semaine", Iso(d.Semaine));
                if (d.Remarque is not null) w.WriteString("remarque", d.Remarque);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (stage.Possible is not null || stage.Souhaitable is not null)
            {
                w.WriteStartObject("stage");
                if (stage.Possible is { } p) w.WriteString("possible", Iso(p));
                if (stage.Souhaitable is { } s) w.WriteString("souhaitable", Iso(s));
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(flux.ToArray());
    }

    /// <summary>Ordre des matières : celui du fichier (première apparition dans les périodes, sinon dans les devoirs).</summary>
    private static int MatiereOrdre(List<PeriodeExcel> periodes, string matiere, List<DevoirExcel>? devoirs = null) =>
        periodes.Select(p => p.Matiere).Concat(devoirs?.Select(d => d.Matiere) ?? []).Distinct().ToList().IndexOf(matiere);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- Texte ----------------------------------------------------------------------------------

    /// <summary>Espaces insécables et multiples retirés, astérisques de notes de bas de page retirés.</summary>
    private static string Nettoyer(string texte) =>
        Espaces().Replace(texte.Replace(' ', ' ').Replace("*", ""), " ").Trim();

    private static string Normaliser(string texte) => SansAccents(Nettoyer(texte)).ToLowerInvariant();

    private static string SansAccents(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (char c in texte.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"^(QZ|DV)\s*\d+$", RegexOptions.IgnoreCase)] private static partial Regex CodeDevoir();
    [GeneratedRegex(@"bloc de competences\s*(\d)")] private static partial Regex BlocCompetences();
    [GeneratedRegex(@"B\s*(\d)\s*\.\s*(\d)", RegexOptions.IgnoreCase)] private static partial Regex CodeBloc();
    [GeneratedRegex(@"PARTIE\s*(I{1,3}|\d)\b", RegexOptions.IgnoreCase)] private static partial Regex PartieRomaine();
    [GeneratedRegex(@"SQ\s*(\d+)(?:\s*(-|–|à|a|et)\s*(?:SQ\s*)?(\d+))?", RegexOptions.IgnoreCase)] private static partial Regex PlageSequences();
    [GeneratedRegex(@"(Module\s*\d+)\s*-\s*", RegexOptions.IgnoreCase)] private static partial Regex ModuleTiret();
    [GeneratedRegex(@"^(\d+)\s*h", RegexOptions.IgnoreCase)] private static partial Regex HeuresAnglais();
    [GeneratedRegex(@"(?:S[ée]quence|SQ)\s*(\d+)", RegexOptions.IgnoreCase)] private static partial Regex SequenceNumero();
    [GeneratedRegex(@"Th[èe]me\s*(\d+)", RegexOptions.IgnoreCase)] private static partial Regex ThemeNumero();
    [GeneratedRegex(@"\s+")] private static partial Regex Espaces();
    [GeneratedRegex(@"\s*\(\d+\)\s*$")] private static partial Regex CopieWindows();
    [GeneratedRegex(@"^proposition[\s_-]*(de[\s_-]*)?planning[\s_-]*")] private static partial Regex DebutProposition();
    [GeneratedRegex(@"(\d)(a)(slam|sisr)")] private static partial Regex AnneeOption();
    [GeneratedRegex(@"^cned-(\d)a-(slam|sisr)-(\d{4})-(\d{4})$")] private static partial Regex IdAnneeOption();
    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonAlphanumerique();
}
