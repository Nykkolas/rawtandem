# AUDIT.md - Grille d'audit du code

Procédure à suivre quand l'utilisateur demande un audit / une review du code.
L'audit ne modifie **aucun fichier** : il produit des recommandations, sauf demande explicite de corriger.

## 🔍 Procédure

1. Exécuter `dotnet build` et `dotnet test` : rapporter d'abord leur statut.
2. Lire en entier les fichiers concernés (pas d'audit sur extrait).
3. Passer chaque thème de la grille ci-dessous.
4. Vérifier aussi l'application des règles d'`AGENTS.md` (notamment la section « Erreurs à ne pas reproduire »).
5. Produire le rapport au format défini en fin de fichier.

## ⚠️ Niveaux de sévérité

- **Critique** : perte de données possible, bug, crash, fichier modifié/supprimé sans y être autorisé.
- **Majeur** : défaut de design, maintenabilité, test manquant sur un comportement essentiel.
- **Mineur** : style, préférence, cosmétique.

## 🛡️ 1. Sécurité des données
- [ ] Aucune suppression définitive directe (`File.Delete`) : uniquement la corbeille OS (`gio trash` sous Linux).
- [ ] Aucune opération fichier effectuée en dehors du plan calculé par le Core.
- [ ] Toute opération destructive nécessite une confirmation explicite dans l'UI.
- [ ] Orphelins gérés explicitement : JPG sans RAW, RAW sans JPG ne sont ni silencieusement traités ni avalés.
- [ ] Collisions de noms au déplacement détectées et signalées, pas écrasées.
- [ ] Extensions comparées sans casse ; appariement strictement par stem de nom de fichier.
- [ ] Aucun fichier RAW chargé en mémoire pour l'affichage.

## 🧯 2. Gestion d'erreurs
- [ ] Pattern matching exhaustif ; pas de wildcard `_` qui avale des cas inattendus dans un DU important.
- [ ] Pas de `ignore` sur un résultat qui peut échouer.
- [ ] Pas de `try/with` vide ou qui avale silencieusement.
- [ ] Les erreurs remontent à l'utilisateur via le champ `Status` (rouge), pas de crash silencieux.
- [ ] Les opérations async qui peuvent échouer utilisent `Cmd.OfAsync.either` (règle AGENTS.md).
- [ ] Les échecs partiels (un fichier du couple déplacé, l'autre non) sont signalés à l'utilisateur.

## 👁️ 3. Clarté du code pour un humain
- [ ] Noms explicites : verbes pour les fonctions, intentions lisibles sans commentaire.
- [ ] Fonctions courtes, une seule responsabilité.
- [ ] Pas de « code clever » : le code se lit de haut en bas sans effort mental.
- [ ] Les commentaires expliquent le *pourquoi*, jamais le *quoi*.

## ✂️ 4. Simplicité
- [ ] Pas de sur-abstraction : une seule utilisation = pas d'interface ou d'indirection intermédiaire.
- [ ] Pas de code mort, pas de branche inatteignable.
- [ ] Duplication → extraire un helper (règle DRY d'AGENTS.md).
- [ ] La solution la plus simple qui résout le problème a été préférée.

## 🏛️ 5. Bonnes pratiques F# et Design with types
- [ ] *Make illegal states unrepresentable* : DU plutôt que combinaisons de bool + string.
- [ ] `Option` / `Result` plutôt que `null` ou exceptions pour les cas attendus et prévisibles.
- [ ] State Elmish immuable, mises à jour via `with` ; `init`/`update` retournent `State * Cmd<Msg>`.
- [ ] `mutable` évité sauf justification écrite.
- [ ] Tous les cas de `Msg` gérés dans `update`.

## 🏷️ 6. Nommage et conventions
- [ ] **Code en anglais** (identifiants, messages Elmish, tests) ; **UI en français** (règle AGENTS.md).
- [ ] Casing F# : `PascalCase` pour modules/types/UDU, `camelCase` pour paramètres et locaux.
- [ ] Messages Elmish explicites (`PairDeletionRequested`, `FolderLoaded`), pas d'abréviations obscures.
- [ ] Strings magiques remplacées par des constantes ou des DU (règle AGENTS.md).

## 🗂️ 7. Organisation des fichiers
- [ ] Un module logique par fichier ; nom de fichier = nom du module.
- [ ] Pas de fichier fourre-tout ; responsabilités séparées (Core vs UI).
- [ ] Tests en miroir de `src/` : un fichier de test par module testé.
- [ ] Rien de généré ni de binaire committé (`bin/`, `obj/` ignorés ; `.gitignore` à jour).
- [ ] Fichiers et dossiers nommés de façon cohérente entre eux.

## 🧪 8. Couverture des tests
- [ ] Chaque module de `src/RawTandem.Core` a son fichier de tests correspondant.
- [ ] Cas limites couverts : dossier vide, JPG sans RAW, RAW sans JPG, extensions en majuscules/minuscules, stems identiques.
- [ ] Un test = un comportement, nom du test décrit l'attente.
- [ ] `dotnet test` au vert avant de rapporter.
- [ ] Optionnel : couverture chiffrée via coverlet (`dotnet test /collect:"XPlat Code Coverage"`).

## 📄 Format du rapport

Un tableau unique, trié par sévérité, puis une conclusion d'une à trois phrases :

| Sévérité | Emplacement | Constat | Recommandation |
|---|---|---|---|
| Critique | src/RawTandem.Core/PairFinder.fs:30 | `File.Delete` direct sur le RAW | Passer par la corbeille OS |

Conclure par : ce qui est sain (ne pas réinventer), le top 2-3 des corrections prioritaires, et les points laissés à l'appréciation de l'utilisateur.
