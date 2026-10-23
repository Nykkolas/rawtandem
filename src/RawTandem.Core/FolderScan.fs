module RawTandem.Core.FolderScan

open System
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems

/// Anomalie du sous-dossier RAW : soit « RAW » existe en tant que fichier,
/// soit seul un dossier au nom mal cassé existe (ex. « raw »).
type RawFolderIssue =
    | RawFolderIsFile
    | RawFolderCaseMismatch of foundName: string

/// Jumeau de casse : deux fichiers d'un même répertoire partagent le même stem
/// et la même extension sans la casse (ex. `P1.JPG` + `P1.jpg`, possible sur un
/// filesystem sensible à la casse). Le gagnant (premier nom en ordre ordinal)
/// sert à l'appariement ; le jumeau est signalé, jamais avalé.
type CaseCollision = { FileName: string; InRawSubfolder: bool }

/// Résultat du scan d'un dossier, classé selon l'arbre de décision de
/// l'organisation initiale (voir docs/VISION.md). Chaque catégorie est triée
/// par stem pour un affichage stable.
/// Conventions : dans les tandems, JpgFileName et RawFileName désignent les
/// fichiers en place (le JPG de la racine, le RAW du sous-dossier s'il existe).
/// DuplicateJpgs contient les noms des JPG de RAW/ (hors de leur place),
/// DuplicateRaws ceux des RAW de la racine.
type FolderScan =
    { TandemsToOrganize: Tandem list
      OrganizedTandems: Tandem list
      CrossedTandems: Tandem list
      InvertedTandems: Tandem list
      OrphanJpgs: string list
      OrphanJpgsInRaw: string list
      OrphanRawsToOrganize: string list
      FiledOrphanRaws: string list
      DuplicateJpgs: string list
      DuplicateRaws: string list
      CaseCollisions: CaseCollision list
      RawFolderIssue: RawFolderIssue option }

/// Scan vide, valeur de départ avant toute analyse de dossier.
let emptyScan =
    { TandemsToOrganize = []
      OrganizedTandems = []
      CrossedTandems = []
      InvertedTandems = []
      OrphanJpgs = []
      OrphanJpgsInRaw = []
      OrphanRawsToOrganize = []
      FiledOrphanRaws = []
      DuplicateJpgs = []
      DuplicateRaws = []
      CaseCollisions = []
      RawFolderIssue = None }

let private stemOf (fileName: string) = Path.GetFileNameWithoutExtension fileName

let private hasExtension (expected: string) (fileName: string) =
    String.Equals(Path.GetExtension fileName, expected, StringComparison.OrdinalIgnoreCase)

let private photosOf (fileNames: string seq) =
    fileNames
    |> Seq.filter (fun f -> hasExtension JpgExtension f || hasExtension RawExtension f)
    |> List.ofSeq

/// Anomalie du dossier RAW : « RAW » est un fichier, ou seul un dossier mal
/// cassé existe (« raw »). Quand « RAW » existe, un dossier mal cassé est un
/// sous-dossier ordinaire : silence.
let private rawFolderIssue (folderPath: string) =
    let rawPath = Path.Combine(folderPath, RawSubfolderName)

    if File.Exists(rawPath) then
        Some RawFolderIsFile
    elif not (Directory.Exists(rawPath)) then
        Directory.EnumerateDirectories(folderPath)
        |> Seq.map Path.GetFileName
        |> Seq.tryFind (fun name ->
            String.Equals(name, RawSubfolderName, StringComparison.OrdinalIgnoreCase)
            && not (String.Equals(name, RawSubfolderName, StringComparison.Ordinal)))
        |> Option.map RawFolderCaseMismatch
    else
        None

/// Classification d'un stem : un stem peut produire plusieurs faits
/// (un tandem ET un doublon, par exemple).
type private StemFact =
    | TandemToOrganize of tandem: Tandem
    | OrganizedTandem of tandem: Tandem
    | CrossedTandem of tandem: Tandem
    | InvertedTandem of tandem: Tandem
    | OrphanJpg of fileName: string
    | OrphanJpgInRaw of fileName: string
    | OrphanRawToOrganize of fileName: string
    | FiledOrphanRaw of fileName: string
    | DuplicateJpg of fileName: string
    | DuplicateRaw of fileName: string
    | CaseCollision of collision: CaseCollision

