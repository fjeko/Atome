using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Revisions.Core.Planning;

/// <summary>Une cellule d'une feuille Excel. Ligne et colonne comptent à partir de 1 (A = 1).</summary>
/// <param name="Texte">Contenu tel qu'affiché (texte, ou chiffres écrits pour un nombre). Jamais vide.</param>
/// <param name="Nombre">Valeur si la cellule contient un nombre (les dates Excel sont des nombres), sinon null.</param>
public sealed record CelluleXlsx(int Ligne, int Colonne, string Texte, double? Nombre);

/// <summary>Une plage de cellules fusionnées (« D7:L7 »), bornes incluses.</summary>
public sealed record FusionXlsx(int Ligne1, int Colonne1, int Ligne2, int Colonne2);

/// <summary>Contenu utile d'une feuille : les cellules non vides et les fusions.</summary>
public sealed class FeuilleXlsx
{
    private readonly Dictionary<(int Ligne, int Colonne), CelluleXlsx> _cellules = [];

    public IReadOnlyCollection<CelluleXlsx> Cellules => _cellules.Values;
    public List<FusionXlsx> Fusions { get; } = [];
    public int DerniereLigne { get; private set; }

    internal void Ajouter(CelluleXlsx c)
    {
        _cellules[(c.Ligne, c.Colonne)] = c;
        DerniereLigne = Math.Max(DerniereLigne, c.Ligne);
    }

    public CelluleXlsx? Cellule(int ligne, int colonne) => _cellules.GetValueOrDefault((ligne, colonne));

    /// <summary>La fusion dont la cellule est le coin haut-gauche, ou null si elle n'est pas fusionnée.</summary>
    public FusionXlsx? FusionDepuis(int ligne, int colonne) =>
        Fusions.FirstOrDefault(f => f.Ligne1 == ligne && f.Colonne1 == colonne);
}

/// <summary>
/// Lecteur minimal de fichiers .xlsx (un .xlsx est un ZIP de fichiers XML) : textes partagés,
/// cellules et plages fusionnées de la première feuille. Pas de dépendance externe, pas de formules,
/// pas de styles : c'est suffisant pour lire la grille de planning du CNED.
/// </summary>
public static class LecteurXlsx
{
    private static readonly XNamespace Principal = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace RelationsPaquet = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Lit la première feuille. Lève <see cref="InvalidDataException"/> si le fichier n'est pas un .xlsx lisible.</summary>
    public static FeuilleXlsx Lire(Stream flux)
    {
        try
        {
            using var zip = new ZipArchive(flux, ZipArchiveMode.Read, leaveOpen: true);
            var textes = LireTextesPartages(zip);
            string chemin = CheminPremiereFeuille(zip);
            var entree = zip.GetEntry(chemin) ?? throw new InvalidDataException("La feuille de calcul est introuvable dans le fichier.");
            using var lecture = entree.Open();
            return LireFeuille(XDocument.Load(lecture), textes);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException)
        {
            throw new InvalidDataException("Ce fichier n'est pas un classeur Excel (.xlsx) lisible.", e);
        }
    }

    private static List<string> LireTextesPartages(ZipArchive zip)
    {
        var textes = new List<string>();
        var entree = zip.GetEntry("xl/sharedStrings.xml");
        if (entree is null) return textes;
        using var lecture = entree.Open();
        foreach (var si in XDocument.Load(lecture).Descendants(Principal + "si"))
            textes.Add(TexteRiche(si));
        return textes;
    }

    /// <summary>Texte d'un élément « si » ou « is » : concatène les morceaux « t » (hors prononciation phonétique).</summary>
    private static string TexteRiche(XElement e) =>
        string.Concat(e.Descendants(Principal + "t").Where(t => t.Parent?.Name != Principal + "rPh").Select(t => t.Value));

    /// <summary>Chemin (dans le ZIP) de la première feuille du classeur, lue dans workbook.xml puis ses relations.</summary>
    private static string CheminPremiereFeuille(ZipArchive zip)
    {
        const string parDefaut = "xl/worksheets/sheet1.xml";
        var classeur = zip.GetEntry("xl/workbook.xml");
        var relations = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (classeur is null || relations is null) return parDefaut;

        string? id;
        using (var s = classeur.Open())
            id = XDocument.Load(s).Descendants(Principal + "sheet").FirstOrDefault()?.Attribute(Relations + "id")?.Value;
        if (id is null) return parDefaut;

        using var r = relations.Open();
        string? cible = XDocument.Load(r).Descendants(RelationsPaquet + "Relationship")
            .FirstOrDefault(x => x.Attribute("Id")?.Value == id)?.Attribute("Target")?.Value;
        if (cible is null) return parDefaut;
        return cible.StartsWith('/') ? cible.TrimStart('/') : "xl/" + cible;
    }

    private static FeuilleXlsx LireFeuille(XDocument document, List<string> textes)
    {
        var feuille = new FeuilleXlsx();
        foreach (var c in document.Descendants(Principal + "c"))
        {
            var reference = c.Attribute("r")?.Value;
            if (reference is null || !TryReference(reference, out int ligne, out int colonne)) continue;

            string? type = c.Attribute("t")?.Value;
            string? valeur = c.Element(Principal + "v")?.Value;
            string? texte;
            double? nombre = null;
            switch (type)
            {
                case "s":
                    texte = int.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < textes.Count ? textes[i] : null;
                    break;
                case "inlineStr":
                    texte = c.Element(Principal + "is") is { } inline ? TexteRiche(inline) : null;
                    break;
                case "str" or "e":
                    texte = valeur;
                    break;
                case "b":
                    texte = valeur;
                    break;
                default: // nombre (ou date : un nombre de jours depuis 1899-12-30)
                    texte = valeur;
                    if (valeur is not null && double.TryParse(valeur, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) nombre = d;
                    break;
            }
            if (!string.IsNullOrWhiteSpace(texte)) feuille.Ajouter(new CelluleXlsx(ligne, colonne, texte.Trim(), nombre));
        }

        foreach (var m in document.Descendants(Principal + "mergeCell"))
        {
            string? reference = m.Attribute("ref")?.Value;
            string[]? bornes = reference?.Split(':');
            if (bornes is { Length: 2 } && TryReference(bornes[0], out int l1, out int c1) && TryReference(bornes[1], out int l2, out int c2))
                feuille.Fusions.Add(new FusionXlsx(l1, c1, l2, c2));
        }
        return feuille;
    }

    /// <summary>« AB12 » → ligne 12, colonne 28.</summary>
    public static bool TryReference(string reference, out int ligne, out int colonne)
    {
        ligne = colonne = 0;
        int i = 0;
        while (i < reference.Length && char.IsAsciiLetter(reference[i]))
            colonne = colonne * 26 + (char.ToUpperInvariant(reference[i++]) - 'A' + 1);
        return i > 0 && i < reference.Length &&
               int.TryParse(reference.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out ligne);
    }

    /// <summary>Colonne 28 → « AB ».</summary>
    public static string NomColonne(int colonne)
    {
        string nom = "";
        for (; colonne > 0; colonne = (colonne - 1) / 26) nom = (char)('A' + (colonne - 1) % 26) + nom;
        return nom;
    }
}
