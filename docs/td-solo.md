# TD en solo : conception

Objectif : de **vrais exercices de TD**, comme ceux du CNED, avec une correction exacte et pas à pas.
On peut faire un petit TD de 15 minutes ou un TD de plus d'une heure, comme le jour de l'examen.
Les cartes restent le format « 5 minutes » ; le TD sert à **s'entraîner sur les méthodes** du cours.

Décisions prises avec Florian (18/11/2026) :

| Question | Réponse |
| --- | --- |
| Durée | Au choix : 15 min, 30 min, 1 h, 2 h. |
| Erreurs | Un exercice raté renvoie ses notions dans les révisions. |
| Matières | Toutes celles où un TD **correct** est possible, sur le modèle des types d'exercices des TD du CNED. |

## Règles qui ne bougent pas

- **Mode groupe intact** : les anciens codes (`MAT1`, `MAT2`, `SEA1`) génèrent toujours le même TD. On n'ajoute
  que de **nouveaux préfixes**. Les générateurs existants gardent leur `Generer`.
- **Rien du CNED dans le code** : les TD du CNED ont seulement servi à choisir les **types** d'exercices. Les
  énoncés sont générés : nombres, graphes, tâches, adresses et droits sont tirés au hasard. Les mises en
  situation sont écrites pour Atome.
- **Correction exacte** : un générateur calcule lui-même la réponse et la correction. On ne fait pas de TD
  automatique sur une matière où la réponse ne se vérifie pas : ces matières passent par les exercices rédigés
  des packs (voir plus bas).
- **Jamais punitif** : le chrono n'est qu'un repère, on peut quitter à tout moment et les exercices faits
  comptent quand même.

## Ce que voit Florian

Écran **TD** avec deux onglets : **Seul** (par défaut) et **À plusieurs** (le TD en groupe actuel, inchangé).

Onglet **Seul** (icône : une personne ; « À plusieurs » : la même personne, trois fois, en profondeur) :
1. **Domaine › Thème › Notion** (décision du 18/11 : un TD = une notion travaillée à fond, comme les TD du CNED) :
   - domaine : Mathématiques, Réseau, Cybersécurité, SLAM, ou « Exercices rédigés de mes packs » ;
   - thème : « Tout le domaine » (mélange, façon examen) ou un thème (Matrices, Graphes…) ;
   - notion : « Toutes les notions » ou une seule (Produit de deux matrices, Dates au plus tôt…) ; pour les
     exercices rédigés, la notion est une séance.
2. **Durée** : 15 min · 30 min · 1 h · 2 h.
3. **Niveau** : Découverte · Entraînement · Comme à l'examen.
4. **Correction** : *après chaque exercice* (défaut jusqu'à 30 min) ou *à la fin, comme à l'examen*
   (défaut à partir de 1 h).
5. Case **« Seulement ce que j'ai déjà lu »** (cochée par défaut s'il y a au moins une séance lue) : ne garde
   que les thèmes dont la séance du cours a été ouverte.

Pendant le TD : un chrono global indicatif (« 23 min / 1 h »), le même écran de saisie que le TD en groupe
(grilles, nombres, choix, et un nouveau **champ texte** pour une adresse IP, des droits `rwxr-x---` ou un
chemin `A-C-F`), la touche I pour l'indice et la correction pas à pas avec Espace.
En mode *à la fin*, on valide chaque exercice sans voir le résultat. À la fin, on reprend tous les exercices
un par un : vérification, puis correction.

Récap de fin : réussite par exercice, XP comme en groupe et une ligne « Ces notions reviennent dans tes
révisions : … » pour les exercices à retravailler.

## Composition d'un TD solo (`Revisions.Core/TdGroupe/CompositeurTd.cs`)

Chaque générateur « à sujets » (`IGenerateurASujets`) décrit ses **sujets** : clé, nom, durée estimée en
minutes selon le niveau, section du cours. Il sait créer un exercice d'un sujet donné.

