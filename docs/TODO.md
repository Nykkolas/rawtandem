# TODO - RawTandem

Cases à cocher en Markdown : `[ ]` → `[x]` (cliquable aussi dans l'UI web GitHub).

## Fonctionnalités

- [x] Squelette de la solution : `src/RawTandem.Core` + `src/RawTandem.UI` + tests en miroir, stack Avalonia 12 / FuncUI 2 / Elmish 5 / Expecto, net10.0
- [x] Appariement JPG+RW2 par stem de nom de fichier (TDD, dans le Core) — `FolderScan.scanFolder`
- [x] Type domaine `Tandem` dans le Core : radical du nom de fichier (stem) + noms des fichiers JPG et RAW, dans un module de domaine pur (`Tandems`) ; le State UI porte un `FolderScan` (tandems + orphelins), plus de stems nus
- [x] Déplacement des RAW appariés vers le sous-dossier `RAW/` lors de l'organisation : plan calculé par le Core (`Organization.planRawMoves`), exécuté par l'UI (`applyRawMovePlan`), collisions jamais écrasées, échecs partiels signalés
- [ ] Valider dans l'UI graphique en conditions réelles l'ouverture de dossier et le déplacement des RAW (sélecteur natif branché, non vérifiable en headless)
- [ ] Suppression d'un tandem JPG+RAW vers la corbeille OS (`gio trash` sous Linux)
- [ ] UI minimale : ~~choisir un dossier~~ (fait) ; visualiser un JPG ; supprimer le tandem
- [ ] Déplacement des tandems : décision à prendre (destinations nommées / sélection à la volée / destination fixe), puis implémentation
- [ ] **Bug confirmé (itération 2)** : ouvrir un dossier déjà organisé (JPG à la racine, RAW déjà dans `RAW/`) n'affiche aucun tandem — tous les JPG apparaissent comme orphelins. Comportement attendu : reconnaître que le dossier est déjà organisé et apparier les JPG de la racine avec les RAW du sous-dossier `RAW/`. Cause : le scan est limité à la racine (décision d'itération) et ne regarde pas dans `RAW/`.
- [ ] Vue de tri : bouton pour ouvrir un autre dossier sans repasser par l'accueil
- [ ] Vue liste (éventuellement avec aperçu) pour sauter rapidement d'une photo à l'autre, en plus de Précédent/Suivant
- [ ] Gestion d'erreur façon Railway Oriented Programming dans le Core : `Result<'a, TandemError>` via [FsToolkit.ErrorHandling](https://github.com/demystifyfp/FsToolkit.ErrorHandling), à appliquer à l'appariement et aux plans d'opérations

## Évolutions

- [ ] Support d'autres formats RAW : CR2, CR3 (Canon), NEF (Nikon), ARW (Sony), DNG, RAF (Fuji), ORF (Olympus) — cible actuelle : RW2 (Panasonic Lumix TZ200) uniquement
- [ ] Corbeille OS multi-plateforme (Windows recycle bin, macOS Trash)
- [ ] Orientation EXIF des JPG à l'affichage (portraits stockés horizontaux)
- [ ] Grille de vignettes paresseuse / performance sur dossiers de plusieurs milliers de photos
- [ ] Gestion des orphelins (JPG sans RAW, RAW sans JPG) : décision à prendre sur l'action au fichier seul (les orphelins sont déjà signalés dans le résumé du scan et laissés en place lors de l'organisation)
- [ ] Workflow CI (build + tests, publication de binaires)

## Documentation

- [ ] Fichier README.md (description, captures, build, usage)
- [ ] Mettre à jour la section « Crée une version x.y.z » d'AGENTS.md quand le workflow CI existera
