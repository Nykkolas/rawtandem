# COVERAGE - Historique des mesures de couverture

Chaque mesure est prise quand toute la suite de tests est verte à la fin d'un
changement (règle dans `AGENTS.md` « Couverture de code »), puis ajoutée ici :
la dernière ligne sert de référence pour signaler l'écart à la mesure suivante.

Méthode : `dotnet test --collect:"XPlat Code Coverage"` sur chaque projet de
tests (après nettoyage des `TestResults/`), rapports fusionnés par ReportGenerator
dans `coveragereport/`. Les pourcentages sont en lignes ; `State.fs` est la
logique Elmish couverte par les tests headless — App/Main/Program (wiring
Avalonia) restent à 0 %, c'est un plafond connu, pas une dette.

| Date | Total | Branches | Core | UI | State.fs | Écart / note |
|---|---|---|---|---|---|---|
| 2026-10-23 | 53,7 % | 66,6 % | 98,7 % | 41,5 % | 85,9 % | Mesure initiale (avant le workflow « la spec est le UC ») |
| 2026-10-26 | 61,7 % | 77,1 % | 99,4 % | 51,4 % | 97,3 % | UC trier-dossier-deja-organise (tri direct, `hasNothingToFix`) + correctif scan périmé : State +11,4 pts, Core +0,7 |
| 2026-10-26 (2) | 61,4 % | 77,2 % | 99,4 % | 51,1 % | 97,3 % | Nom du fichier JPG affiché au tri (`currentJpgFileName`) : UI −0,3 pt, uniquement des lignes de vue non couvertes (plafond Main.fs 0 %, déclaré) ; State et Core stables |
| 2026-10-26 (3) | 60,5 % | 77,2 % | 99,4 % | 50,2 % | 97,3 % | Disposition de la vue de tri (légende attachée, flèches flanquantes) : vue seule, −0,9 pt en lignes de vue non couvertes ; testable inchangé |
| 2026-10-26 (4) | 60,5 % | 77,3 % | 99,4 % | 50,4 % | 97,4 % | Règle commune RC-01 (première règle transversale, test E2E générique) : Home/End dans update couverts, State.fs stable ; Main.fs KeyDown non couvert (plafond headless déclaré) |
| 2026-10-26 (5) | 60,2 % | 75,8 % | 99,4 % | 50,1 % | 97,4 % | Correctif première flèche + subscription Elmish clavier en tunnel (remplace le hack `currentDispatch`) : tout l'écart est dans `Main.fs` (0 %, plafond headless déclaré — 8 branches du mapping `keyToMsg` non jouables sans vue) ; State et Core stables |
