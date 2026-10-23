# TODO - RawTandem

Cases à cocher en Markdown : `[ ]` → `[x]` (cliquable aussi dans l'UI web GitHub).

## Fonctionnalités

- [x] Squelette de la solution : `src/RawTandem.Core` + `src/RawTandem.UI` + tests en miroir, stack Avalonia 12 / FuncUI 2 / Elmish 5 / Expecto, net10.0
- [x] Appariement JPG+RW2 par stem de nom de fichier (TDD, dans le Core) — `FolderScan.scanFolder`
- [x] Type domaine `Tandem` dans le Core : radical du nom de fichier (stem) + noms des fichiers JPG et RAW, dans un module de domaine pur (`Tandems`) ; le State UI porte un `FolderScan` (tandems + orphelins), plus de stems nus
- [x] Déplacement des RAW appariés vers le sous-dossier `RAW/` lors de l'organisation : plan calculé par le Core (`Organization.planOrganization`), exécuté par l'UI, échecs partiels signalés
- [x] **Bug confirmé (itération 2)** : ouvrir un dossier déjà organisé n'affichait aucun tandem — corrigé en itération 3 : le scan lit la racine **et** `RAW/`, un dossier organisé est reconnu (« Dossier déjà organisé »)
- [x] Itération 3 : arbre de décision complet de l'organisation initiale — 9 catégories (tandems à organiser / déjà organisés / croisés / inversés, orphelins JPG/RAW selon emplacement, doublons) + anomalies du dossier RAW (fichier, casse) ; doublons résolus par renommage système (« nom (1).ext ») jamais d'écrasement ; orphelins RAW de la racine rangés par l'organisation
- [ ] Valider dans l'UI graphique en conditions réelles l'ouverture de dossier et le déplacement des RAW (sélecteur natif branché, non vérifiable en headless)
- [x] **Bug confirmé (itération 3)** : jumeaux de casse dans un même répertoire (`P1.JPG` + `P1.jpg`, ou `P1.RW2` + `P1.rw2` — possibles sur Linux) : le scan n'en voyait qu'un seul et avalait l'autre en silence. Corrigé : catégorie `CaseCollisions` (gagnant = premier nom en ordre ordinal, jumeau signalé en orange), résolution par renommage système « nom (1).ext » via le même flux que les doublons
- [ ] Confirmation : donner à l'utilisateur un moyen de **visualiser les changements** avant de confirmer (liste des fichiers déplacés avec avant/après, aperçu par catégorie, etc.) — pour le moment la confirmation ne montre que des comptes
- [ ] Suppression d'un tandem JPG+RAW vers la corbeille OS (`gio trash` sous Linux)
- [ ] UI minimale : ~~choisir un dossier~~ (fait) ; visualiser un JPG ; supprimer le tandem
- [ ] Déplacement des tandems : décision à prendre (destinations nommées / sélection à la volée / destination fixe), puis implémentation
- [ ] Vue de tri : bouton pour ouvrir un autre dossier sans repasser par l'accueil
- [ ] Vue liste (éventuellement avec aperçu) pour sauter rapidement d'une photo à l'autre, en plus de Précédent/Suivant
- [ ] Gestion d'erreur façon Railway Oriented Programming dans le Core : `Result<'a, TandemError>` via [FsToolkit.ErrorHandling](https://github.com/demystifyfp/FsToolkit.ErrorHandling), à appliquer à l'appariement et aux plans d'opérations
- [x] Couverture de code : `coverlet.collector` + `Microsoft.NET.Test.Sdk` + `YoloDev.Expecto.TestSdk` (adaptateur VSTest moderne — les packages historiques `Expecto.TestAdapter` et `Expecto.VisualStudio.TestAdapter` sont dépubliés/obsolètes), avec `GenerateProgramFile=false` pour garder le `[<EntryPoint>]` Expecto ; exécution via `dotnet test` (pas d'EXE dédié), collecte `--collect:"XPlat Code Coverage"`, rapport ReportGenerator dans `coveragereport/` ; résultat initial : Core 98,7 %, UI 41,5 % (App/Main/Program = wiring Avalonia non couvert en headless)

## Évolutions

- [ ] Support d'autres formats RAW : CR2, CR3 (Canon), NEF (Nikon), ARW (Sony), DNG, RAF (Fuji), ORF (Olympus) — cible actuelle : RW2 (Panasonic Lumix TZ200) uniquement
- [ ] Corbeille OS multi-plateforme (Windows recycle bin, macOS Trash)
- [ ] Orientation EXIF des JPG à l'affichage (portraits stockés horizontaux)
- [ ] Grille de vignettes paresseuse / performance sur dossiers de plusieurs milliers de photos
- [ ] Gestion des orphelins (JPG sans RAW, RAW sans JPG) : pour l'instant les orphelins sont signalés en rouge (nom seul) dans le résumé d'organisation et pris en compte par l'arbre de décision ; aller plus loin : action au fichier seul, affichage dédié dans le tri
- [ ] Workflow CI (build + tests, publication de binaires)

## Documentation

- [ ] Fichier README.md (description, captures, build, usage)
- [ ] Mettre à jour la section « Crée une version x.y.z » d'AGENTS.md quand le workflow CI existera
