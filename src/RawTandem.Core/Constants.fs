module RawTandem.Core.Constants

/// Nom du sous-dossier où les RAW sont déplacés à l'étape d'organisation.
[<Literal>]
let RawSubfolderName = "RAW"

/// Extension du fichier visualisable, comparée sans casse (`.jpg` = `.JPG`).
[<Literal>]
let JpgExtension = ".JPG"

/// Extension du RAW cible (Panasonic Lumix), comparée sans casse.
[<Literal>]
let RawExtension = ".RW2"

/// Commande de la corbeille OS (spécification freedesktop Trash, Linux) :
/// les fichiers supprimés restent récupérables, jamais détruits.
[<Literal>]
let TrashCommand = "gio"
