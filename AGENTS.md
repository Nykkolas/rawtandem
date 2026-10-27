# AGENTS.md - Règles et meilleures pratiques pour ce projet

## 🤝 Mode de collaboration

### Commits et push
- Ne jamais commiter ni pousser sans l'**autorisation explicite** de l'utilisateur : proposer, attendre le oui.

### "Propose-moi" = proposer, ne pas exécuter
- Quand l'utilisateur demande de **proposer** (améliorations, changements, refactor), NE PAS modifier les fichiers.
- Expliquer d'abord ce qui est prévu : fichiers concernés, commandes à exécuter, risques.
- Attendre l'**autorisation explicite** de l'utilisateur avant d'appliquer quoi que ce soit.
- Les demandes directes ("fais", "corrige", "ajoute") restent des autorisations à agir immédiatement.

### "Audit / review du code"
- Lire `docs/AUDIT.md` (checklist spécifique) et `docs/AUDIT-GENERIC.md` (procédure, thèmes, sévérités, format de rapport).
- L'audit ne modifie aucun fichier : recommandations uniquement, sauf demande explicite de corriger.

### "Crée une version x.y.z"
- Équivaut à : `git tag vx.y.z` + `git push origin vx.y.z` (autorisation explicite incluse).
- Le workflow CI exécute les tests puis publie les binaires en GitHub Release (à mettre en place ; voir `docs/TODO.md`).
- Après le push du tag, vérifier le run et la Release obtenue, et rapporter le résultat.

### Workflow "la spec est le UC" (adopté le 2026-10-26, éprouvé sur organiser-et-trier-dossier)
- La boucle complète (UC → questionnement forme/fond sans plume → test E2E relu → « go » →
  implémentation TDD → vérification utilisateur → commit) vit dans `docs/SPEC-UC.md`
  (agents-shared) : la lire avant de démarrer une fonctionnalité ou une correction.
- Spécifique au projet : UC dans `docs/usecases/` (un fichier par UC) ; scénarios exécutables
  dans `tests/RawTandem.UI.Tests/ScenarioTests.fs` avec l'interpréteur `step` (le test ne joue
  que les gestes de l'utilisateur, les commandes Elmish sont exécutées réellement en cascade,
  vraie corbeille OS sur dossier temporaire).

## 🌍 Langue

- **Code en anglais** : identifiants, fonctions, modules, messages Elmish, noms de tests.
- **UI en français** : libellés, statuts, messages affichés à l'utilisateur.
- La documentation (AGENTS.md, AUDIT.md, TODO.md, README) est en français.

## 📚 Règles partagées (agents-shared, à lire selon le contexte)

Ces fichiers sont des symlinks vers `~/Source/agents-shared/` :

