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

/// Action en attente de confirmation explicite de l'utilisateur.
type PendingAction =
    | OrganizeFolder of rawMoves: int * jpgMoves: int
    | ResolveDuplicates of count: int
    | NormalizeRawFolder of foundName: string
    | DeleteTandem of stem: string

/// Écran courant : une action en attente de confirmation est un écran à part
/// entière, pas un champ optionnel. Une confirmation ne peut donc pas exister
/// sans action en attente.
type Screen =
    | Working of phase: Phase
    | Confirming of phase: Phase * action: PendingAction

type State =
    { Screen: Screen
      FolderPath: string option
      /// Résultat du scan du dossier, classé selon l'arbre de décision.
      Scan: FolderScan
      CurrentIndex: int
      Status: string }

type Msg =
    | FolderSelected of path: string
    | FolderScanned of scan: FolderScan
    | FolderScanFailed of error: string
    | OrganizeRequested
    | OrganizePlanned of plan: OrganizationPlan
    | OrganizePlanningFailed of error: string
    | OrganizeConfirmed
    | OrganizeCompleted of outcome: MoveOutcome
    | OrganizeNothingToMove
    | OrganizeFailed of error: string
    | OrganizeCancelled
    | ResolveDuplicatesRequested
    | ResolveDuplicatesConfirmed
    | ResolveDuplicatesCompleted of outcome: MoveOutcome
    | ResolveDuplicatesFailed of error: string
    | ResolveDuplicatesCancelled
    | NormalizeRawFolderRequested
    | NormalizeRawFolderConfirmed
    | NormalizeRawFolderCompleted
    | NormalizeRawFolderFailed of error: string
    | NormalizeRawFolderCancelled
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

// ========== Helpers ==========

/// Nombre de déplacements que l'organisation peut planifier à partir du
/// scan seul (sans I/O) : sert au libellé du bouton et aux gardes.
let fixableCount (scan: FolderScan) =
    scan.TandemsToOrganize.Length
    + scan.OrphanRawsToOrganize.Length
    + scan.CrossedTandems.Length * 2
    + scan.InvertedTandems.Length
    + scan.OrphanJpgsInRaw.Length

/// Tandems visibles dans le tri : tous ceux dont le JPG est à la racine.
let triableTandems (state: State) =
    state.Scan.TandemsToOrganize @ state.Scan.OrganizedTandems

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

/// Statut de synthèse après un scan : organisation à faire ou déjà faite.
let private scanStatus (scan: FolderScan) =
    let duplicates = scan.DuplicateJpgs.Length + scan.DuplicateRaws.Length

    let nothingToDo =
        List.isEmpty scan.TandemsToOrganize
        && List.isEmpty scan.CrossedTandems
        && List.isEmpty scan.InvertedTandems
        && List.isEmpty scan.OrphanRawsToOrganize
        && List.isEmpty scan.OrphanJpgsInRaw
        && duplicates = 0
        && List.isEmpty scan.CaseCollisions
        && scan.RawFolderIssue.IsNone

    let anomalyNote =
        match scan.RawFolderIssue with
        | Some RawFolderIsFile ->
            Some(sprintf " « %s » existe mais est un fichier : organisation impossible." RawSubfolderName)
        | Some (RawFolderCaseMismatch foundName) ->
            Some(sprintf " Dossier « %s » mal cassé : « %s » n'existe pas." foundName RawSubfolderName)
        | None -> None

    let baseStatus =
        if nothingToDo then
            sprintf
                "%sDossier déjà organisé : %d tandems, rien à remettre en ordre."
                SuccessStatusPrefix
                scan.OrganizedTandems.Length
        else
            sprintf
                "%s%d tandems à organiser, %d déjà organisés."
                SuccessStatusPrefix
                scan.TandemsToOrganize.Length
                scan.OrganizedTandems.Length

    match anomalyNote with
    | Some note -> baseStatus + note
    | None -> baseStatus

/// Commande asynchrone de scan d'un dossier.
let private scanCmd (folderPath: string) : Cmd<Msg> =
    Cmd.OfAsync.either
        (fun path -> async { return scanFolder path })
        folderPath
        FolderScanned
        (fun ex -> FolderScanFailed ex.Message)

