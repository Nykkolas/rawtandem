# PROPOSITION - Passe complète sur les actions asynchrones

Statut : **presque clos** — la passe a été réalisée les 2026-10-28/29 : RC-06
(règle validée par l'utilisateur) implémentée pour **tous** les vols (garde dans
`update` + grisage des widgets), garde RC-05 pour la sélection de dossier,
modalité du picker, statuts jamais avalés (Majeur 8 réglé : `OrganizePlanningFailed`
et `OrganizeNothingToMove` écrivent toujours le bandeau bas). Reste ouvert : P4
(garde d'écran de F5 à l'accueil). À supprimer une fois P4 traité.

## Suivi de traitement (2026-10-29)

| Préconisation | État |
|---|---|
| 1. RC « opération en vol » (garde + grisage) | ✅ RC-06 (`docs/usecases/regles-communes.md`, coverage générique) : drapeau `InFlight` dans `State`, garde centralisée `isBlockedGesture` dans `update`, grisage générique dans la vue — tous les vols couverts (plan, analyse, réanalyses, exécution, suppression) |
| 2. Extension UC « recliquer Ranger pendant le rangement » | ✅ couvert par RC-06 sans extension UC (test générique « un second rangement est sans effet pendant le vol ») |
| 3. Course du picker (UC/RC) | ✅ traitée **sans nouvelle règle** : RC-05 la couvre par principe ; garde dans `update` (no-op silencieux, actions du bandeau exemptées) + modalité du picker (fenêtre désactivée pendant le dialogue, `Main.fs`) — le chemin utilisateur est fermé d'amont par RC-06 (ouverture refusée pendant le vol du plan), la garde reste le filet |
| 4. Marqueur d'opération en vol | ✅ champ `InFlight` + DU `InFlightOperation` (`AnalyzingFolder`/`ReanalyzingFolder` identifiées par requestId, `PlanningOrganize`, `OrganizeExecution`, `TandemDeletion`) — mécanisme retenu différent du cas d'écran `Operating` proposé, même intention ; les résultats dépassés lèvent le drapeau qu'ils retrace |
| 5. Statuts avalés pendant une question (Majeur 8) | ✅ **traité (P3, 2026-10-29)** : `OrganizePlanningFailed` et `OrganizeNothingToMove` écrivent toujours le bandeau bas, quel que soit l'écran affiché (question comprise), sans jamais toucher l'écran ni la question — branches avalantes supprimées ; 2 tests unitaires convertis au contrat annoncé + 2 tests nouveaux sous question (rouges avant correction) |
| 6. Garde `FolderSelected` pendant `Confirming` | ✅ filet : no-op silencieux (statut inchangé), 2 tests E2E « folder selection during a … question is refused » + come-back ; les actions du bandeau ne sont jamais bloquées |
| 7. Garde d'écran de `RescanRequested` à l'accueil | ❌ **reste ouvert (P4)** : F5 sans dossier reste accepté à l'accueil (statut « Aucun dossier analysé. »), le geste est spec'd sur l'écran d'organisation |

L'inventaire ci-dessous reste la photographie du 2026-10-28 (avant-traitement) ;
le tableau ci-dessus fait foi pour l'état courant.

