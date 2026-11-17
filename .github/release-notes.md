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
Atome est livré avec **440 cours libres** pour commencer tout de suite (voir « Nouveautés »). Pour réviser tes propres cours, lis la section « Créer ses propres packs » du README ; le pack d'exemple (`samples/exemple.json`) permet aussi d'essayer l'application.

## Nouveautés
- **Cours libres** : 440 cours originaux (licence CC BY-SA 4.0) livrés avec Atome ; proposés au premier lancement, interrupteur dans les Réglages.
- **Mise à jour automatique** : Atome propose lui-même les nouvelles versions. **C'est la dernière installation à faire à la main** : les prochaines se feront depuis l'application.
- **Signalement d'erreurs** : si quelque chose plante, tu peux envoyer le rapport en un clic.
- **TD en groupe** : le même TD à plusieurs, chacun sur son PC, avec un code à partager.
- **Planning** : import direct de l'Excel de planning de l'organisme de formation.
- **Démarrage** : un accueil qui règle les premières cartes sur ton niveau.
- **Partage** : export et import de ton contenu dans un seul fichier `.atome`.
