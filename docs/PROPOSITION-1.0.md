# PROPOSITION - Priorisation pour une 1.0

Statut : **arbitrée le 2026-10-30, transformée en feuille de route** — issue de
l'audit complet du 2026-10-28 (grilles `docs/AUDIT.md` + `docs/AUDIT-GENERIC.md`,
couverture mesurée). La numérotation P0/P1/P2 est celle proposée le jour de
l'audit ; l'avancement est tenu à jour ci-dessous.

## Feuille de route vers 1.0 (arbitrage du 2026-10-30, dans l'ordre)

1. **Raccourcis `Entrée`/`Échap` du bandeau jaune** (point 6 ci-dessous) —
   proposition de règle RC-05 et liste de tests en relecture.
2. **Cas limite du répertoire sans photo triable** (point 3 ci-dessous) —
   décision de comportement + scénario UC dédié.
3. **Recette manuelle Windows réelle** (point 5 ci-dessous) — puis tag 1.0.

Le reste est renvoyé aux versions suivant la 1.0 (points 8 complément, 9 à 15,
et déplacement des tandems annoncé hors 1.0 — voir point 7).

Contexte au jour de l'audit : build 0 warning / 0 erreur, tests verts (Core 58,
UI 123 avant le P0-1), couverture 78,8 % lignes / 82,1 % branches ; sécurité des
données jugée exemplaire (plans Core uniquement, jamais d'écrasement, corbeille
OS, courses traitées avec tests de non-régression nommés).

## P0 — bloquants pour tagger 1.0

1. **[FAIT — commit 0e49b43, 2026-10-28]** Corriger le bandeau de confirmation
   mensonger (« RAW sans JPG inclus » alors que le plan d'option A ne déplace que
   les RAW appariés) + ménage du code mort option A (`MoveKind`,
   `duplicatesOutcomeStatus`, `OrganizeFolder` réduit). Couverture après :
   79,7 % lignes, State.fs 98,7 %.
2. **[FAIT — 2026-10-29, commits 0092e34 → 242a071]** Terminer la « passe
   complète sur les actions asynchrones » : garde d'opération en vol (double
   « Ranger les RAW », double `Suppr` sur le même stem), statuts avalés pendant
   une question (`OrganizePlanningFailed`, `OrganizeNothingToMove`), course du
   picker (`FolderSelected` pendant `Confirming`), F5 à l'accueil (RC-06).
   `PROPOSITION-actions-asynchrones.md` supprimée après arbitrage.
3. **[PRIORITÉ 2]** Cas limite dossier sans photo triable (entrée TODO
   existante) : un dossier d'orphelins JPG seuls saute aujourd'hui au tri vide
   (« Aucune photo dans ce dossier ») — impasse utilisateur. Décision de
   comportement (rester à l'organisation avec statut explicite ?) + scénario UC
   dédié.
4. **[FAIT — 2026-10-30]** Workflow de release au tag (GitHub Release, binaires
   Linux + Windows) : `.github/workflows/release.yml` — suite de tests rejouée,
   `tar.gz` + AppImage (Linux), `zip` + installeur Inno Setup (Windows), version
   injectée depuis le tag, Release en **brouillon** (publication toujours
   manuelle — règle gravée dans AGENTS.md §« Crée une version x.y.z » et
   agents-shared le 2026-10-30). Éprouvé en réel par la v0.5.1 publiée le
   2026-10-30 avec description relue.
5. **[PRIORITÉ 3, dernier pas avant le tag 1.0]** Recette manuelle Windows
   réelle : `WindowsRecycle` (SHFileOperation) n'est couvert que par la CI ; une
   recette visuelle sur un vrai bureau Windows (corbeille restaurable) avant de
   tagger — le `setup.exe` de la v0.5.1 est disponible pour ça. + Fermer
   l'entrée TODO obsolète « scan périmé après résolution des doublons » (le
   flux `ResolveDuplicates*` a été supprimé par l'option A — le bug qu'elle
   décrit n'existe plus).

## P1 — souhaitables avant 1.0

6. **[PRIORITÉ 1, en cours]** Raccourcis `Entrée`/`Échap` pour trancher la
   question affichée (le clavier peut ouvrir une confirmation avec `Suppr` mais
   pas la trancher — entrée TODO existante, audit itération 4). Proposition
   déposée le 2026-10-30 : extension de RC-05 (`Entrée` accepte, `Échap`
   refuse, priorité question > sortie de plein écran, no-op hors question) +
   6 tests E2E en relecture.
7. **[ARBITRÉ — hors 1.0]** Décision « déplacement des tandems » : seule
   promesse de VISION non tenue (visualiser ✔, supprimer ✔, déplacer ✘) —
   renvoyé aux versions suivant la 1.0 (arbitrage du 2026-10-30) ; VISION et
   README devront l'annoncer explicitement au moment du tag 1.0.
8. **[MINIMUM COUVERT — complément après 1.0]** Retours visuels pour les
   opérations longues (entrée TODO existante) : la désactivation des actions
   pendant les opérations est couverte par RC-06 ; le reste (spinner, délais
   NN/g, placeholder de décodage) suit en versions suivantes.

## P2 — après 1.0

9. Mutation testing artisanal (mutants écrits à la main sur les règles
   critiques : gardes d'écran, plans Core, fraîcheur des messages).
10. Gestion d'erreur ROP dans le Core (`Result<'a, TandemError>`,
    FsToolkit.ErrorHandling) — à appliquer à l'appariement et aux plans.
11. Vue liste (avec aperçu) pour sauter d'une photo à l'autre.
12. Grille de vignettes paresseuse / performance sur les gros dossiers.
13. Gestion avancée des orphelins (action au fichier seul, affichage dédié).
14. Retouche externe (ouvrir l'application associée d'un clic).
15. Support d'autres formats RAW (CR2/CR3, NEF, ARW, DNG, RAF, ORF) et corbeille
    macOS.

## Constats d'audit encore ouverts (non bloquants, à garder en vue)

- `Key.F` sans contrôle de modificateur dans `keyToMsg` — inoffensif tant qu'aucun
  champ texte saisissable n'existe ; à revoir dès qu'un apparaît.
- Limitation documentée : deux statuts rapprochés en plein écran (commande Elmish
  non annulable) — assumée dans State.fs.
- `DeleteTandemFailed` sans garde de dossier sur le bandeau : choix conforme à la
  règle VISION (« le bandeau reflète toujours l'effet réel »), rien à changer.

## Ce qui est sain (ne pas réinventer)

Sécurité des données (plans Core uniquement, jamais d'écrasement, corbeille OS
multi-plateforme), traitement des courses de scan/opérations avec tests de
non-régression nommés, frontière Core/UI propre, gardes d'écran systématiques,
workflow « la spec est le UC » avec scénarios E2E réellement discriminants
(ils ont attrapé le bug du scan périmé). Le rapport d'audit complet du
2026-10-28 vit dans l'historique de conversation ; sa synthèse est ici.
