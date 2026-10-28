module RawTandem.UI.State

open System
open System.IO
open Elmish
open Avalonia
open Avalonia.Media
open Avalonia.Media.Imaging
open RawTandem.Core.Constants
open RawTandem.Core.Navigation
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Organization
open RawTandem.Core.Deletion
open RawTandem.Core.Exif

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

/// Mode d'affichage de la vue de tri (règle RC-01) : le plein écran est un
/// mode d'exposition, pas un écran — il est orthogonal à `Screen`.
type DisplayMode =
    | Normal
    | Fullscreen

type State =
    { Screen: Screen
      FolderPath: string option
      /// Identifiant de la dernière demande de scan : incrémenté à chaque
      /// `FolderSelected`, embarqué dans chaque résultat de scan — `update`
      /// n'applique que les résultats de la demande la plus récente, les
      /// résultats d'une demande dépassée sont ignorés (courses de scan).
      ScanRequestId: int
      /// Résultat du scan du dossier, classé selon l'arbre de décision.
      Scan: FolderScan
      CurrentIndex: int
      /// Mode d'affichage de la vue de tri (RC-01) : plein écran ou non.
      DisplayMode: DisplayMode
      /// JPG du tandem courant, décodé à taille d'affichage (jamais le RAW).
      CurrentJpg: IImage option
      Status: string }

type Msg =
    | FolderSelected of path: string
    | FolderScanned of requestId: int * folderPath: string * scan: FolderScan
    | FolderScanFailed of requestId: int * folderPath: string * error: string
    | FolderRescanned of requestId: int * folderPath: string * scan: FolderScan
    | FolderRescanFailed of requestId: int * folderPath: string * error: string
    | OrganizeRequested
    | OrganizePlanned of folderPath: string * plan: OrganizationPlan
    | OrganizePlanningFailed of folderPath: string * error: string
    | OrganizeConfirmed
    | OrganizeCompleted of folderPath: string * outcome: MoveOutcome
    | OrganizeNothingToMove of folderPath: string
    | OrganizeFailed of folderPath: string * error: string
    | OrganizeCancelled
    | ResolveDuplicatesRequested
    | ResolveDuplicatesConfirmed
    | ResolveDuplicatesCompleted of folderPath: string * outcome: MoveOutcome
    | ResolveDuplicatesFailed of folderPath: string * error: string
    | ResolveDuplicatesCancelled
    | NormalizeRawFolderRequested
    | NormalizeRawFolderConfirmed
    | NormalizeRawFolderCompleted of folderPath: string
    | NormalizeRawFolderFailed of folderPath: string * error: string
    | NormalizeRawFolderCancelled
    | StartTriagingClicked
    | PreviousClicked
    | NextClicked
    | FirstPhotoRequested
    | LastPhotoRequested
    | FullscreenRequested
    | FullscreenExitRequested
    | StatusHidden
    | DeleteTandemRequested
    | DeleteTandemConfirmed
    | DeleteTandemCompleted of folderPath: string * stem: string * outcome: DeletionOutcome
    | DeleteTandemFailed of folderPath: string * stem: string * error: string
    | DeleteTandemCancelled
    | JpgLoaded of folderPath: string * stem: string * image: IImage
    | JpgLoadFailed of folderPath: string * stem: string * error: string

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

/// Vrai quand une question est affichée (bandeau jaune) : les gestes de
/// l'écran sous-jacent sont sans effet tant qu'elle n'est pas tranchée
/// (règle VISION — la garde vit dans `update`, elle seule fait autorité).
/// Modèle de vue : la vue s'en sert pour griser les actions, l'affordance
/// accompagne la garde sans jamais la remplacer.
let isQuestionPending (state: State) =
    match state.Screen with
    | Confirming _ -> true
    | Working _ -> false

/// Tandems visibles dans le tri : tous ceux dont le JPG est à la racine.
let triableTandems (state: State) =
    state.Scan.TandemsToOrganize @ state.Scan.OrganizedTandems

/// Largeur de décodage des JPG affichés : un cliché TZ200 fait 4896 px de
/// large ; 2048 px suffisent pour un affichage net dans la fenêtre sans
/// occuper ~70 Mo de mémoire par photo (une vingtaine seulement).
[<Literal>]
let private JpgDecodeWidth = 2048

