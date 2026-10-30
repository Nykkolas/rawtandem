# RC-01 Interactions dans les vues de tri
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
