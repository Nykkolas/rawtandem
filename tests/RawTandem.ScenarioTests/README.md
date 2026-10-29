# RawTandem.ScenarioTests — la spec exécutable

Ce projet ne contient pas des tests d'implémentation : c'est **la spec rendue
exécutable** (workflow « la spec est le UC », `docs/SPEC-UC.md`). Chaque test
joue uniquement les gestes de l'utilisateur ; les étapes « application »
transitent par les vraies commandes Elmish, exécutées réellement, cascade
comprise — vrais fichiers temporaires, vraie corbeille OS, vraie vue rendue.

L'autre projet de tests UI, `tests/RawTandem.UI.Tests` (`StateTests.fs`), est
le miroir de la machine à états : pur, rapide (~1 s). Celui-ci est lent (~15 s)
et se joue délibérément — validation d'une règle, d'un UC, recette.

## S'y retrouver : un fichier par document

| Document (`docs/usecases/`) | Scénario (`Specs/`) |
|---|---|
| `organiser-et-trier-dossier.md` | `OrganiserEtTrierDossier.fs` (primaire + extensions 2a, 2b) |
| `trier-dossier-deja-organise.md` | `TrierDossierDejaOrganise.fs` |
| `regles-communes.md` → RC-01 | `RC01.fs` |
| `regles-communes.md` → RC-04 | `RC04.fs` |
| `regles-communes.md` → RC-05 | `RC05.fs` |
| `regles-communes.md` → RC-06 | `RC06.fs` (sections dans l'ordre du vécu utilisateur) |

Chaque fichier commence par une **cartouche** : le document source, la
couverture (générique/instancié, cf. `regles-communes.md`) et le **non couvert**
(sélecteur natif de dossier, branchement clavier — hors headless).

`Main.fs` assemble les testLists dans l'ordre des documents (UC puis
RC-01 → RC-06) — c'est aussi l'ordre de l'arbre dans le Test Explorer.

## Le harnais (`Harness/`, on n'y revient pas pour lire une spec)

- **`Steps.fs`** — l'interpréteur :
  - `step msg state` : traite un geste utilisateur puis exécute ses commandes
    et leur cascade (le rôle du runtime Elmish). Un pas = un geste.
  - `takeOff msg state` : traite le geste **sans** exécuter ses commandes —
    l'opération part, l'utilisateur garde la main pendant le vol.
  - `landFlight (state, cmd)` : atterrit tout (commandes + cascade) —
    l'opération est terminée, la main est rendue.
  - `landOneWave (state, cmd)` : atterrit une seule vague, la suivante reste
    en vol — le moment « l'opération principale est finie, l'arrière-plan
    tourne encore » (ex. réanalyse post-rangement).
- **`Headless.fs`** — session Avalonia headless unique (Skia enregistré pour
  tout le processus, `PerAssembly`) ; initialisée avant tout test depuis
  `Main.fs`.
- **`Fixtures.fs`** — dossiers temporaires recréés par test et détruits en
  finally (jamais des vraies photos), fabriques JPEG/EXIF, sonde de capacité
  de casse (`testScenarioCaseSensitiveOnly` : test ignoré — jamais silencieux
  — sur un filesystem insensible).
- **`Rendering.fs`** — rend la **vraie vue** (`Main.view`) : lecture des
  pixels (`renderPhoto`) et de l'activation des boutons (`renderedButtons`)
  dans l'arbre logique.

## Commandes

```bash
dotnet test tests/RawTandem.ScenarioTests                  # tout (24 tests)
dotnet test tests/RawTandem.ScenarioTests --filter "FullyQualifiedName~RC-06"
dotnet test tests/RawTandem.UI.Tests                      # miroir machine, ~1 s
dotnet run --project tests/RawTandem.ScenarioTests        # sortie Expecto directe
```

Couverture : voir la procédure dans `docs/COVERAGE.md` (fusion des trois
projets de tests).
