module Harness.Fixtures

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Expecto
open Elmish
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.Hosts
open Avalonia.Headless
open Avalonia.Media.Imaging
open Avalonia.LogicalTree
open SkiaSharp
open RawTandem.Core.Constants
open RawTandem.Core.Exif
open RawTandem.Core.FolderScan
open RawTandem.Core.Tandems
open RawTandem.UI.State

/// Préconditions du UC : dossier « Pellicule », 3 couples JPG+RW2 appariés à
/// la racine, aucun sous-répertoire RAW. Fichiers vides créés par le test :
/// aucune photo, seuls les noms appariés comptent. Le dossier est un déchet de
/// test recréé à chaque lancement, détruit en finally (pas des photos).
let internal withPellicule (run: string -> unit) =
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Pellicule-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    for stem in [ "P1"; "P2"; "P3" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(folder, stem + ".RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Préconditions du UC trier-dossier-deja-organise : dossier « Photos »,
/// 3 couples JPG+RW2 déjà organisés — JPG à la racine, RW2 dans RAW/,
/// aucune anomalie. Fichiers vides créés par le test : aucune photo, seuls
/// les noms appariés comptent. Le dossier est un déchet de test recréé à
/// chaque lancement, détruit en finally (pas des photos).
let internal withPhotos (run: string -> unit) =
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Photos-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    for stem in [ "P1"; "P2"; "P3" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(rawDir, stem + ".RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Préconditions de l'extension 2a : dossier « Pellicule » avec des problèmes
/// d'organisation — 2 bons couples à organiser (P1, P2), 1 couple inversé
/// (P3 : JPG et RW2 tous les deux dans RAW/), 1 doublon (P4 : couple organisé
/// + un RW2 en trop à la racine) et 1 JPG orphelin à la racine (P5). Fichiers
/// vides créés par le test, dossier détruit en finally (pas des photos).
let internal withPelliculeAProblemes (run: string -> unit) =
    let folder =
        Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Pellicule-2a-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore
    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    for stem in [ "P1"; "P2" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(folder, stem + ".RW2"), "")

    // P3 : couple inversé — les deux fichiers dans RAW/.
    File.WriteAllText(Path.Combine(rawDir, "P3.JPG"), "")
    File.WriteAllText(Path.Combine(rawDir, "P3.RW2"), "")

    // P4 : couple organisé + doublon (le RW2 hors de sa place à la racine).
    File.WriteAllText(Path.Combine(folder, "P4.JPG"), "")
    File.WriteAllText(Path.Combine(folder, "P4.RW2"), "")
    File.WriteAllText(Path.Combine(rawDir, "P4.RW2"), "")

    // P5 : JPG orphelin — signalé, jamais déplacé par l'application.
    File.WriteAllText(Path.Combine(folder, "P5.JPG"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Préconditions du cas limite « orphelins JPG seuls » (reproduit dans
/// l'application le 2026-10-30) : dossier « Pellicule » avec 2 JPG sans RAW
/// à la racine — aucun couple nulle part, aucun sous-répertoire RAW.
/// Fichiers vides créés par le test.
let internal withPelliculeOrphelinsJpgSeuls (run: string -> unit) =
    let folder =
        Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Pellicule-orphelins-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    for stem in [ "P1"; "P2" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Indique si le filesystem du dossier temporaire distingue la casse des
/// noms. Sonde de capacité réelle, pas test d'OS (règle AGENTS/ERREURS-
/// GENERIC) — même sonde que `RawTandem.Core.Tests/TestHelpers.fs`,
/// dupliquée ici car les projets de tests ne se référencent pas.
let private isCaseSensitiveFileSystem () =
    let folder = Path.Combine(Path.GetTempPath(), "rawtandem-scenario-case-probe", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    let probe = Path.Combine(folder, "sonde.tmp")
    File.WriteAllText(probe, "1")
    File.WriteAllText(Path.Combine(folder, "SONDE.tmp"), "2")
    let sensitive = File.ReadAllText probe = "1"
    Directory.Delete(folder, true)
    sensitive

/// Scénario joué uniquement si le filesystem distingue la casse. L'état « raw »
/// distinct de « RAW » (UC organiser-et-trier-dossier 2b) est impossible à
/// créer sur un FS insensible (NTFS fusionne les deux noms : le scan lit
/// « raw » comme « RAW », l'anomalie n'existe pas) — le scénario y serait
/// rouge pour une raison erronée : il est ignoré (compté « ignoré », jamais
/// silencieusement absent ni silencieusement vert).
let internal testScenarioCaseSensitiveOnly (name: string) (scenario: unit -> unit) : Expecto.Test =
    if isCaseSensitiveFileSystem () then
        Expecto.Tests.testCase name scenario
    else
        Expecto.Tests.ptestCase name scenario

/// Préconditions de l'extension 2b : dossier avec un tandem à organiser (P1)
/// et un répertoire « raw » en minuscules contenant un RW2 (P2) — invisible au
/// scan, qui ne lit que « RAW ». Fichiers vides, dossier détruit en finally.
let internal withDossierRawMalCassee (run: string -> unit) =
    let folder =
        Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-raw-2b-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore
    File.WriteAllText(Path.Combine(folder, "P1.JPG"), "")
    File.WriteAllText(Path.Combine(folder, "P1.RW2"), "")

    Directory.CreateDirectory(Path.Combine(folder, "raw")) |> ignore
    File.WriteAllText(Path.Combine(folder, "raw", "P2.RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Segment APP1 « Exif » minimal ne portant que le tag Orientation (RC-04),
/// inséré dans un JPEG encodé : en-tête "Exif\0\0", TIFF petit-boutiste (II),
/// IFD d'une entrée (tag 0x0112, type SHORT, count 1, valeur `value`).
let private exifOrientationSegment (value: byte) : byte[] =
    let payload =
        // "Exif\0\0" : en F#, \0 n'est pas une séquence d'échappement (il
        // deviendrait backslash + '0' littéraux) — octets explicites.
        [| yield! [| 0x45uy; 0x78uy; 0x69uy; 0x66uy; 0x00uy; 0x00uy |]
           yield! [| 0x49uy; 0x49uy |] // II (petit-boutiste)
           yield! [| 0x2Auy; 0x00uy |] // marqueur TIFF 42
           yield! [| 0x08uy; 0x00uy; 0x00uy; 0x00uy |] // IFD0 à l'octet 8
           yield! [| 0x01uy; 0x00uy |] // une entrée
           yield! [| 0x12uy; 0x01uy |] // tag 0x0112 Orientation
           yield! [| 0x03uy; 0x00uy |] // type SHORT
           yield! [| 0x01uy; 0x00uy; 0x00uy; 0x00uy |] // count 1
           yield! [| value; 0x00uy; 0x00uy; 0x00uy |] |] // valeur, alignée à gauche

    let nextIfdOffset = [| 0x00uy; 0x00uy; 0x00uy; 0x00uy |] // pas d'IFD suivante
    let body = Array.append payload nextIfdOffset
    let length = 2 + body.Length // le champ longueur se compte lui-même

    Array.append [| 0xFFuy; 0xE1uy; byte (length >>> 8); byte (length &&& 0xFF) |] body

/// Position d'insertion du segment APP1 : après le SOI et l'éventuel APP0
/// (JFIF) — l'ordre APP0 puis APP1 est la convention EXIF 2.2.
let private app1InsertPosition (jpg: byte[]) =
    if jpg.Length > 5 && jpg.[2] = 0xFFuy && jpg.[3] = 0xE0uy then
        2 + 2 + (int jpg.[4] * 256 + int jpg.[5]) // fin du segment APP0
    else
        2 // juste après le SOI

/// Fixture RC-04 : un JPG **paysage** (320×240, largeur > hauteur) à motif
/// orienté, encodé par Skia, accompagné ou non du tag EXIF Orientation. Le
/// quart haut-gauche est rouge (Firebrick), le reste bleu (RoyalBlue) : seule
/// la métadonnée dit comment afficher la photo — exactement le fichier que
/// produit un appareil photo tenu à la verticale (tag 6 : le capteur stocke
/// en paysage, l'EXIF dit « à afficher en portrait, quart de tour horaire »).
/// Le motif rend le **sens** de la rotation observable dans les pixels : une
/// simple comparaison hauteur/largeur ne distinguerait pas une rotation juste
/// d'une rotation à l'envers, ni ne verrait jamais un miroir.
let private landscapeJpeg (exifOrientation: byte option) : byte[] =
    let info = SKImageInfo(320, 240, SKColorType.Bgra8888)
    use surface = SKSurface.Create(info)
    let canvas = surface.Canvas
    canvas.Clear(SKColors.RoyalBlue)

    use paint = new SKPaint(Color = SKColors.Firebrick)
    canvas.DrawRect(SKRect.Create(0.0f, 0.0f, 160.0f, 120.0f), paint)

    use image = surface.Snapshot()
    use data = image.Encode(SKEncodedImageFormat.Jpeg, 90)
    let jpg = data.ToArray()

    match exifOrientation with
    | None -> jpg
    | Some value ->
        let at = app1InsertPosition jpg
        let segment = exifOrientationSegment value
        Array.concat [ jpg.[0 .. at - 1]; segment; jpg.[at ..] ]

/// Préconditions de la règle RC-04 : dossier « Verticales » organisé (JPG à la
/// racine, RW2 dans RAW/), 2 couples. P1 : JPG paysage sans EXIF. P2 : JPG
/// paysage avec EXIF Orientation 6 (photo « prise à la verticale »). Déchets
/// de test recréés à chaque lancement, détruits en finally.
let internal withVerticales (run: string -> unit) =
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Verticales-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    File.WriteAllBytes(Path.Combine(folder, "P1.JPG"), landscapeJpeg None)
    File.WriteAllBytes(Path.Combine(folder, "P2.JPG"), landscapeJpeg (Some 6uy))
    File.WriteAllText(Path.Combine(rawDir, "P1.RW2"), "")
    File.WriteAllText(Path.Combine(rawDir, "P2.RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)
