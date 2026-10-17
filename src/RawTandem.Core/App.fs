namespace RawTandem.Core

open Elmish

module App =

    [<Literal>]
    let RawSubfolderName = "RAW"

    // ========== Types ==========

    type Phase =
        | Home
        | Organizing
        | Triaging

    /// Action destructive en attente de confirmation explicite de l'utilisateur.
    type PendingAction =
        | MoveRawFiles of count: int
        | DeleteTandem of stem: string

    type State =
        { Phase: Phase
          FolderPath: string option
          /// Stems des photos (ex. "P1230001"), sans extension.
          Photos: string list
          CurrentIndex: int
          RawCountAtRoot: int
          PendingAction: PendingAction option
          Status: string }

    type Msg =
        | OpenFolderClicked
        | DemoFolderLoaded
        | MoveRawsRequested
        | MoveRawsConfirmed
        | MoveRawsCancelled
        | StartTriagingClicked
        | PreviousClicked
        | NextClicked
        | DeleteTandemRequested
        | DeleteTandemConfirmed
        | DeleteTandemCancelled

    // ========== Logic ==========

    let demoFolderPath = "/home/utilisateur/Photos/demonstration"

    let demoPhotoStems = [ for i in 1 .. 12 -> sprintf "P123%04d" i ]

    let init () : State * Cmd<Msg> =
        { Phase = Home
          FolderPath = None
          Photos = []
          CurrentIndex = 0
          RawCountAtRoot = 0
          PendingAction = None
          Status = "" },
        Cmd.none

    let private clampIndex (photos: string list) (index: int) =
        if photos.IsEmpty then 0
        else index |> max 0 |> min (photos.Length - 1)

    let update (msg: Msg) (state: State) : State * Cmd<Msg> =
        match msg with
        | OpenFolderClicked ->
            // Stub : le sélecteur de dossier natif n'est pas encore branché.
            { state with Status = "Action serait : ouvrir le sélecteur de dossier." }, Cmd.none

        | DemoFolderLoaded ->
            // Stub : jeu de données de démonstration, aucun fichier n'est lu.
            { state with
                Phase = Organizing
                FolderPath = Some demoFolderPath
                Photos = demoPhotoStems
                CurrentIndex = 0
                RawCountAtRoot = demoPhotoStems.Length
                PendingAction = None
                Status = sprintf "Dossier de démonstration chargé : %d tandems." demoPhotoStems.Length },
            Cmd.none

        | MoveRawsRequested ->
            if state.RawCountAtRoot <= 0 then
                { state with Status = "Aucun RAW à déplacer." }, Cmd.none
            else
                { state with
                    PendingAction = Some(MoveRawFiles state.RawCountAtRoot)
                    Status = "" },
                Cmd.none

        | MoveRawsConfirmed ->
            match state.PendingAction with
            | Some(MoveRawFiles count) ->
                // Stub : le déplacement réel des fichiers n'est pas encore branché.
                { state with
                    Phase = Triaging
                    PendingAction = None
                    Status = sprintf "Action serait : déplacer %d RAW vers le sous-dossier « %s »." count RawSubfolderName },
                Cmd.none
            | _ -> state, Cmd.none

        | MoveRawsCancelled ->
            { state with
                PendingAction = None
                Status = "Déplacement des RAW annulé." },
            Cmd.none

        | StartTriagingClicked ->
            { state with
                Phase = Triaging
                PendingAction = None
                Status = "" },
            Cmd.none

        | PreviousClicked ->
            { state with CurrentIndex = clampIndex state.Photos (state.CurrentIndex - 1) }, Cmd.none

        | NextClicked ->
            { state with CurrentIndex = clampIndex state.Photos (state.CurrentIndex + 1) }, Cmd.none

        | DeleteTandemRequested ->
            match List.tryItem state.CurrentIndex state.Photos with
            | Some stem ->
                { state with PendingAction = Some(DeleteTandem stem) }, Cmd.none
            | None ->
                { state with Status = "Aucune photo à supprimer." }, Cmd.none

        | DeleteTandemConfirmed ->
            match state.PendingAction with
            | Some(DeleteTandem stem) ->
                // Stub : la suppression réelle (corbeille OS) n'est pas encore branchée.
                let photos = state.Photos |> List.filter (fun s -> s <> stem)
                { state with
                    Photos = photos
                    CurrentIndex = clampIndex photos state.CurrentIndex
                    PendingAction = None
                    Status = sprintf "Action serait : supprimer %s.JPG et %s.RW2 (vers la corbeille)." stem stem },
                Cmd.none
            | _ -> state, Cmd.none

        | DeleteTandemCancelled ->
            { state with
                PendingAction = None
                Status = "Suppression du tandem annulée." },
            Cmd.none

