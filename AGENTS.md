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
- Lire `docs/AUDIT.md` et suivre la grille : procédure, thèmes, niveaux de sévérité, format de rapport.
- L'audit ne modifie aucun fichier : recommandations uniquement, sauf demande explicite de corriger.

### "Crée une version x.y.z"
- Équivaut à : `git tag vx.y.z` + `git push origin vx.y.z` (autorisation explicite incluse).
- Le workflow CI exécute les tests puis publie les binaires en GitHub Release (à mettre en place ; voir `docs/TODO.md`).
- Après le push du tag, vérifier le run et la Release obtenue, et rapporter le résultat.

## 🌍 Langue

- **Code en anglais** : identifiants, fonctions, modules, messages Elmish, noms de tests.
- **UI en français** : libellés, statuts, messages affichés à l'utilisateur.
- La documentation (AGENTS.md, AUDIT.md, TODO.md, README) est en français.

## 🛡️ Opérations fichiers (sécurité des données)

- **Corbeille OS obligatoire** : jamais de suppression définitive directe (`File.Delete`).
- Sous Linux : passer par `gio trash` (ou l'API Trash freedesktop) ; les fichiers doivent être récupérables.
- **Aucune écriture, déplacement ou suppression de fichier hors de ce que le Core a planifié** : le Core calcule la liste des fichiers concernés, l'UI n'exécute que ce plan.
- Les photos sont irremplaçables : toute opération destructive passe par une confirmation explicite dans l'UI.
- Appariement JPG+RAW : **même stem de nom de fichier** (ex. `P1230001.JPG` + `P1230001.RW2`). Extensions comparées sans casse (`.jpg` = `.JPG`).
- Ne jamais charger un fichier RAW en mémoire pour l'affichage : seul le JPG est visualisé.
- Gérer explicitement : orphelins (JPG sans RAW, RAW sans JPG), collisions de noms au déplacement.

## 📚 Documentation et Ressources

- **But de l'application et décisions** : `docs/VISION.md` (à lire au début d'une session ; le mettre à jour à chaque décision structurante)
- **Async Commands in Avalonia.FuncUI** : [https://brandewinder.com/2025/02/19/async-commands-in-avalonia-funcui/](https://brandewinder.com/2025/02/19/async-commands-in-avalonia-funcui/)

---

## F# / Elmish

### Pattern Elmish
- **State** : Toujours immutable. Utiliser `with` pour les mises à jour.
- **Msg** : Utiliser des Discriminated Unions pour les messages.
- **init** : Doit retourner `State * Cmd<Msg>`.
- **update** : Doit retourner `State * Cmd<Msg>`. Gérer tous les cas de Msg.

### Commandes asynchrones
- **Avec gestion d'erreur** : Utiliser `Cmd.OfAsync.either`
  ```fsharp
  Cmd.OfAsync.either
      asyncFn
      ()
      (fun result -> OperationSucceeded result)
      (fun ex -> OperationFailed ex.Message)
  ```
- **Sans gestion d'erreur** : Utiliser `Cmd.OfAsync.perform`
  ```fsharp
  Cmd.OfAsync.perform
      asyncFn
      OperationSucceeded
      (fun ex -> OperationFailed ex.Message)
  ```

---

## Avalonia.FuncUI

### Structure DSL
- **StackPanel** : Toujours utiliser `StackPanel.children` pour les éléments enfants.
  ```fsharp
  StackPanel.create [
      StackPanel.margin (Thickness 20.0)
      StackPanel.children [
          Button.create [...]
          TextBlock.create [...]
      ]
  ]
  ```

### Syntaxe des types géométriques
- **Toujours utiliser des parenthèses** :
  - ✅ `Thickness(12.0, 8.0)`
  - ❌ `Thickness 12.0, 8.0`
  - ✅ `CornerRadius(4.0)`
  - ❌ `CornerRadius 4.0`

### Lists
- **Ne pas utiliser `yield`/`yield!`** dans des listes simples. Utiliser `@` pour fusionner :
  ```fsharp
  // ✅ Bon
  Button.create (commonStyle @ [Button.content "OK"])

  // ❌ Éviter
  Button.create [ yield! commonStyle; Button.content "OK" ]
  ```

### Contrôles
- **TextBox** : Pour du texte sélectionnable. Utiliser `isReadOnly true` pour éviter les modifications.
- **TextBlock** : Pour du texte statique.
- **Button.content** : Peut contenir du texte ou des emojis.

---

## UI/UX

### Feedback utilisateur
- **Statuts** : Un seul champ `Status` dans le State pour tous les messages.
- **Couleurs** :
  - Vert (`Brushes.Green`) pour les succès
  - Rouge (`Brushes.Red`) pour les échecs
  - Noir par défaut
- **Préfixe succès** : Utiliser `✓ ` (checkmark + espace) devant les messages de succès.

### Disposition
- **Boutons et statuts** : Regrouper chaque bouton avec son statut dans un `StackPanel` horizontal.
- **Espacement** : Utiliser `StackPanel.spacing` pour l'espacement entre les enfants.

---

## Avalonia Spécifique

### Fenêtre
- **Taille** : Définir `Width` et `Height` dans `MainWindow`.
- **Position** : `WindowStartupLocation.CenterScreen` pour centrer.

---

## Bonnes pratiques générales

### TDD
- Approche par défaut pour les corrections et nouvelles fonctionnalités : écrire d'abord le test qui échoue, le voir échouer, puis corriger le code, puis revérifier (rouge/vert).
- Chaque bug corrigé doit avoir son test de non-régression.

### DRY
- Éviter la répétition de code. Extraire les conditions répétées en fonctions helpers.

### Strings magiques
- Éviter les strings littérales dans le code. Préférer des constantes ou des types DU.

### Nommage
- **Messages** : Utiliser des noms explicites (`FolderSelected`, `PairDeletionRequested`, `PairDeletionFailed`).
- **State** : Utiliser des noms clairs (`IsDeleting`, `Status`).
- **Fonctions** : Utiliser des verbes (`findPairs`, `planDeletion`).

---

## Erreurs à ne pas reproduire

1. ❌ Oublier `StackPanel.children` → Erreur : `IView` incompatible avec `IAttr`
2. ❌ `Thickness 12.0, 8.0` sans parenthèses → Erreur de compilation
3. ❌ `CornerRadius 4.0` sans parenthèses → Erreur de compilation
4. ❌ Utiliser `yield` dans une liste simple → Comportement inattendu
5. ❌ Supprimer un fichier directement (`File.Delete`) au lieu de la corbeille OS → Perte de données irréversible
6. ❌ Comparer les extensions en tenant compte de la casse → `P1230001.jpg` et `P1230001.RW2` doivent être reconnus comme un couple
7. ❌ Charger un RAW en mémoire pour l'affichage → latence énorme, risque de saturation mémoire

---

## Programme Elmish

- Utiliser `Program.mkProgram` quand `init` et `update` retournent `State * Cmd<Msg>`.
- Utiliser `Program.mkSimple` seulement si pas de Cmd.
- Toujours utiliser `Program.withHost this` et `Program.runWithAvaloniaSyncDispatch ()` pour Avalonia.
