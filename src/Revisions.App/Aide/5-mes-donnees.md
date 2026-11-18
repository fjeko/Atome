# Mes données et les sauvegardes

Tout reste sur ton ordinateur. L'application fonctionne sans Internet et n'envoie jamais tes données.
Sa seule connexion : demander à GitHub, à chaque lancement (une fois par heure au plus), s'il existe une nouvelle version (voir la page **Mises à jour** ; tu peux la désactiver dans **Réglages**).

### Où sont mes données ?

Dans ce dossier :

`{{DOSSIER_DONNEES}}`

| Fichier | Contenu |
| --- | --- |
| `revisions.db` | la base : cours importés, calendrier des cartes, historique, XP, réglages |
| `sauvegardes\` | les copies automatiques de la base |
| `journal.log` | les erreurs éventuelles (utile si quelque chose ne marche pas) |

Le bouton **Ouvrir** de l'écran **Réglages** t'y emmène directement.

### Les sauvegardes automatiques

Chaque jour où tu ouvres l'application, une copie de la base est faite dans `sauvegardes\`. Les **7 dernières** sont gardées, les plus anciennes sont supprimées.

### Restaurer une sauvegarde

1. Ferme l'application.
2. Dans le dossier des données, renomme `revisions.db` (par exemple en `revisions-ancien.db`).
3. Copie la sauvegarde voulue depuis `sauvegardes\` et renomme-la `revisions.db`.
4. Relance l'application.

### Changer d'ordinateur

Copie le dossier des données sur le nouvel ordinateur, au même endroit. Les packs, eux, sont dans le dossier des packs : copie-le aussi, ou choisis son nouvel emplacement dans **Réglages**.

### Supprimer un pack

Retirer un fichier du dossier des packs **ne supprime rien** dans l'application : le cours et ton historique restent. C'est voulu, pour ne jamais perdre de travail par accident.