/// Un message d'arrière-plan ne referme jamais une question affichée : annuler
/// une action en attente est le choix de l'utilisateur, pas celui d'un échec
/// qui arrive en arrière-plan. Sans question affichée, l'échec ramène à l'écran
/// d'organisation : c'est le seul endroit où l'on peut relancer.
let private reportBackgroundFailure (state: State) (status: string) : State =
    match state.Screen with
    | Confirming _ -> { state with Status = status }
    | Working _ -> { state with Screen = Working Organizing; Status = status }

/// Statut de synthèse d'une exécution de plan réussie.
let private completedStatus (outcome: MoveOutcome) =
    let rawMoved = outcome.Moved |> List.filter (fun m -> m.Kind = RawToSubfolder) |> List.length
    let jpgMoved = outcome.Moved |> List.filter (fun m -> m.Kind = JpgToRoot) |> List.length
    let renamed = outcome.Moved |> List.filter (fun m -> m.Kind = RenameInPlace) |> List.length

    let parts =
        [ if rawMoved > 0 then
              sprintf "%d RAW déplacés vers « %s »" rawMoved RawSubfolderName
          if jpgMoved > 0 then
              sprintf "%d JPG replacés à la racine" jpgMoved
          if renamed > 0 then
              sprintf "%d fichiers renommés (jumeaux de casse)" renamed ]

    if List.isEmpty parts then sprintf "%sRien à déplacer." SuccessStatusPrefix
    else sprintf "%s%s." SuccessStatusPrefix (String.concat ", " parts)

