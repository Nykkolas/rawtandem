module RawTandem.UI.State

open System
open System.IO
open Elmish
open RawTandem.Core.Constants
open RawTandem.Core.Navigation
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Organization

// ========== Types ==========

type Phase =
    | Home
    | Organizing
    | Triaging

/// Action destructive en attente de confirmation explicite de l'utilisateur.
type PendingAction =
    | MoveRawFiles of count: int
    | DeleteTandem of stem: string

/// Écran courant : une action destructive en attente de confirmation est un
/// écran à part entière, pas un champ optionnel. Une confirmation ne peut
/// donc pas exister sans action en attente.
type Screen =
    | Working of phase: Phase
    | Confirming of phase: Phase * action: PendingAction

type State =
    { Screen: Screen
      FolderPath: string option
      /// Résultat du scan du dossier : tandems appariés et orphelins.
      Scan: FolderScan
      CurrentIndex: int
      Status: string }

type Msg =
    | FolderSelected of path: string
    | FolderScanned of scan: FolderScan
    | FolderScanFailed of error: string
    | MoveRawsRequested
    | MoveRawsPlanned of plan: RawMovePlan
    | MoveRawsPlanningFailed of error: string
    | MoveRawsConfirmed
    | MoveRawsCompleted of outcome: RawMoveOutcome
    | MoveRawsNothingToMove of collisions: int
    | MoveRawsFailed of error: string
    | MoveRawsCancelled
    | StartTriagingClicked
    | PreviousClicked
    | NextClicked
    | DeleteTandemRequested
    | DeleteTandemConfirmed
    | DeleteTandemCancelled

// ========== Statuts ==========

[<Literal>]
let SuccessStatusPrefix = "✓ "

[<Literal>]
let ErrorStatusPrefix = "✗ "

[<Literal>]
let private UnexpectedConfirmationStatus =
    "Confirmation inattendue : aucune action effectuée."

// ========== Logic ==========

let init () : State * Cmd<Msg> =
    { Screen = Working Home
      FolderPath = None
      Scan = emptyScan
      CurrentIndex = 0
      Status = "" },
    Cmd.none

/// Signal en status une confirmation qui ne correspond à aucune action
/// en attente : l'incohérence n'est jamais avalée en silence.
let private reportUnexpectedConfirmation (state: State) : State * Cmd<Msg> =
    { state with Status = UnexpectedConfirmationStatus }, Cmd.none

/// Statut de synthèse après un scan : tandems et éventuels orphelins.
let private scanStatus (scan: FolderScan) =
    let orphanNotes =
        [ if not (List.isEmpty scan.OrphanJpgs) then
              sprintf "%d JPG sans RAW (%s…)"
                  scan.OrphanJpgs.Length
                  (List.head scan.OrphanJpgs)
          if not (List.isEmpty scan.OrphanRaws) then
              sprintf "%d RAW sans JPG (%s…)"
                  scan.OrphanRaws.Length
                  (List.head scan.OrphanRaws) ]

    let orphanSuffix =
        if List.isEmpty orphanNotes then ""
        else " Orphelins : " + String.concat ", " orphanNotes

    sprintf
        "%s%d tandems JPG+RAW détectés.%s"
        SuccessStatusPrefix
        scan.Tandems.Length
        orphanSuffix

