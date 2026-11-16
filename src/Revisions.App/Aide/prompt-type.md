# Prompt type – transformer un cours en pack

À coller dans une conversation Claude avec le PDF (ou le fichier `.md`) d'une séance.
Pour un cours de plusieurs séances, faire un pack par séance.

---

Tu vas transformer le cours joint en **pack de révision** au format JSON décrit ci-dessous. Je suis étudiant en BTS SIO ; ce pack sera importé dans mon application de révision.

**Objectif :** que je puisse (1) lire tout le cours dans l'application et (2) réviser chaque notion avec des cartes.

**Règles :**
1. Reste fidèle au cours : reformule pour la clarté, n'invente rien, ne supprime aucune notion.
2. `sections` contient **tout** le cours, découpé en parties courtes (une idée par section). Markdown autorisé (gras, listes, tableaux, blocs de code avec le langage). Formules et matrices en LaTeX : `$…$` en ligne, `$$…$$` en bloc. Indique `pageSource` (page du PDF) pour chaque section.
3. `notions` liste toutes les notions de la séance. Chaque notion a au moins une section et une carte.
4. Cartes : `question` (recto/verso courts, une idée), `qcm` (4 choix, distracteurs plausibles = erreurs fréquentes, `explication`), `exercice` (tiré des TD/exemples, `etapes` = correction pas à pas, `reponse`). Vise 1 à 3 cartes par section, et teste la compréhension, pas des détails inutiles.
5. Chaque section et chaque carte a un `id` unique et stable, préfixé par l'`id` du pack (`<pack>-s01`, `<pack>-c001`…). Chaque carte indique la `section` à laquelle elle se rattache.
6. `difficulte` : 1 facile, 2 moyen, 3 difficile.
7. Réponds **uniquement** avec le JSON, valide, sans commentaire.

**Informations du pack :**
- id : `<ex. maths-sq1-s03>`
- matiere : `<ex. Mathématiques pour l'informatique>`
- bloc : `<ex. Bloc 6>`
- partie : `<ex. SQ1 – Calcul matriciel>`
- seance : `<ex. S3 – Multiplication de matrices (TD2)>`
- ordre : `<numéro de la séance>`
- source.fichier : `<chemin du fichier, relatif au dossier Cours>`

**Format :** (voir `packs/FORMAT.md` pour le détail des champs)

```json
{
  "formatVersion": 1,
  "id": "",
  "matiere": "",
  "bloc": "",
  "partie": "",
  "sequence": null,
  "seance": "",
  "ordre": 1,
  "source": { "fichier": "", "pages": 0 },
  "resume": "",
  "notions": [],
  "sections": [
    { "id": "", "titre": "", "contenu": "", "pageSource": 1, "notions": [] }
  ],
  "cartes": [
    { "id": "", "section": "", "type": "question", "recto": "", "verso": "", "difficulte": 1 },
    { "id": "", "section": "", "type": "qcm", "question": "", "choix": [], "bonneReponse": 0, "explication": "", "difficulte": 2 },
    { "id": "", "section": "", "type": "exercice", "enonce": "", "etapes": [], "reponse": "", "difficulte": 2 }
  ]
}
```