/// Matrice de transformation d'affichage (repère écran, y vers le bas) pour
/// un bitmap de taille `size` (RC-04). La décomposition suit la norme EXIF :
/// miroir horizontal d'abord, rotation ensuite (sens horaire).
let private displayMatrix (transform: DisplayTransform) (size: Size) : Matrix =
    let w, h = size.Width, size.Height

    match transform with
    | Identity -> Matrix.Identity
    | MirrorHorizontal -> Matrix(-1.0, 0.0, 0.0, 1.0, w, 0.0)
    | Rotate180 -> Matrix(-1.0, 0.0, 0.0, -1.0, w, h)
    | MirrorVertical -> Matrix(1.0, 0.0, 0.0, -1.0, 0.0, h)
    | MirrorHorizontalRotate270 -> Matrix(0.0, 1.0, 1.0, 0.0, 0.0, 0.0) // transposée
    | Rotate90Clockwise -> Matrix(0.0, 1.0, -1.0, 0.0, h, 0.0)
    | MirrorHorizontalRotate90 -> Matrix(0.0, -1.0, -1.0, 0.0, h, w) // anti-transposée
    | Rotate270Clockwise -> Matrix(0.0, -1.0, 1.0, 0.0, 0.0, w)

/// True si la transformation échange largeur et hauteur du bitmap.
let private swapsDimensions (transform: DisplayTransform) =
    match transform with
    | Identity
    | MirrorHorizontal
    | Rotate180
    | MirrorVertical -> false
    | MirrorHorizontalRotate270
    | Rotate90Clockwise
    | MirrorHorizontalRotate90
    | Rotate270Clockwise -> true

/// Applique la transformation d'affichage EXIF au bitmap décodé (RC-04) :
/// l'image **affichée** est orientée selon la photo, le fichier n'est jamais
/// modifié. Identity : le bitmap passe tel quel, sans copie. Publiée pour
/// être vérifiée pixel par pixel par les tests (sens des rotations, miroirs).
let applyDisplayTransform (transform: DisplayTransform) (source: Bitmap) : Bitmap =
    match transform with
    | Identity -> source
    | _ ->
        let pw, ph = source.PixelSize.Width, source.PixelSize.Height
        let targetSize = if swapsDimensions transform then PixelSize(ph, pw) else PixelSize(pw, ph)
        // RenderTargetBitmap : cible bitmap avec DrawingContext (WriteableBitmap
        // n'en a pas), même compte de pixels que le source, DPI préservé.
        let target = new RenderTargetBitmap(targetSize, source.Dpi)

        (
            use context = target.CreateDrawingContext()
            use pushedTransform = context.PushTransform(displayMatrix transform source.Size)
            context.DrawImage(source, Rect(0.0, 0.0, source.Size.Width, source.Size.Height))
        )

        target :> Bitmap

/// Tandem courant dans le tri (None si la liste est vide) : sert au stem et au
/// nom de fichier exposés à la vue.
let private currentTandem (state: State) =
    triableTandems state |> List.tryItem state.CurrentIndex

/// Stem du tandem courant dans le tri (None si la liste est vide).
let private currentStem (state: State) =
    currentTandem state |> Option.map (fun tandem -> tandem.Stem)

/// Nom du fichier JPG du tandem courant, tel que scanné (casse d'origine
/// comprise — c'est lui qui existe sur disque, pas une reconstruction) :
/// la vue de tri l'affiche (UC organiser-et-trier-dossier étape 8,
/// trier-dossier-deja-organise étape 4). None si aucune photo n'est triable.
let currentJpgFileName (state: State) =
    currentTandem state |> Option.map (fun tandem -> tandem.JpgFileName)

/// Photo courante affichée à la verticale (mode portrait : plus haute que
/// large, RC-04) ou non. Lit l'image réellement chargée pour l'affichage :
/// la rotation EXIF doit se voir sur le bitmap décodé, pas seulement dans
/// l'intention. None : aucune photo chargée.
let currentJpgIsPortrait (state: State) =
    state.CurrentJpg |> Option.map (fun image -> image.Size.Height > image.Size.Width)

/// Libère la mémoire d'une image : un bitmap décodé n'attend pas le garbage
/// collector, la navigation reste légère photo après photo.
let private disposeImage (image: IImage) =
    match image with
    | :? Bitmap as bitmap -> bitmap.Dispose()
    | _ -> ()

/// Un JPG courant ne se charge que sur un écran de tri, avec ou sans question
/// en attente par-dessus : jamais depuis l'accueil ou l'organisation.
let private isTriagingScreen (state: State) =
    match state.Screen with
    | Working Triaging
    | Confirming(Triaging, _) -> true
    | Working Home
    | Working Organizing
    | Confirming(Home, _)
    | Confirming(Organizing, _) -> false

