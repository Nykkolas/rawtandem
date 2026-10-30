# RC-05 Bandeau jaune modal
***applies to:*** Toute vue, au moment où une confirmation est demandée à un utilisateur par l'affichage du bandeau jaune

***rule:***
- Aucune action d'utilisateur, autre que les actions du bandeau jaune ci-dessous, n'est possible (ni clic sur un bouton, ni touche clavier mappée par l'application)
- Les raccourcis clavier suivants fonctionnent :
  - Echap. annule l'action pour laquelle le bandeau jaune est apparu (comme un clic sur "Annuler")
    - En plein écran, Echap. tranche la question sans quitter le plein écran
  - Entrée lance l'action pour laquelle le bandeau jaune est apparu (comme un clic sur le bouton accepter, quelque soit sa formulation; par exemple "Déplacer" ou "Supprimer")
  - Au cas où un des deux boutons ait le focus: Entrée active le bouton du bandeau qui a le focus ; sans focus sur un bouton du bandeau, Entrée accepte
- L'utilisateur voit sans ambiguïté (bouton grisé par exemple) que l'action est impossible

***observable effect:***
- Le bandeau jaune disparait quand un des deux choix est fait (annuler ou exécuter l'action)
- Les widgets d'interaction sont visuellement bloqués (bouton grisé par exemple)
- Les actions sur le clavier mappées par l'application (touche F, flèches, etc...), hors interactions avec les boutons du bandeau, n'ont aucun effet

***coverage:*** générique (tests dédiés)
