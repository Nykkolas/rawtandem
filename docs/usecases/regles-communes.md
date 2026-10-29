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

## RC-04 Affichage des JPG orienté selon la rotation EXIF
***applies to:*** Toute vue affichant une photo

***rule:*** 
- L'orientation de l'image est conforme à la rotation du tag EXIF Orientation
- Si il n'y a pas de tag EXIF Orientation ou s'il n'y a pas d'info EXIF, pas de rotation

***observable effect:*** 
- Une photo qui a été prise avec l'appareil photo à la verticale s'affiche à la verticale dans l'application
- Idem pour les autres rotations, mirroir, etc...

***coverage:*** générique

## RC-05 Bandeau jaune modal
***applies to:*** Toute vue, au moment où une confirmation est demandée à un utilisateur par l'affichage du bandeau jaune

***rule:*** 
- Aucune action d'utilisateur, autre que les actions du bandeau jaune, n'est possible (ni clic sur un bouton, ni touche clavier mappée par l'application)
- L'utilisateur voit sans ambiguïté (bouton grisé par exemple) que l'action est impossible

***observable effect:*** 
- Les widgets d'interaction sont visuellement bloqués (bouton grisé par exemple)
- Les actions sur le clavier mappées par l'application (touche F, flèches, etc...), hors interactions avec les boutons du bandeau, n'ont aucun effet

***coverage:*** générique (tests dédiés)

## RC-06 Actions asynchrones juste nécessaires
***applies to:*** 
- Toutes les vues et toutes les actions utilisateur
- Les actions asynchrones lancées automatiquement par l'application suite à une action initiée par l'utilisateur (ex. réanalyse du dossier suite au rangement) comptent comme l'action de l'utilisateur et entrent dans le scope de cette règle

***rule:*** 
- L'utilisateur ne peut pas lancer une nouvelle action tant que la précédente n'est pas terminée
- Exceptions : 
  - l'affichage de l'image dans la vue tri. L'affichage d'une image ne bloque pas les autres actions (ie. l'utilisateur peut passer à la suivante / précédente, ouvrir un dossier, décider de la supprimer, etc...)
  - L'ouverture d'un dossier est possible pendant une analyse ou réanalyse
  
***observable effect:*** 
- Les widgets d'action visuellement bloqués (bouton grisé par exemple)
- Les widgets d'action sont visuellement débloqués une fois que les actions sont terminées, y compris les actions déclenchées par l'application suite à une action de l'utilisateur
- Les actions sur le clavier mappées par l'application (touche F, flèches, etc...) suivent les règles de widgets et les exceptions

***coverage:*** générique (tests dédiés)