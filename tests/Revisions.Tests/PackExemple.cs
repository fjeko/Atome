using System.Text.Json.Nodes;

namespace Revisions.Tests;

/// <summary>
/// Fabrique des packs de test à partir de l'exemple de packs/FORMAT.md.
/// Chaque test part du pack valide et y change une seule chose.
/// </summary>
internal static class PackExemple
{
    public const string Json =
        """
        {
          "formatVersion": 1,
          "id": "maths-sq1-s03",
          "matiere": "Mathématiques pour l'informatique",
          "bloc": "Bloc 6",
          "partie": "SQ1 – Calcul matriciel",
          "sequence": null,
          "seance": "S3 – Multiplication de matrices (TD2)",
          "ordre": 3,
          "source": { "fichier": "cours/maths/td2.pdf", "pages": 6 },
          "resume": "Le produit de matrices.",
          "notions": ["Condition de compatibilité"],
          "sections": [
            {
              "id": "maths-sq1-s03-s01",
              "titre": "Quand peut-on multiplier deux matrices ?",
              "contenu": "Formule : $A \\times B$.",
              "pageSource": 2,
              "notions": ["Condition de compatibilité"]
            }
          ],
          "cartes": [
            {
              "id": "maths-sq1-s03-c001",
              "section": "maths-sq1-s03-s01",
              "type": "question",
              "recto": "À quelle condition peut-on calculer $A \\times B$ ?",
              "verso": "Colonnes de $A$ = lignes de $B$.",
              "difficulte": 1
            },
            {
              "id": "maths-sq1-s03-c002",
              "section": "maths-sq1-s03-s01",
              "type": "qcm",
              "question": "Taille de $A \\times B$ ?",
              "choix": ["$2 \\times 4$", "$3 \\times 3$", "$4 \\times 2$", "Impossible"],
              "bonneReponse": 0,
              "explication": "Lignes de $A$, colonnes de $B$.",
              "difficulte": 2
            },
            {
              "id": "maths-sq1-s03-c003",
              "section": "maths-sq1-s03-s01",
              "type": "exercice",
              "enonce": "Calculer le produit.",
              "etapes": ["Étape 1", "Étape 2"],
              "reponse": "Résultat",
              "difficulte": 2
            }
          ]
        }
        """;

    /// <summary>Renvoie le pack d'exemple après une modification.</summary>
    public static string Modifie(Action<JsonObject> modification)
    {
        var racine = JsonNode.Parse(Json)!.AsObject();
        modification(racine);
        return racine.ToJsonString();
    }

    public static JsonObject Carte(this JsonObject racine, int index) => racine["cartes"]![index]!.AsObject();
}
