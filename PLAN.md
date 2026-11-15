# Plan de l'application « Révisions BTS SIO »

> **État au 15/11/2026 : les étapes 1 à 7 sont construites.** L'application se lance, importe les packs,
> affiche les cours, fait réviser et suit la progression. Voir `README.md` pour la lancer.
>
> Écarts par rapport au plan ci-dessous (décidés pendant la construction, pour garder un code simple) :
> - **Pas d'interfaces de dépôt dans le Core** : le Core ne contient que la logique pure (validation, FSRS,
>   sessions, gamification) ; l'import (`ImportateurPacks`) et les requêtes sont dans `Revisions.Data`,
>   et les ViewModels les utilisent directement. Les tests d'import tournent sur une vraie base SQLite temporaire.
> - **Trois dépôts** au lieu de cinq : `DepotBibliotheque`, `DepotRevisions`, `DepotProfil` (qui porte aussi les statistiques).
> - **Une seule page web** `Web/rendu.html` (au lieu de `lecteur.html` + `carte.html`) et le greffon
>   **markdown-it-texmath** (au lieu de `@vscode/markdown-it-katex`, qui n'existe pas en version navigateur).
> - **Une zone WebView2 par écran**, recréée à chaque ouverture d'écran (pas de préchargement) : assez rapide en pratique.
> - Table `etat_carte` sans colonne `statut` (une carte est « nouvelle » tant que sa stabilité est vide) ;
>   table `profil` sans `meilleure_serie` (recalculée à partir de l'historique) ; colonne `pack.date_lecture` ajoutée.
> - Pas de gestion centralisée des versions NuGet (`Directory.Packages.props`) : inutile à cette taille.
> - **Ajout : les choix des QCM sont mélangés à chaque affichage.** Dans les packs, la bonne réponse est en
>   première position 3 fois sur 4 ; sans mélange il suffirait de toujours répondre A.
> - Formules FSRS-5 et paramètres **vérifiés** contre la documentation officielle (awesome-fsrs, « The Algorithm »).
>   Les tests comparent aux valeurs des formules, pas à des sorties de l'implémentation de référence.
> - Non fait (reste possible plus tard) : recherche dans le contenu des cours, lien cliquable vers Internet
>   dans un cours (volontairement ignoré : application hors ligne), optimiseur des paramètres FSRS.

Document de conception rédigé le 14/11/2026 par la session « Application ».
Références : `../CLAUDE.md`, `../docs/cahier-des-charges.md`, `../packs/FORMAT.md` (le contrat des packs, qui fait foi).

---

## 0. Prérequis sur la machine (vérifiés le 14/11/2026)

| Outil | État | Action |
| --- | --- | --- |
| SDK .NET | **10.0.403** installé (runtimes NETCore, AspNetCore et WindowsDesktop 10.0.14) | rien à faire |
| WebView2 Runtime | **157.0.4341.46** installé (Evergreen, machine) | rien à faire |
| Git | **absent** (`git` introuvable, ni dans `C:\Program Files\Git`) | **Florian doit l'installer** (voir ci-dessous) |
| winget | présent | sert à installer Git |

**À faire par Florian avant l'étape 1** (une seule fois) :

```bash
winget install --id Git.Git -e
```

Puis fermer et rouvrir l'application Claude (pour que `git` soit dans le PATH), et configurer son identité :

```bash
git config --global user.name "Florian"
```

```bash
git config --global user.email "ton-adresse@exemple.fr"
```

Si Git n'est pas encore là au moment de construire, l'étape 1 se fait quand même et `git init` est lancé dès qu'il est disponible.

---

## 1. Architecture

### 1.1 Solution et projets

```
app/
  PLAN.md                     ← ce document
  README.md                   ← installation, lancement, captures (portfolio)
  Revisions.slnx              ← solution (format .slnx, par défaut en .NET 10)
  Directory.Build.props       ← réglages communs : net10.0, Nullable, TreatWarningsAsErrors (sauf WPF0001), LangVersion latest
  Directory.Packages.props    ← versions des paquets NuGet centralisées
  .gitignore / .editorconfig
  samples/                    ← packs d'exemple pour le développement (jamais dans ../packs)
  src/
    Revisions.Core/           ← cœur métier, sans WPF ni SQLite : testable seul
    Revisions.Data/           ← accès SQLite (implémente les interfaces du Core)
    Revisions.App/            ← interface WPF (MVVM) + WebView2
  tests/
    Revisions.Tests/          ← xUnit : validation, import, planification, sessions, gamification
```

Sens des dépendances : `App → Core, Data` ; `Data → Core` ; `Core → rien` (seulement la bibliothèque standard .NET).
Le Core définit des interfaces (`IDepotPacks`, `IDepotCartes`, `IDepotRevisions`, `IDepotProfil`…), la couche Data les implémente. C'est l'architecture en couches demandée par le cahier des charges, et elle se présente bien au BTS.

### 1.2 Contenu des projets

**Revisions.Core**
```
Modeles/        Matiere, Pack, Section, Carte (abstraite) → CarteQuestion, CarteQcm, CarteExercice,
                EtatCarte, Note (Rate=1, Difficile=2, Bien=3, Facile=4), Revision, SessionRevision, Reglages, Profil
Packs/          LecteurPack (JSON → modèle, par formatVersion), ValidateurPack, ProblemeValidation,
                ResultatValidation, ImportateurPacks (scan du dossier, empreinte, réimport)
Planification/  Fsrs (algorithme pur), ParametresFsrs, Planificateur (applique une note à un EtatCarte)
Sessions/       ConstructeurSession (choisit et ordonne les cartes), SessionEnCours (file, remise en file des ratées)
Gamification/   CalculXp, Niveaux, SerieHebdomadaire, DetecteurReprise
Statistiques/   CalculStatistiques (régularité, réussite, points faibles, maîtrise)
Temps/          JourDeRevision (le jour commence à 4 h du matin), utilise System.TimeProvider
```

**Revisions.Data**
```
BaseDeDonnees.cs     ouverture de la connexion, PRAGMA, migrations (PRAGMA user_version)
Migrations/          001_initial.sql, 002_….sql (ressources incorporées, appliquées dans l'ordre)
Depots/              DepotPacks, DepotCartes, DepotRevisions, DepotProfil, DepotStatistiques
Sauvegarde.cs        copie quotidienne de la base (VACUUM INTO), 7 copies gardées
```

**Revisions.App**
```
App.xaml(.cs)        démarrage, injection de dépendances, gestion globale des erreurs, thème
MainWindow           barre de navigation à gauche + zone de contenu (ContentControl lié au ViewModel courant)
ViewModels/          AccueilVM, SessionVM, FinSessionVM, BibliothequeVM, LectureVM, StatistiquesVM, ReglagesVM, RapportImportVM
Views/               un UserControl par écran ; DataTemplate ViewModel → Vue dans App.xaml
Services/            Navigation, Dialogues, EnvironnementWebView (partagé), ServiceRendu (Markdown → WebView2), OuvreurPdf
Web/                 lecteur.html, carte.html, app.css (variables clair/sombre), app.js, vendor/ (bibliothèques JS figées)
Themes/              couleurs et styles complémentaires
```

### 1.3 Choix techniques et paquets

| Besoin | Choix | Pourquoi |
| --- | --- | --- |
| MVVM | **CommunityToolkit.Mvvm** (MIT) | `[ObservableProperty]`, `[RelayCommand]` : beaucoup moins de code répétitif, standard Microsoft |
| Injection de dépendances | **Microsoft.Extensions.DependencyInjection** | simple, connue en entreprise |
| SQLite | **Microsoft.Data.Sqlite** + **Dapper** (Apache 2.0) | SQL écrit à la main (lisible, pédagogique), Dapper évite le mapping manuel des colonnes |
| JSON | **System.Text.Json** (`JsonDocument` pour valider, avec messages précis) | imposé ; strict par défaut (pas de commentaires ni de virgules finales) |
| Rendu cours | **Microsoft.Web.WebView2** + markdown-it + KaTeX + highlight.js embarqués | voir §4 |
| Thème | **Fluent de WPF** (`Application.ThemeMode = System`, .NET 9+) | look Windows 11, clair/sombre automatique ; avertissement expérimental WPF0001 neutralisé. Repli : dictionnaires de ressources maison |
| Temps | **System.TimeProvider** | permet de « simuler » des dates dans les tests (pause de 10 jours, changement d'année…) |
| Tests | **xUnit** + `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider) | |

Pas d'Entity Framework : trop lourd pour ce projet et cache le SQL, qui est justement au programme du BTS.

### 1.4 Conventions de code

- Noms du domaine en **français** (comme le format des packs : `Carte`, `Seance`, `Echeance`), mots-clés C# en anglais évidemment.
- Commentaires XML `///` sur chaque classe et méthode publique, en français, qui expliquent le *pourquoi*.
- Méthodes courtes, une responsabilité par classe. Tout ce qui est calcul (planification, XP, série) est dans le Core, sans dépendance à l'interface → testé.
- Accès disque et base en `async` pour ne jamais figer l'interface.

---

## 2. Schéma SQLite

Base : `%LOCALAPPDATA%\RevisionsBTS\revisions.db` (une base par utilisateur Windows : prépare G7 sans effort).
Au démarrage : `PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;` puis migrations selon `PRAGMA user_version`.
Dates stockées en texte ISO 8601 UTC (`2026-11-14T18:05:00Z`) ; les échéances en **jour de révision** local (`2026-11-20`).

```sql
CREATE TABLE matiere (
  id          INTEGER PRIMARY KEY,
  nom         TEXT NOT NULL UNIQUE,     -- « Mathématiques pour l'informatique »
  bloc        TEXT                      -- « Bloc 6 » (facultatif)
);

CREATE TABLE pack (
  id              TEXT PRIMARY KEY,     -- id stable du pack (« maths-sq1-s03 »)
  matiere_id      INTEGER NOT NULL REFERENCES matiere(id),
  partie          TEXT NOT NULL,
  sequence        TEXT,
  seance          TEXT NOT NULL,
  ordre           INTEGER,
  source_fichier  TEXT NOT NULL,        -- relatif au dossier Cours
  source_pages    INTEGER,
  resume          TEXT NOT NULL,
  notions_json    TEXT NOT NULL,        -- ["…","…"]
  format_version  INTEGER NOT NULL,
  chemin_fichier  TEXT,                 -- dernier fichier importé (info)
  empreinte       TEXT NOT NULL,        -- SHA-256 du fichier : réimport ignoré si inchangé
  date_import     TEXT NOT NULL,
  date_maj        TEXT NOT NULL
);

CREATE TABLE section (
  id            TEXT PRIMARY KEY,
  pack_id       TEXT NOT NULL REFERENCES pack(id),
  position      INTEGER NOT NULL,       -- ordre dans le pack
  titre         TEXT NOT NULL,
  contenu       TEXT NOT NULL,          -- Markdown + LaTeX
  page_source   INTEGER,
  notions_json  TEXT,
  archivee      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE carte (
  id            TEXT PRIMARY KEY,       -- id stable (doit être unique dans TOUTE la base)
  pack_id       TEXT NOT NULL REFERENCES pack(id),
  section_id    TEXT NOT NULL REFERENCES section(id),
  position      INTEGER NOT NULL,
  type          TEXT NOT NULL CHECK (type IN ('question','qcm','exercice')),
  difficulte    INTEGER NOT NULL CHECK (difficulte BETWEEN 1 AND 3),
  donnees_json  TEXT NOT NULL,          -- champs propres au type (recto/verso, choix…), extensible pour « code » en v2
  archivee      INTEGER NOT NULL DEFAULT 0,
  date_ajout    TEXT NOT NULL
);

CREATE TABLE etat_carte (               -- état FSRS courant (1 ligne par carte)
  carte_id           TEXT PRIMARY KEY REFERENCES carte(id),
  statut             TEXT NOT NULL,     -- 'nouvelle' | 'revision' | 'reapprentissage'
  stabilite          REAL,              -- S en jours (NULL si nouvelle)
  difficulte_fsrs    REAL,              -- D entre 1 et 10
  echeance           TEXT,              -- jour de révision
  derniere_revision  TEXT,
  repetitions        INTEGER NOT NULL DEFAULT 0,
  oublis             INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE session_revision (
  id         INTEGER PRIMARY KEY,
  debut      TEXT NOT NULL,
  fin        TEXT,
  mode       TEXT NOT NULL,             -- 'quotidienne' | 'reprise' | 'seance' | 'point_faible'
  jour       TEXT NOT NULL              -- jour de révision (pour la série hebdomadaire)
);

CREATE TABLE revision (                 -- historique : jamais modifié ni supprimé
  id                INTEGER PRIMARY KEY,
  carte_id          TEXT NOT NULL REFERENCES carte(id),
  session_id        INTEGER REFERENCES session_revision(id),
  date_heure        TEXT NOT NULL,
  jour              TEXT NOT NULL,
  note              INTEGER NOT NULL CHECK (note BETWEEN 1 AND 4),
  duree_ms          INTEGER,
  etait_due         INTEGER NOT NULL,   -- pour la règle XP G1
  retrievabilite    REAL,               -- R au moment de la réponse
  stabilite_avant   REAL, stabilite_apres REAL,
  difficulte_avant  REAL, difficulte_apres REAL,
  xp                INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE profil (                   -- une seule ligne (id = 1)
  id          INTEGER PRIMARY KEY CHECK (id = 1),
  xp_total    INTEGER NOT NULL DEFAULT 0,   -- ne baisse jamais (R1)
  meilleure_serie INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE reglage (cle TEXT PRIMARY KEY, valeur TEXT NOT NULL);
-- objectif_minimal=5, plafond_quotidien=40, nouvelles_par_jour=10, taille_reprise=10,
-- jours_avant_reprise=7, retention_cible=0.9, dossier_packs=…, dossier_cours=…, theme=systeme

CREATE TABLE journal_import (
  id INTEGER PRIMARY KEY, date_heure TEXT NOT NULL, fichier TEXT NOT NULL,
  pack_id TEXT, resultat TEXT NOT NULL,  -- 'importe' | 'mis_a_jour' | 'inchange' | 'refuse'
  messages_json TEXT
);

CREATE INDEX ix_etat_echeance ON etat_carte(echeance);
CREATE INDEX ix_carte_pack    ON carte(pack_id);
CREATE INDEX ix_revision_jour ON revision(jour);
CREATE INDEX ix_revision_carte ON revision(carte_id);
```

Remarque : l'historique `revision` garde S et D avant/après. On pourra donc plus tard recalculer tous les états avec de meilleurs paramètres FSRS (optimiseur) sans rien perdre.

---

## 3. Répétition espacée : **FSRS** (version 5), pas SM-2

### 3.1 Pourquoi FSRS

1. **Rythme irrégulier.** SM-2 ignore le retard : une carte revue après 40 jours au lieu de 10 est traitée comme si elle avait été revue à l'heure. FSRS calcule la **probabilité de se souvenir** (retrievabilité R) à partir du temps réellement écoulé : une bonne réponse après une longue pause augmente fortement la stabilité, un oubli est attendu et peu pénalisé. C'est exactement le profil de Florian.
2. **Mode reprise (R2) :** « cartes les plus fragiles d'abord » = trier par R croissant. SM-2 n'a pas de notion équivalente.
3. **Quatre boutons** Raté / Difficile / Bien / Facile = les quatre notes natives de FSRS.
4. **Moins de révisions** pour la même mémorisation (comparatifs publics de l'algorithme, utilisé par défaut dans Anki depuis 2023), ce qui sert l'objectif « 10 à 25 min ».
5. **Rétention cible réglable** (0,9 par défaut ≈ objectif « 80 % ou plus » du cahier des charges, avec une marge).

Inconvénient : formules un peu plus complexes que SM-2. Elles tiennent dans une classe d'environ 150 lignes, très commentée, sans dépendance externe, avec les paramètres par défaut publiés (pas d'optimiseur en v1).

### 3.2 Formules retenues (FSRS-5, paramètres par défaut w0…w18)

- Retrievabilité : `R(t, S) = (1 + F·t/S)^C` avec `C = −0,5`, `F = 19/81` (t = jours écoulés).
- Intervalle pour une rétention cible r : `I = S/F · (r^(1/C) − 1)` → pour r = 0,9, I = S. Arrondi au jour, minimum 1, maximum 3650.
- Première note G : `S0 = w[G−1]` ; `D0(G) = w4 − e^(w5·(G−1)) + 1`, borné entre 1 et 10.
- Difficulté après une note : `D' = D − w6·(G−3)·(10−D)/9`, puis retour vers la moyenne `D'' = w7·D0(4) + (1−w7)·D'`, borné [1 ; 10].
- Stabilité après un succès (G ≥ 2) : `S' = S · (e^w8 · (11−D) · S^(−w9) · (e^(w10·(1−R)) − 1) · pénalité(G=2 : w15) · bonus(G=4 : w16) + 1)`.
- Stabilité après un oubli (G = 1) : `S' = min(S, w11 · D^(−w12) · ((S+1)^w13 − 1) · e^(w14·(1−R)))`.
- Révision le même jour (carte ratée revue dans la session) : `S' = S · e^(w17·(G − 3 + w18))`.
- Paramètres par défaut FSRS-5 : `0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192, 1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621`.
  → **À l'étape 4, vérifier ces valeurs et formules contre l'implémentation de référence** (dépôt open-spaced-repetition) et en tirer des vecteurs de test.

### 3.3 Règles d'usage

- **Jour de révision** : il commence à 4 h du matin (réviser à 1 h du matin compte pour la veille). Les échéances sont des jours, pas des heures.
- Une carte **ratée** pendant une session revient plus loin dans la même session (environ 5 cartes après, une seule fois) : révision « même jour ». Elle ne rapporte pas d'XP la deuxième fois.
- La `difficulte` (1 à 3) des packs **ne modifie pas FSRS** : elle sert à présenter les nouvelles cartes de la plus facile à la plus difficile dans une séance.
- **Révision de fond (G5)** : automatique avec FSRS (les intervalles des cartes maîtrisées s'allongent, jusqu'à 10 ans max). Une carte est dite **maîtrisée** quand S ≥ 21 jours.
- Révision **en avance** (séance relue, US12) : FSRS gère (R élevé → faible gain de stabilité). On l'enregistre normalement.

---

## 4. Rendu Markdown + LaTeX hors ligne

### 4.1 Principe

- **WebView2** (Chromium d'Edge, déjà installé) affiche des pages HTML **locales** : `Web/lecteur.html` (cours) et `Web/carte.html` (contenu d'une carte).
- Bibliothèques JavaScript **copiées dans `Web/vendor/`** avec leur licence, versions figées et notées dans `Web/vendor/VERSIONS.md` (récupérées une seule fois depuis le registre npm à l'étape 3, puis versionnées dans Git) :
  - **markdown-it** (MIT) : Markdown → HTML, avec `html: false` (le HTML brut d'un pack est échappé : aucun script ne peut s'exécuter) ;
  - **@vscode/markdown-it-katex** (MIT) : reconnaît `$…$` et `$$…$$` **avant** que markdown-it ne touche aux `_`, `*` et `\\` des formules (sinon les matrices `\begin{pmatrix}1 & 2\\3 & 4\end{pmatrix}` seraient cassées) ;
  - **KaTeX** (MIT) + ses polices (woff2) : rendu des formules ;
  - **highlight.js** (BSD-3), sous-ensemble C#, Python, SQL, JSON, HTML/CSS, PowerShell : coloration des blocs de code.
- Les fichiers sont servis par `SetVirtualHostNameToFolderMapping("app.local", <dossier Web>, Deny)` → adresse `https://app.local/lecteur.html`. Aucune requête réseau ; une balise CSP `default-src 'self'` le garantit.

### 4.2 Échanges C# ↔ page

- C# → page : `PostWebMessageAsJson({ type: "afficher", markdown, theme })`. La page rend le contenu **sans se recharger** (rapide en session).
- Page → C# : `chrome.webview.postMessage(...)` pour les liens cliqués (ouverts dans le navigateur par défaut après confirmation, ou ignorés) et pour **relayer les touches** (Espace, 1-4, A-E, Entrée) quand la WebView a le focus, sinon les raccourcis clavier de session ne marcheraient plus.
- Thème : variables CSS clair/sombre, changées par message quand le thème WPF change.

### 4.3 Performance

- Un seul `CoreWebView2Environment` partagé, dossier de données dans `%LOCALAPPDATA%\RevisionsBTS\WebView2`.
- Une WebView par écran (Lecture, Session), **créée une fois et réutilisée** ; pré-initialisée en arrière-plan au démarrage pour masquer les ~0,5 s d'initialisation.
- Contrainte « airspace » : rien de WPF ne peut se superposer à une WebView. Les boutons (Retourner, Noter, Revoir la notion) restent donc en WPF, **sous** la zone de contenu.
- Menu contextuel et outils de développement désactivés en Release.

### 4.4 PDF d'origine (US8)

- Deuxième dossier virtuel `cours.local` → dossier `Cours`, puis ouverture dans une fenêtre WebView2 dédiée à `https://cours.local/<source.fichier>#page=<pageSource>` (visionneuse PDF intégrée d'Edge, hors ligne).
- Repli si ça ne marche pas : ouverture avec le lecteur PDF par défaut de Windows (la page n'est alors pas garantie). À vérifier à l'étape 3.
- Fichier introuvable → message simple avec le chemin attendu et un bouton « Choisir le dossier Cours ».

---

## 5. Import et validation des packs

### 5.1 Où sont les packs

- Réglage `dossier_packs`. Valeur par défaut détectée au premier lancement : on remonte depuis le dossier de l'exécutable jusqu'à trouver `packs/FORMAT.md` ; sinon `Documents\Cours\packs` ; sinon on demande.
- `dossier_cours` = parent de `dossier_packs` par défaut (sert à résoudre `source.fichier`).
- **Au démarrage** : scan récursif de `*.json` en arrière-plan (l'accueil s'affiche tout de suite, un petit bandeau indique « 3 packs mis à jour »).
- **Import manuel** (Bibliothèque → Importer) : un ou plusieurs fichiers choisis n'importe où. Le contenu est stocké en base, le fichier d'origine n'est plus nécessaire ensuite.

### 5.2 Validation (ValidateurPack)

On parcourt le JSON avec `JsonDocument` (et non une désérialisation directe) pour produire des **messages précis** : fichier, chemin du champ (`cartes[12].choix`), id de la carte, et ligne/colonne pour une erreur de syntaxe.

**Refus du pack** (règles de `FORMAT.md`, appliquées à la lettre) :
- JSON invalide (`JsonException` → ligne et position) ; commentaires et virgules finales interdits ;
- `formatVersion` absent ou non entier ; version supérieure à celle connue → « Ce pack demande une version plus récente de l'application » ;
- champ obligatoire manquant ou de mauvais type : `id`, `matiere`, `partie`, `seance`, `source.fichier`, `resume`, `notions`, `sections` (et pour chaque section `id`, `titre`, `contenu`), `cartes` (et pour chaque carte `id`, `section`, `type`, `difficulte`) ;
- `type` inconnu (y compris `code`, réservé à la v2) ;
- champs propres au type manquants : `question` → `recto`, `verso` ; `qcm` → `question`, `choix`, `bonneReponse`, `explication` ; `exercice` → `enonce`, `etapes`, `reponse` ;
- `id` de section ou de carte en double dans le pack ;
- `cartes[].section` qui ne correspond à aucune section.

**Refus supplémentaires** (cohérence, déduits du contrat) :
- `id` du pack qui ne respecte pas `^[a-z0-9-]+$` ;
- `difficulte` hors de 1 à 3 ; `choix` hors de 2 à 5 éléments ; `bonneReponse` hors des indices de `choix` ; `etapes` vide ;
- `id` de carte ou de section **déjà utilisé par un autre pack** (sinon l'historique serait mélangé).

**Avertissements** (le pack est importé) :
- notion sans carte (une notion est « couverte par une carte » si une carte pointe vers une section qui la liste dans `sections[].notions`) ; notion sans section ;
- `source.fichier` introuvable dans le dossier Cours ;
- `pageSource` supérieure à `source.pages` ;
- BOM UTF-8 présent (toléré).

Les champs inconnus sont ignorés (compatibilité avec les futures versions). Le lecteur est organisé par version (`LecteurPackV1`) : une future `formatVersion: 2` ajoutera un lecteur, sans casser la v1.

### 5.3 Réimport qui conserve l'historique (ImportateurPacks)

Tout se fait **dans une transaction** (soit tout est appliqué, soit rien) :
1. Empreinte SHA-256 du fichier identique à celle en base → rien à faire (démarrage rapide).
2. Pack inconnu → insertion de la matière (si nouvelle), du pack, des sections, des cartes et d'un `etat_carte` « nouvelle » par carte.
3. Pack connu (même `id`) → mise à jour des métadonnées et :
   - sections et cartes présentes : contenu mis à jour, `archivee = 0` (une carte qui revient retrouve son historique) ;
   - nouvelles cartes : ajout + état « nouvelle » ;
   - cartes et sections disparues : `archivee = 1` (jamais supprimées, historique gardé ; elles ne sortent plus en session) ;
   - **`etat_carte` et `revision` ne sont jamais touchés** pour une carte existante.
4. Une ligne dans `journal_import`. À la fin, un **rapport d'import** clair : importés / mis à jour / inchangés / refusés (avec la liste des problèmes).

Deux fichiers avec le même `id` de pack dans le dossier → le second est refusé avec un message qui cite les deux chemins.

---

## 6. Sessions, gamification et règles « rythme irrégulier »

### 6.1 Construction de la session du jour (US4)

1. **Cartes dues** (échéance ≤ aujourd'hui, non archivées), triées par **R croissant** (les plus fragiles d'abord).
2. **Nouvelles cartes** : au plus `nouvelles_par_jour` (10 par défaut) moins celles déjà vues aujourd'hui ; ordre : pack (ordre de la séance), puis difficulté croissante. Les nouvelles cartes des séances **déjà ouvertes en lecture** passent en premier (on ne bloque pas les autres : ce serait trop contraignant).
3. Taille totale limitée par le **plafond quotidien** (R3, 40 par défaut) moins les cartes déjà faites aujourd'hui.
4. **Mélange des matières** : entrelacement tour à tour par matière (une carte de maths, une de CEJM, une de C#…), nouvelles cartes réparties dans la session.
5. Estimation « environ X min » = nombre de cartes × durée médiane des 200 dernières réponses (25 s par défaut).

### 6.2 Objectif minimal (US9) et plafond (R3)

- Objectif minimal (5 cartes par défaut) : une fois atteint, message positif « Objectif du jour atteint ! Tu peux t'arrêter là ou continuer. » + petit bonus d'XP. Aucune conséquence si non atteint.
- Plafond atteint : « Tu as fait X cartes aujourd'hui, c'est beaucoup ! Le reste attendra demain. » avec « Terminer » (bouton principal) et « Encore 5 cartes » (discret).

### 6.3 Mode reprise (R2)

- Déclenché si la dernière session date de **7 jours ou plus** (réglable).
- Accueil : « Content de te revoir ! On reprend en douceur : 10 cartes. » (jamais « tu as X cartes en retard »).
- Session = `taille_reprise` (10) cartes, **les plus fragiles d'abord** (R croissant), **aucune nouvelle carte**.
- Retard étalé : les jours suivants, la proposition grandit doucement (10, 15, 20… jusqu'au plafond). Le mode s'arrête quand les cartes dues ≤ plafond. Les cartes non revues restent simplement dues : FSRS tient compte du temps écoulé, rien n'est « perdu ».

### 6.4 XP et niveaux (G1, R1)

| Événement | XP |
| --- | --- |
| Carte **due** notée Facile | 8 |
| Carte due notée Bien | 10 |
| Carte due notée **Difficile** (bonne réponse avec effort) | **12** (bonus) |
| Carte due notée Raté | 2 (on récompense l'essai, jamais zéro) |
| Nouvelle carte découverte | 5 |
| Carte non due (révision en avance, séance relue) ou carte ratée revue dans la même session | 0 |
| Objectif minimal atteint (une fois par jour) | 15 |

- L'XP totale ne fait que monter (R1). Pas d'XP retirée, jamais.
- Niveau N atteint à `50 × N × (N+1)` XP cumulés (niveau 1 à 100 XP, 2 à 300, 5 à 1 500, 10 à 5 500) : progression rapide au début, plus lente ensuite.

### 6.5 Série en semaines (G2)

- Semaine ISO (lundi → dimanche, selon le jour de révision). Une semaine compte dès qu'**une carte** a été révisée.
- Série = nombre de semaines consécutives comptées, en finissant par la semaine en cours **ou la précédente** (la semaine en cours ne casse rien tant qu'elle n'est pas finie).
- Affichage positif : « Série : 3 semaines », « 3 semaines actives sur les 4 dernières » (indicateur du cahier des charges), « Meilleure série : 6 ». Quand une série s'arrête : aucun message.

### 6.6 Notation selon le type de carte (US5, US6)

| Type | Déroulé | Note |
| --- | --- | --- |
| question | recto → **Espace** → verso | 1 à 4 au choix |
| qcm | clic ou touches **A à E** → correction + explication | mauvais choix → Raté automatiquement (bouton « Continuer ») ; bon choix → Difficile / Bien / Facile (Entrée = Bien) |
| exercice | énoncé → **Espace** révèle les étapes une à une → réponse | 1 à 4 au choix |

Après un Raté (ou à tout moment) : bouton **« Revoir la notion »** (US7) qui ouvre la section liée dans une fenêtre de lecture, puis on revient à la session au même endroit.

---

## 7. Écrans

Navigation : barre latérale (Accueil, Bibliothèque, Statistiques, Réglages) ; la Session et la Lecture prennent tout l'écran avec un bouton retour. Une décision par écran, textes courts, thème clair/sombre, police lisible (Segoe UI Variable, 15-16 px dans le contenu).

| Écran | Contenu | Action principale |
| --- | --- | --- |
| **Accueil** | « Aujourd'hui : 18 cartes, environ 10 min » (ou message de reprise, ou « Rien de prévu, tu peux lire une séance »), objectif minimal, barre de la semaine (7 jours), XP + niveau + barre de progression, série | **Commencer** |
| **Session** | compteur (« 4 / 18 »), matière et séance de la carte, contenu (WebView2), boutons Retourner / Noter (1-4) avec l'intervalle prévu sous chaque bouton (« 3 j ») | Noter |
| **Fin de session** | cartes revues, taux de réussite, XP gagnée (et niveau si changé), liste des cartes ratées avec « Revoir la notion » | Retour à l'accueil |
| **Bibliothèque** | arbre Bloc > matière > partie > séance (tri par `ordre`), pour chaque séance : nombre de cartes, % maîtrisées, dues ; recherche par texte | Lire / Réviser cette séance / Importer |
| **Lecture** | sommaire latéral (sections), une section par page (Précédent/Suivant, flèches), résumé et notions en tête | Réviser cette séance (US12) / Voir le PDF (US8) |
| **Statistiques** | semaines actives (12 dernières, barres), réussite par matière (30 derniers jours), points faibles (5 sections au plus faible R moyen ou au plus fort taux d'oubli), cartes maîtrisées par matière | Session sur un point faible |
| **Réglages** | objectif minimal, plafond quotidien, nouvelles cartes par jour, taille et seuil de reprise, rétention cible (0,80 à 0,95), dossiers packs et Cours, thème | — |
| **Rapport d'import** | fenêtre : liste des fichiers et résultat, problèmes détaillés (refus en premier) | Fermer |

Graphiques dessinés en WPF simple (rectangles dans un `ItemsControl`) : pas de bibliothèque de graphiques. Listes longues (bibliothèque) avec virtualisation.

---

## 8. Plan de tests

### 8.1 Tests unitaires (xUnit, `tests/Revisions.Tests`)

**Validation des packs** (un petit fichier JSON par cas dans `Fixtures/`) :
- pack valide de `FORMAT.md` → accepté, 0 erreur ;
- JSON invalide (virgule finale, commentaire, texte tronqué) → refus avec ligne ;
- chaque champ obligatoire manquant → refus qui nomme le champ ;
- type inconnu, `code` → refus ; champs manquants par type ;
- id en double (carte, section), section inexistante, id d'un autre pack → refus ;
- `bonneReponse` hors bornes, `choix` à 1 ou 6 éléments, `difficulte` = 0 ou 4 → refus ;
- notion sans carte, fichier source absent → avertissement seulement ;
- `formatVersion: 2` → message « version plus récente » ; BOM → toléré.

**Import / réimport** (SQLite sur fichier temporaire) :
- premier import → lignes attendues, états « nouvelle » ;
- réimport identique → « inchangé », rien n'est écrit ;
- réimport modifié : carte conservée → historique et état intacts, contenu mis à jour ; carte supprimée → archivée ; carte revenue → désarchivée avec son historique ; nouvelle carte → état « nouvelle » ;
- erreur au milieu → transaction annulée, base inchangée.

**FSRS** :
- vecteurs de référence (calculés avec l'implémentation officielle) pour quelques suites de notes ;
- propriétés : R(S, S) = 0,9 ; intervalle = S à r = 0,9 ; Raté diminue S ; Facile > Bien > Difficile ; D reste dans [1 ; 10] ; intervalle ≤ 3650.

**Sessions et rythme** (avec `FakeTimeProvider`) :
- plafond et limite de nouvelles cartes respectés, y compris après une première session dans la journée ;
- tri par R croissant ; entrelacement des matières ;
- reprise déclenchée à 7 jours, pas à 6 ; aucune nouvelle carte en reprise ; progression 10 → 15 → 20 ;
- jour de révision : 3 h 59 = la veille, 4 h 00 = aujourd'hui ;
- carte ratée remise en file une seule fois.

**Gamification** :
- barème XP (dues / non dues / nouvelles / bonus Difficile) ; XP jamais négative ni décroissante ;
- courbe des niveaux ;
- série : semaines consécutives, semaine en cours vide qui ne casse pas, trou d'une semaine, passage d'année (semaine ISO 53 / 1).

### 8.2 Tests manuels (liste à cocher dans le README, à chaque étape)

Démarrage sans base existante ; import du dossier `packs/` réel ; affichage d'une matrice, d'un tableau, d'un bloc de code C# ; PDF à la bonne page ; session complète au clavier seul ; thème sombre ; fermeture puis réouverture (données présentes) ; démarrage < 2 s avec une centaine de packs.

---

## 9. Découpage en étapes livrables

Chaque étape se termine par : compilation sans avertissement, tests verts, **l'application se lance**, un commit Git (« Étape N – … »), et une mise à jour du README.

| Étape | Contenu | Résultat visible |
| --- | --- | --- |
| **1. Squelette** | `git init` dans `app/`, solution et 4 projets, Directory.*.props, DI, MainWindow avec navigation et écrans vides, thème Fluent clair/sombre, gestion globale des erreurs, journal dans `%LOCALAPPDATA%`, README | une fenêtre qui navigue entre les écrans |
| **2. Données et import** | migrations SQLite, modèles, ValidateurPack, ImportateurPacks (scan au démarrage + manuel), rapport d'import, **Bibliothèque** (arbre + compteurs), pack d'exemple dans `samples/`, tests de validation et d'import | les packs apparaissent dans la bibliothèque |
| **3. Lecture** | WebView2 partagée, `Web/` + vendor (markdown-it, KaTeX, highlight.js), écran **Lecture** (sommaire, pages, thème), **Voir le PDF** à la bonne page | on lit un cours avec ses formules |
| **4. Révision** | FSRS + tests de référence, ConstructeurSession, écran **Session** (3 types, clavier, intervalle sous chaque bouton, remise en file des ratées), **Fin de session**, « Réviser cette séance » (US12), « Revoir la notion » (US7) | on révise et les cartes sont replanifiées |
| **5. Accueil et gamification** | **Accueil** complet, XP, niveaux, série hebdomadaire, objectif minimal, plafond, nouvelles cartes/jour, **mode reprise**, écran **Réglages**, tests | l'application devient le point de départ quotidien |
| **6. Statistiques** | régularité, réussite par matière, maîtrise, points faibles + session ciblée | US10 couverte : MVP complet |
| **7. Finition** | sauvegarde quotidienne automatique, performances (démarrage, virtualisation), accessibilité clavier, messages relus (aucun ton culpabilisant), `dotnet publish` (dossier + raccourci), README d'installation et captures pour le portfolio | version 1.0 utilisable au quotidien |

Ensuite (V2, hors de ce plan) : badges, rappels surprise (notifications Windows), cartes de code, mode examen, Pomodoro, compte à rebours, optimiseur FSRS à partir de l'historique.

---

## 10. Points d'attention pour la session « Packs »

- Les `id` de cartes et de sections doivent être **uniques dans tous les packs** (préfixer par l'id du pack, comme dans l'exemple de `FORMAT.md`) : un id déjà pris par un autre pack fait refuser le pack.
- Pour que l'avertissement « notion sans carte » soit utile, renseigner `sections[].notions` (sinon l'application ne peut pas relier une carte à une notion).
- `difficulte`, `choix` (2 à 5) et `bonneReponse` sont contrôlés strictement.
