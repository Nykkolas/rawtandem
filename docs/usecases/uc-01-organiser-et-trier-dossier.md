# Résumé
level: user goal
title: Organiser et trier les photos d'un dossier
user: utilisateur de l'application
but: supprimer les photos qui ne doivent pas être conservées et avoir les fichiers RAW dans un sous répertoire pour que l'affichage soit correct dans l'application de gestion des photos

# Contexte
preconditions: le dossier "Pellicule" contient des fichiers JPG et RW2 appairés, c'est à dire que le radical est le même, seule l'extension change. Aucun sous-répertoire RAW n'existe
success guarantee: les fichiers indésirables sont dans la corbeille (fichiers JPG + RW2), les RAW sont dans un sous répertoire RAW
minimal guarantee: aucun fichier n'est effacé, tout est récupérable


# Scénario primaire
1. Ouvrir le répertoire "Pellicule" (un dossier avec la sortie brute de l'appareil photo = JPG + RW2 côte à côte)
2. l'application propose d'organiser les fichiers, c'est à dire de déplacer tous les RW2 appariés (ie. tandems correctement formés) vers un dossier RAW 
3. l'utilisateur demande l'organisation
4. l'application demande confirmation en listant clairement le nombre de fichiers qui seront déplacés (RC-05)
5. L'utilisateur confirme 
6. L'application crée le répertoire et déplace les fichiers RW2 appariés (ie. tandems correctement formés) 
7. L'utilisateur navigue dans les photos JPG affichées à l'écran (cf RC-01)
8. L'application affiche la photo en cours et le nom du fichier (XXXX.JPG)
9. L'utilisateur demande la suppression d'une photo 
10. L'application demande à confirmer la suppression en affichant les noms des fichiers du tandem (XXXX.JPG + XXXX.RW2) à supprimer (cf RC-02) (RC-05)
11. L'utilisateur confirme 
12. Les fichiers JPG et RW2 sont envoyés vers la corbeille (RC-03)
13. L'utilisateur répète les étapes 7 à 12 jusqu'à ce qu'il n'y ait plus de photo à supprimer

# Extensions
2a. Cas de problèmes d'organisation
    1. Si le dossier ouvert contient des problèmes d'organisation, les lister à l'utilisateur
    2. Si l'utilisateur le souhaite, il peut corriger les problèmes d'organisation manuellement dans l'explorateur
    3. Si l'utilisateur le souhaite, il peut réanalyser le dossier
    4. Si le dossier est réanalysé alors l'interface se met entièrement à jour (nombre de fichiers à ranger, problèmes d'organisation)
    5. Les étapes 2a. étapes 2 à 4 se répètent jusqu'à ce que l'utilisateur soit satisfait, à ce moment-là le scénario principal reprend

2b. Cas d'un répertoire raw ou d'un fichier nommé RAW existants
    1. Si un répertoire "raw" (en minuscules) existe déjà ou qu'un fichier nommé RAW existe alors l'application ne permet pas de ranger et signale l'erreur de la même manière que l'extension 2a étape 1
    2. L'utilisateur peut corriger la casse du répertoire ou le nom du fichier dans l'explorateur
    3. L'utilisateur peut réanalyser
    4. Retour à l'étape 2 du scénario primaire