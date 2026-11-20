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

### Sauvegarder ma progression en un clic

**Réglages → Données → Sauvegarder ma progression…** crée un seul fichier `.atome-sauvegarde` avec ton historique de révisions, ton XP, tes réglages, tes acquis, ton planning et tes dates personnelles. Idéal pour **changer d'ordinateur** ou te mettre à l'abri (clé USB, OneDrive…).

Pour la retrouver sur un autre PC : installe Atome, puis **Restaurer…** et choisis le fichier. Atome vérifie le fichier, garde une **copie de sécurité** de ta progression actuelle (dans `sauvegardes\`, fichiers `avant-restauration-…`), la remplace, puis redémarre tout seul. Les cours viennent des packs (ou des cours libres) : rien d'autre à copier.

Tu peux aussi choisir un **dossier de sauvegarde automatique** (facultatif) : une copie par jour d'utilisation, les 5 dernières sont gardées. Si la clé USB est débranchée, Atome continue sans rien dire.

### Restaurer à la main une copie automatique

1. Ferme l'application.
2. Dans le dossier des données, renomme `revisions.db` (par exemple en `revisions-ancien.db`).
3. Copie la sauvegarde voulue depuis `sauvegardes\` et renomme-la `revisions.db`.
4. Relance l'application.

### Changer d'ordinateur

Le plus simple : **Sauvegarder ma progression…** puis **Restaurer…** (voir plus haut). Les packs, eux, sont dans le dossier des packs : copie-le aussi, ou choisis son nouvel emplacement dans **Réglages**.

### Supprimer un pack

Retirer un fichier du dossier des packs **ne supprime rien** dans l'application : le cours et ton historique restent. C'est voulu, pour ne jamais perdre de travail par accident.
