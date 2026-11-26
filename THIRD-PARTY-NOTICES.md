# Composants tiers

Atome est sous licence MIT (voir [LICENSE](LICENSE)). Les cours libres sont sous CC BY-SA 4.0
(voir [contenu-libre/LICENSE](contenu-libre/LICENSE)). Ce fichier liste les composants d'autres auteurs.

## Voix anglaises naturelles (téléchargées une fois, hors du setup)

Au premier lancement, Atome télécharge en arrière-plan, dans `%LOCALAPPDATA%\RevisionsBTS\voix\piper-1\`,
les cinq fichiers ci-dessous. Ils sont hébergés dans la Release
[`voix-piper-1`](https://github.com/fjeko/Atome/releases/tag/voix-piper-1) du dépôt (copies identiques des
originaux, avec les mêmes empreintes SHA-256), avec repli sur leurs adresses d'origine.

| Fichier | Taille (octets) | SHA-256 |
| --- | --- | --- |
| `piper_windows_amd64.zip` | 22 477 236 | `f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea` |
| `en_GB-cori-high.onnx` | 114 219 352 | `470b4dd634c98f8a4850d7626ffc3dfc90774628eeef6605a6dd8f88f30a5903` |
| `en_GB-cori-high.onnx.json` | 4 963 | `9e7fb5b5671612c22f3c81cbe46c1ae87b031a4632bcb509e499dad6f1e2adec` |
| `en_US-norman-medium.onnx` | 63 531 379 | `b9739443232a80a59c7d18810dd856899bf16a7964725f5ab81ea49b1351cb71` |
| `en_US-norman-medium.onnx.json` | 4 968 | `6c2db7f558a4a8deb9fe822583c1c5105f6c4e834dd0f9de8ad17a888ee9fe1d` |

### Moteur : Piper 2023.11.14-2 (`piper_windows_amd64.zip`)

- Origine : <https://github.com/rhasspy/piper/releases/tag/2023.11.14-2> (dépôt archivé, version figée).
- Contenu et licences :
  - `piper.exe` : Piper, © Michael Hansen, licence **MIT** ;
  - `piper_phonemize.dll` : piper-phonemize, © Michael Hansen, licence **MIT** ;
  - `espeak-ng.dll` et `espeak-ng-data` : eSpeak NG (fork rhasspy/espeak-ng), licence **GPL-3.0 ou ultérieure** ;
  - `onnxruntime.dll` : ONNX Runtime 1.14.1, © Microsoft, licence **MIT** ;
  - `libtashkeel_model.ort` : modèle libtashkeel (arabe, inutilisé par Atome), licence **MIT**.
- Parce qu'il contient eSpeak NG, ce paquet se redistribue selon la **GPL-3.0**. Atome ne fait que lancer
  `piper.exe` comme un programme séparé (il lui envoie le texte et lit les fichiers WAV produits) : ils ne sont pas
  liés entre eux, Atome garde donc sa licence MIT.
- Code source correspondant (GPL-3.0, section 6), publié dans la même Release `voix-piper-1` :
  `piper-2023.11.14-2-source.zip`, `piper-phonemize-2023.11.14-4-source.zip` et `espeak-ng-rhasspy-0f65aa3-source.zip`
  (copies de <https://github.com/rhasspy/piper>, <https://github.com/rhasspy/piper-phonemize> et
  <https://github.com/rhasspy/espeak-ng>, commit `0f65aa3` du 13/09/2023, celui que contenait cette version de Piper).
  Le texte de la GPL-3.0 est dans `COPYING` de l'archive eSpeak NG.
- Pourquoi cette version : la branche récente (OHF-Voice/piper1-gpl, GPL-3.0) n'est publiée que sous forme de
  paquet Python, sans exécutable Windows autonome.

### Voix

Origine : dépôt <https://huggingface.co/rhasspy/piper-voices> (licence MIT), révision figée
`5ec43b186ef915a195d3ddbcfd9916e76c503c3f`. La fiche de chaque voix (`MODEL_CARD`) est jointe à la Release.

- **Cori** (`en_GB-cori-high`, anglais britannique, voix féminine, voix par défaut) : entraînée de zéro par
  Bryce Beattie (<https://brycebeattie.com/files/tts/>) sur environ 24 h d'enregistrements de
  [LibriVox](https://librivox.org), dans le **domaine public** (lectrice : Cori Samuel).
- **Norman** (`en_US-norman-medium`, anglais américain, voix masculine, pour le 2e personnage des dialogues) :
  entraînée de zéro par Bryce Beattie sur environ 15,5 h d'enregistrements LibriVox, dans le **domaine public**.
- Aucune donnée « recherche uniquement » ou « non commerciale » dans leur lignée.
- Voix écartées : les voix récentes (« Piper 1.5 », dont `cori-medium`, `northern_english_male`, `vctk`) ne marchent
  pas avec ce moteur ; `semaine`, `hfc_male`, `ryan` (données CC BY-NC) ; `alan` (licence non précisée, base `lessac`).

## Composants web embarqués (`src/Revisions.App/Web/vendor`)

- markdown-it — MIT ; KaTeX — MIT ; markdown-it-texmath — MIT ; highlight.js — BSD-3-Clause.
