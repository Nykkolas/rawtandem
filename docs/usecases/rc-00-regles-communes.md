# Règles communes

Index des règles transversales applicables à plusieurs UC. Elles ne se
répètent pas dans les scénarios : un UC qui s'en réclame les référence dans son
contexte (`règles communes: RC-01, RC-02`) ou inline à une ou des étape(s)
particulière(s).

Leur couverture vit dans `tests/RawTandem.ScenarioTests/` (un fichier par
règle, `Specs/Rcxx…fs` — cartouche de chaque fichier : couverture et non couvert) :
- ***coverage: générique*** : la règle a son propre test E2E dédié, valable pour
  toutes les vues/UC qui s'en réclament. Il est écrit à la validation de la règle
  (rouge pour la bonne raison s'il y a du nouveau), relu par l'utilisateur avant
  l'implémentation.
- ***coverage: instancié*** : la règle est couverte par les tests de scénario des
  UC qui la référencent ; pas de test dédié. À la validation de la règle, vérifier
  que ces tests l'exercent ; sinon, l'UC gagne l'étape/extension qui l'exerce, ou
  la règle passe en `générique`.
- Si rien n'est écrit, par défaut c'est `instancié`.

## Les règles

| Règle | Fichier |
|---|---|
| RC-01 Interactions dans les vues de tri | `rc-01-interactions-vues-de-tri.md` |
| RC-02 Confirmation des actions destructives | `rc-02-confirmation-actions-destructives.md` |
| RC-03 Corbeille système multi-plateforme | `rc-03-corbeille-systeme-multi-plateforme.md` |
| RC-04 Affichage des JPG orienté selon la rotation EXIF | `rc-04-affichage-jpg-rotation-exif.md` |
| RC-05 Bandeau jaune modal | `rc-05-bandeau-jaune-modal.md` |
| RC-06 Actions asynchrones juste nécessaires | `rc-06-actions-asynchrones.md` |
