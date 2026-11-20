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
- **Cartes de code** : une vraie zone d'éditeur en session et dans « Lire puis contrôler » (Entrée garde l'indentation, Tab et Maj+Tab indentent, Ctrl+Entrée affiche la correction), puis ton code s'affiche à côté de la correction.
- **Police du code** sans ligatures : `!=` ne s'affiche plus en `=` barré.
- **Recherche de la Bibliothèque** : elle ne ralentit plus la saisie.
- **28 cours libres corrigés** : les cartes qui supposaient le code d'un exercice absent sont devenues autonomes (ta progression est conservée).
- Aucune donnée ni progression touchée.
