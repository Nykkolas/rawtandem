# RC-05 Bandeau jaune modal
***applies to:*** Toute vue, au moment où une confirmation est demandée à un utilisateur par l'affichage du bandeau jaune

***rule:***
- Aucune action d'utilisateur, autre que les actions du bandeau jaune, n'est possible (ni clic sur un bouton, ni touche clavier mappée par l'application)
- L'utilisateur voit sans ambiguïté (bouton grisé par exemple) que l'action est impossible

***observable effect:***
- Les widgets d'interaction sont visuellement bloqués (bouton grisé par exemple)
- Les actions sur le clavier mappées par l'application (touche F, flèches, etc...), hors interactions avec les boutons du bandeau, n'ont aucun effet

***coverage:*** générique (tests dédiés)
