# VISION.md - Ce que RawTandem doit faire

Document de référence sur le but de l'application. Il évolue avec les décisions de l'utilisateur :
toute décision importante prise en conversation doit s'y refléter.

## Le besoin

Je prends des photos avec un appareil photo (Panasonic Lumix TZ200) qui sort chaque cliché
en deux fichiers : un JPG (visualisable) et un RAW `.RW2` (même nom de base, ex. `P1230001.JPG`
+ `P1230001.RW2`). Trier ces photos à la main est fastidieux : deux fichiers par photo,
et les outils classiques ne manipulent jamais le couple d'un seul geste.

## Ce que fait l'application

Un trieur de photos qui considère chaque cliché comme **un couple JPG+RAW** :

- **Visualiser les JPG** d'un dossier (jamais les RAW, trop lourds).
- **Supprimer un couple** JPG+RAW en une seule manipulation (vers la corbeille OS, récupérable).
- **Déplacer un couple** JPG+RAW en une seule manipulation (modalité à définir : destinations
  nommées, sélection à la volée, ou destination fixe).

## Décisions prises

| Sujet | Décision |
|---|---|
| Vocabulaire | Un couple JPG+RAW s'appelle un **tandem** (UI comme code) |
| Appariement | Même stem de nom de fichier ; extensions sans casse |
| Emplacement après organisation | JPG à la racine du dossier, RAW dans le sous-dossier `RAW/` |
| Workflow | 3 phases : accueil → organisation (déplacement des RAW, avec confirmation) → tri |
| Cible RAW | `.RW2` uniquement (autres formats en évolution, voir `docs/TODO.md`) |
| Suppression | Corbeille OS, jamais de suppression définitive |
| Ouverture de dossier | Sélecteur de dossier natif Avalonia (`StorageProvider.OpenFolderPickerAsync`) ; aucun message si l'utilisateur annule |
| Scan du dossier | **Racine uniquement** : les sous-dossiers (dont un `RAW/` déjà organisé) sont ignorés |
| Déplacement (organisation) | **RAW des tandems appariés uniquement** ; les orphelins sont signalés dans le résumé mais laissés en place (action au fichier seul : à décider plus tard) |
| Collisions | Un RAW déjà présent dans `RAW/` n'est **jamais écrasé** : signalé et ignoré, au plan comme à l'exécution |
| Après un déplacement réussi | Statut de confirmation (`✓ N RAW déplacés…`) puis **passage automatique au tri** ; en cas d'échec partiel, on reste à l'organisation et chaque échec est signalé |
| Déplacement des tandems (tri) | Non décidé — la décision est attendue de l'utilisateur |
| Sécurité des données | Le Core calcule un plan d'opérations, l'UI n'exécute que ce plan |
| Orphelins | JPG sans RAW / RAW sans JPG : affichés dans le résumé du scan et gérés explicitement (action dédiée à implémenter) |
| Architecture | `src/RawTandem.Core` = domaine pur, réutilisable par n'importe quel front (console, web, GUI), **sans Elmish ni Avalonia** ; la machine à états Elmish (`Msg`, `init`, `update`, `Status`) vit dans `src/RawTandem.UI/State.fs` ; tests Expecto dans `tests/` en miroir |
| Confirmations | Une action destructive en attente est un écran `Confirming of Phase * PendingAction`, pas un champ optionnel : une confirmation sans action en attente est signalée à l'utilisateur, jamais avalée |
| Garde des actions | Les messages (navigation, suppression) ne s'appliquent qu'à leur écran, la garde est dans `update` (tout producteur de messages est couvert, raccourcis clavier inclus) |
| Raccourcis clavier | ← / → pour la navigation, `Suppr` pour demander la suppression d'un tandem |

## Hors périmètre (pour l'instant)

- Développement/retouche photo, gestion de catalogue.
- Support d'autres formats RAW (CR2, CR3, NEF, ARW, DNG…) — listé en évolution.
- Corbeille OS sur Windows/macOS — Linux d'abord.

## Méthode de travail

Développement **itératif avec l'utilisateur** : il demande une implémentation simple, valide
ou fait un retour ; les demandes trop grosses sont redécoupées. Approche TDD par défaut
(rouge/vert), tests en miroir de `src/`, documentation en français, code en anglais.
