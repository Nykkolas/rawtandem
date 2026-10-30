# RC-06 Actions asynchrones juste nécessaires
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
