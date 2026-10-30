# RC-02 Confirmation des actions destructives
***applies to:*** toute action envoyant des fichiers vers la corbeille

***rule:*** aucun envoi vers la corbeille sans écran de confirmation listant les fichiers concernés 

***observable effect:***
- l'utilisateur a la possibilité d'accepter ou refuser. 
- S'il accepte l'action est exécutée
- S'il refuse, rien n'est fait
- Dans tous les cas, un rappel de son choix est affiché dans la barre de statut en bas

***coverage:*** instancié
