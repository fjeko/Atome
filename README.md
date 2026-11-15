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

101 tests : validation des packs, import et réimport (historique conservé), algorithme FSRS,
construction des sessions, mode reprise, XP, niveaux, série hebdomadaire.

## Organisation du code

```
app/
  src/
    Revisions.Core/     Cœur métier, sans interface ni base : modèles, validation des packs,
                        FSRS, construction des sessions, gamification. Entièrement testable.
    Revisions.Data/     SQLite (Microsoft.Data.Sqlite + Dapper) : schéma, import, requêtes.
    Revisions.App/      Interface WPF en MVVM (CommunityToolkit.Mvvm) + rendu WebView2.
      ViewModels/       Un ViewModel par écran : toute la logique d'affichage.
      Views/            Les écrans en XAML (aucune logique dans le code-behind).
      Controles/        VueMarkdown : la zone qui affiche Markdown + LaTeX.
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
| 1, 2, 3, 4 | Raté, Difficile, Bien, Facile |
| A à E | Choisir une réponse de QCM |
| Entrée | Suite logique (révéler, continuer, ou « Bien ») |
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
- [ ] Réimport d'un pack modifié : l'historique des cartes est conservé.

## Outils de développement (version Debug uniquement)

- `Revisions.exe --ecran=bibliotheque` (ou `statistiques`, `reglages`, `lecture:<id du pack>`,
  `session:<id du pack>`) ouvre directement un écran.
- Variable d'environnement `REVISIONS_DONNEES` : utilise un autre dossier de données, pour essayer
  sans toucher à son vrai historique.
