# Format des packs – version 1

Ce fichier est le **contrat** entre les packs (produits à partir des cours) et l'application qui les lit.
Toute évolution du format passe par ce fichier : on incrémente `formatVersion` et l'application reste capable de lire les versions précédentes.

## Emplacement et nommage

```
packs/
  <matiere>/<partie>/<seance>.json
```

Exemple : `packs/Mathematiques/SQ1 - Calcul matriciel/S03 - Multiplication de matrices.json`

- Un pack = une séance (ou une synthèse, ou un chapitre court).
- Encodage UTF-8 sans BOM, JSON strict (pas de commentaires, pas de virgule finale).

## Structure

```json
{
  "formatVersion": 1,
  "id": "maths-sq1-s03",
  "matiere": "Mathématiques pour l'informatique",
  "bloc": "Bloc 6",
  "partie": "SQ1 – Calcul matriciel",
  "sequence": null,
  "seance": "S3 – Multiplication de matrices (TD2)",
  "ordre": 3,
  "source": {
    "fichier": "cours/Bloc 6 - Mathematiques pour l'informatique/Séquence 1 - Matrices/SQ1 - S3 - TD2 - Multiplication de matrices.pdf",
    "pages": 6
  },
  "resume": "Une ou deux phrases qui disent l'essentiel de la séance.",
  "notions": ["Condition de compatibilité", "Taille du produit", "Non-commutativité"],
  "sections": [
    {
      "id": "maths-sq1-s03-s01",
      "titre": "Quand peut-on multiplier deux matrices ?",
      "contenu": "Markdown. Formules en LaTeX : $A \\times B$ en ligne, $$\\begin{pmatrix}1 & 2\\\\3 & 4\\end{pmatrix}$$ en bloc.",
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
      "verso": "Si le nombre de colonnes de $A$ est égal au nombre de lignes de $B$.",
      "difficulte": 1
    },
    {
      "id": "maths-sq1-s03-c002",
      "section": "maths-sq1-s03-s01",
      "type": "qcm",
      "question": "$A$ est $2 \\times 3$ et $B$ est $3 \\times 4$. Taille de $A \\times B$ ?",
      "choix": ["$2 \\times 4$", "$3 \\times 3$", "$4 \\times 2$", "Impossible"],
      "bonneReponse": 0,
      "explication": "On garde les lignes de $A$ et les colonnes de $B$.",
      "difficulte": 2
    },
    {
      "id": "maths-sq1-s03-c003",
      "section": "maths-sq1-s03-s01",
      "type": "exercice",
      "enonce": "Calculer $\\begin{pmatrix}1 & 5\\\\3 & 9\\end{pmatrix} \\times \\begin{pmatrix}3 & 1\\\\6 & 2\\end{pmatrix}$.",
      "etapes": [
        "Coefficient (1,1) : $1 \\times 3 + 5 \\times 6 = 33$",
        "Coefficient (1,2) : $1 \\times 1 + 5 \\times 2 = 11$",
        "Ligne 2 : $3 \\times 3 + 9 \\times 6 = 63$ et $3 \\times 1 + 9 \\times 2 = 21$"
      ],
      "reponse": "$\\begin{pmatrix}33 & 11\\\\63 & 21\\end{pmatrix}$",
      "difficulte": 2
    }
  ]
}
```

## Champs

| Champ | Obligatoire | Description |
| --- | --- | --- |
| `formatVersion` | oui | Toujours `1` pour l'instant. |
| `id` | oui | Identifiant unique et **stable** du pack (minuscules, chiffres, tirets). |
| `matiere` | oui | Nom de la matière, tel qu'affiché dans la bibliothèque. |
| `bloc` | non | « Bloc 1 », « Bloc 6 »… pour regrouper les matières. |
| `partie` | oui | Compétence ou séquence (ex. « B1.2 Partie I – SQ1 », « SQ1 – Calcul matriciel »). |
| `sequence` | non | Niveau intermédiaire si la partie en contient plusieurs. |
| `seance` | oui | Titre de la séance. |
| `ordre` | non | Entier pour trier les séances d'une même partie. |
| `source.fichier` | oui | Chemin du document d'origine, **relatif au dossier `Cours`**. |
| `source.pages` | non | Nombre de pages du PDF. |
| `resume` | oui | 1 à 3 phrases. |
| `notions` | oui | Toutes les notions de la séance. Chacune doit être couverte par au moins une section et une carte. |
| `sections` | oui | Le cours complet, découpé en parties courtes. |
| `sections[].id` | oui | Unique et stable. |
| `sections[].titre` | oui | Titre de la section. |
| `sections[].contenu` | oui | Markdown (titres `###` et moins, gras, listes, tableaux, blocs de code avec langage) + LaTeX `$…$` et `$$…$$`. |
| `sections[].pageSource` | non | Page du PDF où se trouve la section (pour « Voir le PDF »). |
| `sections[].notions` | non | Notions couvertes par la section. |
| `cartes` | oui | Au moins une carte par notion. |
| `cartes[].id` | oui | Unique et **stable** : réimporter un pack ne doit jamais faire perdre l'historique. |
| `cartes[].section` | oui | `id` de la section liée (bouton « Revoir la notion »). |
| `cartes[].type` | oui | `question`, `qcm` ou `exercice` (v1). `code` est réservé pour la v2. |
| `cartes[].difficulte` | oui | 1 (facile) à 3 (difficile). |

Champs par type de carte :

- `question` : `recto`, `verso`.
- `qcm` : `question`, `choix` (2 à 5 éléments), `bonneReponse` (index à partir de 0), `explication`.
- `exercice` : `enonce`, `etapes` (liste, la correction pas à pas), `reponse`.

Tous les textes acceptent Markdown + LaTeX.

## Règles de validation (côté application)

- JSON invalide, champ obligatoire manquant ou type de carte inconnu → pack refusé avec un message clair (fichier, champ, carte).
- `id` de carte ou de section en double → refusé.
- `cartes[].section` qui ne correspond à aucune section → refusé.
- Une notion sans carte → avertissement (pas un refus).
- Réimport d'un pack existant (même `id`) : le contenu est mis à jour, l'historique de révision des cartes dont l'`id` existe toujours est conservé ; les cartes disparues sont archivées, pas supprimées.

## Règles de rédaction (côté packs)

- Le contenu reste **fidèle au cours** : on reformule pour la clarté, on n'invente rien, on ne supprime aucune notion.
- Les cartes testent la compréhension autant que la mémoire : éviter les cartes « par cœur » sans intérêt (numéros de page, noms de fichiers d'exercice).
- Une carte = une idée. Recto court, verso court.
- Les QCM ont des distracteurs plausibles (erreurs fréquentes), jamais absurdes.
- Les exercices viennent des TD et des corrigés quand il y en a.
- Les cours sont réservés à l'usage personnel de leur destinataire (mention CNED) : les packs ne se publient pas.
