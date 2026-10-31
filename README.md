# RawTandem

Trieur de photos qui considère chaque cliché comme **un couple JPG+RAW** d'un seul geste :
visualiser les JPG, supprimer le couple entier (corbeille OS, récupérable) et organiser le
dossier (le RAW `.RW2` rejoint le sous-dossier `RAW/`, jamais chargé en mémoire).
Le déplacement des couples vers une destination choisie est prévu dans une version suivante.

![tests](https://github.com/Nykkolas/rawtandem/actions/workflows/tests.yml/badge.svg)
![coverage Core](https://raw.githubusercontent.com/Nykkolas/rawtandem/badges/coverage-core.svg)
![coverage State](https://raw.githubusercontent.com/Nykkolas/rawtandem/badges/coverage-state.svg)
![coverage Avalonia](https://raw.githubusercontent.com/Nykkolas/rawtandem/badges/coverage-avalonia.svg)

Couverture séparée : `Core` (domaine pur), `State` (machine à états Elmish) et
`Avalonia` (wiring `App`/`Main`/`Program`, non testable en headless — 0 % attendu).

## Build et tests

```bash
dotnet build
dotnet test --collect:"XPlat Code Coverage"
```

Le rapport de couverture HTML se génère via ReportGenerator :

```bash
reportgenerator -reports:"tests/*/TestResults/*/coverage.cobertura.xml" -targetdir:coveragereport
```

## Documentation

- But de l'application et décisions : [`docs/VISION.md`](docs/VISION.md)
- Travail en cours : [`docs/TODO.md`](docs/TODO.md)
- Règles du projet : [`AGENTS.md`](AGENTS.md)

Stack : F#, Avalonia.FuncUI + Elmish (UI), domaine pur dans `src/RawTandem.Core`,
tests Expecto dans `tests/` en miroir (exécution via `dotnet test`, couverture coverlet).

## IA
Application développée avec IA Mistral (Vibe Code)
Use Cases et Règles communes écrites par un humain
Tests de ces scénarios (ScenarioTests.fs), relus et validés par un humain
