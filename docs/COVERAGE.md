# COVERAGE - Historique des mesures de couverture

Chaque mesure est prise quand toute la suite de tests est verte à la fin d'un
changement (règle dans `AGENTS.md` « Couverture de code »), puis ajoutée ici :
la dernière ligne sert de référence pour signaler l'écart à la mesure suivante.

Méthode : `dotnet test --collect:"XPlat Code Coverage"` sur chaque projet de
tests (après nettoyage des `TestResults/`), rapports fusionnés par ReportGenerator
dans `coveragereport/`. Les pourcentages sont en lignes ; `State.fs` est la
logique Elmish couverte par les tests headless — App/Main/Program (wiring
Avalonia) restent à 0 %, c'est un plafond connu, pas une dette.
La plateforme de la mesure est notée quand elle change ; le delta se lit
entre mesures de même plateforme (les branches dispatchées par plateforme
et les tests ignorés par sonde de capacité ne sont couverts que là où ils
tournent).

| Date | Total | Branches | Core | UI | State.fs | Écart / note |
|---|---|---|---|---|---|---|
| 2026-10-23 | 53,7 % | 66,6 % | 98,7 % | 41,5 % | 85,9 % | Mesure initiale (avant le workflow « la spec est le UC ») |
| 2026-10-26 | 61,7 % | 77,1 % | 99,4 % | 51,4 % | 97,3 % | UC trier-dossier-deja-organise (tri direct, `hasNothingToFix`) + correctif scan périmé : State +11,4 pts, Core +0,7 |
| 2026-10-26 (2) | 61,4 % | 77,2 % | 99,4 % | 51,1 % | 97,3 % | Nom du fichier JPG affiché au tri (`currentJpgFileName`) : UI −0,3 pt, uniquement des lignes de vue non couvertes (plafond Main.fs 0 %, déclaré) ; State et Core stables |
| 2026-10-26 (3) | 60,5 % | 77,2 % | 99,4 % | 50,2 % | 97,3 % | Disposition de la vue de tri (légende attachée, flèches flanquantes) : vue seule, −0,9 pt en lignes de vue non couvertes ; testable inchangé |
| 2026-10-26 (4) | 60,5 % | 77,3 % | 99,4 % | 50,4 % | 97,4 % | Règle commune RC-01 (première règle transversale, test E2E générique) : Home/End dans update couverts, State.fs stable ; Main.fs KeyDown non couvert (plafond headless déclaré) |
| 2026-10-26 (5) | 60,2 % | 75,8 % | 99,4 % | 50,1 % | 97,4 % | Correctif première flèche + subscription Elmish clavier en tunnel (remplace le hack `currentDispatch`) : tout l'écart est dans `Main.fs` (0 %, plafond headless déclaré — 8 branches du mapping `keyToMsg` non jouables sans vue) ; State et Core stables |
| 2026-10-26 (6) | 57,3 % | 74,2 % | 99,4 % | 47,3 % | 97,5 % | Règle RC-01 plein écran (F/F11/double-clic, Echap, garde confirmation) : machine couverte (State +0,1 pt), tout l'écart en lignes de vue `Main.fs` (composition plein écran, icône en Border — plafond headless déclaré) ; Core stable |
| 2026-10-26 (7) | 55,6 % | 73,4 % | 99,4 % | 45,6 % | 97,5 % | Suite RC-01 plein écran : vrai écran OS (WindowState.FullScreen par subscription clée) + statut éphémère 3 s en plein écran (StatusHidden, minuterie par subscription) : machine couverte, écart en lignes de vue/subscriptions `Main.fs` (plafond headless déclaré) ; State et Core stables |
| 2026-10-26 (8) | 56,9 % | 74,7 % | 99,4 % | 47,2 % | 97,6 % | Refactor : l'éphémérisation du statut quitte la vue (minuterie supprimée de Main.fs) pour la machine — enveloppe `update` planifiant une commande temporisée (StatusHidden) ; désormais couverte par un scénario E2E (délai réel attendu, suite UI ~14 s) + 3 tests unitaires miroir ; State +0,1 pt, branches +1,3 pt |
| 2026-10-27 (1) — Windows | 56,7 % | 72,6 % | 94,5 % | 47,1 % | 97,6 % | Première mesure sous Windows (RC-03) : Core −4,9 pts, entièrement expliqué par la plateforme — branches Linux de `sendToTrash` (`gio`, erreur stderr non vide) non exécutées ici, et 6 tests casse ignorés sur NTFS (sonde) → `normalizeRawFolderName` + agrégation `CaseCollisions` non couverts sur cette mesure ; miroir : `WindowsRecycle` n'est couvert que sous Windows ; UI/State stables |
| 2026-10-27 (2) — Linux | 55,7 % | 73,8 % | 89,1 % | 47,2 % | 97,6 % | Retour sous Linux après correction warning FS0066 (upcast redondant retiré dans `Main.fs`, zéro effet comportemental) ; vs 2026-10-26 (8) : Core −10,3 pts entièrement dus à `WindowsRecycle` (RC-03, ajouté après la dernière mesure Linux, non couvrable ici — miroir déjà déclaré dans la mesure Windows) ; UI/State stables |
| 2026-10-27 (3) — Linux | 70,6 % | 77,1 % | 89,8 % | 65,2 % | 97,9 % | RC-04 orientation EXIF : gros bond UI +18 pts — le hôte de test rend maintenant la vraie vue (`Main.view` via HostControl + Skia headless), `Main.fs` passe de 0 % à 32,1 % (plafond partiellement levé) ; nouveau module Core `Exif` couvert à 96,1 % (lecture + mapping des 8 tags) ; State +0,3 pt (rotation au décodage) ; Deletion inchangé (miroir WindowsRecycle) |
| 2026-10-28 (1) — Linux | 71,3 % | 77,5 % | 89,8 % | 66,3 % | 97,8 % | Correctif courses de scan (`requestId`) : 7 tests unitaires « scan request races » (résultats en ordre inverse, échec tardif, double-scan du même dossier, question affichée) ; State −0,1 pt — nouvelles branches de garde majoritairement couvertes, reste le `FolderScanFailed` frais pendant une question (statut seul) ; UI +1,1 pt, branches +0,4 pt ; Core inchangé |
| 2026-10-28 (2) — Linux | 71,7 % | 78,4 % | 89,8 % | 67,0 % | 97,9 % | Correctif courses des résultats d'opérations (dossier embarqué) : 9 tests unitaires « operation result races » (complétion/échec/plan/renommage/résolution d'un dossier quitté — bandeau informé, écran/scan protégés) ; branches +0,9 pt ; State +0,1 pt (retour au niveau d'avant le scan `requestId` après couverture des 4 branches d'échec dépassé) ; Core inchangé |
