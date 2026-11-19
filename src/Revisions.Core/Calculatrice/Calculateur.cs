using System.Globalization;
using System.Numerics;

namespace Revisions.Core.Calculatrice;

/// <summary>
/// La calculatrice : garde les matrices définies (A, B, C…) et le dernier résultat (Ans) le temps de la session,
/// et calcule une expression écrite au clavier. Les erreurs sont des <see cref="ErreurCalcul"/> en français.
/// </summary>
public sealed class Calculateur
{
    /// <summary>Taille maximale d'une matrice dans l'éditeur (comme sur une calculatrice graphique).</summary>
    public const int TailleMax = 5;

    private readonly Dictionary<string, MatriceRationnelle> _matrices = [];

    public IReadOnlyDictionary<string, MatriceRationnelle> Matrices => _matrices;

    /// <summary>Dernier résultat, que « Ans » reprend dans le calcul suivant.</summary>
    public Valeur? Ans { get; private set; }

    /// <summary>Noms possibles pour une matrice : A, B, C… Z.</summary>
    public static IReadOnlyList<string> NomsPossibles { get; } = Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).ToList();

    public void DefinirMatrice(string nom, MatriceRationnelle matrice)
    {
        if (nom.Length != 1 || !char.IsAsciiLetterUpper(nom[0])) throw new ArgumentException("Le nom d'une matrice est une lettre majuscule.", nameof(nom));
        _matrices[nom] = matrice;
    }

    public void SupprimerMatrice(string nom) => _matrices.Remove(nom);

    /// <summary>Calcule l'expression et la garde dans Ans. Lève <see cref="ErreurCalcul"/> si elle est fausse.</summary>
    public Valeur Evaluer(string expression)
    {
        var arbre = Analyseur.Analyser(expression);
        var evaluateur = new Evaluateur(expression, _matrices, Ans);
        var resultat = evaluateur.Calculer(arbre);
        Ans = resultat;
        return resultat;
    }
}

/// <summary>Étape 3 : calcule l'arbre. Garde le texte de départ pour citer « A (2×3) » dans les messages d'erreur.</summary>
internal sealed class Evaluateur(string source, IReadOnlyDictionary<string, MatriceRationnelle> matrices, Valeur? ans)
{
    private const int PuissanceMax = 1000;

    public Valeur Calculer(Noeud n) => n switch
    {
        NNombre x => Nombre.De(x.Valeur),
        NConstante { Nom: "pi" } => Nombre.De(Math.PI),
        NConstante => Nombre.De(Math.E),
        NAns => ans ?? throw new ErreurCalcul("Il n'y a pas encore de résultat : fais d'abord un calcul, puis « Ans » le reprendra."),
        NMatrice x => matrices.TryGetValue(x.Nom, out var m)
            ? new ValeurMatrice(m)
            : throw new ErreurCalcul($"La matrice {x.Nom} n'est pas définie : crée-la dans l'éditeur de matrices."),
        NIdentite x => new ValeurMatrice(MatriceRationnelle.Identite(x.Taille)),
        NUnaire x => Oppose(x),
        NBinaire x => Binaire(x),
        NAppel x => Appel(x),
        NTransposee x => Transposer(x.Operande),
        _ => throw new ErreurCalcul("Calcul non reconnu."),
    };

    /// <summary>Le texte tapé pour ce morceau de calcul, tel quel (« A », « (A+B) »).</summary>
    private string Texte(Noeud n) => source.Substring(n.Debut, n.Fin - n.Debut).Trim();

    /// <summary>« A (2×3) » pour une matrice, « 3 » pour un nombre : sert aux messages.</summary>
    private static string Decrire(string texte, Valeur v) => v is ValeurMatrice m ? $"{texte} ({m.Matrice.Taille})" : texte;

    private Valeur Oppose(NUnaire x) => Calculer(x.Operande) switch
    {
        Nombre { Exact: { } r } => Nombre.De(-r),
        Nombre z => Nombre.De(-z.Approche),
        ValeurMatrice m => new ValeurMatrice(-m.Matrice),
        _ => throw new ErreurCalcul("Calcul non reconnu."),
    };

    private Valeur Binaire(NBinaire x)
    {
        var g = Calculer(x.Gauche);
        var d = Calculer(x.Droite);
        string tg = Texte(x.Gauche), td = Texte(x.Droite);
        try
        {
            return x.Op switch
            {
                '+' or '-' => Somme(x.Op, g, d, tg, td),
                '*' => Produit(g, d, tg, td),
                '/' => Quotient(g, d, tg, td),
                _ => Puissance(g, d, tg, td),
            };
        }
        catch (DivideByZeroException)
        {
            throw new ErreurCalcul("Division par zéro : ce calcul n'a pas de résultat.");
        }
    }

