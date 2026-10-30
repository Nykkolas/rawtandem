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
    | OrganizeFolder of rawMoves: int
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

/// Opération asynchrone en vol (règle RC-06) : tant qu'elle tourne,
/// l'utilisateur ne lance pas de nouvelle action. L'affichage d'une image
/// n'en est jamais une (exception 1 : il ne pose jamais ce drapeau) ;
/// l'ouverture d'un dossier reste possible pendant une analyse ou une
/// réanalyse (exception 2). Les vols identifiés par demande (analyse,
/// réanalyse) portent leur requestId : un résultat dépassé lève le drapeau
/// qu'il retrace sans toucher un vol plus récent.
type InFlightOperation =
    | PlanningOrganize
    | OrganizeExecution
    | AnalyzingFolder of requestId: int
    | ReanalyzingFolder of requestId: int
    | TandemDeletion

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
      /// Opération asynchrone en vol (RC-06) : les gestes utilisateur sont
      /// bloqués tant qu'elle n'est pas terminée.
      InFlight: InFlightOperation option
      Status: string }

type Msg =
    | FolderSelected of path: string
    | FolderScanned of requestId: int * folderPath: string * scan: FolderScan
    | FolderScanFailed of requestId: int * folderPath: string * error: string
    | FolderRescanned of requestId: int * folderPath: string * scan: FolderScan
    | FolderRescanFailed of requestId: int * folderPath: string * error: string
    | RescanRequested
    | OrganizeRequested
    | OrganizePlanned of folderPath: string * plan: OrganizationPlan
    | OrganizePlanningFailed of folderPath: string * error: string
    | OrganizeConfirmed
    | OrganizeCompleted of folderPath: string * outcome: MoveOutcome
    | OrganizeNothingToMove of folderPath: string
    | OrganizeFailed of folderPath: string * error: string
    | OrganizeCancelled
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

/// Vrai quand une question est affichée (bandeau jaune) : les gestes de
/// l'écran sous-jacent sont sans effet tant qu'elle n'est pas tranchée
/// (règle VISION — la garde vit dans `update`, elle seule fait autorité).
/// Modèle de vue : la vue s'en sert pour griser les actions, l'affordance
/// accompagne la garde sans jamais la remplacer.
let isQuestionPending (state: State) =
    match state.Screen with
    | Confirming _ -> true
    | Working _ -> false

/// RC-06 : vrai quand une opération asynchrone bloque les nouvelles actions
/// utilisateur — la garde vit dans `update`, le grisage des widgets dans la
/// vue (l'affordance accompagne la garde sans jamais la remplacer).
let isBlockingOperationInFlight (state: State) = state.InFlight.IsSome

/// RC-06, exception 2 : l'ouverture d'un dossier reste possible pendant une
/// analyse ou une réanalyse — jamais pendant les autres vols.
let canOpenFolder (state: State) =
    match state.InFlight with
    | Some(AnalyzingFolder _) | Some(ReanalyzingFolder _) | None -> true
    | Some _ -> false

/// RC-06 : un message est un geste utilisateur (bouton, touche mappée) —
/// ni un résultat d'opération, ni le clavier non mappé, ni l'affichage
/// d'image (exception 1 : il ne pose jamais de drapeau). L'ouverture d'un
/// dossier en est un, bloqué pendant les vols sauf la réanalyse (exception
/// 2, tranchée dans isBlockedGesture).
let private isUserGesture (msg: Msg) =
    match msg with
    | FolderScanned _ | FolderScanFailed _
    | FolderRescanned _ | FolderRescanFailed _
    | OrganizePlanned _ | OrganizePlanningFailed _
    | OrganizeCompleted _ | OrganizeNothingToMove _ | OrganizeFailed _
    | DeleteTandemCompleted _ | DeleteTandemFailed _
    | JpgLoaded _ | JpgLoadFailed _
    | StatusHidden -> false
    | FolderSelected _
    | RescanRequested
    | OrganizeRequested | OrganizeConfirmed | OrganizeCancelled
    | StartTriagingClicked
    | PreviousClicked | NextClicked | FirstPhotoRequested | LastPhotoRequested
    | FullscreenRequested | FullscreenExitRequested
    | DeleteTandemRequested | DeleteTandemConfirmed | DeleteTandemCancelled -> true