/// Commande de chargement du JPG du tandem courant — jamais le RAW, trop
/// lourd pour l'affichage. Décodage à largeur bornée, ratio préservé, puis
/// rotation selon le tag EXIF Orientation (RC-04) : l'image stockée par
/// l'appareil reste sur disque, seule l'image affichée est orientée.
/// Le dossier et le stem voyagent avec le résultat : un stem identique dans un
/// autre dossier est une autre photo, et un résultat du dossier précédent ne
/// doit ni s'afficher ni écraser le bandeau du courant.
let private loadCurrentJpgCmd (state: State) : Cmd<Msg> =
    match state.FolderPath, triableTandems state |> List.tryItem state.CurrentIndex with
    | Some folder, Some tandem when isTriagingScreen state ->
        let path = tandemJpgPath folder tandem
        let stem = tandem.Stem

        Cmd.OfAsync.either
            (fun () -> async {
                use stream = File.OpenRead(path)
                let bitmap = Bitmap.DecodeToWidth(stream, JpgDecodeWidth, BitmapInterpolationMode.HighQuality)
                let oriented =
                    match readDisplayTransform path with
                    | Some transform -> applyDisplayTransform transform bitmap
                    | None -> bitmap

                return JpgLoaded(folder, stem, oriented :> IImage)
            })
            ()
            id
            (fun ex -> JpgLoadFailed(folder, stem, ex.Message))
    | _ -> Cmd.none

// ========== Logic ==========

let init () : State * Cmd<Msg> =
    { Screen = Working Home
      FolderPath = None
      ScanRequestId = 0
      Scan = emptyScan
      CurrentIndex = 0
      DisplayMode = Normal
      CurrentJpg = None
      Status = "" },
    Cmd.none

/// Signal en status une confirmation qui ne correspond à aucune action
/// en attente : l'incohérence n'est jamais avalée en silence.
let private reportUnexpectedConfirmation (state: State) : State * Cmd<Msg> =
    { state with Status = UnexpectedConfirmationStatus }, Cmd.none

/// Fraîcheur d'un résultat de scan (ouverture ou rafraîchissement) : il
/// n'appartient qu'à la demande la plus récente ET au dossier courant —
/// double critère, comme le JPG (dossier + stem). Un résultat dépassé
/// (dossier changé, ou double demande du même dossier) est ignoré : ses
/// stems ne doivent jamais s'associer aux chemins d'un autre dossier.
let private isCurrentScanRequest (state: State) (requestId: int) (folderPath: string) =
    requestId = state.ScanRequestId && state.FolderPath = Some folderPath

/// Vrai quand le scan ne montre rien à remettre en ordre : aucun tandem à
/// organiser et aucune anomalie (croisés, inversés, orphelins rangés,
/// doublons, jumeaux de casse, dossier RAW mal cassé). Un dossier dans cet
/// état n'a rien à proposer à l'écran d'organisation.
let private hasNothingToFix (scan: FolderScan) =
    let duplicates = scan.DuplicateJpgs.Length + scan.DuplicateRaws.Length

    List.isEmpty scan.TandemsToOrganize
    && List.isEmpty scan.CrossedTandems
    && List.isEmpty scan.InvertedTandems
    && List.isEmpty scan.OrphanRawsToOrganize
    && List.isEmpty scan.OrphanJpgsInRaw
    && duplicates = 0
    && List.isEmpty scan.CaseCollisions
    && scan.RawFolderIssue.IsNone

/// Statut de synthèse après un scan : organisation à faire ou déjà faite.
let private scanStatus (scan: FolderScan) =
    let anomalyNote =
        match scan.RawFolderIssue with
        | Some RawFolderIsFile ->
            Some(sprintf " « %s » existe mais est un fichier : organisation impossible." RawSubfolderName)
        | Some (RawFolderCaseMismatch foundName) ->
            Some(sprintf " Dossier « %s » mal cassé : « %s » n'existe pas." foundName RawSubfolderName)
        | None -> None

    let baseStatus =
        if hasNothingToFix scan then
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

/// Commande asynchrone de scan d'un dossier : le résultat embarque la demande
/// (identifiant + dossier) qui l'a lancée, pour que `update` n'applique que
/// les résultats de la demande la plus récente.
let private scanCmd (requestId: int) (folderPath: string) : Cmd<Msg> =
    Cmd.OfAsync.either
        (fun path -> async { return scanFolder path })
        folderPath
        (fun scan -> FolderScanned(requestId, folderPath, scan))
        (fun ex -> FolderScanFailed(requestId, folderPath, ex.Message))