Pour remplir la durée choisie, le compositeur fait ceci :
1. Il garde les sujets des thèmes cochés, et seulement ceux des séances lues si la case est cochée.
2. Il donne un poids à chaque sujet :
   - **×3** s'il a été raté récemment (historique) ;
   - **×2** s'il n'a jamais été fait ;
   - **×1** sinon.
   Un même sujet ne revient pas deux fois de suite.
3. Il tire des sujets jusqu'à atteindre la durée visée, à 10 % près. Si un TD très court ne peut contenir
   aucun exercice, il en prend quand même un.
4. Il range le TD par thème, puis dans l'ordre du cours, comme une vraie feuille de TD.

Le TD solo ne produit pas de code : il n'a pas besoin d'être reproductible. Les tests fixent la graine pour
vérifier la composition.

## Lien avec les révisions

- Nouvelle table `td_exercice` (migration) : jour, générateur, sujet, section, résultat. Elle sert à pondérer
  les prochains TD (voir « Composition »). Elle pourra aussi servir plus tard aux statistiques.
- Exercice **à retravailler** avec une section : les cartes **déjà vues** de cette section, 5 au plus, passent
  à l'échéance **d'aujourd'hui**. La stabilité FSRS ne change pas : on ramène seulement la date. Les cartes
  hors cours sont exclues.
- XP : même barème que le TD en groupe.

## Thèmes et types d'exercices

Ces types reprennent ceux des TD du CNED (`cours/`, privés) et des fiches de cours. Chaque nouveau
générateur a son **propre préfixe** : il est donc aussi disponible en groupe, en version 1.

| Préfixe | Thème | Types d'exercices | Saisie |
| --- | --- | --- | --- |
| `MAT` | Matrices (existant) | dimension, somme, réel, identité, produit, puissance, situation concrète | grille, nombre, choix |
| `INV` | Inverse et systèmes | vérifier que B est l'inverse de A, inverse d'une 2 × 2, écrire un système sous forme AX = Y, résoudre avec l'inverse | grille, choix |
| `GRA` | Graphes | représentation (successeurs ↔ matrice, arcs, boucles), chemins (M², M³, nombre de chemins), fermeture transitive (M[2], M̂), niveaux des sommets, chemin optimal d'un graphe pondéré | grille, nombre, texte |
| `ORD` | Ordonnancement MPM | niveaux des tâches, dates au plus tôt / au plus tard, durée du projet, chemin critique, marges totale et libre | grille, nombre, texte |
| `IPV` | Réseau IPv4 | masque ↔ /n, adresse du réseau, diffusion, nombre d'hôtes, première et dernière adresse, même réseau ? | texte, nombre, choix |
| `DRL` | Droits Linux | rwx ↔ octal, effet d'un `chmod` symbolique, `umask` → droits par défaut, qui peut lire / écrire / entrer | texte, choix |
| `SQL` | SQL (LID) | petite table générée + requête → nombre de lignes, valeur d'un agrégat, résultat d'un `GROUP BY` | nombre, choix |
| `SEA` | Exercices rédigés d'une séance (existant) | cartes « exercice » des packs : CEJM, culture générale, anglais, conception… | auto-évaluation |

Thème **« Exercices rédigés de mes packs »** en solo : seulement les cartes de type **exercice** (pas les
questions ni les QCM), hors cours exclu, tirées des séances lues des matières cochées. C'est la seule voie
pour les matières sans calcul. Réponse auto-évaluée.

Pas de générateur pour l'algorithmique Python en v1 : la trace d'un programme généré est possible, mais
le cours en est encore aux bases. On le garde pour une prochaine étape.

## Étapes (un commit chacune)

1. Ce document.
2. Core : réponse **texte** (`ReponseTexte`, comparaison tolérante), champ de saisie ; interface
   `IGenerateurASujets` ; sujets des matrices.
3. Générateurs `GRA` et `ORD` + tests (déterminisme, justesse des calculs sur des exemples du cours).
4. Générateurs `INV`, `IPV`, `DRL` + tests.
5. Générateur `SQL` + tests.
6. Compositeur, migration `td_exercice`, remise en révision + tests.
7. Écran : onglets Seul / À plusieurs, correction à la fin, chrono global, récap.
8. README, Aide, notes de version.
