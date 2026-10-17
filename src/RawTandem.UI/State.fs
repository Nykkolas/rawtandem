module RawTandem.UI.State

open Elmish
open RawTandem.Core.Constants
open RawTandem.Core.Navigation

// ========== Données de démonstration (maquette, aucun fichier lu) ==========

let demoFolderPath = "/home/utilisateur/Photos/demonstration"

let demoPhotoStems = [ for i in 1 .. 12 -> sprintf "P123%04d" i ]

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
      /// Stems des photos (ex. "P1230001"), sans extension.
      Photos: string list
      CurrentIndex: int
      RawCountAtRoot: int
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

[<Literal>]
let private UnexpectedConfirmationStatus =
    "Confirmation inattendue : aucune action effectuée."

let init () : State * Cmd<Msg> =
    { Screen = Working Home
      FolderPath = None
      Photos = []
      CurrentIndex = 0
      RawCountAtRoot = 0
      Status = "" },
    Cmd.none

/// Signal en status une confirmation qui ne correspond à aucune action
/// en attente : l'incohérence n'est jamais avalée en silence.
let private reportUnexpectedConfirmation (state: State) : State * Cmd<Msg> =
    { state with Status = UnexpectedConfirmationStatus }, Cmd.none

let update (msg: Msg) (state: State) : State * Cmd<Msg> =
    match msg with
    | OpenFolderClicked ->
        // Stub : le sélecteur de dossier natif n'est pas encore branché.
        { state with Status = "Action serait : ouvrir le sélecteur de dossier." }, Cmd.none

    | DemoFolderLoaded ->
        // Maquette : jeu de données factices, aucun fichier n'est lu.
        { state with
            Screen = Working Organizing
            FolderPath = Some demoFolderPath
            Photos = demoPhotoStems
            CurrentIndex = 0
            RawCountAtRoot = demoPhotoStems.Length
            Status = sprintf "Dossier de démonstration chargé : %d tandems." demoPhotoStems.Length },
        Cmd.none

    | MoveRawsRequested ->
        match state.Screen with
        | Working Organizing ->
            if state.RawCountAtRoot <= 0 then
                { state with Status = "Aucun RAW à déplacer." }, Cmd.none
            else
                { state with Screen = Confirming(Organizing, MoveRawFiles state.RawCountAtRoot) },
                Cmd.none
        | Working _ | Confirming _ ->
            // Le bouton n'est rendu qu'à l'écran d'organisation ; garde défensive.
            state, Cmd.none

    | MoveRawsConfirmed ->
        match state.Screen with
        | Confirming(Organizing, MoveRawFiles count) ->
            // Stub : le déplacement réel des fichiers n'est pas encore branché.
            { state with
                Screen = Working Triaging
                Status = sprintf "Action serait : déplacer %d RAW vers le sous-dossier « %s »." count RawSubfolderName },
            Cmd.none
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

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
                CurrentIndex = previous state.Photos.Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | NextClicked ->
        match state.Screen with
        | Working Triaging ->
            { state with
                CurrentIndex = next state.Photos.Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemRequested ->
        match state.Screen with
        | Working Triaging ->
            match List.tryItem state.CurrentIndex state.Photos with
            | Some stem ->
                { state with Screen = Confirming(Triaging, DeleteTandem stem) }, Cmd.none
            | None ->
                { state with Status = "Aucune photo à supprimer." }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemConfirmed ->
        match state.Screen with
        | Confirming(Triaging, DeleteTandem stem) ->
            // Stub : la suppression réelle (corbeille OS) n'est pas encore branchée.
            let photos, index = removeItem stem state.Photos state.CurrentIndex
            { state with
                Photos = photos
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