/// RC-05 : les actions du bandeau jaune ne sont jamais bloquées — ce sont
/// elles qui tranchent la question affichée.
let private isBandeauAction (msg: Msg) =
    match msg with
    | OrganizeConfirmed | OrganizeCancelled
    | DeleteTandemConfirmed | DeleteTandemCancelled -> true
    | _ -> false

/// RC-06 : un geste utilisateur est bloqué pendant une opération en vol —
/// sauf l'ouverture d'un dossier pendant une analyse ou une réanalyse
/// (exception 2). RC-05 : pendant une question affichée, tout geste
/// utilisateur est bloqué, sauf les actions du bandeau — la sélection de
/// dossier est le seul producteur sans garde d'écran (filet : le chemin
/// utilisateur est fermé d'amont par RC-06, un message peut néanmoins
/// arriver d'une file ou d'un autre front sans fenêtre).
let private isBlockedGesture (state: State) (msg: Msg) =
    match state.InFlight with
    | None -> isQuestionPending state && isUserGesture msg && not (isBandeauAction msg)
    | Some(AnalyzingFolder _) | Some(ReanalyzingFolder _) ->
        match msg with
        | FolderSelected _ -> false
        | _ -> isUserGesture msg
    | Some _ -> isUserGesture msg

/// RC-06 : l'analyse suivie par le drapeau est terminée — le drapeau se
/// lève dès que son résultat (ou son échec) revient, frais **ou dépassé** :
/// un résultat dépassé (dossier re-demandé pendant l'analyse, ce que
/// l'exception 2 permet) ne doit jamais laisser l'application bloquée sans
/// rien en vol.
let private clearTrackedAnalysis (requestId: int) (state: State) =
    match state.InFlight with
    | Some(AnalyzingFolder tracked) when tracked = requestId -> { state with InFlight = None }
    | _ -> state

/// RC-06 : la réanalyse suivie par le drapeau est terminée — le drapeau se
/// lève dès que son résultat (ou son échec) revient, frais **ou dépassé** :
/// un résultat dépassé (dossier changé pendant la réanalyse, ce que
/// l'exception 2 permet) ne doit jamais laisser l'application bloquée sans
/// rien en vol.
let private clearTrackedRescan (requestId: int) (state: State) =
    match state.InFlight with
    | Some(ReanalyzingFolder tracked) when tracked = requestId -> { state with InFlight = None }
    | _ -> state

/// RC-06 : le drapeau de réanalyse se pose quand un rafraîchissement part
/// (demandé par l'utilisateur ou déclenché par le rangement) — sans
/// dossier, rien ne reviendra jamais le lever.
let private withReanalysisInFlight (state: State) =
    match state.FolderPath with
    | Some _ -> { state with InFlight = Some(ReanalyzingFolder state.ScanRequestId) }
    | None -> { state with InFlight = None }

/// « 1 photo », « 2 photos » — pluriel français régulier pour les comptes
/// dynamiques de l'UI, jamais de « photo(s) ».
let pluralized (count: int) (noun: string) =
    if count = 1 then sprintf "%d %s" count noun else sprintf "%d %ss" count noun

