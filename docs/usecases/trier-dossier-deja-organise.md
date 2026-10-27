# Résumé
level: user goal
title: Trier un dossier déjà organisé
user: utilisateur de l'application
but: Supprimer les fichiers d'un dossier déjà organisé

# Contexte
preconditions: Un dossier Photos avec des fichiers JPG et pour chaque fichier JPG un fichier RW2 correspondant dans un sous-répertoire RAW. Il n'y a pas de cas anormal (problème de nommage, un RW2 manque, un JPG manque, etc...)
success guarantee: les fichiers indésirables sont dans la corbeille (fichiers JPG + RW2), les autres photos restent intactes 
minimal guarantee: aucun fichier n'est effacé, tout est récupérable


# Scénario primaire
1. Ouvrir le répertoire "Photos" (un dossier avec des photos au format JPG, un sous-répertoire RAW et les fichiers RW2 correspondant aux JPG dans ce sous-répertoire)
2. L'application affiche directement le mode tri (le dossier est déjà organisé : rien à proposer)
3. L'utilisateur navigue dans les photos JPG affichées à l'écran (cf RC-01)
4. L'application affiche la photo en cours et le nom du fichier (XXXX.JPG)
5. L'utilisateur demande la suppression d'une photo
6. L'application demande à confirmer la suppression en affichant les noms des fichiers du tandem (XXXX.JPG + XXXX.RW2) à supprimer (cf RC-02)
7. L'utilisateur confirme 
8. Les fichiers JPG et RW2 sont envoyés vers la corbeille  (RC-03)
9. L'utilisateur répète les étapes 3 à 8 jusqu'à ce qu'il n'y ait plus de photo à supprimer

# Extensions
TODO