/// Commande de rafraîchissement du scan après une opération de l'application
/// (des fichiers ont bougé) : contrairement à scanCmd, le résultat ne change
/// pas d'écran — le dossier courant continue d'être affiché, le scan seul est
/// remplacé. Comme scanCmd, le résultat embarque la demande qui l'a lancée.
let private rescanCmd (requestId: int) (folderPath: string) : Cmd<Msg> =
    Cmd.OfAsync.either
        (fun path -> async { return scanFolder path })
        folderPath
        (fun scan -> FolderRescanned(requestId, folderPath, scan))
        (fun ex -> FolderRescanFailed(requestId, folderPath, ex.Message))

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

/// Statut de synthèse d'une exécution de plan, réussite ou échec : le bandeau
/// bas reflète toujours l'effet réel sur les fichiers.
let private movesOutcomeStatus (outcome: MoveOutcome) =
    if List.isEmpty outcome.Failed then
        completedStatus outcome
    else
        let failedMove, failedReason = List.head outcome.Failed

        sprintf
            "%s%d déplacements effectués, %d en échec (ex. %s : %s)."
            ErrorStatusPrefix
            outcome.Moved.Length
            outcome.Failed.Length
            (Path.GetFileName failedMove.SourcePath)
            failedReason

/// Statut de synthèse d'une résolution de doublons, réussite ou échec.
let private duplicatesOutcomeStatus (outcome: MoveOutcome) =
    if List.isEmpty outcome.Failed then
        sprintf "%s%d doublons et jumeaux de casse renommés et rangés." SuccessStatusPrefix outcome.Moved.Length
    else
        let failedMove, failedReason = List.head outcome.Failed

        sprintf
            "%s%d anomalies résolues, %d en échec (ex. %s : %s)."
            ErrorStatusPrefix
            outcome.Moved.Length
            outcome.Failed.Length
            (Path.GetFileName failedMove.SourcePath)
            failedReason

/// Vrai si un dossier autre que celui du résultat est affiché : le résultat
/// arrive après un changement de dossier (l'opération tournait en arrière-plan).
/// Il ne touche alors ni l'écran, ni le scan, ni l'index du dossier courant :
/// seul le bandeau bas reflète l'effet réel, jamais avalé — un dossier sans
/// dossier affiché (état incohérent) garde son comportement d'origine.
let private resultIsFromAnotherFolder (state: State) (folderPath: string) =
    state.FolderPath <> Some folderPath && state.FolderPath.IsSome

