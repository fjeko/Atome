# Révisions BTS SIO

Application Windows de révision pour le BTS SIO : elle importe des **packs** de cours (fichiers JSON),
permet de **lire le cours** (Markdown et formules LaTeX, hors ligne) et de **réviser** avec des cartes
planifiées par **répétition espacée**, le tout avec une gamification qui ne punit jamais une absence.

- Cahier des charges : `../docs/cahier-des-charges.md`
- Format des packs : `../packs/FORMAT.md`
- Conception détaillée : [`PLAN.md`](PLAN.md)

## Lancer l'application

Prérequis : Windows 10/11, **SDK .NET 10**, **WebView2 Runtime** (inclus dans Windows 11).

Depuis le dossier `app/` :

```bash
dotnet run --project src/Revisions.App
```

Pour fabriquer la version de tous les jours (dossier `app/publish`) et créer un raccourci sur le Bureau :

```bash
powershell -ExecutionPolicy Bypass -File publier.ps1 -Raccourci
```

Au démarrage, l'application importe automatiquement les packs du dossier `packs/` (trouvé tout seul en
remontant depuis l'exécutable ; modifiable dans Réglages).

## Lancer les tests

```bash
dotnet test
```

511 tests : validation des packs, import et réimport (historique conservé), algorithme FSRS,
construction des sessions, mode reprise, XP, niveaux, série hebdomadaire, regroupement de la
bibliothèque en niveaux, couleurs et contrastes de chaque thème, enregistrement des réglages,
ordre du cours (tri naturel, chiffres romains) et tourniquet des matières, planning du CNED (priorités,
devoirs, avancement avant une échéance, dates personnelles, migration).

## Ce que fait l'application

- **Accueil** : la session du jour en un clic, objectif minimal, série en semaines, XP et niveau.
  Avec un planning : 1 à 3 séances « Au programme » et les prochaines échéances, sur un ton neutre.