    private static Valeur Somme(char op, Valeur g, Valeur d, string tg, string td)
    {
        string verbe = op == '+' ? "additionner" : "soustraire";
        switch (g, d)
        {
            case (Nombre a, Nombre b):
                if (a.Exact is { } ra && b.Exact is { } rb) return Nombre.De(op == '+' ? ra + rb : ra - rb);
                return Verifier(Nombre.De(op == '+' ? a.Approche + b.Approche : a.Approche - b.Approche));
            case (ValeurMatrice a, ValeurMatrice b):
                if (a.Matrice.Lignes != b.Matrice.Lignes || a.Matrice.Colonnes != b.Matrice.Colonnes)
                    throw new ErreurCalcul(
                        $"Impossible de {verbe} {Decrire(tg, g)} et {Decrire(td, d)} : " +
                        "il faut deux matrices de même taille.");
                return new ValeurMatrice(op == '+' ? a.Matrice + b.Matrice : a.Matrice - b.Matrice);
            default:
                throw new ErreurCalcul($"Impossible de {verbe} {Decrire(tg, g)} et {Decrire(td, d)} : un nombre et une matrice ne s'ajoutent pas.");
        }
    }

    private static Valeur Produit(Valeur g, Valeur d, string tg, string td)
    {
        switch (g, d)
        {
            case (Nombre a, Nombre b):
                if (a.Exact is { } ra && b.Exact is { } rb) return Nombre.De(ra * rb);
                return Verifier(Nombre.De(a.Approche * b.Approche));
            case (Nombre k, ValeurMatrice m):
                return new ValeurMatrice(Facteur(k, tg) * m.Matrice);
            case (ValeurMatrice m, Nombre k):
                return new ValeurMatrice(Facteur(k, td) * m.Matrice);
            case (ValeurMatrice a, ValeurMatrice b):
                if (a.Matrice.Colonnes != b.Matrice.Lignes)
                    throw new ErreurCalcul(
                        $"Les tailles de {Decrire(tg, g)} et {Decrire(td, d)} ne permettent pas le produit {tg}×{td} : " +
                        $"{tg} a {a.Matrice.Colonnes} colonne{(a.Matrice.Colonnes > 1 ? "s" : "")} mais {td} a {b.Matrice.Lignes} ligne{(b.Matrice.Lignes > 1 ? "s" : "")}.");
                return new ValeurMatrice(a.Matrice * b.Matrice);
            default:
                throw new ErreurCalcul("Calcul non reconnu.");
        }
    }

    /// <summary>Le nombre qui multiplie une matrice doit être exact (une fraction) : les matrices restent exactes.</summary>
    private static Rationnel Facteur(Nombre k, string texte) =>
        k.Exact ?? throw new ErreurCalcul(
            $"« {texte} » est une valeur approchée (pi, e, ln, exp ou une racine non entière) : " +
            "elle ne peut pas multiplier une matrice, qui reste en valeurs exactes.");

    private static Valeur Quotient(Valeur g, Valeur d, string tg, string td)
    {
        switch (g, d)
        {
            case (Nombre a, Nombre b):
                if (b.Exact is { EstZero: true } || (b.Exact is null && b.Approche == 0)) throw new DivideByZeroException();
                if (a.Exact is { } ra && b.Exact is { } rb) return Nombre.De(ra / rb);
                return Verifier(Nombre.De(a.Approche / b.Approche));
            case (ValeurMatrice m, Nombre k):
                var diviseur = Facteur(k, td);
                if (diviseur.EstZero) throw new DivideByZeroException();
                return new ValeurMatrice(Rationnel.Un / diviseur * m.Matrice);
            default:
                throw new ErreurCalcul(
                    $"On ne divise pas par une matrice ({td}). Utilise son inverse : par exemple {tg}*inv({td}) ou {tg}*{td}^-1.");
        }
    }

    private static Valeur Puissance(Valeur g, Valeur d, string tg, string td)
    {
        if (d is ValeurMatrice) throw new ErreurCalcul($"Une matrice ({td}) ne peut pas servir d'exposant.");
        var exposant = (Nombre)d;
        if (g is ValeurMatrice m)
        {
            if (exposant.Exact is not { EstEntier: true } e || e.Numerateur > PuissanceMax || e.Numerateur < -PuissanceMax)
                throw new ErreurCalcul($"L'exposant de {Decrire(tg, g)} doit être un entier entre -{PuissanceMax} et {PuissanceMax}.");
            if (!m.Matrice.EstCarree)
                throw new ErreurCalcul($"Seule une matrice carrée a des puissances : {Decrire(tg, g)} n'est pas carrée.");
            int n = (int)e.Numerateur;
            if (n >= 0) return new ValeurMatrice(m.Matrice.Puissance(n));
            var inverse = Inverser(m.Matrice, tg);
            return new ValeurMatrice(inverse.Puissance(-n));
        }

        var b = (Nombre)g;
        if (b.Exact is { } rb && exposant.Exact is { EstEntier: true } en && BigInteger.Abs(en.Numerateur) <= PuissanceMax)
        {
            if (rb.EstZero && en.Signe < 0) throw new DivideByZeroException();
            return Nombre.De(rb.Puissance((int)en.Numerateur));
        }
        double r = Math.Pow(b.Approche, exposant.Approche);
        if (double.IsNaN(r))
            throw new ErreurCalcul($"{tg}^{td} n'est pas un nombre réel (une puissance non entière d'un nombre négatif).");
        return Verifier(Nombre.De(r));
    }