/// Question posée dans le bandeau jaune (RC-05) pour une action en attente de
/// confirmation. Le périmètre annoncé doit être exactement celui du plan qui
/// sera exécuté : la question ne promet jamais plus que l'application ne fera.
/// Modèle de vue : fonction pure testée, la vue n'en affiche que le texte.
let confirmationQuestion (action: PendingAction) =
    match action with
    | OrganizeFolder rawMoves ->
        // Option A : le plan ne déplace que les RAW des couples appariés —
        // la question n'annonce ni les orphelins ni d'autres fichiers.
        sprintf
            "Déplacer %s de la racine vers le sous-dossier « %s » ?"
            (pluralized rawMoves "RAW apparié")
            RawSubfolderName
    | DeleteTandem stem ->
        sprintf
            "Supprimer le tandem %s.JPG + %s.RW2 ? Les deux fichiers iront dans la corbeille (récupérables)."
            stem
            stem

/// Un problème d'organisation signalé à l'utilisateur — jamais réparé par
/// l'application (option A, PROPOSITION-organisation.md) : une phrase
/// humaine (le problème + le geste attendu, toujours visibles) et les
/// fichiers concernés avec leur emplacement pour le détail replié.
type SignaledProblem =
    { /// Phrase du panneau « À corriger vous-même » : quoi et comment
      /// corriger, jamais cachés derrière un clic (NN/g).
      Sentence: string
      /// Fichiers concernés avec leur emplacement, ex. « P3.JPG (dans RAW/) »
      /// — liste vide : rien à replier.
      FileNames: string list }

let private located fileName place = sprintf "%s (%s)" fileName place

