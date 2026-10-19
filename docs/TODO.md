# TODO - RawTandem

Cases à cocher en Markdown : `[ ]` → `[x]` (cliquable aussi dans l'UI web GitHub).

## Fonctionnalités

- [x] Squelette de la solution : `src/RawTandem.Core` + `src/RawTandem.UI` + tests en miroir, stack Avalonia 12 / FuncUI 2 / Elmish 5 / Expecto, net10.0
- [ ] Appariement JPG+RW2 par stem de nom de fichier (TDD, dans le Core)
- [ ] Type domaine `Tandem` dans le Core : radical du nom de fichier (stem) + fonctions pour déduire l'emplacement du JPG (racine) et du RAW (sous-dossier `RAW/`) ; remplacer `Photos: string list` (stems nus) dans l'UI State par ce type — suggestion à étudier quand l'appariement réel atterrit
- [ ] Suppression d'un tandem JPG+RAW vers la corbeille OS (`gio trash` sous Linux)
- [ ] UI minimale : choisir un dossier, visualiser un JPG, supprimer le tandem
- [ ] Déplacement des tandems : décision à prendre (destinations nommées / sélection à la volée / destination fixe), puis implémentation
- [ ] Vue de tri : bouton pour ouvrir un autre dossier sans repasser par l'accueil
- [ ] Vue liste (éventuellement avec aperçu) pour sauter rapidement d'une photo à l'autre, en plus de Précédent/Suivant
- [ ] Gestion d'erreur façon Railway Oriented Programming dans le Core : `Result<'a, TandemError>` via [FsToolkit.ErrorHandling](https://github.com/demystifyfp/FsToolkit.ErrorHandling), à appliquer à l'appariement et aux plans d'opérations

## Évolutions

- [ ] Support d'autres formats RAW : CR2, CR3 (Canon), NEF (Nikon), ARW (Sony), DNG, RAF (Fuji), ORF (Olympus) — cible actuelle : RW2 (Panasonic Lumix TZ200) uniquement
- [ ] Corbeille OS multi-plateforme (Windows recycle bin, macOS Trash)
- [ ] Orientation EXIF des JPG à l'affichage (portraits stockés horizontaux)
- [ ] Grille de vignettes paresseuse / performance sur dossiers de plusieurs milliers de photos
- [ ] Gestion des orphelins (JPG sans RAW, RAW sans JPG) : affichage dédié et action au fichier seul
- [ ] Workflow CI (build + tests, publication de binaires)

## Documentation

- [ ] Fichier README.md (description, captures, build, usage)
- [ ] Mettre à jour la section « Crée une version x.y.z » d'AGENTS.md quand le workflow CI existera