    private Valeur Transposer(Noeud operande) => Calculer(operande) switch
    {
        ValeurMatrice m => new ValeurMatrice(m.Matrice.Transposee()),
        Nombre z => z,
        _ => throw new ErreurCalcul("Calcul non reconnu."),
    };

    // ---- Fonctions -----------------------------------------------------------------------------

    private Valeur Appel(NAppel x)
    {
        var v = Calculer(x.Argument);
        string arg = Texte(x.Argument);
        return x.Fonction switch
        {
            "det" => Determinant(v, arg),
            "inv" => Inverse(v, arg),
            "trans" => v is ValeurMatrice m ? new ValeurMatrice(m.Matrice.Transposee()) : v,
            _ => FonctionNombre(x.Fonction, v, arg),
        };
    }

    private static Valeur Determinant(Valeur v, string arg)
    {
        if (v is not ValeurMatrice m) throw new ErreurCalcul($"det() demande une matrice : « {arg} » est un nombre.");
        if (!m.Matrice.EstCarree)
            throw new ErreurCalcul($"Le déterminant n'existe que pour une matrice carrée : {Decrire(arg, v)} n'est pas carrée.");
        return Nombre.De(m.Matrice.Determinant());
    }

    private static Valeur Inverse(Valeur v, string arg)
    {
        if (v is Nombre z)
        {
            if (z.Exact is { } r) return r.EstZero ? throw new DivideByZeroException() : Nombre.De(Rationnel.Un / r);
            return z.Approche == 0 ? throw new DivideByZeroException() : Verifier(Nombre.De(1 / z.Approche));
        }
        var m = ((ValeurMatrice)v).Matrice;
        return new ValeurMatrice(Inverser(m, arg));
    }

    private static MatriceRationnelle Inverser(MatriceRationnelle m, string nom)
    {
        if (!m.EstCarree)
            throw new ErreurCalcul($"Seule une matrice carrée peut avoir un inverse : {nom} ({m.Taille}) n'est pas carrée.");
        return m.Inverse() ?? throw new ErreurCalcul($"{nom} n'est pas inversible : son déterminant vaut 0.");
    }

    private static Valeur FonctionNombre(string fonction, Valeur v, string arg)
    {
        if (v is not Nombre n)
            throw new ErreurCalcul($"{fonction}() s'applique à un nombre, pas à une matrice ({Decrire(arg, v)}).");
        switch (fonction)
        {
            case "abs":
                return n.Exact is { } r ? Nombre.De(r.Signe < 0 ? -r : r) : Nombre.De(Math.Abs(n.Approche));

            case "sqrt":
                if (n.Approche < 0) throw new ErreurCalcul($"La racine carrée d'un nombre négatif ({arg}) n'existe pas dans les réels.");
                if (n.Exact?.RacineExacte() is { } racine) return Nombre.De(racine);
                return Verifier(Nombre.De(Math.Sqrt(n.Approche)));

            case "exp":
                if (n.Exact is { EstZero: true }) return Nombre.De(Rationnel.Un);
                return Verifier(Nombre.De(Math.Exp(n.Approche)));

            case "ln":
                if (n.Approche <= 0) throw new ErreurCalcul($"ln({arg}) n'existe pas : le logarithme demande un nombre strictement positif.");
                if (n.Exact is { } un && un == Rationnel.Un) return Nombre.De(Rationnel.Zero);
                return Verifier(Nombre.De(Math.Log(n.Approche)));

            default: // log : décimal (base 10)
                if (n.Approche <= 0) throw new ErreurCalcul($"log({arg}) n'existe pas : le logarithme demande un nombre strictement positif.");
                if (n.Exact is { } x && PuissanceDeDix(x) is { } k) return Nombre.De(k);
                return Verifier(Nombre.De(Math.Log10(n.Approche)));
        }
    }

    /// <summary>Si x vaut 10^k (k entier, positif ou négatif), renvoie k ; sinon null.</summary>
    private static Rationnel? PuissanceDeDix(Rationnel x)
    {
        for (int k = -30; k <= 30; k++)
            if (Rationnel.DixPuissance(k) == x) return (Rationnel)k;
        return null;
    }

    private static Nombre Verifier(Nombre n)
    {
        if (double.IsNaN(n.Approche) || double.IsInfinity(n.Approche))
            throw new ErreurCalcul("Le résultat est trop grand ou n'est pas défini.");
        return n;
    }
}