/// Problèmes d'organisation signalés, jamais réparés : une entrée par
/// catégorie réellement présente dans le scan — le panneau « À corriger
/// vous-même » est absent si la liste est vide (pas d'UI morte). Le geste de
/// réanalyse vit au pied du panneau, pas dans chaque phrase.
let signaledProblems (scan: FolderScan) : SignaledProblem list =
    let inverted =
        if List.isEmpty scan.InvertedTandems then []
        else
            [ { Sentence =
                    sprintf
                        "%s : JPG et RAW tous les deux dans RAW/ — déplacez le JPG à la racine dans l'explorateur."
                        (pluralized scan.InvertedTandems.Length "photo")
                FileNames =
                    scan.InvertedTandems
                    |> List.collect (fun t -> [ located t.JpgFileName "dans RAW/"; located t.RawFileName "dans RAW/" ]) } ]

    let crossed =
        if List.isEmpty scan.CrossedTandems then []
        else
            [ { Sentence =
                    sprintf
                        "%s : JPG dans RAW/, RAW à la racine — remettez-les en place dans l'explorateur."
                        (pluralized scan.CrossedTandems.Length "photo")
                FileNames =
                    scan.CrossedTandems
                    |> List.collect (fun t -> [ located t.JpgFileName "dans RAW/"; located t.RawFileName "à la racine" ]) } ]

    let duplicateRaws =
        if List.isEmpty scan.DuplicateRaws then []
        else
            [ { Sentence =
                    sprintf
                        "%s : le même RW2 à la racine et dans RAW/ — supprimez ou déplacez le mauvais fichier."
                        (pluralized scan.DuplicateRaws.Length "doublon")
                FileNames = scan.DuplicateRaws |> List.map (fun f -> located f "à la racine") } ]

    let duplicateJpgs =
        if List.isEmpty scan.DuplicateJpgs then []
        else
            [ { Sentence =
                    sprintf
                        "%s : le même JPG à la racine et dans RAW/ — supprimez ou déplacez le mauvais fichier."
                        (pluralized scan.DuplicateJpgs.Length "doublon")
                FileNames = scan.DuplicateJpgs |> List.map (fun f -> located f "dans RAW/") } ]

    let caseCollisions =
        if List.isEmpty scan.CaseCollisions then []
        else
            [ { Sentence =
                    sprintf
                        "%s : deux fichiers ne diffèrent que par la casse — renommez le fichier en trop dans l'explorateur."
                        (if scan.CaseCollisions.Length = 1 then "1 jumeau de casse"
                         else sprintf "%d jumeaux de casse" scan.CaseCollisions.Length)
                FileNames =
                    scan.CaseCollisions
                    |> List.map (fun c ->
                        located c.FileName (if c.InRawSubfolder then "dans RAW/" else "à la racine")) } ]

    let orphanJpgsAtRoot =
        if List.isEmpty scan.OrphanJpgs then []
        else
            [ { Sentence =
                    sprintf "%d JPG sans RAW à la racine — appariez-les ou supprimez-les dans l'explorateur." scan.OrphanJpgs.Length
                FileNames = scan.OrphanJpgs |> List.map (fun f -> located f "à la racine") } ]

    let orphanRawsAtRoot =
        if List.isEmpty scan.OrphanRawsToOrganize then []
        else
            [ { Sentence =
                    sprintf
                        "%d RAW sans JPG à la racine — appariez-les ou supprimez-les dans l'explorateur."
                        scan.OrphanRawsToOrganize.Length
                FileNames = scan.OrphanRawsToOrganize |> List.map (fun f -> located f "à la racine") } ]

    let filedOrphanRaws =
        if List.isEmpty scan.FiledOrphanRaws then []
        else
            [ { Sentence =
                    sprintf
                        "%d RAW sans JPG déjà rangés dans RAW/ — appariez-les ou supprimez-les dans l'explorateur."
                        scan.FiledOrphanRaws.Length
                FileNames = scan.FiledOrphanRaws |> List.map (fun f -> located f "dans RAW/") } ]

    let orphanJpgsInRaw =
        if List.isEmpty scan.OrphanJpgsInRaw then []
        else
            [ { Sentence =
                    sprintf "%d JPG isolés dans RAW/ — appariez-les ou supprimez-les dans l'explorateur." scan.OrphanJpgsInRaw.Length
                FileNames = scan.OrphanJpgsInRaw |> List.map (fun f -> located f "dans RAW/") } ]

    let rawFolderIssue =
        match scan.RawFolderIssue with
        | Some(RawFolderCaseMismatch foundName) ->
            [ { Sentence =
                    sprintf
                        "Le dossier « %s » est mal cassé — renommez-le en « %s » dans l'explorateur pour permettre le rangement."
                        foundName
                        RawSubfolderName
                FileNames = [] } ]
        | Some RawFolderIsFile ->
            [ { Sentence =
                    sprintf
                        "Un fichier nommé « %s » existe — renommez-le ou supprimez-le dans l'explorateur pour permettre le rangement."
                        RawSubfolderName
                FileNames = [] } ]
        | None -> []

    inverted
    @ crossed
    @ duplicateRaws
    @ duplicateJpgs
    @ caseCollisions
    @ orphanJpgsAtRoot
    @ orphanRawsAtRoot
    @ filedOrphanRaws
    @ orphanJpgsInRaw
    @ rawFolderIssue

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
      InFlight = None
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

