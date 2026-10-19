module RawTandem.Core.Tandems

/// Un couple JPG+RAW reconnu par son stem de nom de fichier (partie sans extension).
/// Les noms de fichiers sont relatifs à la racine du dossier scanné.
/// Type du domaine : vocabulaire de l'utilisateur (voir docs/VISION.md), sans I/O.
type Tandem = { Stem: string; JpgFileName: string; RawFileName: string }
