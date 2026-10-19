module RawTandem.Core.Organization

open System
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems

/// Un déplacement élémentaire d'un RAW, calculé par le Core.
/// Type de frontière Core/UI : l'UI n'exécute que ce plan, elle n'en invente pas.
type RawMove = { SourcePath: string; TargetPath: string }

/// Plan de déplacement des RAW vers le sous-dossier : l'UI n'exécute que ce plan.
/// Les cibles déjà présentes dans le sous-dossier sont des collisions : signalées,
/// jamais planifiées, jamais écrasées.
type RawMovePlan =
    { RawDirectoryPath: string
      Moves: RawMove list
      Collisions: string list }

/// Résultat de l'exécution d'un plan : les déplacements réussis et les échecs
/// (un échec n'interrompt pas les autres déplacements).
type RawMoveOutcome = { Moved: RawMove list; Failed: (RawMove * string) list }

/// Calcule le plan de déplacement des RAW des tandems vers le sous-dossier RAW.
let planRawMoves (folderPath: string) (tandems: Tandem list) : RawMovePlan =
    let rawDirectoryPath = Path.Combine(folderPath, RawSubfolderName)

    let planned, collisions =
        tandems
        |> List.map (fun t ->
            let source = Path.Combine(folderPath, t.RawFileName)
            let target = Path.Combine(rawDirectoryPath, t.RawFileName)
            t.RawFileName, { SourcePath = source; TargetPath = target })
        |> List.partition (fun (fileName, _) -> not (File.Exists(Path.Combine(rawDirectoryPath, fileName))))

    { RawDirectoryPath = rawDirectoryPath
      Moves = planned |> List.map snd
      Collisions = collisions |> List.map fst }

/// Exécute le plan : crée le sous-dossier RAW si besoin (jamais pour un plan vide),
/// déplace chaque RAW sans jamais écraser une cible existante. Un échec n'empêche
/// pas les autres déplacements : il est renvoyé dans Failed avec son message.
let applyRawMovePlan (plan: RawMovePlan) : RawMoveOutcome =
    let moveOne (move: RawMove) =
        try
            File.Move(move.SourcePath, move.TargetPath)
            Ok move
        with ex ->
            Error(move, ex.Message)

    match plan.Moves with
    | [] -> { Moved = []; Failed = [] }
    | moves ->
        Directory.CreateDirectory(plan.RawDirectoryPath) |> ignore

        let results = moves |> List.map moveOne

        { Moved = results |> List.choose (fun r -> match r with Ok m -> Some m | Error _ -> None)
          Failed = results |> List.choose (fun r -> match r with Error f -> Some f | Ok _ -> None) }
