# VISION.md - Ce que RawTandem doit faire

Document de référence sur le but de l'application. Il évolue avec les décisions de l'utilisateur :
toute décision importante prise en conversation doit s'y refléter.

## Le besoin

Je prends des photos avec un appareil photo (Panasonic Lumix TZ200) qui sort chaque cliché
en deux fichiers : un JPG (visualisable) et un RAW `.RW2` (même nom de base, ex. `P1230001.JPG`
+ `P1230001.RW2`). Trier ces photos à la main est fastidieux : deux fichiers par photo,
et les outils classiques ne manipulent jamais le couple d'un seul geste.

## Ce que fait l'application

Un trieur de photos qui considère chaque cliché comme **un couple JPG+RAW** :

- **Visualiser les JPG** d'un dossier (jamais les RAW, trop lourds).
- **Supprimer un couple** JPG+RAW en une seule manipulation (vers la corbeille OS, récupérable).
- **Déplacer un couple** JPG+RAW en une seule manipulation (modalité à définir : destinations
  nommées, sélection à la volée, ou destination fixe).

## Décisions prises

| Sujet | Décision |
|---|---|
| Appariement | Même stem de nom de fichier ; extensions sans casse |
| Cible RAW | `.RW2` uniquement (autres formats en évolution, voir `docs/TODO.md`) |
| Suppression | Corbeille OS, jamais de suppression définitive |
| Déplacement | Non décidé — la décision est attendue de l'utilisateur |
| Sécurité des données | Le Core calcule un plan d'opérations, l'UI n'exécute que ce plan |
| Orphelins | JPG sans RAW / RAW sans JPG : affichés et gérés explicitement (à implémenter) |

## Hors périmètre (pour l'instant)

- Développement/retouche photo, gestion de catalogue.
- Support d'autres formats RAW (CR2, CR3, NEF, ARW, DNG…) — listé en évolution.
- Corbeille OS sur Windows/macOS — Linux d'abord.

## Méthode de travail

Développement **itératif avec l'utilisateur** : il demande une implémentation simple, valide
ou fait un retour ; les demandes trop grosses sont redécoupées. Approche TDD par défaut
(rouge/vert), tests en miroir de `src/`, documentation en français, code en anglais.
