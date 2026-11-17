Application Windows de révision pour le BTS SIO.

## Installer
Télécharge **Atome-Setup-x.y.z.exe**, lance-le et suis l'assistant. Rien d'autre à installer.
Une version sans installation est aussi disponible : **Atome-portable.zip** (décompresse, puis ouvre `Atome.exe`).
Pour mettre à jour, installe la nouvelle version par-dessus : tes données sont conservées.
Si Atome a été installé avec le setup, il propose lui-même les nouvelles versions (bandeau en bas de l'accueil).
`Atome-Setup-x.y.z.exe.sha256` est l'empreinte de sécurité du setup : Atome la vérifie avant d'installer.

## Windows affiche un avertissement ?
L'application n'est pas encore signée numériquement, donc Windows se méfie. C'est normal.
- Écran bleu « Windows a protégé votre ordinateur » : clique sur **Informations complémentaires**, puis **Exécuter quand même**.
- Si le **Contrôle intelligent des applications** bloque l'installation, tu peux le désactiver le temps de l'installer (Sécurité Windows → Contrôle des applications et du navigateur), ou utiliser le ZIP portable.
Le code est entièrement visible dans ce dépôt si tu veux vérifier.

## Pour commencer
Atome est livré avec **440 cours libres**, basés sur le référentiel officiel du programme de 1ère année de BTS SIO option SLAM, pour commencer tout de suite (voir « Nouveautés »). Pour réviser tes propres cours, lis la section « Créer ses propres packs » du README ; le pack d’exemple (`samples/exemple.json`) permet aussi d’essayer l’application.

## Nouveautés
- **Choisis ta révision** : sur l'accueil, la « Session du jour » (recommandée) ou « Autre révision… » : une matière, une partie, tes points faibles, ce que tu viens de lire, ou la préparation du prochain devoir. Chaque choix affiche son nombre de cartes et une durée estimée.
- **Cartes expliquées** : le motif de chaque carte (pourquoi elle est proposée) et le chemin « Matière › Partie › Séance » s'affichent au-dessus.
- **Cours non lus** : Atome ne propose plus de nouvelles cartes d'un cours que tu n'as pas ouvert (réglage disponible), avec un bloc « À lire pour débloquer » sur l'accueil.
- **« Encore 5 cartes »** : tu peux continuer par paquets de 5 même quand la limite du jour est atteinte.
- **Collections** : affiche et révise seulement les packs que tu choisis (cours libres, mes packs, contenu importé). Ta progression est conservée quand tu masques une collection.
- **Cours libres** : nettoyage des cartes d'organisation d'épreuve.
