# Faire un TD, seul ou à plusieurs

L'écran **TD** (barre de gauche) propose de vrais exercices de TD, **générés à chaque fois** : les nombres, les graphes, les projets, les adresses changent, et Atome calcule lui-même la correction. Deux onglets : **Seul** et **À plusieurs**.

## Seul

1. **Choisis où tu veux travailler**, du plus large au plus précis :
   - le **domaine** : Mathématiques, Réseau, Cybersécurité, SLAM (exercices générés, correction automatique), ou *Exercices rédigés de mes packs* (les cartes « exercice » de tes cours : tu compares toi-même avec la correction) ;
   - le **thème** : par exemple *Matrices*, ou *Tout le domaine* pour un mélange façon examen ;
   - la **notion** : par exemple *Produit de deux matrices*, ou *Toutes les notions*.
2. Choisis la **durée** (15 min, 30 min, 1 h ou 2 h comme à l'examen) et le **niveau**. Atome remplit la durée avec des exercices de durée estimée, rangés par thème comme une vraie feuille de TD.
3. **Correction à la fin, comme à l'examen** (activée par défaut à partir d'1 h) : tu valides chaque exercice sans voir le résultat, puis tout est corrigé à la fin. Sinon, la correction vient après chaque exercice.
4. **Seulement ce que j'ai déjà lu** : ne garde que les exercices des notions dont tu as ouvert la séance du cours.

Un chrono en haut indique le temps passé : c'est un simple repère, il ne se passe rien quand la durée est dépassée.

**Tes erreurs servent** : un exercice raté revient plus souvent dans tes prochains TD, et les cartes de sa notion repassent dans ta prochaine révision (5 au plus par exercice, sans effacer ta progression).

Pour saisir une réponse courte (adresse IP, droits `rwxr-x---`, chemin `A-C-F`), tape-la dans la zone de texte : les espaces et les majuscules ne comptent pas, et pour un chemin `A C F` ou `A → C → F` sont acceptés.

## À plusieurs, sur Discord

Le mode **À plusieurs** permet de faire le même TD à plusieurs, chacun sur son PC, au même rythme. On se retrouve en vocal (sur Discord par exemple) et on avance ensemble, exercice par exercice, comme en classe. Ce n'est pas un quiz chronométré : on cherche, on corrige, on discute.

Il n'y a **ni réseau, ni serveur, ni compte** : le seul lien entre vous est un **code** que l'un dicte aux autres. Tous les thèmes générés existent aussi en groupe.

### 1. L'un crée le TD

Dans **TD → À plusieurs** :

1. choisis le **thème** : *Matrices*, *Graphes*, *Ordonnancement MPM*, *Adressage IPv4*… ou *Une séance de ma bibliothèque* ;
2. choisis le **niveau** et le **nombre d'exercices** (la case « Inclure les exercices hors cours », décochée par défaut, ajoute des exercices en plus du programme, comme la transposée ; elle est mémorisée dans le code, donc tout le groupe a le même TD) ;
3. clique sur **Créer le TD**.

Atome affiche un code, par exemple `MAT1-2-8-48217`. Dicte-le aux autres (ou copie-le sur Discord avec le bouton à côté).

| Morceau du code | Signification |
| --- | --- |
| `MAT1` | type de TD (MAT = matrices, INV = inverse, GRA = graphes, ORD = MPM, IPV = IPv4, DRL = droits Linux, SQL, SEA = séance) et version du générateur |
| `2` | niveau |
| `8` | nombre d'exercices |
| `48217` | le tirage au sort ; le dernier chiffre sert à repérer une faute de frappe |

### 2. Les autres le rejoignent

Ils saisissent le code dans **Rejoindre un camarade** (majuscules, minuscules ou espaces : peu importe). Tout le monde obtient **exactement** le même TD : mêmes exercices, mêmes nombres, même ordre.

Pour vérifier, comparez l'**empreinte** affichée sur l'écran « Prêts ? » : deux mots, par exemple *volcan-panda*. Mêmes mots, même TD.

- **« Mets Atome à jour »** : le code a été créé avec une version plus récente d'Atome, qui génère les exercices autrement. Fais la mise à jour (Réglages → Vérifier maintenant) pour avoir le même TD que les autres.
- **« faute de frappe »** : un chiffre a été mal entendu. Faites répéter le code lentement.
- **TD « séance »** : tout le monde doit avoir importé le même pack. Si l'empreinte diffère, vos packs ne sont pas dans la même version.

### 3. Le déroulé d'un exercice

1. **Prêts ?** Quand tout le monde est prêt, quelqu'un dit « go » et chacun appuie sur **Espace** (ou **Go !**). Un compte à rebours de 3 secondes peut être activé à la création.
2. **On cherche.** Chacun répond de son côté : une **grille** à la bonne taille pour une matrice, une case pour un nombre, des boutons pour un choix. Le bouton **Indice** donne un rappel de méthode tiré du cours.
3. **Correction.** **Vérifier** colore chaque case : **vert** si elle est juste, **orange** sinon, avec la bonne valeur dessous. Puis la correction s'affiche **pas à pas** avec **Espace**, comme au tableau. **Revoir la notion** ouvre la section du cours correspondante.
4. **Exercice suivant**, et on recommence avec « Prêts ? ».

Le minuteur par exercice est **désactivé par défaut**. Si tu l'actives, c'est un simple repère : il ne se passe rien quand il arrive à zéro.

### 4. Le récap

À la fin, Atome affiche ton récap : exercices réussis du premier coup, avec l'indice, ou à retravailler, et une petite phrase à dire aux autres, par exemple « J'ai fait 7/10 ». Le TD rapporte de l'**XP** (même un exercice raté en rapporte : l'essai compte), sans toucher au planning de révision de tes cartes (en solo, les notions ratées repassent en révision : voir plus haut).

### Raccourcis clavier

| Touche | Action |
| --- | --- |
| **Tab**, **Maj + Tab** | case suivante, case précédente |
| **← → ↑ ↓** | se déplacer dans la grille d'une matrice |
| **Entrée** (dans une case) | case suivante |
| **Ctrl + Entrée** | vérifier |
| **Espace** | « go » sur l'écran Prêts ?, puis étape suivante de la correction |
| **Entrée** (hors des cases) | la suite logique : go, vérifier, étape suivante, exercice suivant |
| **I** (hors des cases) | indice |

Les cases acceptent les nombres négatifs : tape le signe moins avant le nombre (`-3`).
