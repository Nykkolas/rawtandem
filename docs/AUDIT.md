# AUDIT.md - Checklist d'audit spécifique à RawTandem

Grille **générique** (procédure, niveaux de sévérité, thèmes transverses, format du rapport) :
lire `docs/AUDIT-GENERIC.md`. Le présent fichier ne garde que les vérifications
**spécifiques au projet**, à passer en plus de la grille générique.

L'audit ne modifie **aucun fichier** : il produit des recommandations, sauf demande
explicite de corriger.

## 🛡️ 1. Sécurité des données (spécifique RawTandem)

- [ ] Aucune suppression définitive directe (`File.Delete`) : uniquement la corbeille OS (`gio trash` sous Linux).
- [ ] Aucune opération fichier effectuée en dehors du plan calculé par le Core.
- [ ] Toute opération destructive nécessite une confirmation explicite dans l'UI (DU d'écran `Confirming`).
- [ ] Orphelins gérés explicitement : JPG sans RAW, RAW sans JPG ne sont ni silencieusement traités ni avalés.
- [ ] Collisions de noms au déplacement détectées et signalées, pas écrasées.
- [ ] Extensions comparées sans casse ; appariement strictement par stem de nom de fichier.
- [ ] Aucun fichier RAW chargé en mémoire pour l'affichage.

## 🧯 2. Gestion d'erreurs — compléments spécifiques

- [ ] Les erreurs remontent à l'utilisateur via le champ `Status` (rouge), pas de crash silencieux.
- [ ] Les opérations async qui peuvent échouer utilisent `Cmd.OfAsync.either` (règle `docs/FSHARP.md`).
- [ ] Les échecs partiels (un fichier du couple déplacé, l'autre non) sont signalés à l'utilisateur.
- [ ] Machine à états Elmish (`Msg`, `State`, `Status`, textes UI) côté UI uniquement, jamais dans le Core.

## 🗣️ 3. Vocabulaire domaine / technique (instanciation du thème 8 générique)

Le thème générique est dans `docs/AUDIT-GENERIC.md`. Instances spécifiques au projet :

- [ ] `Tandem` (vocabulaire de VISION.md) vit dans le module de domaine pur `Tandems`, pas dans un service technique.
- [ ] Un registre par module : `Tandems` (domaine pur), `FolderScan` (lecture), `Organization` (opération avec effets).
- [ ] `RawMovePlan` / `RawMoveOutcome` (frontière Core/UI) sont nommés pour ce qu'ils sont : le contrat d'exécution, pas du vocabulaire utilisateur.

## 🗂️ 4. Organisation des fichiers — compléments spécifiques

- [ ] Frontières Core/UI respectées (voir AGENTS.md, section Architecture).
- [ ] Tests en miroir : un fichier de test par module testé, dans `tests/` parallèle à `src/`.

## 🧪 5. Couverture des tests — compléments spécifiques

- [ ] Chaque module de `src/RawTandem.Core` a son fichier de tests correspondant.
- [ ] Cas limites couverts : dossier vide, JPG sans RAW, RAW sans JPG, extensions en majuscules/minuscules, stems identiques.
- [ ] Un test = un comportement, nom du test décrit l'attente.
- [ ] `dotnet build` puis `dotnet test` au vert avant de rapporter.
- [ ] Couverture exécutée pendant l'audit : `dotnet test --collect:"XPlat Code Coverage"` puis ReportGenerator (`reportgenerator -reports:"tests/*/TestResults/*/coverage.cobertura.xml" -targetdir:coveragereport`). Le rapport est analysé (taux par module, lignes et branches non couvertes dans `RawTandem.Core` et `RawTandem.UI.State`) et chaque zone faible devient une préconisation. Le wiring Avalonia (`App`/`Main`/`Program`, attendu à 0 % en headless) est hors périmètre de l'analyse.

Le format du rapport (tableau trié par sévérité + conclusion) est défini dans `docs/AUDIT-GENERIC.md`.
