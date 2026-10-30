# RC-03 Corbeille système multi-plateforme 
***applies to:*** Toute suppression de fichier de l'application

***rule:*** 
- Les fichiers ne sont pas réellement supprimés, ils sont déplacés vers la corbeille système, que ce soit sous Linux ou sous Windows
- L'implémentation pour MacOS est hors scope pour le moment

***observable effect:*** Les fichiers ne sont plus dans le répertoire de départ et sont récupérables dans la corbeille système

***coverage:*** instancié
