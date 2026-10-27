module RawTandem.Core.Deletion

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
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

/// Corbeille Windows (RC-03) : interop shell32 minimal, interne au module.
/// FO_DELETE + FOF_ALLOWUNDO envoie le fichier à la corbeille — jamais de
/// suppression définitive, le fichier reste récupérable.
module private WindowsRecycle =

    [<Literal>]
    let FoDelete = 3u

    [<Literal>]
    let FofAllowUndo = 0x40us

    [<Literal>]
    let FofNoConfirmation = 0x10us

    [<Literal>]
    let FofSilent = 0x4us

    [<Literal>]
    let FofNoErrorUi = 0x400us

    [<Struct>]
    [<StructLayout(LayoutKind.Sequential)>]
    type ShFileOpStruct =
        { mutable hwnd: nativeint
          mutable wFunc: uint32
          mutable pFrom: nativeint
          mutable pTo: nativeint
          mutable fFlags: uint16
          mutable fAnyOperationsAborted: bool
          mutable hNameMappings: nativeint
          mutable lpszProgressTitle: nativeint }

    [<DllImport("shell32.dll", CharSet = CharSet.Unicode)>]
    extern uint32 SHFileOperationW(ShFileOpStruct& fileOp)

    /// Envoie un fichier à la corbeille Windows. Le chemin doit être
    /// doublement terminé par un caractère nul (contrat de SHFileOperation).
    let recycleFile (path: string) : Result<unit, string> =
        try
            let pFrom = Marshal.StringToHGlobalUni(path + "\000")

            try
                let mutable op =
                    { hwnd = IntPtr.Zero
                      wFunc = FoDelete
                      pFrom = pFrom
                      pTo = IntPtr.Zero
                      fFlags = FofAllowUndo ||| FofNoConfirmation ||| FofSilent ||| FofNoErrorUi
                      fAnyOperationsAborted = false
                      hNameMappings = IntPtr.Zero
                      lpszProgressTitle = IntPtr.Zero }

                let result = SHFileOperationW(&op)

                if result = 0u && not op.fAnyOperationsAborted then
                    Ok()
                elif op.fAnyOperationsAborted then
                    Error "the operation was aborted"
                else
                    Error(sprintf "SHFileOperation exited with code %d" (int result))
            finally
                Marshal.FreeHGlobal(pFrom)
        with ex ->
            Error ex.Message

/// Envoie un fichier à la corbeille OS via une commande externe (`gio trash`,
/// spécification freedesktop Trash, Linux) : jamais de suppression définitive,
/// le fichier reste récupérable. La commande et ses arguments sont des
/// paramètres : point d'extension multi-plateforme et chemin de test des
/// erreurs (commande absente, échec silencieux).
let trashFile (command: string) (args: string list) : Result<unit, string> =
    try
        let startInfo = ProcessStartInfo(command)
        args |> List.iter startInfo.ArgumentList.Add
        startInfo.RedirectStandardError <- true

        use proc = Process.Start startInfo
        let stderr = proc.StandardError.ReadToEnd().Trim()
        proc.WaitForExit()

        if proc.ExitCode = 0 then
            Ok()
        else
            Error(
                if String.IsNullOrWhiteSpace stderr then
                    sprintf "%s exited with code %d" command proc.ExitCode
                else
                    stderr
            )
    with ex ->
        Error ex.Message

/// Envoie un fichier à la corbeille OS selon la plateforme (RC-03) :
/// corbeille Windows sous Windows, `gio trash` (freedesktop) ailleurs.
/// Les fichiers ne sont jamais réellement supprimés.
let sendToTrash (path: string) : Result<unit, string> =
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
        WindowsRecycle.recycleFile path
    else
        trashFile TrashCommand [ "trash"; path ]

/// Exécute le plan : envoie chaque fichier à la corbeille OS. Un échec
/// n'empêche pas l'autre fichier de partir : il est renvoyé dans Failed.
let applyDeletion (plan: DeletionPlan) : DeletionOutcome =
    let results = [ plan.JpgPath; plan.RawPath ] |> List.map (fun path -> path, sendToTrash path)

    { Trashed =
        results
        |> List.choose (fun (path, result) -> match result with Ok() -> Some path | Error _ -> None)
      Failed =
        results
        |> List.choose (fun (path, result) -> match result with Error reason -> Some(path, reason) | Ok() -> None) }