let update (msg: Msg) (state: State) : State * Cmd<Msg> =
    match msg with
    | FolderSelected path ->
        // Le scan est asynchrone : la lecture du dossier peut être lente.
        { state with
            FolderPath = Some path
            Status = "Analyse du dossier…" },
        Cmd.OfAsync.either
            (fun path -> async { return scanFolder path })
            path
            FolderScanned
            (fun ex -> FolderScanFailed ex.Message)

    | FolderScanned scan ->
        { state with
            Screen = Working Organizing
            Scan = scan
            CurrentIndex = 0
            Status = scanStatus scan },
        Cmd.none

    | FolderScanFailed error ->
        { state with
            Screen = Working Home
            Status = sprintf "%sAnalyse du dossier impossible : %s" ErrorStatusPrefix error },
        Cmd.none

    | MoveRawsRequested ->
        match state.Screen with
        | Working Organizing ->
            if List.isEmpty state.Scan.Tandems then
                // Aucun tandem : le plan est trivialement vide, pas d'I/O nécessaire.
                { state with Status = "Aucun RAW à déplacer." }, Cmd.none
            else
                match state.FolderPath with
                | None ->
                    { state with Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix }, Cmd.none
                | Some folder ->
                    // Le plan (File.Exists par tandem) est calculé dans une commande
                    // asynchrone : update reste hors du thread d'I/O.
                    { state with Status = "Préparation du déplacement…" },
                    Cmd.OfAsync.either
                        (fun (folder, tandems) -> async { return planRawMoves folder tandems })
                        (folder, state.Scan.Tandems)
                        MoveRawsPlanned
                        (fun ex -> MoveRawsPlanningFailed ex.Message)
        | Working _ | Confirming _ ->
            // Le bouton n'est rendu qu'à l'écran d'organisation ; garde défensive.
            state, Cmd.none

    | MoveRawsPlanned plan ->
        match state.Screen with
        | Working Organizing ->
            if List.isEmpty plan.Moves then
                let status =
                    if List.isEmpty plan.Collisions then
                        "Aucun RAW à déplacer."
                    else
                        sprintf
                            "Aucun RAW à déplacer : %d fichiers déjà présents dans « %s »."
                            plan.Collisions.Length
                            RawSubfolderName

                { state with Status = status }, Cmd.none
            else
                let collisionNote =
                    if List.isEmpty plan.Collisions then ""
                    else
                        sprintf
                            "%d RAW déjà présents dans « %s » seront ignorés."
                            plan.Collisions.Length
                            RawSubfolderName

                { state with
                    Screen = Confirming(Organizing, MoveRawFiles plan.Moves.Length)
                    Status = collisionNote },
                Cmd.none
        | Working _ | Confirming _ ->
            // L'utilisateur a quitté l'organisation : le plan est obsolète.
            state, Cmd.none

    | MoveRawsPlanningFailed error ->
        match state.Screen with
        | Working Organizing ->
            { state with
                Status = sprintf "%sPréparation du déplacement impossible : %s" ErrorStatusPrefix error },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | MoveRawsConfirmed ->
        match state.Screen with
        | Confirming(Organizing, MoveRawFiles _) ->
            // Le plan est recalculé dans la commande d'exécution : les fichiers ont pu
            // changer depuis la demande. L'exécution ne se déclenche qu'une fois : plus
            // d'écran de confirmation pendant le déplacement.
            match state.FolderPath with
            | None ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
            | Some folder ->
                { state with
                    Screen = Working Organizing
                    Status = "Déplacement des RAW en cours…" },
                Cmd.OfAsync.either
                    (fun tandems -> async {
                        let plan = planRawMoves folder tandems

                        if List.isEmpty plan.Moves then
                            return MoveRawsNothingToMove plan.Collisions.Length
                        else
                            return MoveRawsCompleted(applyRawMovePlan plan)
                    })
                    state.Scan.Tandems
                    id
                    (fun ex -> MoveRawsFailed ex.Message)
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | MoveRawsNothingToMove collisions ->
        match state.Screen with
        | Working Organizing ->
            { state with
                Status =
                    sprintf
                        "%sAucun RAW déplacé : %d fichiers déjà présents dans « %s »."
                        ErrorStatusPrefix
                        collisions
                        RawSubfolderName },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | MoveRawsCompleted outcome ->
        if List.isEmpty outcome.Failed then
            let status =
                sprintf
                    "%s%d RAW déplacés vers le sous-dossier « %s »."
                    SuccessStatusPrefix
                    outcome.Moved.Length
                    RawSubfolderName

            match state.Screen with
            | Working Organizing | Confirming(Organizing, _) ->
                // Succès complet : confirmation affichée puis passage au tri.
                { state with Screen = Working Triaging; Status = status }, Cmd.none
            | Working _ | Confirming _ ->
                { state with Status = status }, Cmd.none
        else
            let failedFile, failedReason = List.head outcome.Failed

            let status =
                sprintf
                    "%s%d RAW déplacés, %d en échec (ex. %s : %s)."
                    ErrorStatusPrefix
                    outcome.Moved.Length
                    outcome.Failed.Length
                    (Path.GetFileName failedFile.SourcePath)
                    failedReason

            { state with
                Screen = Working Organizing
                Status = status },
            Cmd.none

    | MoveRawsFailed error ->
        { state with
            Screen = Working Organizing
            Status = sprintf "%sDéplacement impossible : %s" ErrorStatusPrefix error },
        Cmd.none

    | MoveRawsCancelled ->
        match state.Screen with
        | Confirming(Organizing, MoveRawFiles _) ->
            { state with
                Screen = Working Organizing
                Status = "Déplacement des RAW annulé." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | StartTriagingClicked ->
        match state.Screen with
        | Working Organizing ->
            { state with Screen = Working Triaging; Status = "" }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | PreviousClicked ->
        match state.Screen with
        | Working Triaging ->
            { state with
                CurrentIndex = previous state.Scan.Tandems.Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | NextClicked ->
        match state.Screen with
        | Working Triaging ->
            { state with
                CurrentIndex = next state.Scan.Tandems.Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemRequested ->
        match state.Screen with
        | Working Triaging ->
            match List.tryItem state.CurrentIndex state.Scan.Tandems with
            | Some currentTandem ->
                { state with Screen = Confirming(Triaging, DeleteTandem currentTandem.Stem) }, Cmd.none
            | None ->
                { state with Status = "Aucune photo à supprimer." }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemConfirmed ->
        match state.Screen with
        | Confirming(Triaging, DeleteTandem stem) ->
            // Stub : la suppression réelle (corbeille OS) n'est pas encore branchée.
            let tandems, index =
                removeWhere (fun (t: Tandem) -> t.Stem = stem) state.Scan.Tandems state.CurrentIndex

            { state with
                Scan = { state.Scan with Tandems = tandems }
                CurrentIndex = index
                Screen = Working Triaging
                Status = sprintf "Action serait : supprimer %s.JPG et %s.RW2 (vers la corbeille)." stem stem },
            Cmd.none
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | DeleteTandemCancelled ->
        match state.Screen with
        | Confirming(Triaging, DeleteTandem _) ->
            { state with
                Screen = Working Triaging
                Status = "Suppression du tandem annulée." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none
