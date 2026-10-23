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
- [x] Suppression d'un tandem JPG+RAW vers la corbeille OS (`gio trash` sous Linux) — fait : plan `Deletion.planDeletion` (chemins complets JPG+RAW selon l'emplacement du RAW), exécution `applyDeletion` fichier par fichier (un échec n'interrompt pas l'autre), branchée dans le tri derrière l'écran de confirmation existant
- [ ] UI minimale : ~~choisir un dossier~~ (fait) ; visualiser un JPG ; ~~supprimer le tandem~~ (fait)
- [ ] Déplacement des tandems : décision à prendre (destinations nommées / sélection à la volée / destination fixe), puis implémentation
- [x] Vue de tri : bouton pour ouvrir un autre dossier sans repasser par l'accueil — fait : « Ouvrir un autre dossier… » en organisation (en-tête) et en tri (barre du haut), désactivé pendant qu'une question est affichée ; Ctrl+O attendra la subscription Elmish qui remplacera le hack clavier
- [ ] Retouche externe : ouvrir une application de retouche d'un clic — sur le dossier (photo courante du tri) ou sur une photo en particulier ; application à choisir/configurer (par défaut, celle que l'OS associe au type de fichier)
- [ ] Vue liste (éventuellement avec aperçu) pour sauter rapidement d'une photo à l'autre, en plus de Précédent/Suivant
- [ ] Gestion d'erreur façon Railway Oriented Programming dans le Core : `Result<'a, TandemError>` via [FsToolkit.ErrorHandling](https://github.com/demystifyfp/FsToolkit.ErrorHandling), à appliquer à l'appariement et aux plans d'opérations
- [x] Couverture de code : `coverlet.collector` + `Microsoft.NET.Test.Sdk` + `YoloDev.Expecto.TestSdk` (adaptateur VSTest moderne — les packages historiques `Expecto.TestAdapter` et `Expecto.VisualStudio.TestAdapter` sont dépubliés/obsolètes), avec `GenerateProgramFile=false` pour garder le `[<EntryPoint>]` Expecto ; exécution via `dotnet test` (pas d'EXE dédié), collecte `--collect:"XPlat Code Coverage"`, rapport ReportGenerator dans `coveragereport/` ; résultat initial : Core 98,7 %, UI 41,5 % (App/Main/Program = wiring Avalonia non couvert en headless)

## Évolutions

- [ ] Support d'autres formats RAW : CR2, CR3 (Canon), NEF (Nikon), ARW (Sony), DNG, RAF (Fuji), ORF (Olympus) — cible actuelle : RW2 (Panasonic Lumix TZ200) uniquement
- [ ] Corbeille OS multi-plateforme, dans cet ordre : 1. Windows (recycle bin via `Microsoft.VisualBasic.FileIO` `RecycleOption.SendToRecycleBin` ou P/Invoke `SHFileOperation`), 2. macOS (Trash via `NSWorkspace.recycleURLs` ou `osascript`) — point d'extension unique : `trashFile` dans `Deletion.fs`, dispatch `RuntimeInformation.IsOSPlatform` ou package `TrashToolkit` (Meziantou) ; l'échec actuel sur plateforme sans `gio` est propre (fichier signalé, jamais de suppression définitive)
- [ ] Orientation EXIF des JPG à l'affichage (portraits stockés horizontaux)
- [ ] Grille de vignettes paresseuse / performance sur dossiers de plusieurs milliers de photos
- [ ] Gestion des orphelins (JPG sans RAW, RAW sans JPG) : pour l'instant les orphelins sont signalés en rouge (nom seul) dans le résumé d'organisation et pris en compte par l'arbre de décision ; aller plus loin : action au fichier seul, affichage dédié dans le tri
- [x] Workflow CI : build + tests + couverture au push (GitHub Actions `.github/workflows/tests.yml`, rapport en artifact, badges de couverture séparés Core / State / wiring Avalonia publiés sur la branche `badges`, affichés dans le README)
- [ ] Publication de binaires en GitHub Release au push d'un tag (workflow de release, voir section « Crée une version x.y.z » d'AGENTS.md)

## Documentation

- [x] Fichier README.md minimal (description, badges tests/couverture, build, usage) — captures d'écran et détails d'usage à ajouter quand l'UI sera plus aboutie
- [ ] Mettre à jour la section « Crée une version x.y.z » d'AGENTS.md quand le workflow CI existera
