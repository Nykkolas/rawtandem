# RC-04 Affichage des JPG orienté selon la rotation EXIF
***applies to:*** Toute vue affichant une photo

***rule:*** 
- L'orientation de l'image est conforme à la rotation du tag EXIF Orientation
- Si il n'y a pas de tag EXIF Orientation ou s'il n'y a pas d'info EXIF, pas de rotation

***observable effect:***
- Une photo qui a été prise avec l'appareil photo à la verticale s'affiche à la verticale dans l'application
- Idem pour les autres rotations, mirroir, etc...

***coverage:*** générique