- **Planning** : la proposition du CNED en frises par matière (fichier `../planning/*.json`, format décrit dans
  `../planning/FORMAT.md`). Dates modifiables (gardées dans la base, le fichier n'est jamais modifié),
  interrupteur général et par matière, quiz masquables. Sans planning, l'application suit l'ordre du cours.
- **Bibliothèque en tuiles** : matière → compétence (B1.2…) → séquence (SQ1…) → séances, avec fil
  d'Ariane, recherche, nombre de cartes à revoir et barre de maîtrise sur chaque tuile.
- **Lecture** : le cours section par section (Markdown, LaTeX, code coloré), PDF d'origine à la bonne page.
- **Session** : cartes question, QCM (choix mélangés) et exercice pas à pas ; sur une carte question,
  on peut **écrire sa réponse** pour la comparer à la bonne avant de se noter.
- **Statistiques** : régularité, réussite par matière, points faibles avec session ciblée.
- **Réglages** : rythme, reprise, **thèmes** (clair, sombre, six thèmes colorés) et **couleur d'accent**.
- **Aide** : fonctionnement, raccourcis, ajout de cours, création d'un pack avec Claude (prompt à copier), données.

## Organisation du code

```
app/
  src/
    Revisions.Core/     Cœur métier, sans interface ni base : modèles, validation des packs,
                        FSRS, construction des sessions, gamification, niveaux de la bibliothèque
                        (Bibliotheque/), couleurs et thèmes (Apparence/), ordre du cours (Ordre/),
                        planning du CNED (Planning/). Entièrement testable.
    Revisions.Data/     SQLite (Microsoft.Data.Sqlite + Dapper) : schéma, import, requêtes.
    Revisions.App/      Interface WPF en MVVM (CommunityToolkit.Mvvm) + rendu WebView2.
      ViewModels/       Un ViewModel par écran : toute la logique d'affichage.
      Views/            Les écrans en XAML (aucune logique dans le code-behind).
      Controles/        VueMarkdown : la zone qui affiche Markdown + LaTeX.
      Services/         État partagé, thème (ServiceTheme), WebView2, journal d'erreurs.
      Aide/             Pages de l'aide en Markdown, incorporées dans l'exécutable.
                        prompt-type.md est une copie de ../docs/prompt-type.md : la recopier si le prompt change.
      Web/              Page de rendu locale + markdown-it, KaTeX, highlight.js (hors ligne).
  tests/Revisions.Tests/  Tests xUnit.
  samples/exemple.json    Petit pack de démonstration (à importer depuis la Bibliothèque).
```

Sens des dépendances : `App → Data → Core`. Le Core ne dépend de rien.

## Choix techniques, en bref

| Sujet | Choix | Pourquoi |
| --- | --- | --- |
| Répétition espacée | **FSRS-5** (`Core/Planification/Fsrs.cs`) | Tient compte du temps réellement écoulé : adapté à un rythme irrégulier |
| Rendu du cours | WebView2 + markdown-it + KaTeX embarqués | Formules et code bien rendus, sans Internet |
| Base de données | SQLite, SQL écrit à la main | Simple, un seul fichier, et le SQL reste visible |
| Interface | WPF, MVVM, thème Fluent clair/sombre | Standard, testable, look Windows 11 |
| Validation des packs | Parcours manuel du JSON (`Core/Packs/LecteurPack.cs`) | Messages d'erreur précis : champ et carte en cause |

## Où sont mes données ?

`%LOCALAPPDATA%\RevisionsBTS\` :

- `revisions.db` : la base (cours importés, état des cartes, historique, XP, réglages) ;
- `sauvegardes\` : une copie automatique par jour d'utilisation, les 7 dernières sont gardées ;
- `journal.log` : erreurs éventuelles.

Pour restaurer une sauvegarde : fermer l'application, puis remplacer `revisions.db` par la copie voulue.

## Raccourcis clavier en session

| Touche | Action |
| --- | --- |
| Espace | Voir la réponse / l'étape suivante |
| 1, 2, 3, 4 | Raté, Difficile, Bien, Facile (rangée du haut avec ou sans Maj, ou pavé numérique) |
| A à E | Choisir une réponse de QCM |
| Entrée | Suite logique (révéler, continuer, ou « Bien ») |
| Tab | Entrer dans la zone de réponse écrite (cartes « question ») |
| Ctrl + Entrée | Depuis la zone : voir la réponse et comparer |
| Échap | Sortir de la zone (le texte est gardé) |
| ← → | Section précédente / suivante en lecture |

## Liste de vérification manuelle

- [ ] Premier démarrage sans base : les packs sont importés, l'accueil propose une session.
- [ ] Lecture : une matrice, un tableau et un bloc de code s'affichent correctement.
- [ ] « Voir le PDF » ouvre le document à la page de la section.
- [ ] Session complète au clavier seul (question, QCM, exercice).
- [ ] Une carte ratée revient plus loin dans la session.
- [ ] Fin de session : chiffres cohérents, « Revoir la notion » ouvre la bonne section.
- [ ] Fermeture puis réouverture : XP, série et cartes dues sont conservées.
- [ ] Thème clair et thème sombre (Réglages).
- [ ] Changer de thème et de couleur d'accent : tout change tout de suite, et le choix est gardé au redémarrage.
- [ ] Carte « question » : écrire une réponse (Tab), Ctrl+Entrée, comparaison affichée, puis note au clavier.
- [ ] Bibliothèque : descendre dans les tuiles, revenir par « Retour » et par le fil d'Ariane, rechercher.
- [ ] Planning : changer une date, redémarrer, la date est gardée ; « Dates du fichier » la remet.
- [ ] Aide : « Copier le prompt » puis coller dans un éditeur de texte.
- [ ] Réimport d'un pack modifié : l'historique des cartes est conservé.

## Outils de développement (version Debug uniquement)

- `Revisions.exe --ecran=bibliotheque` (ou `statistiques`, `reglages`, `lecture:<id du pack>`,
  `session:<id du pack>`, `aide:<numéro de page>`, `bibliotheque:0,1` pour descendre dans les tuiles,
  `bibliotheque:?texte` pour une recherche) ouvre directement un écran.
- `--theme=ocean` et `--accent=#D8498F` (ou `--accent=auto`) changent l'apparence au lancement ;
  `--hauteur=1700` agrandit la fenêtre pour capturer un écran long.
- Variable d'environnement `REVISIONS_DONNEES` : utilise un autre dossier de données, pour essayer
  sans toucher à son vrai historique.
