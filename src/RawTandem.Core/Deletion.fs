module RawTandem.Core.Deletion

open System
open System.Diagnostics
open System.IO
open RawTandem.Core.Constants
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan

/// Plan de suppression d'un tandem : les chemins complets des deux fichiers
/// (JPG + RAW) à envoyer à la corbeille OS. L'UI n'exécute que ce plan.
type DeletionPlan = { JpgPath: string; RawPath: string }

/// Résultat de l'exécution d'un plan de suppression : fichiers partis à la
/// corbeille et échecs (un échec n'interrompt pas l'autre fichier).
type DeletionOutcome = { Trashed: string list; Failed: (string * string) list }

/// Plan de suppression : les chemins complets du JPG (racine) et du RAW
/// (racine ou sous-dossier RAW/ selon l'emplacement donné par le scan,
/// voir `FolderScan.findTriableTandem`).
let planDeletion (folderPath: string) (rawLocation: RawLocation) (tandem: Tandem) : DeletionPlan =
    { JpgPath = Path.Combine(folderPath, tandem.JpgFileName)
      RawPath =
        match rawLocation with
        | AtRoot -> Path.Combine(folderPath, tandem.RawFileName)
        | InRawSubfolder -> Path.Combine(folderPath, RawSubfolderName, tandem.RawFileName) }

/// Envoie un fichier à la corbeille OS via `gio trash` (freedesktop) :
/// jamais de suppression définitive, le fichier reste récupérable.
/// La commande est un paramètre : point d'extension multi-plateforme
/// (voir TODO : Windows puis macOS) et chemin de test des erreurs
/// (commande absente, échec silencieux).
let trashFile (command: string) (path: string) : Result<unit, string> =
    try
        let startInfo = ProcessStartInfo(command)
        startInfo.ArgumentList.Add "trash"
        startInfo.ArgumentList.Add path
        startInfo.RedirectStandardError <- true

        use proc = Process.Start startInfo
        let stderr = proc.StandardError.ReadToEnd().Trim()
        proc.WaitForExit()

        if proc.ExitCode = 0 then
            Ok()
        else
            Error(
                if String.IsNullOrWhiteSpace stderr then
                    sprintf "%s exited with code %d" TrashCommand proc.ExitCode
                else
                    stderr
            )
    with ex ->
        Error ex.Message

/// Exécute le plan : envoie chaque fichier à la corbeille OS. Un échec
/// n'empêche pas l'autre fichier de partir : il est renvoyé dans Failed.
let applyDeletion (plan: DeletionPlan) : DeletionOutcome =
    let results = [ plan.JpgPath; plan.RawPath ] |> List.map (fun path -> path, trashFile TrashCommand path)

    { Trashed =
        results
        |> List.choose (fun (path, result) -> match result with Ok() -> Some path | Error _ -> None)
      Failed =
        results
        |> List.choose (fun (path, result) -> match result with Error reason -> Some(path, reason) | Ok() -> None) }
