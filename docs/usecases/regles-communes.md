# Règles communes

Règles transversales applicables à plusieurs UC. Elles ne se répètent pas
dans les scénarios : un UC qui s'en réclame les référence dans son contexte
(`règles communes: RC-01, RC-02`) ou inline à une ou des étape(s) particulière(s).

Leur couverture vit dans `tests/RawTandem.UI.Tests/ScenarioTests.fs` :
- ***coverage: générique*** : la règle a son propre test E2E dédié, valable pour
  toutes les vues/UC qui s'en réclament. Il est écrit à la validation de la règle
  (rouge pour la bonne raison s'il y a du nouveau), relu par l'utilisateur avant
  l'implémentation.
- ***coverage: instancié*** : la règle est couverte par les tests de scénario des
  UC qui la référencent ; pas de test dédié. À la validation de la règle, vérifier
  que ces tests l'exercent ; sinon, l'UC gagne l'étape/extension qui l'exerce, ou
  la règle passe en `générique`.
- Si rien n'est écrit, par défaut c'est `instancié`.

## RC-01 Interactions dans les vues de tri
***applies to:*** toute vue affichant une liste de photos (vues de tri)

***rule:***
- flèche à droite / flèche à gauche change la photo courante
- Arrêt à la dernière / première photo 
- Home/End va à la première/dernière photo
- Suppr. l'écran RC-02 s'affiche
- touche F ou F11 / double-clic : l'affichage bascule de plein écran à affichage normal et inversement 
- touche Echap. en plein écran : l'affichage redevient normal, sauf si une demande de confirmation est présentée à l'utilisateur
- Les boutons de l'interface et les interactions clavier sont identiques selon le mapping :
  - flèche à droite / flèche à gauche -> boutons à flèche
  - Suppr. -> bouton "supprimer le tandem"
  - Touche F ou F11, double-clic -> Bouton avec une icône "plein écran" classique
  - Echap. -> pas d'équivalent bouton
- En plein écran, le fonctionnement des touches reste valable
- En plein écran, si une boite de dialogue doit s'afficher, elle s'affiche par dessus l'image, comme le bandeau en vue normale (jaune en haut)
- En plein écran, la confirmation des actions (comme par exemple pour RC-02), est affichée brièvement en bas de l'écran, et elle est cachée le reste du temps

***observable effect:***
- En affichage normal : la photo affichée et le nom de fichier correspondent à la photo sélectionnée
- En plein écran, seule la photo est affichée, encadrée de noir, le bandeau jaune et la barre d'état s'affichent par dessus quand il y a quelque chose à dire
- La photo passe en plein écran
- La photo quitte le plein écran

***coverage:*** générique

## RC-02 Confirmation des actions destructives
***applies to:*** toute action envoyant des fichiers vers la corbeille

***rule:*** aucun envoi vers la corbeille sans écran de confirmation listant les fichiers concernés 

***observable effect:***
- l'utilisateur a la possibilité d'accepter ou refuser. 
- S'il accepte l'action est exécutée
- S'il refuse, rien n'est fait
- Dans tous les cas, un rappel de son choix est affiché dans la barre de statut en bas

***coverage:*** instancié

## RC-03 Corbeille système multi-plateforme 
***applies to:*** Toute suppression de fichier de l'application

***rule:*** 
- Les fichiers ne sont pas réellement supprimés, ils sont déplacés vers la corbeille système, que ce soit sous Linux ou sous Windows
- L'implémentation pour MacOS est hors scope pour le moment

***observable effect:*** Les fichiers ne sont plus dans le répertoire de départ et sont récupérables dans la corbeille système

***coverage:*** instancié