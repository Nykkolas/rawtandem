module RawTandem.Core.Organization

open System
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan

/// Sens d'un déplacement planifié : vers le sous-dossier RAW, vers la racine,
/// ou renommage sur place (jumeaux de casse).
type MoveKind =
    | RawToSubfolder
    | JpgToRoot
    | RenameInPlace

/// Un déplacement élémentaire calculé par le Core. Type de frontière Core/UI :
/// l'UI n'exécute que ce plan, elle n'en invente pas.
type PlannedMove =
    { Kind: MoveKind
      SourcePath: string
      TargetPath: string
      /// Vrai quand la cible existe déjà : le renommage suit la convention
      /// système « stem (n).ext », jamais d'écrasement.
      TargetRenamed: bool }

/// Plan d'organisation : les déplacements à exécuter, l'UI n'exécute que ce plan.
type OrganizationPlan =
    { RawDirectoryPath: string
      Moves: PlannedMove list }

/// Résultat de l'exécution d'un plan : déplacements réussis et échecs
/// (un échec n'interrompt pas les autres déplacements).
type MoveOutcome = { Moved: PlannedMove list; Failed: (PlannedMove * string) list }

/// Première cible libre « stem (n).ext » dans le répertoire cible,
/// en suivant la convention du système pour les doublons de noms.
let rec private renamedTarget (targetDir: string) (fileName: string) (attempt: int) =
    let candidate =
        Path.Combine(
            targetDir,
            sprintf "%s (%d)%s" (Path.GetFileNameWithoutExtension fileName) attempt (Path.GetExtension fileName)
        )

    if File.Exists(candidate) then renamedTarget targetDir fileName (attempt + 1)
    else candidate

let private plannedMove kind (sourceDir: string) (fileName: string) (targetDir: string) =
    let directTarget = Path.Combine(targetDir, fileName)

    if File.Exists(directTarget) then
        { Kind = kind
          SourcePath = Path.Combine(sourceDir, fileName)
          TargetPath = renamedTarget targetDir fileName 1
          TargetRenamed = true }
    else
        { Kind = kind
          SourcePath = Path.Combine(sourceDir, fileName)
          TargetPath = directTarget
          TargetRenamed = false }

/// Plan de remise en ordre d'un dossier : les RAW de la racine (appariés ou
/// orphelins) rejoignent le sous-dossier RAW/, les JPG du sous-dossier
/// (tandems croisés, inversés, orphelins) rejoignent la racine. Les cibles
/// déjà prises ne sont jamais écrasées : renommage système de secours.
let planOrganization (folderPath: string) (scan: FolderScan) : OrganizationPlan =
    let rawDir = Path.Combine(folderPath, RawSubfolderName)

    let moves =
        [ // RAW de la racine → RAW/ : tandems à organiser puis RAW orphelins.
          for t in scan.TandemsToOrganize do
              plannedMove RawToSubfolder folderPath t.RawFileName rawDir
          for fileName in scan.OrphanRawsToOrganize do
              plannedMove RawToSubfolder folderPath fileName rawDir
          // Croisés : le JPG de RAW/ → racine, le RAW de la racine → RAW/.
          for t in scan.CrossedTandems do
              plannedMove JpgToRoot rawDir t.JpgFileName folderPath
              plannedMove RawToSubfolder folderPath t.RawFileName rawDir
          // Inversés et orphelins JPG de RAW/ : le JPG → racine.
          for t in scan.InvertedTandems do
              plannedMove JpgToRoot rawDir t.JpgFileName folderPath
          for fileName in scan.OrphanJpgsInRaw do
              plannedMove JpgToRoot rawDir fileName folderPath ]

    { RawDirectoryPath = rawDir; Moves = moves }

/// Plan de résolution des anomalies de noms : les doublons (fichier hors de sa
/// place) rejoignent leur place, les jumeaux de casse (même stem et extension
/// dans un même répertoire) sont renommés — selon la convention du système
/// (« stem (1).ext », incrémenté jusqu'au premier nom libre). Aucun fichier
/// n'est détruit ni écrasé.
let planDuplicateResolution (folderPath: string) (scan: FolderScan) : OrganizationPlan =
    let rawDir = Path.Combine(folderPath, RawSubfolderName)

    let moves =
        [ for fileName in scan.DuplicateRaws do
              plannedMove RawToSubfolder folderPath fileName rawDir
          for fileName in scan.DuplicateJpgs do
              plannedMove JpgToRoot rawDir fileName folderPath
          for collision in scan.CaseCollisions do
              let home = if collision.InRawSubfolder then rawDir else folderPath
              plannedMove RenameInPlace home collision.FileName home ]

    { RawDirectoryPath = rawDir; Moves = moves }

/// Exécute le plan : crée le sous-dossier RAW seulement si le plan y déplace
/// des fichiers, déplace chaque fichier sans jamais écraser une cible existante.
/// Un échec n'empêche pas les autres déplacements : il est renvoyé dans Failed.
let applyOrganization (plan: OrganizationPlan) : MoveOutcome =
    let moveOne (move: PlannedMove) =
        try
            File.Move(move.SourcePath, move.TargetPath)
            Ok move
        with ex ->
            Error(move, ex.Message)

    match plan.Moves with
    | [] -> { Moved = []; Failed = [] }
    | moves ->
        if moves |> List.exists (fun m -> m.Kind = RawToSubfolder) then
            Directory.CreateDirectory(plan.RawDirectoryPath) |> ignore

        let results = moves |> List.map moveOne

        { Moved = results |> List.choose (fun r -> match r with Ok m -> Some m | Error _ -> None)
          Failed = results |> List.choose (fun r -> match r with Error f -> Some f | Ok _ -> None) }

/// Renomme un dossier RAW mal cassé (ex. « raw ») en « RAW ». Refusé si
/// « RAW » existe déjà (dossier ou fichier) : l'appelant doit lever l'anomalie
/// avec l'utilisateur d'abord.
let normalizeRawFolderName (folderPath: string) (currentName: string) =
    let rawPath = Path.Combine(folderPath, RawSubfolderName)

    if Directory.Exists(rawPath) || File.Exists(rawPath) then
        invalidOp "Cannot normalize the RAW folder name: a RAW entry already exists"
    else
        Directory.Move(Path.Combine(folderPath, currentName), rawPath)