let private updateCore (msg: Msg) (state: State) : State * Cmd<Msg> =
    match msg with
    | FolderSelected path ->
        // Chaque sélection de dossier est une nouvelle demande de scan :
        // l'identifiant voyage avec le résultat, tout résultat d'une demande
        // dépassée est ignoré dans update (courses de scan, audit 2026-10-28).
        let requestId = state.ScanRequestId + 1

        { state with
            FolderPath = Some path
            ScanRequestId = requestId
            Status = "Analyse du dossier…" },
        scanCmd requestId path

    | FolderScanned(requestId, folderPath, scan) ->
        // Garde de fraîcheur : seul le résultat de la dernière demande
        // s'applique. Un scan retardé d'un dossier précédent ne doit ni
        // remplacer le scan affiché ni changer l'écran (ses stems seraient
        // associés aux chemins du dossier courant).
        if isCurrentScanRequest state requestId folderPath then
            state.CurrentJpg |> Option.iter disposeImage

            let scanned =
                { state with
                    Scan = scan
                    CurrentIndex = 0
                    CurrentJpg = None
                    Status = scanStatus scan }

            // Une question affichée n'est jamais refermée par un résultat
            // d'arrière-plan : le scan et le statut se mettent à jour sous
            // elle, l'écran (et la question qu'il porte) restent en place.
            match state.Screen with
            | Confirming _ -> scanned, Cmd.none
            | Working _ ->
                // Dossier déjà organisé sans rien à remettre en ordre : l'écran
                // d'organisation n'a rien à proposer, le tri est affiché directement
                // (UC trier-dossier-deja-organise). Toute anomalie restante garde
                // l'écran d'organisation, seul endroit où elle est signalée.
                if hasNothingToFix scan then
                    let nextState = { scanned with Screen = Working Triaging }
                    nextState, loadCurrentJpgCmd nextState
                else
                    { scanned with Screen = Working Organizing }, Cmd.none
        else
            state, Cmd.none

    | FolderScanFailed(requestId, folderPath, error) ->
        // Même garde : l'échec retardé d'un ancien dossier ne ramène pas
        // l'utilisateur à l'accueil. Sans question affichée, l'échec frais
        // ramène à l'accueil : c'est le seul endroit où l'on peut recommencer.
        if isCurrentScanRequest state requestId folderPath then
            state.CurrentJpg |> Option.iter disposeImage

            let status = sprintf "%sAnalyse du dossier impossible : %s" ErrorStatusPrefix error

            match state.Screen with
            | Confirming _ -> { state with CurrentJpg = None; Status = status }, Cmd.none
            | Working _ ->
                { state with
                    Screen = Working Home
                    CurrentJpg = None
                    Status = status },
                Cmd.none
        else
            state, Cmd.none

    | FolderRescanned(requestId, folderPath, scan) ->
        // Rien ne bouge que le scan : un rafraîchissement d'arrière-plan ne
        // referme jamais une question affichée et n'écrase pas le bilan
        // visible (règles VISION, audit itération 4). Le résultat n'appartient
        // qu'à la demande la plus récente du dossier courant.
        if isCurrentScanRequest state requestId folderPath then
            { state with Scan = scan }, Cmd.none
        else
            state, Cmd.none

    | FolderRescanFailed(requestId, folderPath, error) ->
        if isCurrentScanRequest state requestId folderPath then
            { state with
                Status = sprintf "%sRafraîchissement du dossier impossible : %s" ErrorStatusPrefix error },
            Cmd.none
        else
            state, Cmd.none

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
                        // Le résultat embarque le dossier : un plan d'un dossier
                        // quitté ne doit jamais ouvrir de question.
                        { state with Status = "Préparation du plan…" },
                        Cmd.OfAsync.either
                            (fun (folder, scan) -> async { return planOrganization folder scan })
                            (folder, state.Scan)
                            (fun plan -> OrganizePlanned(folder, plan))
                            (fun ex -> OrganizePlanningFailed(folder, ex.Message))
        | Working _ | Confirming _ -> state, Cmd.none

    | OrganizePlanned(folderPath, plan) ->
        if resultIsFromAnotherFolder state folderPath then
            // Le plan appartient à un dossier quitté : aucune question ne
            // s'ouvre pour des fichiers que l'utilisateur ne regarde plus.
            state, Cmd.none
        else
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

    | OrganizePlanningFailed(folderPath, error) ->
        if resultIsFromAnotherFolder state folderPath then
            // L'échec réel d'un dossier quitté reste annoncé, sans rien
            // d'autre : l'écran et le scan du dossier courant ne bougent pas.
            { state with Status = sprintf "%sPréparation du plan impossible : %s" ErrorStatusPrefix error }, Cmd.none
        else
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
                            return OrganizeNothingToMove folder
                        else
                            return OrganizeCompleted(folder, applyOrganization plan)
                    })
                    state.Scan
                    id
                    (fun ex -> OrganizeFailed(folder, ex.Message))
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | OrganizeCompleted(folderPath, outcome) ->
        if resultIsFromAnotherFolder state folderPath then
            // L'organisation d'un dossier quitté s'est terminée : le bandeau
            // seul reflète l'effet réel, ni l'écran ni le scan du dossier
            // courant ne bougent, aucun rafraîchissement n'est lancé.
            { state with Status = movesOutcomeStatus outcome }, Cmd.none
        else
            // Les fichiers ont bougé : le scan en mémoire est périmé (les RAW
            // déplacés y sont encore notés à la racine — bug du scan périmé,
            // 2026-10-24, attrapé par le scénario organiser-et-trier-dossier). Le dossier est
            // rafraîchi en arrière-plan, sans écran ni statut.
            let refresh =
                match state.FolderPath with
                | Some folder -> rescanCmd state.ScanRequestId folder
                | None -> Cmd.none

            if List.isEmpty outcome.Failed then
                // Passage automatique au tri seulement depuis l'écran d'organisation :
                // une question en attente n'est jamais refermée par un résultat
                // d'arrière-plan, la réussite pas plus que l'échec.
                if state.Screen = Working Organizing then
                    let nextState = { state with Screen = Working Triaging; Status = movesOutcomeStatus outcome }
                    nextState, loadCurrentJpgCmd nextState @ refresh
                else
                    { state with Status = movesOutcomeStatus outcome }, refresh
            else
                reportBackgroundFailure state (movesOutcomeStatus outcome), refresh

    | OrganizeNothingToMove(folderPath) ->
        if resultIsFromAnotherFolder state folderPath then
            { state with Status = "Rien à déplacer : le dossier a changé entre-temps." }, Cmd.none
        else
            match state.Screen with
            | Working Organizing ->
                { state with Status = "Rien à déplacer : le dossier a changé entre-temps." }, Cmd.none
            | Working _ | Confirming _ -> state, Cmd.none

    | OrganizeFailed(folderPath, error) ->
        if resultIsFromAnotherFolder state folderPath then
            // L'échec d'un dossier quitté ne ramène personne à l'écran
            // d'organisation : il est annoncé dans le bandeau, c'est tout.
            { state with Status = sprintf "%sOrganisation impossible : %s" ErrorStatusPrefix error }, Cmd.none
        else
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
                        return ResolveDuplicatesCompleted(folder, applyOrganization plan)
                    })
                    state.Scan
                    id
                    (fun ex -> ResolveDuplicatesFailed(folder, ex.Message))
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | ResolveDuplicatesCompleted(folderPath, outcome) ->
        if resultIsFromAnotherFolder state folderPath then
            // La résolution d'un dossier quitté s'est terminée : le bandeau
            // seul reflète l'effet réel, rien ne bouge pour le dossier courant.
            { state with Status = duplicatesOutcomeStatus outcome }, Cmd.none
        else
            match state.Screen with
            | Working Organizing | Confirming(Organizing, _) ->
                { state with Status = duplicatesOutcomeStatus outcome }, Cmd.none
            | Working _ | Confirming _ -> state, Cmd.none

    | ResolveDuplicatesFailed(folderPath, error) ->
        if resultIsFromAnotherFolder state folderPath then
            // L'échec d'un dossier quitté est annoncé dans le bandeau, sans
            // éjecter l'utilisateur de l'écran courant.
            { state with Status = sprintf "%sRésolution des doublons impossible : %s" ErrorStatusPrefix error }, Cmd.none
        else
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
                        return NormalizeRawFolderCompleted folder
                    })
                    foundName
                    id
                    (fun ex -> NormalizeRawFolderFailed(folder, ex.Message))
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | NormalizeRawFolderCompleted(folderPath) ->
        if resultIsFromAnotherFolder state folderPath then
            // Le renommage réel d'un dossier quitté reste annoncé dans le
            // bandeau : sans écran imposé, sans re-analyse — le dossier
            // affiché n'est pas concerné.
            { state with Status = sprintf "%sDossier renommé en « %s »." SuccessStatusPrefix RawSubfolderName }, Cmd.none
        else
            // Le scan est obsolète : le contenu du dossier renommé devient visible.
            match state.FolderPath with
            | Some folder ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sDossier renommé en « %s », nouvelle analyse…" SuccessStatusPrefix RawSubfolderName },
                scanCmd state.ScanRequestId folder
            | None ->
                { state with
                    Screen = Working Organizing
                    Status = sprintf "%sDossier renommé en « %s »." SuccessStatusPrefix RawSubfolderName },
                Cmd.none

    | NormalizeRawFolderFailed(folderPath, error) ->
        if resultIsFromAnotherFolder state folderPath then
            // L'échec d'un dossier quitté est annoncé dans le bandeau, sans
            // éjecter l'utilisateur de l'écran courant.
            { state with Status = sprintf "%sRenommage impossible : %s" ErrorStatusPrefix error }, Cmd.none
        else
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
            let nextState = { state with Screen = Working Triaging; Status = "" }
            nextState, loadCurrentJpgCmd nextState
        | Working _ | Confirming _ -> state, Cmd.none

    | PreviousClicked ->
        match state.Screen with
        | Working Triaging ->
            let nextState =
                { state with
                    CurrentIndex = previous (triableTandems state).Length state.CurrentIndex }

            nextState, loadCurrentJpgCmd nextState
        | Working _ | Confirming _ -> state, Cmd.none

    | NextClicked ->
        match state.Screen with
        | Working Triaging ->
            let nextState =
                { state with CurrentIndex = next (triableTandems state).Length state.CurrentIndex }

            nextState, loadCurrentJpgCmd nextState
        | Working _ | Confirming _ -> state, Cmd.none

    // RC-01 (docs/usecases/regles-communes.md) : Home → première photo,
    // End → dernière photo. Mêmes gardes et même rechargement que les
    // flèches : la garde est dans update, le mapping touche → message
    // dans la vue.
    | FirstPhotoRequested ->
        match state.Screen with
        | Working Triaging ->
            let nextState = { state with CurrentIndex = 0 }
            nextState, loadCurrentJpgCmd nextState
        | Working _ | Confirming _ -> state, Cmd.none

    | LastPhotoRequested ->
        match state.Screen with
        | Working Triaging ->
            let count = (triableTandems state).Length
            let nextState = { state with CurrentIndex = bounded count (count - 1) }
            nextState, loadCurrentJpgCmd nextState
        | Working _ | Confirming _ -> state, Cmd.none

    // RC-01 (docs/usecases/regles-communes.md) : F/F11/double-clic basculent
    // le plein écran, Echap le quitte — jamais pendant qu'une confirmation est
    // présentée (l'écran Confirming prime : la question doit être tranchée
    // d'abord). Le mode d'affichage est orthogonal à l'écran : les autres
    // gestes de tri restent valables en plein écran.
    | FullscreenRequested ->
        match state.Screen with
        | Working Triaging ->
            let nextMode =
                match state.DisplayMode with
                | Normal -> Fullscreen
                | Fullscreen -> Normal

            { state with DisplayMode = nextMode }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    | FullscreenExitRequested ->
        match state.Screen with
        | Working Triaging when state.DisplayMode = Fullscreen ->
            { state with DisplayMode = Normal }, Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

    // RC-01 : en plein écran, la barre de statut n'apparaît que brièvement
    // (~3 s, minuteur posé par la vue) puis se cache « le reste du temps ».
    // En vue normale, le statut est persistant (RC-02) : un StatusHidden
    // tardif — après sortie du plein écran — ne doit rien effacer.
    | StatusHidden ->
        match state.DisplayMode with
        | Fullscreen -> { state with Status = "" }, Cmd.none
        | Normal -> state, Cmd.none

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
            // Retour immédiat au tri, plus d'écran de confirmation pendant
            // l'exécution : aucune double confirmation possible (comme pour
            // l'organisation).
            match state.FolderPath with
            | None ->
                { state with
                    Screen = Working Triaging
                    Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
            | Some folder ->
                match findTriableTandem state.Scan stem with
                | None ->
                    { state with
                        Screen = Working Triaging
                        Status = sprintf "Le tandem %s n'est plus dans le dossier." stem },
                    Cmd.none
                | Some(tandem, rawLocation) ->
                    let stem = tandem.Stem

                    { state with
                        Screen = Working Triaging
                        Status = "Suppression en cours…" },
                    Cmd.OfAsync.either
                        (fun (tandem, location) -> async {
                            // Le plan (chemins complets des deux fichiers) est
                            // calculé dans la commande : les fichiers ont pu
                            // changer depuis la demande.
                            return DeleteTandemCompleted(folder, stem, applyDeletion (planDeletion folder location tandem))
                        })
                        (tandem, rawLocation)
                        id
                        (fun ex -> DeleteTandemFailed(folder, stem, ex.Message))
        | Confirming _ | Working _ -> reportUnexpectedConfirmation state

    | DeleteTandemCompleted(folder, stem, outcome) ->
        // Le résultat ne mute le scan que s'il vient du dossier courant : un
        // stem identique dans un autre dossier est une autre photo. Le bandeau
        // bas, lui, reflète toujours le résultat (règle VISION : jamais avalé).
        let isCurrentFolder = state.FolderPath = Some folder

        if List.isEmpty outcome.Failed then
            let status = sprintf "%sTandem %s supprimé : %s.JPG et %s.RW2 envoyés à la corbeille." SuccessStatusPrefix stem stem stem

            if isCurrentFolder then
                let toOrganize, _ = removeWhere (fun t -> t.Stem = stem) state.Scan.TandemsToOrganize state.CurrentIndex
                let organized, _ = removeWhere (fun t -> t.Stem = stem) state.Scan.OrganizedTandems state.CurrentIndex

                // L'index est ajusté sur la liste visible (à organiser + déjà organisés),
                // pas sur une seule des deux listes : le tandem supprimé peut être
                // dans l'une ou l'autre.
                let visibleCount = toOrganize.Length + organized.Length

                let nextState =
                    { state with
                        Scan = { state.Scan with TandemsToOrganize = toOrganize; OrganizedTandems = organized }
                        CurrentIndex = bounded visibleCount state.CurrentIndex
                        Status = status }

                nextState, loadCurrentJpgCmd nextState
            else
                { state with Status = status }, Cmd.none
        else
            // Échec (même partiel) : le tandem reste dans le scan — il est
            // toujours visible au tri — et chaque échec est signalé.
            let failedPath, failedReason = List.head outcome.Failed

            { state with
                Status =
                    sprintf
                        "%s%d fichier(s) envoyé(s) à la corbeille, %d en échec (ex. %s : %s)."
                        ErrorStatusPrefix
                        outcome.Trashed.Length
                        outcome.Failed.Length
                        (Path.GetFileName failedPath)
                        failedReason },
            Cmd.none

    | DeleteTandemFailed(_, stem, error) ->
        // Le dossier et le stem voyagent avec l'échec : l'utilisateur sait
        // quelle photo n'a pas pu partir à la corbeille (règle VISION :
        // un échec n'est jamais avalé, quel que soit le dossier affiché).
        { state with
            Status = sprintf "%sSuppression de %s impossible : %s" ErrorStatusPrefix stem error },
        Cmd.none

    // ---------- Affichage du JPG courant ----------

    | JpgLoaded(folder, stem, image) ->
        // Double critère de fraîcheur : le résultat vient du dossier courant ET
        // correspond au tandem courant. Un stem identique dans un autre dossier
        // est une autre photo ; un décodage lent ne doit pas non plus écraser
        // une navigation clavier plus rapide.
        if state.FolderPath = Some folder && currentStem state = Some stem then
            state.CurrentJpg |> Option.iter disposeImage
            { state with CurrentJpg = Some image }, Cmd.none
        else
            disposeImage image
            state, Cmd.none

    | JpgLoadFailed(folder, stem, error) ->
        // Un échec obsolète (photo déjà quittée ou dossier changé) n'écrase pas
        // le bandeau bas. L'échec du tandem courant, lui, est signalé — et ne
        // laisse pas la photo précédente affichée : l'image ne ment jamais sur
        // le tandem montré, même en échec.
        if state.FolderPath = Some folder && currentStem state = Some stem then
            state.CurrentJpg |> Option.iter disposeImage

            { state with
                CurrentJpg = None
                Status = sprintf "%sImpossible d'afficher %s.JPG : %s" ErrorStatusPrefix stem error },
            Cmd.none
        else
            state, Cmd.none

    | DeleteTandemCancelled ->
        match state.Screen with
        | Confirming(Triaging, DeleteTandem _) ->
            { state with
                Screen = Working Triaging
                Status = "Suppression du tandem annulée." },
            Cmd.none
        | Working _ | Confirming _ -> state, Cmd.none

/// RC-01 (docs/usecases/regles-communes.md) : en plein écran, la barre de
/// statut n'apparaît que brièvement puis se cache « le reste du temps ».
/// La machine programme l'effacement : dès qu'un statut apparaît (ou qu'on
/// entre en plein écran avec un statut affiché), une commande temporisée
/// dispatch StatusHidden. Délai de 3 s : convention toast (2–4 s), le temps
/// de lire une ligne (~3 mots/s, NN/g) sans qu'elle gêne la photo. La garde
/// de StatusHidden ignore un effacement arrivé après retour en vue normale,
/// où le statut est persistant (RC-02). Limite assumée : une commande Elmish
/// ne s'annule pas — deux statuts rapprochés en plein écran peuvent voir
/// le premier effacer le second un peu plus tôt.
[<Literal>]
let StatusFullscreenDisplayMs = 3000

let private autoHideStatusCmd : Cmd<Msg> =
    Cmd.OfAsync.perform (fun () -> Async.Sleep StatusFullscreenDisplayMs) () (fun () -> StatusHidden)

/// Update enveloppant updateCore : la logique des messages vit dans
/// updateCore, l'éphémérisation du statut en plein écran est l'unique
/// préoccupation de l'enveloppe — un seul endroit, tous les messages couverts.
let update (msg: Msg) (state: State) : State * Cmd<Msg> =
    let nextState, cmd = updateCore msg state

    let enteredFullscreenWithStatus =
        nextState.DisplayMode = Fullscreen
        && state.DisplayMode = Normal
        && nextState.Status <> ""

    let statusAppearedInFullscreen =
        nextState.DisplayMode = Fullscreen
        && nextState.Status <> ""
        && nextState.Status <> state.Status

    if enteredFullscreenWithStatus || statusAppearedInFullscreen then
        nextState, Cmd.batch [ cmd; autoHideStatusCmd ]
    else
        nextState, cmd