let update (msg: Msg) (state: State) : State * Cmd<Msg> =
    match msg with
    | FolderSelected path ->
        { state with
            FolderPath = Some path
            Status = "Analyse du dossier…" },
        scanCmd path

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

    // ---------- Organisation ----------

    | OrganizeRequested ->
        match state.Screen with
        | Working Organizing ->
            match state.Scan.RawFolderIssue with
            | Some RawFolderIsFile ->
                { state with
                    Status = sprintf "%s« %s » existe mais est un fichier : renommez-le pour pouvoir organiser." ErrorStatusPrefix RawSubfolderName },
                Cmd.none
            | Some(RawFolderCaseMismatch foundName) ->
                // Filet de sécurité derrière le bouton grisé : organiser pendant
                // l'anomalie créerait un second dossier « RAW/ » à côté de « raw »,
                // dont le contenu deviendrait invisible au scan suivant.
                { state with
                    Status =
                        sprintf
                            "%sLe dossier « %s » est mal cassé : renommez-le avant d'organiser."
                            ErrorStatusPrefix
                            foundName },
                Cmd.none
            | _ ->
                if fixableCount state.Scan = 0 then
                    { state with Status = "Rien à remettre en ordre." }, Cmd.none
                else
                    match state.FolderPath with
                    | None ->
                        { state with Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix }, Cmd.none
                    | Some folder ->
                        // Le plan (File.Exists par déplacement) est calculé dans une
                        // commande asynchrone : update reste hors du thread d'I/O.
                        { state with Status = "Préparation du plan…" },
                        Cmd.OfAsync.either
                            (fun (folder, scan) -> async { return planOrganization folder scan })
                            (folder, state.Scan)
                            OrganizePlanned
                            (fun ex -> OrganizePlanningFailed ex.Message)
        | Working _ | Confirming _ -> state, Cmd.none

    | OrganizePlanned plan ->
        match state.Screen with
        | Working Organizing ->
            if List.isEmpty plan.Moves then
                { state with Status = "Rien à remettre en ordre." }, Cmd.none
            else
                let rawMoves = plan.Moves |> List.filter (fun m -> m.Kind = RawToSubfolder) |> List.length
                let jpgMoves = plan.Moves |> List.filter (fun m -> m.Kind = JpgToRoot) |> List.length

                { state with
                    Screen = Confirming(Organizing, OrganizeFolder(rawMoves, jpgMoves)) },
                Cmd.none
        | Working _ | Confirming _ ->
            // L'utilisateur a quitté l'organisation : le plan est obsolète.
            state, Cmd.none

    | OrganizePlanningFailed error ->
        match state.Screen with
        | Working Organizing ->
            { state with
                Status = sprintf "%sPréparation du plan impossible : %s" ErrorStatusPrefix error },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | OrganizeConfirmed ->
        match state.Screen with
        | Confirming(Organizing, OrganizeFolder _) ->
            // Le plan est recalculé dans la commande d'exécution : les fichiers
            // ont pu changer depuis la demande. Plus d'écran de confirmation
            // pendant l'exécution, donc aucune double exécution possible.
            match state.FolderPath with
            | None ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
            | Some folder ->
                { state with
                    Screen = Working Organizing
                    Status = "Organisation en cours…" },
                Cmd.OfAsync.either
                    (fun scan -> async {
                        let plan = planOrganization folder scan

                        if List.isEmpty plan.Moves then
                            return OrganizeNothingToMove
                        else
                            return OrganizeCompleted(applyOrganization plan)
                    })
                    state.Scan
                    id
                    (fun ex -> OrganizeFailed ex.Message)
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | OrganizeCompleted outcome ->
        if List.isEmpty outcome.Failed then
            // Passage automatique au tri seulement depuis l'écran d'organisation :
            // une question en attente n'est jamais refermée par un résultat
            // d'arrière-plan, la réussite pas plus que l'échec.
            if state.Screen = Working Organizing then
                { state with
                    Screen = Working Triaging
                    Status = completedStatus outcome },
                Cmd.none
            else
                { state with Status = completedStatus outcome }, Cmd.none
        else
            let failedMove, failedReason = List.head outcome.Failed

            let status =
                sprintf
                    "%s%d déplacements effectués, %d en échec (ex. %s : %s)."
                    ErrorStatusPrefix
                    outcome.Moved.Length
                    outcome.Failed.Length
                    (Path.GetFileName failedMove.SourcePath)
                    failedReason

            reportBackgroundFailure state status, Cmd.none

    | OrganizeNothingToMove ->
        match state.Screen with
        | Working Organizing ->
            { state with Status = "Rien à déplacer : le dossier a changé entre-temps." }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | OrganizeFailed error ->
        reportBackgroundFailure state (sprintf "%sOrganisation impossible : %s" ErrorStatusPrefix error), Cmd.none

    | OrganizeCancelled ->
        match state.Screen with
        | Confirming(Organizing, OrganizeFolder _) ->
            { state with
                Screen = Working Organizing
                Status = "Organisation annulée." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    // ---------- Résolution des doublons ----------

    | ResolveDuplicatesRequested ->
        match state.Screen with
        | Working Organizing ->
            let count =
                state.Scan.DuplicateJpgs.Length
                + state.Scan.DuplicateRaws.Length
                + state.Scan.CaseCollisions.Length

            if count = 0 then
                { state with Status = "Aucun doublon." }, Cmd.none
            else
                { state with Screen = Confirming(Organizing, ResolveDuplicates count) }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | ResolveDuplicatesConfirmed ->
        match state.Screen with
        | Confirming(Organizing, ResolveDuplicates _) ->
            match state.FolderPath with
            | None ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
            | Some folder ->
                { state with
                    Screen = Working Organizing
                    Status = "Résolution des doublons en cours…" },
                Cmd.OfAsync.either
                    (fun scan -> async {
                        let plan = planDuplicateResolution folder scan
                        return ResolveDuplicatesCompleted(applyOrganization plan)
                    })
                    state.Scan
                    id
                    (fun ex -> ResolveDuplicatesFailed ex.Message)
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | ResolveDuplicatesCompleted outcome ->
        match state.Screen with
        | Working Organizing | Confirming(Organizing, _) ->
            if List.isEmpty outcome.Failed then
                { state with
                    Status = sprintf "%s%d doublons et jumeaux de casse renommés et rangés." SuccessStatusPrefix outcome.Moved.Length },
                Cmd.none
            else
                let failedMove, failedReason = List.head outcome.Failed

                { state with
                    Status =
                        sprintf
                            "%s%d anomalies résolues, %d en échec (ex. %s : %s)."
                            ErrorStatusPrefix
                            outcome.Moved.Length
                            outcome.Failed.Length
                            (Path.GetFileName failedMove.SourcePath)
                            failedReason },
                Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | ResolveDuplicatesFailed error ->
        reportBackgroundFailure state (sprintf "%sRésolution des doublons impossible : %s" ErrorStatusPrefix error), Cmd.none

    | ResolveDuplicatesCancelled ->
        match state.Screen with
        | Confirming(Organizing, ResolveDuplicates _) ->
            { state with
                Screen = Working Organizing
                Status = "Résolution des doublons annulée." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    // ---------- Normalisation du nom du dossier RAW ----------

    | NormalizeRawFolderRequested ->
        match state.Screen with
        | Working Organizing ->
            match state.Scan.RawFolderIssue with
            | Some(RawFolderCaseMismatch foundName) ->
                { state with Screen = Confirming(Organizing, NormalizeRawFolder foundName) }, Cmd.none
            | _ ->
                { state with Status = "Aucun dossier mal cassé à renommer." }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | NormalizeRawFolderConfirmed ->
        match state.Screen with
        | Confirming(Organizing, NormalizeRawFolder foundName) ->
            match state.FolderPath with
            | None ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
            | Some folder ->
                { state with
                    Screen = Working Organizing
                    Status = "Renommage du dossier en cours…" },
                Cmd.OfAsync.either
                    (fun name -> async {
                        do normalizeRawFolderName folder name
                        return NormalizeRawFolderCompleted
                    })
                    foundName
                    id
                    (fun ex -> NormalizeRawFolderFailed ex.Message)
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | NormalizeRawFolderCompleted ->
        // Le scan est obsolète : le contenu du dossier renommé devient visible.
        match state.FolderPath with
        | Some folder ->
            { state with
                Screen = Working Organizing
                Status = sprintf "%sDossier renommé en « %s », nouvelle analyse…" SuccessStatusPrefix RawSubfolderName },
            scanCmd folder
        | None ->
            { state with
                Screen = Working Organizing
                Status = sprintf "%sDossier renommé en « %s »." SuccessStatusPrefix RawSubfolderName },
            Cmd.none

    | NormalizeRawFolderFailed error ->
        reportBackgroundFailure state (sprintf "%sRenommage impossible : %s" ErrorStatusPrefix error), Cmd.none

    | NormalizeRawFolderCancelled ->
        match state.Screen with
        | Confirming(Organizing, NormalizeRawFolder _) ->
            { state with
                Screen = Working Organizing
                Status = "Renommage du dossier annulé." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    // ---------- Tri ----------

    | StartTriagingClicked ->
        match state.Screen with
        | Working Organizing ->
            { state with Screen = Working Triaging; Status = "" }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | PreviousClicked ->
        match state.Screen with
        | Working Triaging ->
            { state with
                CurrentIndex = previous (triableTandems state).Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | NextClicked ->
        match state.Screen with
        | Working Triaging ->
            { state with
                CurrentIndex = next (triableTandems state).Length state.CurrentIndex },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemRequested ->
        match state.Screen with
        | Working Triaging ->
            match triableTandems state |> List.tryItem state.CurrentIndex with
            | Some currentTandem ->
                { state with Screen = Confirming(Triaging, DeleteTandem currentTandem.Stem) }, Cmd.none
            | None ->
                { state with Status = "Aucune photo à supprimer." }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | DeleteTandemConfirmed ->
        match state.Screen with
        | Confirming(Triaging, DeleteTandem stem) ->
            // Stub : la suppression réelle (corbeille OS) n'est pas encore branchée.
            let toOrganize, _ = removeWhere (fun t -> t.Stem = stem) state.Scan.TandemsToOrganize state.CurrentIndex
            let organized, _ = removeWhere (fun t -> t.Stem = stem) state.Scan.OrganizedTandems state.CurrentIndex

            // L'index est ajusté sur la liste visible (à organiser + déjà organisés),
            // pas sur une seule des deux listes : le tandem supprimé peut être
            // dans l'une ou l'autre.
            let visibleCount = toOrganize.Length + organized.Length

            { state with
                Scan = { state.Scan with TandemsToOrganize = toOrganize; OrganizedTandems = organized }
                CurrentIndex = bounded visibleCount state.CurrentIndex
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
