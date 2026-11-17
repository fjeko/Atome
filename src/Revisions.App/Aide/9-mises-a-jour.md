# Mises à jour

Atome peut te prévenir quand une nouvelle version est publiée, et l'installer pour toi.

### La vérification au démarrage

Quand le réglage **Vérifier les mises à jour au démarrage** est activé (c'est le cas au départ), Atome demande à **GitHub** (`api.github.com`), **une fois par jour au plus**, quel est le numéro de la dernière version publiée.

- C'est la **seule** connexion à Internet d'Atome.
- Rien de personnel n'est envoyé : ni tes données, ni ton nom, ni tes réglages. GitHub voit seulement une demande anonyme venant de ta connexion, comme pour n'importe quelle page web.
- Elle se fait en arrière-plan, après l'ouverture : elle ne ralentit jamais le démarrage.
- Sans Internet, ou si GitHub ne répond pas, il ne se passe rien (une ligne est notée dans `journal.log`).

Pour la désactiver : **Réglages** → **Mises à jour** → décoche la case. Le bouton **Vérifier maintenant** reste disponible pour vérifier quand tu veux.

### Le bandeau « nouvelle version disponible »

Il apparaît en bas de l'**accueil** (jamais pendant une révision), avec un résumé des nouveautés et trois boutons :

| Bouton | Effet |
| --- | --- |
| **Mettre à jour** | télécharge la nouvelle version et l'installe |
| **Plus tard** | ferme le bandeau ; il reviendra au prochain lancement |
| **Ignorer cette version** | cette version ne sera plus proposée (les suivantes, si) |

### Ce qui se passe quand tu cliques sur « Mettre à jour »

1. Atome télécharge l'installateur (tu vois la progression, et tu peux **Annuler**).
2. Il vérifie son **empreinte de sécurité** (SHA-256) : si le fichier est abîmé ou ne correspond pas exactement à celui publié, l'installation est **refusée**.
3. Il fait une **copie de sauvegarde** de ta base (`sauvegardes\avant-maj-<version>.db`), puis se ferme.
4. L'installateur travaille quelques secondes, puis **Atome se rouvre tout seul** dans la nouvelle version.

Tes données (`revisions.db`, sauvegardes, réglages) ne sont **jamais touchées** par l'installateur : elles sont dans un dossier à part.

### Version portable ou version de développement

La mise à jour automatique ne marche que si Atome a été installé avec **Atome-Setup**. Avec la version portable (ZIP), le bouton s'appelle **Télécharger** : il ouvre la page de la nouvelle version sur GitHub. Télécharge le nouveau ZIP et décompresse-le à la place de l'ancien (tes données ne sont pas dans ce dossier, elles ne bougent pas).

### Si Windows bloque l'installation

Atome n'est pas signé numériquement, donc Windows peut se méfier de l'installateur :

- **« Windows a protégé votre ordinateur »** (SmartScreen) : clique sur **Informations complémentaires**, puis **Exécuter quand même**.
- **Contrôle intelligent des applications** : il peut bloquer l'installateur sans proposer de le lancer.

Si l'installation n'a pas pu se faire, Atome s'est peut-être déjà fermé. Rien n'est perdu :

1. **Relance Atome** comme d'habitude (menu Démarrer ou raccourci du Bureau) : l'ancienne version est toujours là, avec toutes tes données. Atome te dira que la mise à jour n'a pas abouti.
2. Pour installer quand même la nouvelle version, **télécharge le setup à la main** : https://github.com/fjeko/Atome/releases/latest, puis lance `Atome-Setup-x.y.z.exe`. Il s'installe par-dessus l'ancien, tes données sont conservées.
3. Si le Contrôle intelligent des applications bloque aussi ce setup, utilise la **version portable** (`Atome-portable.zip`) de la même page.

### En cas de souci

Toutes les étapes sont notées dans `journal.log` (dossier des données, bouton **Ouvrir** dans **Réglages**). Si une mise à jour a mal tourné, la copie `avant-maj-<version>.db` permet de revenir en arrière (voir la page **Mes données et les sauvegardes**).