Références : `docs/TODO.md` entrée « Passe complète sur les actions asynchrones »
(reste ouvert : garde d'opération en vol — audit Majeur 4 —, statuts gelés par garde
d'écran — audit Majeur 8), `docs/VISION.md` lignes « Résultats d'arrière-plan »,
« Courses de scan », « Résultats d'opérations et changement de dossier ».

## Inventaire exhaustif (vérifié dans State.fs le 2026-10-28)

Les quatre invariants par commande : **identité** embarquée dans le message
(demande/dossier/stem), **fraîcheur** (garde dans `update`), **écran** (jamais
refermer une question), **échec au bandeau** (jamais avalé).

| # | Commande (producteur → messages) | Identité | Fraîcheur | Écran | Échec bandeau | En vol |
|---|---|---|---|---|---|---|
| 1 | Scan (`FolderSelected` → `FolderScanned`/`Failed`) | ✔ requestId + dossier | ✔ | ✔ | ✔ | ✔ relances innocuelles |
| 2 | Rescan (`RescanRequested`, post-organisation → `FolderRescanned`/`Failed`) | ✔ | ✔ | ✔ | ✔ | ⚠ F5 accepté à l'accueil |
| 3 | Plan (`OrganizeRequested` → `OrganizePlanned`/`PlanningFailed`) | ✔ dossier | ✔ | ✔ | **✗ avalé pendant question** | ✗ relançable |
| 4 | Exécution (`OrganizeConfirmed` → `OrganizeCompleted`/`NothingToMove`/`Failed`) | ✔ dossier | ✔ | ✔ | **✗ `NothingToMove` avalé pendant question** | ✗ double exécution (Majeur 4) |
| 5 | Suppression (`DeleteTandemConfirmed` → `DeleteTandemCompleted`/`Failed`) | ✔ dossier + stem | ✔ | ✔ | ✔ | ✗ double `Suppr` même stem |
| 6 | JPG (`loadCurrentJpgCmd` → `JpgLoaded`/`LoadFailed`) | ✔ dossier + stem | ✔ | ✔ | ✔ | ✔ dernier gagne |
| 7 | Statut éphémère (`autoHideStatusCmd` → `StatusHidden`) | n/a | n/a | ✔ gardé `DisplayMode` | n/a | limitation documentée |
| 8 | **`pickFolder`** (Main.fs, hors Cmd → `FolderSelected`) | n/a | n/a | **✗ non gardé contre question** | n/a | course du picker |

Constat global : identité et fraîcheur sont partout (le chantier 2026-10-28 a porté
ses fruits). Ce qui reste : la **vie en vol** (#3, #4, #5), **deux avalages de
statut** pendant une question, la **course du picker**, et un garde-fou mineur sur F5.

## Préconisations (à arbitrer)

### À se répercuter dans les UC / RC (plume de l'utilisateur)

1. **Nouvelle RC, prolongement de RC-05, formulée par principe sans énumération** :
   > Pendant qu'une opération de l'application est en cours (analyse d'un dossier
   > demandée, rangement, suppression), toute action de l'écran est sans effet et se
   > voit grisée. La garde vit dans `update` ; le grisage est l'affordance.

   Même structure que RC-05 (« aucune action tant qu'il est affiché, hors
   accepter/refuser »), qui n'énumère pas et n'a pas bougé quand des actions ont été
   ajoutées depuis. Une action ajoutée plus tard suitra le pattern existant et sera
   couverte sans retoucher la règle — c'est le critère qui rend la RC durable.
2. **Extension UC organiser-et-trier-dossier** (ex. 5a) : « l'utilisateur reclique
   "Ranger les RAW" pendant le rangement → rien ne se passe, l'opération unique se
   termine ». Sans elle, la garde n'a pas de scénario exécutable qui l'éprouve.
3. **Extension RC-05 ou UC 2a-adjacente pour la course du picker** : « un dossier
   choisi dans un picker resté ouvert pendant qu'une question s'affiche → la
   sélection est refusée, la question reste entière ». Jouable par l'interpréteur
   (geste « ouvrir un autre dossier »).

### Le reste (code, tests, docs — implémentable au « go », en TDD)

4. **Marqueur d'opération en vol comme cas d'écran** : `Screen` gagne
   `Operating of phase * operation` avec `RunningOperation` DU
   (`PlanningOrganize of folderPath` / `ExecutingOrganize of folderPath` /
   `DeletingTandem of folderPath * stem`). Posé au lancement, retiré à la
   complétion via les gardes de fraîcheur existantes.
   - **Zéro ligne dans les ~20 gardes existantes** : chaque message ne s'applique
     déjà que sur `Working <son écran>` — un geste sur `Operating` tombe dans le
     no-op existant, comme aujourd'hui sur `Confirming`. Une nouvelle action
     future suit le pattern et est couverte automatiquement : aucune liste à
     maintenir.
   - Prédicat de modèle de vue `isOperationInProgress` (ou
     `screenActionsFrozen` = question OU opération) pour le grisage : les ~8
     `isEnabled (not questionPending)` de la vue deviennent un appel partagé.
5. **Statuts avalés pendant une question** (Majeur 8) : `OrganizePlanningFailed`
   et `OrganizeNothingToMove` mettent à jour le bandeau bas sans toucher l'écran
   pendant `Confirming` — rattrapage de conformité à la règle VISION « jamais
   avalé » (la règle existe déjà, rien à rédiger côté spec).
6. **Garde `FolderSelected` pendant `Confirming`** : sélection refusée + statut
   explicite (« répondez d'abord à la question »). Alternative rejetée : embarquer
   le dossier dans `PendingAction` et exécuter quand même — contredit « la question
   doit être tranchée d'abord » et pourrait ranger les fichiers d'un dossier que la
   question ne concerne pas. Même mécanisme que la RC du point 1 (gel total).
7. **Garde d'écran de `RescanRequested`** : F5 sans effet à l'accueil (le geste est
   spec'd sur l'écran d'organisation, UC 2a/2b — le code s'aligne, pas la spec).
8. **Documentation de la passe** : fermer l'entrée TODO avec l'inventaire
   ci-dessus et épingler dans VISION la règle générale : *toute commande
   asynchrone embarque son identité (demande/dossier/stem), `update` n'applique
   qu'un résultat frais, jamais par-dessus une question, et le bandeau reflète
   toujours l'effet réel*.

Rien à faire pour le scan (#1), le JPG (#6) et le statut éphémère (#7) : déjà
conformes, plafonds et limitations documentés.

## Volume de code estimé (option gel total)

| Endroit | Impact |
|---|---|
| `State.fs` — types | `RunningOperation` DU + cas `Operating` dans `Screen` : ~12 lignes |
| `State.fs` — lancement/fin | ~3 lanceurs + ~7 complétions (retour à `Working phase`) : ~25-35 lignes |
| `State.fs` — gardes existantes | **zéro ligne** (structurel) |
| `State.fs` — modèle de vue | prédicat pure testé : ~5 lignes |
| `Main.fs` — grisage | ~8 remplacements d'un token, zéro ligne nouvelle |
| Tests | ~10 tests unitaires miroir : ~100-150 lignes (le gros du volume) |

Production ~50-60 lignes net ; tests ~le double. Petite passe, pas un chantier.

## Arbitrages ouverts (décisions de l'utilisateur)

- **Gel total vs exceptions** : « toute action » gèle aussi la navigation et
  « Ouvrir un autre dossier » pendant l'opération (opérations courtes, quelques
  secondes). L'alternative — liste d'actions libres — est une énumération à
  maintenir dans la RC, les gardes et le grisage : c'est le piège. Reco 1.0 :
  **gel total, une seule règle, zéro liste**. Si une attente devient longue
  (gros dossier), le TODO « retours visuels pour les opérations longues » sera le
  moment de réintroduire de la liberté — comme une extension, pas une exception
  silencieuse.
- **La RC du point 1** : la rédiger et la valider avant l'implémentation (workflow
  SPEC-UC : UC/RC → questionnement → test relu → go → TDD).
- Confirmation que le scénario 5a (relicage pendant le rangement) est bien le
  parcours à couvrir en premier.
