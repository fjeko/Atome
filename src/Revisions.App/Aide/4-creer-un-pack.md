# Créer un pack avec Claude

Tu peux transformer n'importe quel cours (un PDF, un fichier texte) en pack, avec Claude.

### Les étapes

1. Ouvre une conversation avec Claude et **joins le cours** d'une séance (le PDF ou le fichier `.md`).
2. Clique sur **Copier le prompt** en haut de cette page, puis colle-le dans la conversation.
3. Complète la partie **« Informations du pack »** à la fin du prompt : identifiant, matière, bloc, partie, séance, numéro d'ordre et chemin du fichier.
4. Claude répond avec un fichier JSON. **Enregistre-le** dans le dossier des packs, avec l'extension `.json`.
5. Dans l'application : **Bibliothèque → Actualiser**.

### Quelques conseils

- **Un pack par séance.** Pour un cours de plusieurs séances, recommence pour chacune.
- L'**identifiant** du pack doit être unique et ne plus changer ensuite (minuscules, chiffres et tirets, par exemple `maths-sq1-s03`). C'est lui qui permet de garder ton historique si tu refais le pack plus tard.
- Pour que la bibliothèque soit bien rangée, écris la **partie** toujours de la même façon pour une même séquence, par exemple `B1.2 Partie I – SQ1 Corrections et évolutions d'applications`.
- Si l'import est refusé, le rapport te dit quoi corriger. Tu peux copier le message d'erreur à Claude et lui demander de corriger le pack.
- Relis les cartes : si une carte te paraît fausse ou inutile, corrige-la dans le fichier et actualise. Ton historique est conservé.

### Le prompt

Voici le texte copié par le bouton. Les parties entre `<…>` sont à remplacer.
