module RawTandem.Core.FolderScan

open System
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems

/// Résultat du scan d'un dossier : tandems appariés et orphelins,
/// tous triés par stem pour un affichage stable.
type FolderScan =
    { Tandems: Tandem list
      OrphanJpgs: string list
      OrphanRaws: string list }

/// Scan vide, valeur de départ avant toute analyse de dossier.
let emptyScan = { Tandems = []; OrphanJpgs = []; OrphanRaws = [] }

let private stemOf (fileName: string) = Path.GetFileNameWithoutExtension fileName

let private hasExtension (expected: string) (fileName: string) =
    String.Equals(Path.GetExtension fileName, expected, StringComparison.OrdinalIgnoreCase)

/// Scanne la racine du dossier (les sous-dossiers sont ignorés) et apparie
/// les JPG et RAW par stem, extensions comparées sans casse. Un JPG sans RAW
/// ou un RAW sans JPG est un orphelin : signalé, jamais traité en tandem.
let scanFolder (folderPath: string) : FolderScan =
    let fileNames =
        Directory.EnumerateFiles(folderPath) |> Seq.map Path.GetFileName |> List.ofSeq

    let jpgs = fileNames |> List.filter (hasExtension JpgExtension)
    let raws = fileNames |> List.filter (hasExtension RawExtension)

    let rawByStem = raws |> List.map (fun r -> stemOf r, r) |> Map.ofList
    let jpgStems = jpgs |> List.map stemOf |> Set.ofList

    let tandems =
        jpgs
        |> List.choose (fun j ->
            rawByStem
            |> Map.tryFind (stemOf j)
            |> Option.map (fun r -> { Stem = stemOf j; JpgFileName = j; RawFileName = r }))
        |> List.sortBy (fun t -> t.Stem)

    let orphanJpgs =
        jpgs
        |> List.filter (fun j -> not (rawByStem.ContainsKey(stemOf j)))
        |> List.sortBy stemOf

    let orphanRaws =
        raws
        |> List.filter (fun r -> not (jpgStems.Contains(stemOf r)))
        |> List.sortBy stemOf

    { Tandems = tandems; OrphanJpgs = orphanJpgs; OrphanRaws = orphanRaws }
