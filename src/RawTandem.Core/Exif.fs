module RawTandem.Core.Exif

open MetadataExtractor
open MetadataExtractor.Formats.Exif

/// Transformation à appliquer à l'image stockée pour l'afficher droite,
/// selon le tag EXIF Orientation (valeurs 1 à 8 de la norme, RC-04).
/// Les cas composés portent la décomposition de la norme : le miroir
/// horizontal d'abord, la rotation ensuite (sens horaire).
type DisplayTransform =
    | Identity // 1 : aucune transformation
    | MirrorHorizontal // 2 : miroir gauche-droite
    | Rotate180 // 3 : demi-tour
    | MirrorVertical // 4 : miroir haut-bas
    | MirrorHorizontalRotate270 // 5 : transposée (miroir diagonale ↘)
    | Rotate90Clockwise // 6 : quart de tour horaire
    | MirrorHorizontalRotate90 // 7 : anti-transposée (miroir diagonale ↗)
    | Rotate270Clockwise // 8 : quart de tour anti-horaire

/// Transformation d'affichage prescrite par un tag EXIF Orientation.
/// None si le tag est hors norme : aucune rotation devinée (règle RC-04 —
/// pas de tag exploitable, pas de rotation).
let displayTransform (tag: byte) : DisplayTransform option =
    match tag with
    | 1uy -> Some Identity
    | 2uy -> Some MirrorHorizontal
    | 3uy -> Some Rotate180
    | 4uy -> Some MirrorVertical
    | 5uy -> Some MirrorHorizontalRotate270
    | 6uy -> Some Rotate90Clockwise
    | 7uy -> Some MirrorHorizontalRotate90
    | 8uy -> Some Rotate270Clockwise
    | _ -> None

/// Tag EXIF Orientation (0x0112) du fichier image (1..8), None s'il est
/// absent. Fichier illisible (absent, vide, pas un JPEG…) : None, jamais
/// une exception — l'appelant est dans un pipeline de décodage qui rapporte
/// déjà les échecs réels.
let readOrientationTag (path: string) : byte option =
    try
        ImageMetadataReader
            .ReadMetadata(path)
            |> Seq.tryPick (fun directory ->
                match directory with
                | :? ExifDirectoryBase as exif ->
                    let mutable value = 0us

                    if exif.TryGetUInt16(ExifDirectoryBase.TagOrientation, &value) then
                        Some(byte value)
                    else
                        None
                | _ -> None)
    with _ ->
        None

/// Transformation à appliquer pour afficher l'image droite (RC-04) : lit le
/// tag EXIF Orientation du fichier et le traduit. None : pas de tag ou valeur
/// hors norme → aucune transformation.
let readDisplayTransform (path: string) : DisplayTransform option =
    readOrientationTag path |> Option.bind displayTransform
