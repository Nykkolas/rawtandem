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
| Suppression (implémentation) | Le Core calcule un plan (`Deletion.planDeletion` : chemins complets du JPG à la racine et du RAW selon son emplacement, donné par la requête du scan `FolderScan.findTriableTandem`), l'UI l'exécute via `gio trash` (freedesktop), fichier par fichier : l'échec d'un fichier n'interrompt pas l'autre, ni ne retire le tandem du scan ; suppression complète confirmée dans le bandeau bas, le tandem quitte le scan et l'index s'ajuste sur la liste visible |
| Ouverture de dossier | Sélecteur de dossier natif Avalonia (`StorageProvider.OpenFolderPickerAsync`) ; aucun message si l'utilisateur annule |
| Changement de dossier | « Ouvrir un autre dossier… » disponible sur tous les écrans où un dossier est actif (organisation, tri) ; une question affichée doit être tranchée d'abord (jamais refermée par la navigation) ; l'écran courant reste pendant l'analyse du nouveau dossier |
| Scan du dossier | **Racine + sous-dossier `RAW/`** ; les autres sous-dossiers sont ignorés en silence |
| Organisation initiale (itération 3) | Toutes les combinaisons sont classées par l'arbre de décision (9 catégories + anomalies du dossier RAW). Le bouton Organiser propose de remettre dans l'ordre : RAW de la racine (appariés **et orphelins**) → `RAW/`, JPG de `RAW/` (croisés, inversés, orphelins) → racine. Jamais d'écrasement : renommage de secours « stem (n).ext » |
| Doublons (même nom racine et `RAW/`) | Le fichier hors de sa place rejoint sa place **renommé selon la convention système** (« stem (1).ext », incrémenté) ; jamais détruit ni écrasé ; signalé en orange ; action dédiée avec confirmation |
| RAW doublé sans JPG (racine + `RAW/`) | Le doublon gagne seul : signalé uniquement en orange comme doublon, sans ligne d'orphelin ; après résolution, les deux copies se révèlent comme RAW orphelins rangés (rouge) |
| Dossier « raw » mal cassé | Signalé orange, renommage « raw » → « RAW » proposé avec confirmation (si « RAW » n'existe pas) ; Organiser désactivé tant que l'anomalie est présente (casse ou « RAW » fichier) : organiser pendant l'anomalie créerait un second dossier « RAW/ » à côté de « raw » et rendrait son contenu invisible, donc renommer d'abord est le seul chemin vers l'organisation |
| Orphelins | Signalés en rouge (nom seul pour l'instant, gestion avancée au TODO), mais pris en compte par l'organisation ; jamais avalés en silence |
| Dossier déjà organisé | Plan vide, aucun doublon ni anomalie : message dédié, passage direct au tri proposé |
| Après une organisation réussie | Statut de confirmation puis **passage automatique au tri** (seulement depuis l'écran d'organisation, jamais par-dessus une question en attente) ; en cas d'échec partiel, on reste à l'organisation et chaque échec est signalé |
| Déplacement des tandems (tri) | Non décidé — la décision est attendue de l'utilisateur |
| Sécurité des données | Le Core calcule un plan d'opérations, l'UI n'exécute que ce plan |
| Architecture | `src/RawTandem.Core` = domaine pur, réutilisable par n'importe quel front (console, web, GUI), **sans Elmish ni Avalonia** ; la machine à états Elmish (`Msg`, `init`, `update`, `Status`) vit dans `src/RawTandem.UI/State.fs` ; tests Expecto dans `tests/` en miroir |
| Confirmations | Une action destructive en attente est un écran `Confirming of Phase * PendingAction`, pas un champ optionnel : une confirmation sans action en attente est signalée à l'utilisateur, jamais avalée. Réciproquement, une question affichée n'est jamais refermée par un résultat d'arrière-plan (échec comme réussite) : annuler reste le choix de l'utilisateur, le bandeau bas reflète le résultat |
| Garde des actions | Les messages (navigation, suppression) ne s'appliquent qu'à leur écran, la garde est dans `update` (tout producteur de messages est couvert, raccourcis clavier inclus) |
| Raccourcis clavier | ← / → pour la navigation, `Suppr` pour demander la suppression d'un tandem |
| Couverture de code | Via `coverlet.collector` + `YoloDev.Expecto.TestSdk` (tests exécutés par `dotnet test`, pas d'EXE dédié — préférence transverse consignée dans agents-shared `DECISIONS.md`) ; installée le 2026-10-23, rapport ReportGenerator dans `coveragereport/` |

## Hors périmètre (pour l'instant)

- Développement/retouche photo, gestion de catalogue.
- Support d'autres formats RAW (CR2, CR3, NEF, ARW, DNG…) — listé en évolution.
- Corbeille OS sur Windows/macOS — Linux d'abord.

## Méthode de travail

Développement **itératif avec l'utilisateur** : il demande une implémentation simple, valide
ou fait un retour ; les demandes trop grosses sont redécoupées. Approche TDD par défaut
(rouge/vert), tests en miroir de `src/`, documentation en français, code en anglais.
