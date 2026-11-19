using System.Globalization;
using System.Numerics;

namespace Revisions.Core.Calculatrice;

// =====================================================================================================
// Analyseur d'expressions fait maison, en trois temps :
//   1. le découpage en « jetons » (nombres, noms, opérateurs)  → Lexeur
//   2. la construction d'un arbre selon les priorités          → Analyseur (descente récursive)
//   3. le calcul de l'arbre                                    → Evaluateur
// Priorités, de la plus faible à la plus forte :  + -   <   * / (et multiplication implicite)   <   - unaire   <   ^
// =====================================================================================================

internal enum GenreJeton { Nombre, Nom, Operateur, ParG, ParD, Fin }

internal readonly record struct Jeton(GenreJeton Genre, string Texte, int Debut, int Fin);

/// <summary>Étape 1 : découpe le texte en jetons.</summary>
internal static class Lexeur
{
    /// <summary>Noms reconnus de plus d'une lettre (la casse est ignorée).</summary>
    internal static readonly HashSet<string> NomsConnus = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqrt", "ln", "log", "exp", "abs", "det", "inv", "trans", "pi", "ans",
    };

    public static List<Jeton> Decouper(string texte)
    {
        var jetons = new List<Jeton>();
        int i = 0;
        while (i < texte.Length)
        {
            char c = texte[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsAsciiDigit(c) || ((c == ',' || c == '.') && i + 1 < texte.Length && char.IsAsciiDigit(texte[i + 1])))
            {
                int debut = i;
                while (i < texte.Length && char.IsAsciiDigit(texte[i])) i++;
                if (i < texte.Length && (texte[i] == ',' || texte[i] == '.'))
                {
                    i++;
                    while (i < texte.Length && char.IsAsciiDigit(texte[i])) i++;
                }
                jetons.Add(new Jeton(GenreJeton.Nombre, texte[debut..i], debut, i));
                continue;
            }

            if (char.IsLetter(c))
            {
                int debut = i;
                while (i < texte.Length && char.IsLetter(texte[i])) i++;
                string mot = texte[debut..i];
                // « I3 » : la matrice identité, la lettre I suivie de sa taille.
                if (mot == "I" && i < texte.Length && char.IsAsciiDigit(texte[i]))
                {
                    while (i < texte.Length && char.IsAsciiDigit(texte[i])) i++;
                    jetons.Add(new Jeton(GenreJeton.Nom, texte[debut..i], debut, i));
                    continue;
                }
                AjouterMot(jetons, mot, debut, texte);
                continue;
            }

            int pos = i++;
            switch (c)
            {
                case '+': case '-': case '*': case '/': case '^':
                    jetons.Add(new Jeton(GenreJeton.Operateur, c.ToString(), pos, i)); break;
                case '×': case '·':
                    jetons.Add(new Jeton(GenreJeton.Operateur, "*", pos, i)); break;
                case '÷':
                    jetons.Add(new Jeton(GenreJeton.Operateur, "/", pos, i)); break;
                case '−': case '–':
                    jetons.Add(new Jeton(GenreJeton.Operateur, "-", pos, i)); break;
                case '(': jetons.Add(new Jeton(GenreJeton.ParG, "(", pos, i)); break;
                case ')': jetons.Add(new Jeton(GenreJeton.ParD, ")", pos, i)); break;
                case '√': jetons.Add(new Jeton(GenreJeton.Nom, "sqrt", pos, i)); break;
                case 'π': jetons.Add(new Jeton(GenreJeton.Nom, "pi", pos, i)); break;
                case '²':
                    jetons.Add(new Jeton(GenreJeton.Operateur, "^", pos, i));
                    jetons.Add(new Jeton(GenreJeton.Nombre, "2", pos, i)); break;
                case '³':
                    jetons.Add(new Jeton(GenreJeton.Operateur, "^", pos, i));
                    jetons.Add(new Jeton(GenreJeton.Nombre, "3", pos, i)); break;
                default:
                    throw new ErreurCalcul($"Je ne comprends pas le caractère « {c} » (position {pos + 1}).");
            }
        }
        jetons.Add(new Jeton(GenreJeton.Fin, "", texte.Length, texte.Length));
        return jetons;
    }

    /// <summary>
    /// Un mot de lettres : un nom connu (sqrt, pi…), la constante e, une matrice (une majuscule),
    /// ou plusieurs majuscules collées (« AB » = A × B).
    /// </summary>
    private static void AjouterMot(List<Jeton> jetons, string mot, int debut, string texte)
    {
        // On avance dans le mot : un nom connu (« piA » = pi × A, « 2sqrt »…), la constante e, ou une lettre majuscule = une matrice.
        int k = 0;
        while (k < mot.Length)
        {
            string? nom = NomA(mot, k);
            if (nom is not null)
            {
                jetons.Add(new Jeton(GenreJeton.Nom, nom, debut + k, debut + k + nom.Length));
                k += nom.Length;
            }
            else if (mot[k] == 'e' || char.IsAsciiLetterUpper(mot[k]))
            {
                jetons.Add(new Jeton(GenreJeton.Nom, mot[k].ToString(), debut + k, debut + k + 1));
                k++;
            }
            else
                throw new ErreurCalcul(
                    $"« {mot[k..]} » n'est pas connu. Les fonctions sont sqrt, ln, log, exp, abs, det, inv et trans ; " +
                    "les matrices s'écrivent avec une lettre majuscule (A, B, C…).");
        }
    }

    /// <summary>
    /// Nom connu qui commence en position <paramref name="k"/> : en minuscules (« sqrt »), « Ans »,
    /// ou tout en majuscules pour les noms d'au moins trois lettres (« SQRT » avec le verrouillage majuscules).
    /// Les noms de deux lettres (ln, pi) restent en minuscules : « LN » serait les matrices L et N.
    /// </summary>
    private static string? NomA(string mot, int k)
    {
        foreach (string nom in NomsConnus.OrderByDescending(n => n.Length))
        {
            if (k + nom.Length > mot.Length) continue;
            var morceau = mot.AsSpan(k, nom.Length);
            if (!morceau.Equals(nom, StringComparison.OrdinalIgnoreCase)) continue;
            bool minuscules = morceau.SequenceEqual(nom);
            bool ans = morceau.SequenceEqual("Ans");
            bool majuscules = nom.Length >= 3 && morceau.SequenceEqual(nom.ToUpperInvariant());
            if (minuscules || ans || majuscules) return nom;
        }
        return null;
    }
}

