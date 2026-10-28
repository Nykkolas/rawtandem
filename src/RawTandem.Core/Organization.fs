module RawTandem.Core.Organization

open System
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan

/// Un déplacement élémentaire calculé par le Core. Type de frontière Core/UI :
/// l'UI n'exécute que ce plan, elle n'en invente pas. Le plan d'option A ne
/// déplace que des RAW appariés vers le sous-dossier.
type PlannedMove =
    { SourcePath: string
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

let private plannedMove (sourceDir: string) (fileName: string) (targetDir: string) =
    let directTarget = Path.Combine(targetDir, fileName)

    if File.Exists(directTarget) then
        { SourcePath = Path.Combine(sourceDir, fileName)
          TargetPath = renamedTarget targetDir fileName 1
          TargetRenamed = true }
    else
        { SourcePath = Path.Combine(sourceDir, fileName)
          TargetPath = directTarget
          TargetRenamed = false }

/// Plan de rangement (option A : signaler, jamais réparer) : seuls les RAW
/// des **couples appariés** de la racine rejoignent le sous-dossier RAW/.
/// Orphelins, croisés, inversés, doublons et jumeaux de casse sont signalés
/// par le panneau « À corriger vous-même » — l'application ne les touche
/// jamais, l'utilisateur les corrige lui-même puis réanalyse. Les cibles déjà
/// prises ne sont jamais écrasées : renommage système de secours.
let planOrganization (folderPath: string) (scan: FolderScan) : OrganizationPlan =
    let rawDir = Path.Combine(folderPath, RawSubfolderName)

    let moves =
        [ for t in scan.TandemsToOrganize do
              plannedMove folderPath t.RawFileName rawDir ]

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
        // Le plan ne cible que le sous-dossier RAW : il est créé seulement
        // quand des fichiers y sont déplacés.
        Directory.CreateDirectory(plan.RawDirectoryPath) |> ignore

        let results = moves |> List.map moveOne

        { Moved = results |> List.choose (fun r -> match r with Ok m -> Some m | Error _ -> None)
          Failed = results |> List.choose (fun r -> match r with Error f -> Some f | Ok _ -> None) }
