# Règles communes

Règles transversales applicables à plusieurs UC. Elles ne se répètent pas
dans les scénarios : un UC qui s'en réclame les référence dans son contexte
(`règles communes: RC-01, RC-02`) ou inline à une ou des étape(s) particulière(s). 
Dans certains cas, la couverture sera faite de façon générique, parfois par instance. L'étiquette "coverage" le précisera. Si rien n'est écrit, par défaut c'est par instance

## RC-01 Interactions dans les vues de tri
applies to: toute vue affichant une liste de photos (vues de tri)
rule: 
- flèche à droite / flèche à gauche change la photo courante
- Arrêt à la dernière / première photo 
- Home/End va à la première/dernière photo
- Suppr. l'écran RC-02 s'affiche
- Les boutons de l'interface et les interactions clavier sont identiques selon le mapping :
  - flèche à droite / flèche à gauche -> boutons à flèche
  - Suppr. -> bouton "supprimer le tandem"
observable effect: 
- la photo affichée et le nom de fichier correspondent à la photo sélectionnée
coverage: générique

## RC-02 Confirmation des actions destructives
applies to: toute action envoyant des fichiers vers la corbeille
rule: aucune corbeille sans écran de confirmation listant les fichiers concernés 
observable effect: 
- l'utilisateur a la possibilité d'accepter ou refuser. 
- S'il accepte l'action est exécutée
- S'il refuse, rien n'est fait
- Dans tous les cas, un rappel de son choix est affiché dans la barre de statut en bas
coverage: instancié