/// Statut de synthèse d'une exécution de plan réussie : le plan d'option A ne
/// déplace que les RAW appariés vers le sous-dossier, le bilan les compte.
let private completedStatus (outcome: MoveOutcome) =
    if List.isEmpty outcome.Moved then
        sprintf "%sRien à déplacer." SuccessStatusPrefix
    else
        sprintf
            "%s%s vers « %s »."
            SuccessStatusPrefix
            (pluralized outcome.Moved.Length "RAW déplacé")
            RawSubfolderName

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
        // RC-06 : l'analyse est en vol dès la sélection — l'exception 2
        // permet justement de la superseder par une nouvelle ouverture.
        let requestId = state.ScanRequestId + 1

        { state with
            FolderPath = Some path
            ScanRequestId = requestId
            Status = "Analyse du dossier…"
            InFlight = Some(AnalyzingFolder requestId) },
        scanCmd requestId path

    | FolderScanned(requestId, folderPath, scan) ->
        // Garde de fraîcheur : seul le résultat de la dernière demande
        // s'applique. Un scan retardé d'un dossier précédent ne doit ni
        // remplacer le scan affiché ni changer l'écran (ses stems seraient
        // associés aux chemins du dossier courant).
        // RC-06 : le drapeau d'analyse se lève dès que le résultat revient,
        // frais ou dépassé (clearTrackedAnalysis).
        if isCurrentScanRequest state requestId folderPath then
            let state = clearTrackedAnalysis requestId state
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
            // RC-06 : résultat dépassé — le drapeau qu'il retrace se lève
            // quand même (la demande a été supersedée par une plus récente).
            clearTrackedAnalysis requestId state, Cmd.none

    | FolderScanFailed(requestId, folderPath, error) ->
        // Même garde : l'échec retardé d'un ancien dossier ne ramène pas
        // l'utilisateur à l'accueil. Sans question affichée, l'échec frais
        // ramène à l'accueil : c'est le seul endroit où l'on peut recommencer.
        // RC-06 : le drapeau d'analyse se lève, frais ou dépassé.
        if isCurrentScanRequest state requestId folderPath then
            let state = clearTrackedAnalysis requestId state
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
            clearTrackedAnalysis requestId state, Cmd.none

    | FolderRescanned(requestId, folderPath, scan) ->
        // Rien ne bouge que le scan : un rafraîchissement d'arrière-plan ne
        // referme jamais une question affichée et n'écrase pas le bilan
        // visible (règles VISION, audit itération 4). Le résultat n'appartient
        // qu'à la demande la plus récente du dossier courant.
        // RC-06 : le drapeau de la réanalyse se lève dès que son résultat
        // revient, frais ou dépassé (clearTrackedRescan).
        if isCurrentScanRequest state requestId folderPath then
            { clearTrackedRescan requestId state with Scan = scan }, Cmd.none
        else
            clearTrackedRescan requestId state, Cmd.none

    | FolderRescanFailed(requestId, folderPath, error) ->
        if isCurrentScanRequest state requestId folderPath then
            { clearTrackedRescan requestId state with
                Status = sprintf "%sRafraîchissement du dossier impossible : %s" ErrorStatusPrefix error },
            Cmd.none
        else
            clearTrackedRescan requestId state, Cmd.none

    | RescanRequested ->
        // Geste « Réanalyser » (UC organiser-et-trier-dossier, extensions 2a
        // et 2b) : spec'd sur l'écran d'organisation — le bouton n'existe
        // qu'à cet endroit et le raccourci F5 suit le même destin (la garde
        // dans update couvre tous les producteurs de messages : bouton,
        // clavier, autres fronts). Le dossier courant est re-scanné en
        // arrière-plan — ni l'écran ni l'index ni le bandeau ne bougent,
        // seul le scan est remplacé (FolderRescanned, garde de fraîcheur
        // requestId).
        match state.Screen with
        | Working Organizing ->
            match state.FolderPath with
            | Some folder ->
                // RC-06 : la réanalyse est en vol dès le geste — silencieuse
                // pour l'utilisateur (aucun observable ne bouge), seul le
                // drapeau bloque les gestes jusqu'au retour du scan.
                { state with InFlight = Some(ReanalyzingFolder state.ScanRequestId) },
                rescanCmd state.ScanRequestId folder
            | None ->
                { state with Status = sprintf "%sAucun dossier analysé." ErrorStatusPrefix },
                Cmd.none
        | _ -> state, Cmd.none

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
                // Option A : le rangement ne déplace que les RAW des couples
                // appariés — les anomalies signalées ne comptent pas dans la
                // garde (planOrganization suit la même règle).
                if List.isEmpty state.Scan.TandemsToOrganize then
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
                        // RC-06 : le vol du plan démarre — le bandeau de question
                        // ne s'affiche qu'à son retour.
                        { state with
                            Status = "Préparation du plan…"
                            InFlight = Some PlanningOrganize },
                        Cmd.OfAsync.either
                            (fun (folder, scan) -> async { return planOrganization folder scan })
                            (folder, state.Scan)
                            (fun plan -> OrganizePlanned(folder, plan))
                            (fun ex -> OrganizePlanningFailed(folder, ex.Message))
        | Working _ | Confirming _ -> state, Cmd.none

    | OrganizePlanned(folderPath, plan) ->
        // RC-06 : le vol du plan est terminé — le drapeau se lève, la
        // question peut s'afficher (la main passe par elle).
        if resultIsFromAnotherFolder state folderPath then
            // Le plan appartient à un dossier quitté : aucune question ne
            // s'ouvre pour des fichiers que l'utilisateur ne regarde plus.
            { state with InFlight = None }, Cmd.none
        else
            match state.Screen with
            | Working Organizing ->
                if List.isEmpty plan.Moves then
                    { state with Status = "Rien à remettre en ordre."; InFlight = None }, Cmd.none
                else
                    { state with
                        Screen = Confirming(Organizing, OrganizeFolder plan.Moves.Length)
                        InFlight = None },
                    Cmd.none
            | Working _ | Confirming _ ->
                // L'utilisateur a quitté l'organisation : le plan est obsolète.
                { state with InFlight = None }, Cmd.none

    | OrganizePlanningFailed(_, error) ->
        // RC-06 : le vol du plan est terminé en échec, le drapeau se lève.
        // VISION : l'échec n'est jamais avalé — quel que soit l'écran affiché
        // (question comprise), seul le bandeau bas parle.
        { state with
            Status = sprintf "%sPréparation du plan impossible : %s" ErrorStatusPrefix error
            InFlight = None },
        Cmd.none

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
                // RC-06 : l'exécution est en vol dès la confirmation —
                // aucun geste ne passe jusqu'à son terme.
                { state with
                    Screen = Working Organizing
                    Status = "Organisation en cours…"
                    InFlight = Some OrganizeExecution },
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
            // RC-06 : le vol est terminé, le drapeau se lève.
            { state with Status = movesOutcomeStatus outcome; InFlight = None }, Cmd.none
        else
            // Les fichiers ont bougé : le scan en mémoire est périmé (les RAW
            // déplacés y sont encore notés à la racine — bug du scan périmé,
            // 2026-10-24, attrapé par le scénario organiser-et-trier-dossier). Le dossier est
            // rafraîchi en arrière-plan, sans écran ni statut.
            let refresh =
                match state.FolderPath with
                | Some folder -> rescanCmd state.ScanRequestId folder
                | None -> Cmd.none

            // RC-06 : l'exécution est terminée mais la réanalyse qu'elle
            // déclenche est en vol — la main n'est rendue qu'à son retour
            // (drapeau PostOrganizeRescan, levé par FolderRescanned/Failed).
            if List.isEmpty outcome.Failed then
                // Passage automatique au tri seulement depuis l'écran d'organisation :
                // une question en attente n'est jamais refermée par un résultat
                // d'arrière-plan, la réussite pas plus que l'échec.
                if state.Screen = Working Organizing then
                    let nextState =
                        withReanalysisInFlight
                            { state with
                                Screen = Working Triaging
                                Status = movesOutcomeStatus outcome }
                    nextState, loadCurrentJpgCmd nextState @ refresh
                else
                    withReanalysisInFlight { state with Status = movesOutcomeStatus outcome }, refresh
            else
                withReanalysisInFlight (reportBackgroundFailure state (movesOutcomeStatus outcome)), refresh

    | OrganizeNothingToMove _ ->
        // RC-06 : le vol de l'exécution se termine sans rien déplacer, le
        // drapeau se lève. VISION : le bilan n'est jamais avalé — quel que
        // soit l'écran affiché (question comprise), seul le bandeau bas
        // parle, ni l'écran ni la question ne bougent.
        { state with
            Status = "Rien à déplacer : le dossier a changé entre-temps."
            InFlight = None },
        Cmd.none

    | OrganizeFailed(folderPath, error) ->
        // RC-06 : le vol est terminé en échec, le drapeau se lève.
        if resultIsFromAnotherFolder state folderPath then
            // L'échec d'un dossier quitté ne ramène personne à l'écran
            // d'organisation : il est annoncé dans le bandeau, c'est tout.
            { state with
                Status = sprintf "%sOrganisation impossible : %s" ErrorStatusPrefix error
                InFlight = None }, Cmd.none
        else
            { reportBackgroundFailure state (sprintf "%sOrganisation impossible : %s" ErrorStatusPrefix error) with
                InFlight = None }, Cmd.none

    | OrganizeCancelled ->
        match state.Screen with
        | Confirming(Organizing, OrganizeFolder _) ->
            { state with
                Screen = Working Organizing
                Status = "Organisation annulée." },
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

    // RC-01 (docs/usecases/rc-01-interactions-vues-de-tri.md) : Home → première photo,
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

    // RC-01 (docs/usecases/rc-01-interactions-vues-de-tri.md) : F/F11/double-clic basculent
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

                    // RC-06 : la suppression est en vol dès la confirmation —
                    // aucun geste ne passe jusqu'à son terme.
                    { state with
                        Screen = Working Triaging
                        Status = "Suppression en cours…"
                        InFlight = Some TandemDeletion },
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

                // RC-06 : la suppression est terminée, le drapeau se lève —
                // la main est rendue.
                let nextState =
                    { state with
                        Scan = { state.Scan with TandemsToOrganize = toOrganize; OrganizedTandems = organized }
                        CurrentIndex = bounded visibleCount state.CurrentIndex
                        Status = status
                        InFlight = None }

                nextState, loadCurrentJpgCmd nextState
            else
                { state with Status = status; InFlight = None }, Cmd.none
        else
            // Échec (même partiel) : le tandem reste dans le scan — il est
            // toujours visible au tri — et chaque échec est signalé.
            // RC-06 : le vol est terminé, le drapeau se lève.
            let failedPath, failedReason = List.head outcome.Failed

            { state with
                Status =
                    sprintf
                        "%s%d fichier(s) envoyé(s) à la corbeille, %d en échec (ex. %s : %s)."
                        ErrorStatusPrefix
                        outcome.Trashed.Length
                        outcome.Failed.Length
                        (Path.GetFileName failedPath)
                        failedReason
                InFlight = None },
            Cmd.none

    | DeleteTandemFailed(_, stem, error) ->
        // Le dossier et le stem voyagent avec l'échec : l'utilisateur sait
        // quelle photo n'a pas pu partir à la corbeille (règle VISION :
        // un échec n'est jamais avalé, quel que soit le dossier affiché).
        // RC-06 : le vol est terminé en échec, le drapeau se lève.
        { state with
            Status = sprintf "%sSuppression de %s impossible : %s" ErrorStatusPrefix stem error
            InFlight = None },
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

/// RC-01 (docs/usecases/rc-01-interactions-vues-de-tri.md) : en plein écran, la barre de
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

/// Update enveloppant updateCore : la garde RC-06 (un geste pendant une
/// opération en vol est un no-op, sauf l'ouverture de dossier pendant une
/// analyse ou une réanalyse — exceptions de la règle) et la garde RC-05
/// (pendant une question affichée, tout geste hors bandeau est un no-op)
/// vivent ici, elles seules font autorité, la vue les grise (affordance) ;
/// la logique des messages vit dans updateCore, l'éphémérisation du statut
/// en plein écran est l'unique préoccupation de l'enveloppe — un seul
/// endroit, tous les messages couverts.
let update (msg: Msg) (state: State) : State * Cmd<Msg> =
    if isBlockedGesture state msg then
        state, Cmd.none
    else
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
