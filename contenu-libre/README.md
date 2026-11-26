# Cours libres BTS SIO – 1re année

440 séances de cours avec leurs cartes de révision (questions, QCM, exercices), **originales** et sous licence
**CC BY-SA 4.0**. Elles sont livrées avec Atome pour que tu aies de quoi réviser dès l'installation.

Programme couvert (BTS SIO option SLAM, 1re année) : Bloc 1 (support et services), Bloc 2 (SLAM), Bloc 3
(cybersécurité), algorithmique appliquée, mathématiques, anglais, CEJM (culture économique, juridique et managériale).

## Comment ces cours ont été écrits (méthode « salle blanche »)

Le but : des cours que n'importe qui peut réutiliser, **sans reprendre le texte d'un organisme de formation**.

1. **Fiches abstraites.** Une première étape ne garde que *ce qui est enseigné et évalué* : liste des notions,
   savoir-faire, types d'exercices génériques, pièges fréquents, découpage conseillé. Rien du texte d'origine :
   ni phrases, ni exemples, ni données, ni titres.
2. **Rédaction originale.** Les packs sont ensuite écrits à partir des seules fiches, du référentiel officiel du
   BTS SIO (public) et de connaissances générales. Cette étape n'a jamais accès aux cours d'origine. Explications,
   exemples, entreprises fictives, données, code et exercices sont inventés.
3. **Contrôle de similarité.** Comparaison automatique, par suites de mots, avec les cours d'origine : toute
   suite de 8 mots consécutifs ou plus en commun (hors termes techniques et formules standard) est réécrite.
   Premier contrôle : 457 suites dans 108 packs ; environ 175 passages réécrits ; au dernier contrôle, il ne reste
   que 181 suites (dans 38 packs), toutes examinées et gardées pour une raison valable : textes de loi cités avec
   leur référence, intitulés officiels du référentiel, définitions mathématiques standard, noms d'interface
   imposés, énumérations banales.

Les cours d'origine, les fiches intermédiaires et les rapports de contrôle ne sont **pas** publiés.

## Anglais général (A1 → C1)

Le dossier `anglais/` contient un parcours d'anglais général, du niveau A1 au C1. Il comprend 65 séances, des
modules de méthode pour l'oral et un test de positionnement. Il est rédigé de zéro et ne reprend aucun manuel ni
aucune plateforme. Atome le charge à chaque démarrage dans sa propre collection, « Anglais ». Ses cartes entrent
dans les révisions seulement quand tu lis la séance. La même licence s'applique.

## Licence

[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/deed.fr) : tu peux copier, partager et adapter ces
cours, même commercialement, en citant **« Atome – packs libres BTS SIO »** et en diffusant tes modifications sous la
même licence. Détails dans [`LICENSE`](LICENSE). Chaque pack le rappelle au début de son résumé.

## Signaler une erreur

Ces cours sont écrits par une IA et relus par un étudiant : **ils peuvent contenir des erreurs**.

- Dans Atome : clique sur le petit drapeau d'une carte ou d'une section (touche **S** en session), puis exporte
  tes signalements depuis **Réglages → Mes signalements**.
- Sur GitHub : ouvre un [Issue](../../../issues) avec le nom de la séance et ce qui te semble faux.

## Organisation et mises à jour

Les fichiers sont rangés par matière, puis par compétence du référentiel (B1.1, B2.3…) et par thème numéroté :
`packs/<Matière>/<Compétence>/<Thème>/<séance>.json`. Dans le champ `partie`, la compétence et le thème sont séparés
par « › » (« B1.1 – Gérer le patrimoine informatique › Thème 1 – … »). Le format des fichiers est décrit dans
[`docs/FORMAT.md`](../docs/FORMAT.md). Les identifiants commencent par `libre-` et restent stables : une mise à jour
d'Atome met à jour ces cours **sans toucher à ta progression**.
