# Comment ça marche

*Atome : tes révisions, at home.*

L'application fait deux choses : te laisser **lire tes cours**, et te les faire **réviser au bon moment** pour que ça reste en mémoire.

### Lire un cours

Dans la **Bibliothèque**, choisis une matière, puis une séance, et clique sur **Lire**. Le cours s'affiche section par section. Le bouton **Voir le PDF** ouvre le document d'origine à la bonne page, pratique pour les schémas.

Quand tu as fini de lire, **Réviser cette séance** te fait passer ses cartes tout de suite, pendant que c'est frais.

### La session du jour

Sur l'**Accueil**, le bouton **Session du jour** lance les cartes à revoir aujourd'hui, toutes matières confondues, plus quelques nouvelles cartes **des cours que tu as lus**, rangées en petits paquets par matière. Tu n'as rien à choisir.

Envie d'autre chose ? **Autre révision…** te propose une matière ou une partie, tes points faibles, ce que tu viens de lire ou la préparation d'un devoir. Tout est expliqué dans la page *Comment Atome choisit tes cartes*.

Il y a trois sortes de cartes :

- **Question** : réponds de tête (ou par écrit, si tu veux comparer), puis regarde la réponse ;
- **QCM** : choisis une réponse, l'explication s'affiche ensuite ;
- **Exercice** : cherche, écris ton résultat si tu veux (Tab), puis vois la réponse. Si la réponse est une matrice, une grille à remplir s'affiche (Tab pour passer d'une case à l'autre, Entrée pour valider). Si ton résultat est différent (ou si tu as répondu de tête), la correction détaillée s'affiche étape par étape.

### Les quatre notes

Après avoir vu la réponse, tu te notes toi-même. Sois honnête, c'est ce qui rend le calendrier juste :

| Note | Quand la choisir | Effet |
| --- | --- | --- |
| **1 · Raté** | tu ne savais pas, ou tu t'es trompé | la carte revient dans la session, puis dès demain |
| **2 · Difficile** | tu as trouvé, mais avec effort | elle revient assez vite |
| **3 · Bien** | tu as trouvé normalement | c'est la note habituelle |
| **4 · Facile** | évident, sans réfléchir | elle revient dans longtemps |

Sous chaque bouton, tu vois dans combien de temps la carte reviendra.

### Pourquoi les cartes reviennent (la répétition espacée)

On oublie vite ce qu'on vient d'apprendre, puis de moins en moins vite à chaque fois qu'on le revoit. L'application s'en sert : elle te remontre une carte **juste avant le moment où tu l'aurais oubliée**. Au début c'est le lendemain, puis quelques jours après, puis des semaines, puis des mois.

L'algorithme utilisé s'appelle **FSRS**. Pour chaque carte, il retient deux choses : à quel point elle est difficile *pour toi*, et combien de temps elle tient dans ta mémoire. Chaque note que tu donnes met ces deux valeurs à jour.

Ce qui est bien avec FSRS : il tient compte du **temps réellement écoulé**. Si tu reviens après trois semaines et que tu te souviens encore d'une carte, il comprend qu'elle est solide et l'espace beaucoup. Une pause n'efface rien.

### XP, niveaux et série

- Tu gagnes de l'**XP** pour chaque carte à revoir, avec un petit bonus quand tu réussis une carte « Difficile ». L'XP ne baisse jamais.
- La **série** se compte en **semaines** : une semaine compte dès que tu as révisé une seule carte. Pas besoin de venir tous les jours.
- L'**objectif du jour** (5 cartes par défaut) est le minimum qui suffit pour réussir ta journée. Tu peux t'arrêter dès qu'il est atteint.

### Si tu t'absentes

Aucun souci. Après une semaine ou plus sans révision, l'application passe en **mode reprise** : une session courte (10 cartes), les cartes les plus fragiles d'abord, aucune nouvelle carte. La taille remonte doucement les jours suivants.

### Les plafonds

Pour ne pas te noyer, deux limites réglables dans **Réglages** :

- le **plafond quotidien** : au-delà, l'application te propose d'arrêter ;
- les **nouvelles cartes par jour** : même si tu importes 300 packs, elles arrivent petit à petit.

## À ton goût

Dans **Réglages → Apparence** :

- le **thème** : sombre au départ, clair, comme Windows, ou l'un des six thèmes colorés ; et la **couleur d'accent** (boutons, barres, sélection) ;
- la **taille du texte**, de 90 à 150 %, partout dans l'application (aussi avec **A−** et **A+** pendant la lecture) ;
- **réduire les animations** : les transitions deviennent de simples fondus (Atome suit aussi le réglage « Effets d'animation » de Windows) ;
- les **sons** : trois petits sons doux (bonne réponse, fin de session, niveau gagné), coupés par défaut ;
- la **mascotte** : normale, réduite ou masquée.

Chaque matière garde toujours la même couleur et la même icône, quel que soit le thème : c'est un repère, dans la bibliothèque comme en session.
Une couleur n'est jamais la seule information : un mot ou une icône dit toujours la même chose.