// ---- Étape 2 : l'arbre -------------------------------------------------------------------------------

internal abstract record Noeud(int Debut, int Fin);
internal sealed record NNombre(Rationnel Valeur, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NConstante(string Nom, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NAns(int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NMatrice(string Nom, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NIdentite(int Taille, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NUnaire(char Op, Noeud Operande, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NBinaire(char Op, Noeud Gauche, Noeud Droite, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NAppel(string Fonction, Noeud Argument, int Debut, int Fin) : Noeud(Debut, Fin);
internal sealed record NTransposee(Noeud Operande, int Debut, int Fin) : Noeud(Debut, Fin);

/// <summary>Étape 2 : transforme les jetons en arbre (descente récursive, une méthode par niveau de priorité).</summary>
internal sealed class Analyseur
{
    private static readonly HashSet<string> Fonctions = ["sqrt", "ln", "log", "exp", "abs", "det", "inv", "trans"];

    private readonly List<Jeton> _jetons;
    private int _p;

    private Analyseur(List<Jeton> jetons) => _jetons = jetons;

    private Jeton Courant => _jetons[_p];

    public static Noeud Analyser(string texte)
    {
        var a = new Analyseur(Lexeur.Decouper(texte));
        if (a.Courant.Genre == GenreJeton.Fin) throw new ErreurCalcul("Écris un calcul, par exemple 1/3+1/6.");
        var arbre = a.Somme();
        if (a.Courant.Genre == GenreJeton.ParD)
            throw new ErreurCalcul($"Il y a une parenthèse fermante « ) » en trop (position {a.Courant.Debut + 1}).");
        if (a.Courant.Genre != GenreJeton.Fin) throw a.Inattendu();
        return arbre;
    }

    // somme := terme (('+' | '-') terme)*
    private Noeud Somme()
    {
        var g = Terme();
        while (Courant is { Genre: GenreJeton.Operateur, Texte: "+" or "-" })
        {
            char op = Courant.Texte[0];
            _p++;
            var d = Terme();
            g = new NBinaire(op, g, d, g.Debut, d.Fin);
        }
        return g;
    }

    // terme := unaire (('*' | '/') unaire | unaire_collé)*   — « 2A », « 3(x) », « A(B) » multiplient sans signe
    private Noeud Terme()
    {
        var g = Unaire();
        while (true)
        {
            if (Courant is { Genre: GenreJeton.Operateur, Texte: "*" or "/" })
            {
                char op = Courant.Texte[0];
                _p++;
                var d = Unaire();
                g = new NBinaire(op, g, d, g.Debut, d.Fin);
            }
            else if (Courant.Genre is GenreJeton.Nom or GenreJeton.ParG)
            {
                var d = Puissance(); // multiplication implicite
                g = new NBinaire('*', g, d, g.Debut, d.Fin);
            }
            else return g;
        }
    }

    // unaire := ('-' | '+') unaire | puissance
    private Noeud Unaire()
    {
        if (Courant is { Genre: GenreJeton.Operateur, Texte: "-" or "+" })
        {
            var signe = Courant;
            _p++;
            var operande = Unaire();
            return signe.Texte == "-" ? new NUnaire('-', operande, signe.Debut, operande.Fin) : operande;
        }
        return Puissance();
    }

    // puissance := primaire ('^' exposant)?   — exposant à droite : 2^3^2 = 2^(3^2) ; « A^T » = transposée
    private Noeud Puissance()
    {
        var b = Primaire();
        if (Courant is not { Genre: GenreJeton.Operateur, Texte: "^" }) return b;
        _p++;
        if (Courant is { Genre: GenreJeton.Nom, Texte: "T" })
        {
            var t = Courant;
            _p++;
            return new NTransposee(b, b.Debut, t.Fin);
        }
        Noeud e;
        if (Courant is { Genre: GenreJeton.Operateur, Texte: "-" or "+" })
        {
            var signe = Courant;
            _p++;
            var m = Puissance();
            e = signe.Texte == "-" ? new NUnaire('-', m, signe.Debut, m.Fin) : m;
        }
        else e = Puissance();
        return new NBinaire('^', b, e, b.Debut, e.Fin);
    }

    private Noeud Primaire()
    {
        var j = Courant;
        switch (j.Genre)
        {
            case GenreJeton.Nombre:
                _p++;
                return new NNombre(LireNombre(j.Texte), j.Debut, j.Fin);

            case GenreJeton.ParG:
            {
                _p++;
                var interieur = Somme();
                if (Courant.Genre != GenreJeton.ParD) throw new ErreurCalcul("Il manque une parenthèse fermante « ) ».");
                var fin = Courant.Fin;
                _p++;
                return interieur with { Debut = j.Debut, Fin = fin };
            }

            case GenreJeton.Nom:
                return Nom(j);

            case GenreJeton.Fin:
                throw new ErreurCalcul("Le calcul est incomplet : il manque une valeur à la fin.");

            default:
                throw Inattendu();
        }
    }

    private Noeud Nom(Jeton j)
    {
        _p++;
        string nom = j.Texte;
        if (Fonctions.Contains(nom))
        {
            if (Courant.Genre != GenreJeton.ParG)
                throw new ErreurCalcul($"« {nom} » s'écrit avec des parenthèses, par exemple {nom}(2).");
            _p++;
            var arg = Somme();
            if (Courant.Genre != GenreJeton.ParD) throw new ErreurCalcul($"Il manque une parenthèse fermante « ) » après {nom}(…");
            int fin = Courant.Fin;
            _p++;
            return new NAppel(nom, arg, j.Debut, fin);
        }
        if (nom == "pi" || nom == "e") return new NConstante(nom, j.Debut, j.Fin);
        if (nom == "ans") return new NAns(j.Debut, j.Fin);
        if (nom == "I")
            throw new ErreurCalcul("La matrice identité s'écrit avec sa taille : I2, I3, I4…");
        if (nom[0] == 'I' && nom.Length > 1)
        {
            if (!int.TryParse(nom.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int taille) || taille < 1 || taille > 10)
                throw new ErreurCalcul($"« {nom} » : la matrice identité va de I1 à I10.");
            return new NIdentite(taille, j.Debut, j.Fin);
        }
        if (nom == "T")
            throw new ErreurCalcul("« T » s'utilise pour la transposée : écris A^T (ou trans(A)).");
        return new NMatrice(nom, j.Debut, j.Fin);
    }

    private ErreurCalcul Inattendu()
    {
        var j = Courant;
        return j.Genre switch
        {
            GenreJeton.Fin => new ErreurCalcul("Le calcul est incomplet : il manque une valeur à la fin."),
            GenreJeton.Nombre => new ErreurCalcul($"Il manque un opérateur avant « {j.Texte} » (position {j.Debut + 1}) : écris par exemple 2*{j.Texte}."),
            _ => new ErreurCalcul($"Il manque une valeur avant « {j.Texte} » (position {j.Debut + 1})."),
        };
    }

    /// <summary>« 12 », « 0,5 », « 3.25 » : exacts. Le résultat est la fraction décimale simplifiée.</summary>
    private static Rationnel LireNombre(string texte)
    {
        int sep = texte.IndexOfAny([',', '.']);
        string chiffres = sep < 0 ? texte : texte.Remove(sep, 1);
        int decimales = sep < 0 ? 0 : texte.Length - sep - 1;
        if (chiffres.Length == 0) chiffres = "0";
        return new Rationnel(BigInteger.Parse(chiffres, CultureInfo.InvariantCulture), BigInteger.Pow(10, decimales));
    }
}