let private classifyStem
    (rootJpgByStem: Map<string, string list>)
    (rawJpgByStem: Map<string, string list>)
    (rootRawByStem: Map<string, string list>)
    (rawRawByStem: Map<string, string list>)
    (stem: string)
    : StemFact list =
    // Premier nom en ordre ordinal = gagnant de l'appariement ; les autres
    // sont des jumeaux de casse, signalés pour ne jamais être avalés.
    let winner (names: string list option) = names |> Option.map List.head

    let twins (inRaw: bool) (names: string list option) =
        names
        |> Option.map (fun ns -> ns |> List.tail |> List.map (fun n -> { FileName = n; InRawSubfolder = inRaw }))
        |> Option.defaultValue []

    let jR = winner (rootJpgByStem |> Map.tryFind stem)
    let jI = winner (rawJpgByStem |> Map.tryFind stem)
    let rR = winner (rootRawByStem |> Map.tryFind stem)
    let rI = winner (rawRawByStem |> Map.tryFind stem)

    let caseTwins =
        [ yield! twins false (rootJpgByStem |> Map.tryFind stem)
          yield! twins true (rawJpgByStem |> Map.tryFind stem)
          yield! twins false (rootRawByStem |> Map.tryFind stem)
          yield! twins true (rawRawByStem |> Map.tryFind stem) ]
        |> List.map StemFact.CaseCollision

    let duplicates =
        [ match jR, jI with
          | Some _, Some dup -> DuplicateJpg dup
          | _ -> ()
          match rR, rI with
          | Some dup, Some _ -> DuplicateRaw dup
          | _ -> () ]

    // Tandem : le JPG de la racine s'apparie en priorité au RAW du sous-dossier
    // (état organisé), puis au RAW de la racine ; sinon les fichiers de RAW/
    // forment les tandems inversé et croisé. L'ordre des motifs fait autorité.
    let pairing =
        match jR, rI, rR, jI with
        // jR, rI, rR, jI
        | Some j, Some r, _, _ ->
            [ OrganizedTandem { Stem = stem; JpgFileName = j; RawFileName = r } ]
        | Some j, None, Some r, _ ->
            [ TandemToOrganize { Stem = stem; JpgFileName = j; RawFileName = r } ]
        | None, Some r, _, Some j ->
            [ InvertedTandem { Stem = stem; JpgFileName = j; RawFileName = r } ]
        | None, _, Some r, Some j ->
            [ CrossedTandem { Stem = stem; JpgFileName = j; RawFileName = r } ]
        | Some j, None, None, _ -> [ OrphanJpg j ]
        | None, None, None, Some j -> [ OrphanJpgInRaw j ]
        | None, None, Some r, None -> [ OrphanRawToOrganize r ]
        | None, Some r, None, None -> [ FiledOrphanRaw r ]
        | None, Some _, Some _, None ->
            // RAW aux deux emplacements sans aucun JPG : le fait DuplicateRaw
            // émis plus haut gagne seul, sans fait d'orphelin supplémentaire
            // (décision épinglée dans FolderScanTests : « ... as a duplicate only ») ;
            // les deux copies se révéleront RAW orphelins rangés après résolution.
            []
        | None, None, None, None ->
            // Inatteignable : un stem classifié existe dans au moins une des
            // quatre listes. Le match reste sans joker pour qu'une combinaison
            // oubliée soit une erreur de compilation, pas un silence.
            []

    caseTwins @ duplicates @ pairing

/// Scanne la racine et le sous-dossier RAW/ (les autres sous-dossiers sont
/// ignorés en silence) et classe chaque stem selon l'arbre de décision :
/// emplacements du JPG et du RAW, orphelins signalés, doublons jamais avalés.
/// Extensions comparées sans casse ; stems comparés par valeur exacte.
let scanFolder (folderPath: string) : FolderScan =
    let rawPath = Path.Combine(folderPath, RawSubfolderName)
    let rawDirectoryExists = Directory.Exists(rawPath)

    let rootFiles =
        if Directory.Exists(folderPath) then
            photosOf (Directory.EnumerateFiles(folderPath) |> Seq.map Path.GetFileName)
        else
            []

    let rawFiles =
        if rawDirectoryExists then
            photosOf (Directory.EnumerateFiles(rawPath) |> Seq.map Path.GetFileName)
        else
            []

    /// Tous les noms d'un stem, triés : le premier gagne l'appariement,
    /// les suivants sont des jumeaux de casse (jamais avalés).
    let byStem (isExtension: string -> bool) (files: string list) =
        files
        |> List.filter isExtension
        |> List.groupBy stemOf
        |> List.map (fun (stem, names) -> stem, names |> List.sort)
        |> Map.ofList

    let rootJpgByStem = byStem (hasExtension JpgExtension) rootFiles
    let rawJpgByStem = byStem (hasExtension JpgExtension) rawFiles
    let rootRawByStem = byStem (hasExtension RawExtension) rootFiles
    let rawRawByStem = byStem (hasExtension RawExtension) rawFiles

    let stems =
        [ rootJpgByStem; rawJpgByStem; rootRawByStem; rawRawByStem ]
        |> List.collect Map.toList
        |> List.map fst
        |> List.distinct
        |> List.sort

    let facts =
        stems
        |> List.collect (classifyStem rootJpgByStem rawJpgByStem rootRawByStem rawRawByStem)

    let pickTandems (selector: StemFact -> Tandem option) =
        facts |> List.choose selector

    let pickFiles (selector: StemFact -> string option) =
        facts |> List.choose selector

    { TandemsToOrganize = pickTandems (fun f -> match f with TandemToOrganize t -> Some t | _ -> None)
      OrganizedTandems = pickTandems (fun f -> match f with OrganizedTandem t -> Some t | _ -> None)
      CrossedTandems = pickTandems (fun f -> match f with CrossedTandem t -> Some t | _ -> None)
      InvertedTandems = pickTandems (fun f -> match f with InvertedTandem t -> Some t | _ -> None)
      OrphanJpgs = pickFiles (fun f -> match f with OrphanJpg n -> Some n | _ -> None)
      OrphanJpgsInRaw = pickFiles (fun f -> match f with OrphanJpgInRaw n -> Some n | _ -> None)
      OrphanRawsToOrganize = pickFiles (fun f -> match f with OrphanRawToOrganize n -> Some n | _ -> None)
      FiledOrphanRaws = pickFiles (fun f -> match f with FiledOrphanRaw n -> Some n | _ -> None)
      DuplicateJpgs = pickFiles (fun f -> match f with DuplicateJpg n -> Some n | _ -> None)
      DuplicateRaws = pickFiles (fun f -> match f with DuplicateRaw n -> Some n | _ -> None)
      CaseCollisions =
        facts
        |> List.choose (fun f ->
            match f with
            | CaseCollision c -> Some c
            | _ -> None)
        |> List.sortBy (fun c -> c.FileName)
      RawFolderIssue = rawFolderIssue folderPath }
