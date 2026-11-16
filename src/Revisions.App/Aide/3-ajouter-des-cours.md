# Ajouter des cours

Un cours arrive dans l'application sous la forme d'un **pack** : un fichier `.json` qui contient le cours découpé en sections, et les cartes de révision qui vont avec. Un pack = une séance.

### Le dossier des packs

Au démarrage, l'application lit tout le dossier des packs et importe ce qui est nouveau ou modifié :

`{{DOSSIER_PACKS}}`

Tu peux changer ce dossier dans **Réglages**. Les packs y sont rangés par matière et par partie, mais ce rangement est libre : c'est le contenu du fichier qui compte.

### Trois façons d'ajouter un pack

1. **Le déposer dans le dossier des packs**, puis relancer l'application : il est importé tout seul.
2. Si l'application est déjà ouverte : **Bibliothèque → Actualiser** relit le dossier.
3. **Bibliothèque → Importer…** pour choisir un ou plusieurs fichiers rangés n'importe où.

### Ce qui se passe à l'import

- Un pack déjà importé et inchangé est simplement ignoré.
- Un pack **modifié** est mis à jour, et **ton historique est conservé** : les cartes que tu as déjà révisées gardent leur calendrier.
- Une carte qui a disparu du pack n'est pas supprimée, elle est mise de côté.
- Si un pack contient une erreur, il est **refusé** et un rapport t'indique le fichier, le champ et la carte en cause. Les autres packs sont importés normalement.

### Et les nouvelles cartes ?

Elles ne tombent pas toutes d'un coup. Elles arrivent au rythme réglé dans **Réglages** (10 par jour par défaut), en commençant par les séances que tu as déjà ouvertes en lecture.

Pour travailler une séance précise tout de suite : ouvre-la et clique sur **Réviser cette séance**.