- **F# / Elmish** : lire `docs/FSHARP.md` avant d'écrire ou de modifier du F# (pattern Elmish, commandes async, design with types, aléatoire/crypto, casing, tests).
- **Avalonia.FuncUI** : lire `docs/AVALONIA.md` avant de toucher l'UI (DSL, types géométriques, contrôles, feedback, clipboard, fenêtre).
- **Audit générique** : `docs/AUDIT-GENERIC.md`, complété par la checklist locale `docs/AUDIT.md`.
- **Spec = UC** : `docs/SPEC-UC.md`, workflow de spécification par use cases (la boucle complète y vit).
- **Couverture de code** : `docs/COVERAGE-GENERIC.md` (mesure systématique quand tout est vert, écart rapporté depuis la mesure précédente).
- **Erreurs transverses** : `docs/ERREURS-GENERIC.md`, leçons génériques à ne pas reproduire ; y promouvoir les leçons locales génériques (procédure dans DECISIONS.md d'agents-shared).

## 🛡️ Opérations fichiers (sécurité des données)

- **Corbeille OS obligatoire** : jamais de suppression définitive directe (`File.Delete`).
- Sous Linux : passer par `gio trash` (ou l'API Trash freedesktop) ; sous Windows : corbeille système via shell32 (`Deletion.sendToTrash`, RC-03). Les fichiers doivent être récupérables.
- **Aucune écriture, déplacement ou suppression de fichier hors de ce que le Core a planifié** : le Core calcule la liste des fichiers concernés, l'UI n'exécute que ce plan.
- Les photos sont irremplaçables : toute opération destructive passe par une confirmation explicite dans l'UI.
- Appariement JPG+RAW : **même stem de nom de fichier** (ex. `P1230001.JPG` + `P1230001.RW2`). Extensions comparées sans casse (`.jpg` = `.JPG`).
- Ne jamais charger un fichier RAW en mémoire pour l'affichage : seul le JPG est visualisé.
- Gérer explicitement : orphelins (JPG sans RAW, RAW sans JPG), collisions de noms au déplacement.

## 📖 Documentation et Ressources

- **But de l'application et décisions** : `docs/VISION.md` (à lire au début d'une session ; le mettre à jour à chaque décision structurante)
- **Async Commands in Avalonia.FuncUI** : [https://brandewinder.com/2025/02/19/async-commands-in-avalonia-funcui/](https://brandewinder.com/2025/02/19/async-commands-in-avalonia-funcui/)

---

## 🏛️ Architecture

### Séparation affichage / logique métier (règle systématique)

- **`src/RawTandem.Core` = domaine pur**, sans Elmish ni Avalonia : ses fonctions doivent rester vraies
  pour un front **console, web ou GUI**. Si une fonction serait identique dans une version console,
  elle va dans le Core.
- **Machine à états Elmish dans l'UI** : `Msg`, `State`, `init`, `update`, champ `Status` et textes
  français vivent dans `src/RawTandem.UI/State.fs` (ou équivalent côté front), jamais dans le Core.
- Toute nouvelle logique métier arrive dans le Core **en TDD d'abord** (rouge/vert dans `tests/` en miroir).
- Écran de confirmation : une action destructive en attente est un **DU d'écran** (`Confirming`),
  pas un champ optionnel ; une confirmation sans action en attente est signalée, jamais avalée.
- Garde des actions : un message ne s'applique qu'à son écran, la garde est dans `update`
  (couvre tous les producteurs de messages : boutons, clavier, autres fronts).

---

## Bonnes pratiques générales

### Versions
- Dernières versions stables disponibles par défaut (packages, outils, SDK) ; une version plus ancienne n'est retenue que sur exception justifiée et documentée. Règle transverse détaillée dans `DECISIONS.md` d'agents-shared (« Préférences d'outillage »).

### Multi-plateforme (Linux + Windows)
- Application, Core et tests doivent fonctionner sous Linux **et** Windows (macOS en plus si simple, sinon de côté — RC-03 fixe le périmètre pour la corbeille) ; CI en matrice `.github/workflows/tests.yml`.
- Chemins : toujours `Path.Combine`/`Path.Join`, jamais de séparateur littéral — ni dans le code, ni dans les attendus de tests.
- Fixtures de tests : noms portables — pas d'espaces en début/fin, pas de `< > : " / \ | ? *` (tronqués ou refusés par NTFS).
- États dépendant de la casse (`P1.JPG` + `P1.jpg`, `raw` ≠ `RAW`) : sonde de capacité `isCaseSensitiveFileSystem` / `testCaseSensitiveOnly` (`tests/RawTandem.Core.Tests/TestHelpers.fs`), jamais un test d'OS ; leçon transverse dans `docs/ERREURS-GENERIC.md`.
- Commandes externes : dispatch par plateforme dans le Core (modèle RC-03 `sendToTrash`).

### TDD
- Approche par défaut pour les corrections et nouvelles fonctionnalités : écrire d'abord le test qui échoue, le voir échouer, puis corriger le code, puis revérifier (rouge/vert).
- Chaque bug corrigé doit avoir son test de non-régression.

### Couverture de code (systématique)
- **Règle générique : lire `docs/COVERAGE-GENERIC.md`** — quand toute la suite de tests est verte à la fin d'un changement, mesurer la couverture **sans qu'on le demande, avant de clore le changement** (un changement vert sans mesure n'est pas terminé) et rapporter les chiffres par assemblage **avec l'écart depuis la mesure précédente** ; les tests ignorés (skipped) comptent comme verts avec leur raison ; l'historique vit dans `docs/COVERAGE.md` (une ligne par mesure, la dernière ligne est la référence), la plateforme y est notée quand elle change et le delta se lit entre mesures de même plateforme.
- Spécifique au projet : `dotnet test tests/RawTandem.Core.Tests --collect:"XPlat Code Coverage"` puis idem pour `tests/RawTandem.UI.Tests` (nettoyer d'abord les `TestResults/` pour ne fusionner que les runs du jour), rapports fusionnés via ReportGenerator dans `coveragereport/` (types `TextSummary;Html`), lecture dans `coveragereport/Summary.txt`. Plafond connu : App/Main/Program (wiring Avalonia) à 0 % en headless.

### DRY
- Éviter la répétition de code. Extraire les conditions répétées en fonctions helpers.

### Strings magiques
- Éviter les strings littérales dans le code. Préférer des constantes ou des types DU.

### Nommage
- **Messages** : Utiliser des noms explicites (`FolderSelected`, `PairDeletionRequested`, `PairDeletionFailed`).
- **State** : Utiliser des noms clairs (`IsDeleting`, `Status`).
- **Fonctions** : Utiliser des verbes (`findPairs`, `planDeletion`).

---

## ❌ Erreurs à ne pas reproduire (spécifiques à ce projet)

Les erreurs génériques F#/Avalonia (DSL, parenthèses, `yield`, clipboard) vivent dans
`docs/FSHARP.md` et `docs/AVALONIA.md` ; les erreurs transverses dans `docs/ERREURS-GENERIC.md`.

1. ❌ Supprimer un fichier directement (`File.Delete`) au lieu de la corbeille OS → Perte de données irréversible
2. ❌ Comparer les extensions en tenant compte de la casse → `P1230001.jpg` et `P1230001.RW2` doivent être reconnus comme un couple
3. ❌ Charger un RAW en mémoire pour l'affichage → latence énorme, risque de saturation mémoire
4. ❌ Mettre `init`/`update`/`Msg` ou des textes UI dans le Core → le domaine doit rester réutilisable (console, web, GUI) ; la machine à états Elmish vit dans l'UI
