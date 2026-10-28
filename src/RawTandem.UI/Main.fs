module Main

open System
open Elmish
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform.Storage
open Avalonia.Platform
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Elmish

open RawTandem.Core.Constants
open RawTandem.Core.FolderScan
open RawTandem.UI.State

// ========== Folder picker ==========

/// Ouvre le sélecteur de dossier natif et dispatch le chemin choisi.
/// Aucun dossier choisi (Esc) : aucun message, rien ne change.
let private pickFolder (source: obj) (dispatch: Msg -> unit) : Async<unit> =
    async {
        match source with
        | :? Control as control ->
            let topLevel = TopLevel.GetTopLevel control

            if not (isNull (box topLevel)) then
                let! folders =
                    topLevel.StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(AllowMultiple = false))
                    |> Async.AwaitTask

                match List.ofSeq folders |> List.tryHead with
                | Some folder ->
                    let path = folder.TryGetLocalPath()

                    if not (isNull (box path)) then
                        dispatch (FolderSelected path)
                | None -> ()
        | _ -> ()
    }

// ========== View helpers ==========

/// La navigation vers un autre dossier est proposée sur chaque écran où un
/// dossier est actif, mais jamais pendant qu'une question est affichée :
/// la question doit être tranchée d'abord (jamais refermée à la place de
/// l'utilisateur, cf. règle de VISION sur les confirmations). Bandeau
/// modal : le grisage est l'affordance de la garde qui vit dans `update`.
let private openAnotherFolderButton (state: State) (dispatch: Msg -> unit) : Types.IView =
    Button.create [
        Button.content "Ouvrir un autre dossier…"
        Button.isEnabled (not (isQuestionPending state))
        Button.onClick (fun args -> pickFolder args.Source dispatch |> Async.StartImmediate)
    ] :> Types.IView

let private statusView (state: State) =
    let statusBrush =
        if state.Status.StartsWith SuccessStatusPrefix then Brushes.Green
        elif state.Status.StartsWith ErrorStatusPrefix then Brushes.Red
        else Brushes.Black

    Border.create [
        Border.dock Dock.Bottom
        Border.verticalAlignment VerticalAlignment.Bottom
        Border.background Brushes.Gainsboro
        Border.padding (Thickness(12.0, 6.0))
        Border.child (
            TextBlock.create [
                TextBlock.text (if state.Status = "" then "Prêt." else state.Status)
                TextBlock.foreground statusBrush
            ]
        )
    ]

let private confirmationView (action: PendingAction) (dispatch: Msg -> unit) =
    let question, confirmLabel, confirmMsg, cancelMsg =
        match action with
        | OrganizeFolder (rawMoves, jpgMoves) ->
            let moveQuestion =
                match rawMoves, jpgMoves with
                | r, 0 ->
                    sprintf
                        "Déplacer %d fichiers .RW2 de la racine vers le sous-dossier « %s » ? (RAW sans JPG inclus)"
                        r
                        RawSubfolderName
                | 0, j ->
                    sprintf "Replacer %d JPG du sous-dossier « %s » à la racine ?" j RawSubfolderName
                | r, j ->
                    sprintf
                        "Déplacer %d fichiers .RW2 vers le sous-dossier « %s » et replacer %d JPG de « %s » à la racine ?"
                        r
                        RawSubfolderName
                        j
                        RawSubfolderName
            moveQuestion, "Déplacer", OrganizeConfirmed, OrganizeCancelled
        | ResolveDuplicates count ->
            sprintf
                "Résoudre les %d doublons et jumeaux de casse ? Chaque fichier sera déplacé à sa place ou renommé (« nom (1).ext »). Aucun fichier n'est écrasé ni supprimé."
                count,
            "Résoudre",
            ResolveDuplicatesConfirmed,
            ResolveDuplicatesCancelled
        | NormalizeRawFolder foundName ->
            sprintf
                "Renommer le dossier « %s » en « %s » ? Ses fichiers seront ensuite pris en compte."
                foundName
                RawSubfolderName,
            "Renommer",
            NormalizeRawFolderConfirmed,
            NormalizeRawFolderCancelled
        | DeleteTandem stem ->
            sprintf
                "Supprimer le tandem %s.JPG + %s.RW2 ? Les deux fichiers iront dans la corbeille (récupérables)."
                stem
                stem,
            "Supprimer",
            DeleteTandemConfirmed,
            DeleteTandemCancelled

    Border.create [
        Border.dock Dock.Top
        Border.verticalAlignment VerticalAlignment.Top
        Border.background Brushes.Khaki
        Border.padding (Thickness(12.0, 10.0))
        Border.child (
            StackPanel.create [
                StackPanel.spacing 8.0
                StackPanel.children [
                    TextBlock.create [
                        TextBlock.text question
                        TextBlock.fontWeight FontWeight.Bold
                    ]
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            Button.create [
                                Button.content confirmLabel
                                Button.onClick (fun _ -> dispatch confirmMsg)
                            ]
                            Button.create [
                                Button.content "Annuler"
                                Button.onClick (fun _ -> dispatch cancelMsg)
                            ]
                        ]
                    ]
                ]
            ]
        )
    ]

// ========== Views per phase ==========

/// Icône « plein écran » classique (RC-01) : quatre coins en L composés de
/// Border — pas de glyphe typographique (le rendu ne dépend pas d'une police)
/// ni de Shape (les attributs de formes de FuncUI ne sont pas consommables en
/// F# avec cette version, vérifié sur l'exemple canonique Ellipse+fill).
let private fullscreenIcon : Types.IView =
    let corner (thickness: Thickness) (hAlign: HorizontalAlignment) (vAlign: VerticalAlignment) : Types.IView =
        Border.create [
            Border.width 5.0
            Border.height 5.0
            Border.borderBrush Brushes.Black
            Border.borderThickness thickness
            Border.horizontalAlignment hAlign
            Border.verticalAlignment vAlign
        ] :> Types.IView

    Grid.create [
        Grid.width 14.0
        Grid.height 14.0
        Grid.children [
            corner (Thickness(1.0, 1.0, 0.0, 0.0)) HorizontalAlignment.Left VerticalAlignment.Top
            corner (Thickness(0.0, 1.0, 1.0, 0.0)) HorizontalAlignment.Right VerticalAlignment.Top
            corner (Thickness(1.0, 0.0, 0.0, 1.0)) HorizontalAlignment.Left VerticalAlignment.Bottom
            corner (Thickness(0.0, 0.0, 1.0, 1.0)) HorizontalAlignment.Right VerticalAlignment.Bottom
        ]
    ] :> Types.IView

/// Photo en plein écran (RC-01) : fond noir, l'image occupe la fenêtre.
/// Le bandeau jaune et la barre de statut s'y superposent dans la
/// composition de la vue, pas ici.
let private fullscreenPhotoView (state: State) (dispatch: Msg -> unit) : Types.IView =
    let viewerText =
        match currentJpgFileName state with
        | Some fileName -> sprintf "Chargement de %s…" fileName
        | None -> "Aucune photo dans ce dossier."

    Border.create [
        Border.background Brushes.Black
        // RC-01 : le double-clic bascule dans les deux sens — même geste que
        // F/F11 (FullscreenRequested), le mapping est dans la vue, la garde
        // dans update (aucun effet pendant une confirmation présentée).
        Border.onDoubleTapped (fun _ -> dispatch FullscreenRequested)
        Border.child (
            match state.CurrentJpg with
            | Some image ->
                Image.create [
                    Image.source image
                    Image.stretch Stretch.Uniform
                ] :> Types.IView
            | None ->
                TextBlock.create [
                    TextBlock.text viewerText
                    TextBlock.fontSize 18.0
                    TextBlock.foreground Brushes.DimGray
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ] :> Types.IView
        )
    ]

let private homeView (dispatch: Msg -> unit) =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Center
        StackPanel.verticalAlignment VerticalAlignment.Center
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [
                TextBlock.text "RawTandem"
                TextBlock.fontSize 32.0
                TextBlock.fontWeight FontWeight.Bold
                TextBlock.horizontalAlignment HorizontalAlignment.Center
            ]
            TextBlock.create [
                TextBlock.text "Triez vos photos JPG+RAW, un tandem à la fois."
                TextBlock.horizontalAlignment HorizontalAlignment.Center
            ]
            Button.create [
                Button.content "Ouvrir un dossier"
                Button.fontSize 16.0
                Button.padding (Thickness(16.0, 8.0))
                Button.horizontalAlignment HorizontalAlignment.Center
                Button.onClick (fun args ->
                    pickFolder args.Source dispatch |> Async.StartImmediate)
            ]
        ]
    ]

let private organizingView (state: State) (dispatch: Msg -> unit) =
    let scan = state.Scan
    let toOrganize = scan.TandemsToOrganize.Length
    let organized = scan.OrganizedTandems.Length
    let duplicates = scan.DuplicateJpgs.Length + scan.DuplicateRaws.Length + scan.CaseCollisions.Length
    let plannedCount = fixableCount scan

    let summaryLine =
        sprintf "%d tandem(s) à organiser, %d déjà organisé(s), %d RAW orphelin(s) à ranger." toOrganize organized scan.OrphanRawsToOrganize.Length

    let namedLine (label: string) (names: string list) =
        sprintf "%d %s : %s" names.Length label (String.concat ", " names)

    let orphanLines : (string * IBrush) list =
        [ if not (List.isEmpty scan.OrphanJpgs) then
              (namedLine "JPG sans RAW" scan.OrphanJpgs, Brushes.Red)
          if not (List.isEmpty scan.OrphanRawsToOrganize) then
              (namedLine "RAW sans JPG, à la racine" scan.OrphanRawsToOrganize, Brushes.Red)
          if not (List.isEmpty scan.FiledOrphanRaws) then
              (namedLine "RAW sans JPG, déjà rangés" scan.FiledOrphanRaws, Brushes.Red)
          if not (List.isEmpty scan.OrphanJpgsInRaw) then
              (namedLine "JPG isolés dans RAW/" scan.OrphanJpgsInRaw, Brushes.Red) ]

    let caseCollisionLine =
        if List.isEmpty scan.CaseCollisions then
            None
        else
            let located =
                scan.CaseCollisions
                |> List.map (fun c ->
                    if c.InRawSubfolder then sprintf "%s (dans RAW/)" c.FileName else c.FileName)
                |> String.concat ", "
            Some(sprintf "%d jumeau(x) de casse : %s" scan.CaseCollisions.Length located)

    let anomalyLines : (string * IBrush) list =
        [ if not (List.isEmpty scan.DuplicateJpgs) then
              (namedLine "doublons JPG (dans RAW/)" scan.DuplicateJpgs, Brushes.DarkOrange)
          if not (List.isEmpty scan.DuplicateRaws) then
              (namedLine "doublons RAW (à la racine)" scan.DuplicateRaws, Brushes.DarkOrange)
          match caseCollisionLine with
          | Some line -> (line, Brushes.DarkOrange)
          | None -> ()
          if not (List.isEmpty scan.CrossedTandems) then
              (sprintf "%d tandem(s) croisé(s) (JPG dans RAW/, RAW à la racine)" scan.CrossedTandems.Length, Brushes.DarkOrange)
          if not (List.isEmpty scan.InvertedTandems) then
              (sprintf "%d tandem(s) inversé(s) (JPG et RAW dans RAW/)" scan.InvertedTandems.Length, Brushes.DarkOrange)
          match scan.RawFolderIssue with
          | Some RawFolderIsFile ->
              (sprintf "« %s » existe mais est un fichier : organisation impossible." RawSubfolderName, Brushes.DarkOrange)
          | Some (RawFolderCaseMismatch foundName) ->
              (sprintf "Dossier « %s » mal cassé : « %s » n'existe pas. Renommez-le avant d'organiser." foundName RawSubfolderName, Brushes.DarkOrange)
          | None -> () ]

    let headerViews : Types.IView list =
        [ TextBlock.create [
              TextBlock.text "Dossier chargé :"
              TextBlock.fontWeight FontWeight.Bold
          ]
          TextBox.create [
              TextBox.isReadOnly true
              TextBox.text (defaultArg state.FolderPath "")
          ]
          openAnotherFolderButton state dispatch
          TextBlock.create [ TextBlock.text summaryLine ] ]

    let namedViews : Types.IView list =
        (orphanLines @ anomalyLines)
        |> List.map (fun (line, brush) ->
            TextBlock.create [
                TextBlock.text line
                TextBlock.foreground brush
            ] :> Types.IView)

    let organizeDisabled =
        plannedCount = 0
        || scan.RawFolderIssue.IsSome

    // Bandeau modal : pendant une question, toute action de l'écran est un
    // no-op gardé dans update — elle se voit grisée, jamais de clic mort.
    let questionPending = isQuestionPending state

    let renameButton : Types.IView list =
        match scan.RawFolderIssue with
        | Some (RawFolderCaseMismatch foundName) ->
            [ Button.create [
                  Button.content (sprintf "Renommer « %s » en « %s »" foundName RawSubfolderName)
                  Button.isEnabled (not questionPending)
                  Button.onClick (fun _ -> dispatch NormalizeRawFolderRequested)
              ] :> Types.IView ]
        | _ -> []

    let actionViews : Types.IView list =
        ([ Button.create [
              Button.content (sprintf "Organiser (%d déplacements)" plannedCount)
              Button.isEnabled (not organizeDisabled && not questionPending)
              Button.onClick (fun _ -> dispatch OrganizeRequested)
           ] :> Types.IView
           Button.create [
               Button.content (sprintf "Résoudre les %d doublons et jumeaux de casse (renommage)" duplicates)
               Button.isEnabled (duplicates > 0 && not questionPending)
               Button.onClick (fun _ -> dispatch ResolveDuplicatesRequested)
           ] :> Types.IView ]
         @ renameButton
         @ [ Button.create [
                Button.content "Passer directement au tri"
                Button.isEnabled (not questionPending)
                Button.onClick (fun _ -> dispatch StartTriagingClicked)
            ] :> Types.IView ])

    StackPanel.create [
        StackPanel.margin (Thickness(20.0))
        StackPanel.spacing 12.0
        StackPanel.children (headerViews @ namedViews @ actionViews)
    ]

let private triagingView (state: State) (dispatch: Msg -> unit) =
    let visibleTandems = triableTandems state
    let photoCount = visibleTandems.Length
    // Bandeau modal : pendant une question, toute action de l'écran est un
    // no-op gardé dans update — elle se voit grisée, jamais de clic mort.
    let questionPending = isQuestionPending state
    // Nom du fichier JPG courant, tel que scanné (casse d'origine) : affiché
    // dans la barre de navigation — UC organiser-et-trier-dossier étape 8,
    // trier-dossier-deja-organise étape 4.
    let currentFileName = currentJpgFileName state

    let viewerText =
        match currentFileName with
        | Some fileName -> sprintf "Chargement de %s…" fileName
        | None -> "Aucune photo dans ce dossier."

    let counter =
        if photoCount = 0 then "-"
        else sprintf "%d / %d" (state.CurrentIndex + 1) photoCount

    DockPanel.create [
        DockPanel.children [
            // Barre du haut : actions de dossier uniquement (UX-GENERIC :
            // groupement fonctionnel — la bascule plein écran, action de vue,
            // vit avec les actions photo, en bande du bas).
            DockPanel.create [
                DockPanel.dock Dock.Top
                DockPanel.children [
                    Button.create [
                        Button.dock Dock.Left
                        Button.margin (Thickness(20.0, 12.0, 0.0, 0.0))
                        Button.isEnabled (not (isQuestionPending state))
                        Button.onClick (fun args -> pickFolder args.Source dispatch |> Async.StartImmediate)
                        Button.content "Ouvrir un autre dossier…"
                    ]
                ]
            ]
            // Légende attachée à la photo (principe de proximité) : le nom du
            // fichier à gauche, la progression à droite — l'œil ne quitte pas
            // la zone de décision.
            DockPanel.create [
                DockPanel.dock Dock.Top
                DockPanel.margin (Thickness(20.0, 12.0, 20.0, 0.0))
                DockPanel.children [
                    TextBlock.create [
                        TextBlock.text (currentFileName |> Option.defaultValue "")
                        TextBlock.dock Dock.Left
                        TextBlock.fontSize 16.0
                    ]
                    TextBlock.create [
                        TextBlock.text counter
                        TextBlock.dock Dock.Right
                        TextBlock.fontSize 16.0
                        TextBlock.foreground Brushes.DimGray
                        TextBlock.horizontalAlignment HorizontalAlignment.Right
                    ]
                ]
            ]
            // Bande du bas : les actions sur la photo affichée (UX-GENERIC :
            // groupement fonctionnel). La suppression, destructive, reste au
            // centre, isolée ; la bascule plein écran vit au coin bas-droit
            // (cible Fitts, même geste que F/F11/double-clic, RC-01), à
            // l'opposé du bouton destructif. Les deux partagent la même
            // bande médiane et la même marge de 12 px.
            Grid.create [
                Grid.dock Dock.Bottom
                Grid.columnDefinitions "*,Auto,*"
                Grid.children [
                    Button.create [
                        Grid.column 1
                        Button.verticalAlignment VerticalAlignment.Center
                        Button.content "Supprimer le tandem"
                        Button.tip "Supprimer le tandem (Suppr)"
                        Button.foreground Brushes.Red
                        Button.fontSize 16.0
                        Button.padding (Thickness(16.0, 8.0))
                        Button.margin (Thickness(0.0, 0.0, 0.0, 12.0))
                        Button.isEnabled (photoCount > 0 && not questionPending)
                        Button.onClick (fun _ -> dispatch DeleteTandemRequested)
                    ]
                    Button.create [
                        Grid.column 2
                        Button.horizontalAlignment HorizontalAlignment.Right
                        Button.verticalAlignment VerticalAlignment.Center
                        Button.margin (Thickness(0.0, 0.0, 20.0, 12.0))
                        Button.padding (Thickness(10.0))
                        Button.tip "Plein écran (F)"
                        Button.isEnabled (not questionPending)
                        Button.onClick (fun _ -> dispatch FullscreenRequested)
                        Button.content fullscreenIcon
                    ]
                ]
            ]
            // Navigation flanquante : les gestes les plus fréquents aux bords
            // de la photo, loin du bouton destructif.
            Button.create [
                Button.dock Dock.Left
                Button.verticalAlignment VerticalAlignment.Center
                Button.margin (Thickness(20.0, 0.0, 0.0, 0.0))
                Button.fontSize 22.0
                Button.content "◄"
                Button.tip "Photo précédente (←)"
                Button.isEnabled (state.CurrentIndex > 0 && not questionPending)
                Button.onClick (fun _ -> dispatch PreviousClicked)
            ]
            Button.create [
                Button.dock Dock.Right
                Button.verticalAlignment VerticalAlignment.Center
                Button.margin (Thickness(0.0, 0.0, 20.0, 0.0))
                Button.fontSize 22.0
                Button.content "►"
                Button.tip "Photo suivante (→)"
                Button.isEnabled (state.CurrentIndex < photoCount - 1 && not questionPending)
                Button.onClick (fun _ -> dispatch NextClicked)
            ]
            Border.create [
                Border.margin (Thickness(20.0))
                // Cadre noir : les couleurs du cliché ressortent sur un
                // pourtour qui n'émet pas de lumière (effet salle obscure).
                Border.background Brushes.Black
                Border.padding (Thickness(14.0))
                // RC-01 : le double-clic sur la photo est le même geste que
                // F ou F11 — le mapping est dans la vue, la garde dans update.
                Border.onDoubleTapped (fun _ -> dispatch FullscreenRequested)
                Border.child (
                    Border.create [
                        // Séparateur fin et sombre : une photo aux bords noirs
                        // ne doit pas fondre dans le cadre.
                        Border.borderBrush (SolidColorBrush(Color.FromRgb(0x3Auy, 0x3Auy, 0x3Auy)))
                        Border.borderThickness (Thickness(1.0))
                        Border.child (
                            match state.CurrentJpg with
                            | Some image ->
                                Image.create [
                                    // Le JPG est déjà décodé à taille bornée
                                    // dans update ; Uniform préserve le ratio.
                                    Image.source image
                                    Image.stretch Stretch.Uniform
                                ] :> Types.IView
                            | None ->
                                TextBlock.create [
                                    TextBlock.text viewerText
                                    TextBlock.fontSize 18.0
                                    TextBlock.foreground Brushes.DimGray
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ] :> Types.IView
                        )
                    ]
                )
            ]
        ]
    ]

// ========== View ==========

let view (state: State) (dispatch: Msg -> unit) =
    // L'écran de confirmation se superpose à l'écran de travail d'origine :
    // on affiche la phase sous-jacente plus la bannière si nécessaire.
    let phase, (confirmation: Types.IView list) =
        match state.Screen with
        | Working phase -> phase, []
        | Confirming(phase, action) -> phase, [ confirmationView action dispatch ]

    let root : Types.IView =
        match state.DisplayMode with
        | Normal ->
            let content : Types.IView =
                match phase with
                | Home -> homeView dispatch
                | Organizing -> organizingView state dispatch
                | Triaging -> triagingView state dispatch

            DockPanel.create [
                DockPanel.children ([ statusView state :> Types.IView ] @ confirmation @ [ content ])
            ]
        | Fullscreen ->
            // RC-01 : en plein écran, seule la photo occupe l'écran (fond
            // noir) ; le bandeau jaune et la barre de statut s'affichent
            // par-dessus quand ils ont quelque chose à dire. Dans un Grid,
            // le dernier enfant est rendu au-dessus des précédents : photo,
            // puis bandeau, puis statut.
            let children =
                [ fullscreenPhotoView state dispatch ]
                @ confirmation
                @ (if state.Status = "" then [] else [ statusView state ])

            Grid.create [ Grid.children children ]

    root

// ========== Clavier (subscription Elmish, règle RC-01) ==========

/// Mapping touche → message de geste (RC-01, docs/usecases/regles-communes.md).
/// La vue traduit les touches, la garde d'écran vit dans `update` : tout
/// producteur de messages (clavier, bouton, autre front-end) est couvert.
/// Les touches non mappées (Tab, Entrée, Espace…) restent aux contrôles.
let private keyToMsg (key: Key) : Msg option =
    match key with
    | Key.Left -> Some PreviousClicked
    | Key.Right -> Some NextClicked
    | Key.Home -> Some FirstPhotoRequested
    | Key.End -> Some LastPhotoRequested
    | Key.Delete -> Some DeleteTandemRequested
    | Key.F
    | Key.F11 -> Some FullscreenRequested
    | Key.Escape -> Some FullscreenExitRequested
    | _ -> None

/// Subscription clavier. Le gestionnaire est posé en **tunnel** sur la
/// fenêtre pour voir les touches AVANT la navigation de focus d'Avalonia :
/// en bubble, le KeyboardNavigationHandler du TopLevel captait la première
/// flèche (focus donné au premier contrôle focalisable, événement marqué
/// géré) et le raccourci ne jouait qu'à partir de la deuxième pression
/// (bug confirmé 2026-10-26).
let private keyboardSubscriptions (window: Window) (_state: State) =
    [
        ([ "keyboard-shortcuts" ],
         fun (dispatch: Msg -> unit) ->
             let handler =
                 EventHandler<KeyEventArgs>(fun _ args ->
                     match keyToMsg args.Key with
                     | Some msg ->
                         args.Handled <- true
                         dispatch msg
                     | None -> ())

             window.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel)

             { new IDisposable with
                 member _.Dispose() = window.RemoveHandler(InputElement.KeyDownEvent, handler) })
    ]

/// RC-01 : le plein écran couvre l'écran, pas seulement la fenêtre — la
/// fenêtre passe en WindowState.FullScreen (sans bordure ni barre de titre).
/// Subscription pilotée par l'état : la clé contient le mode d'affichage,
/// Elmish ré-évalue les subscriptions à chaque changement d'état et ne
/// redémarre que celles dont la clé change — chaque bascule relance la
/// subscription qui remet la fenêtre dans l'état correspondant.
let private windowStateSubscriptions (window: Window) (state: State) =
    let modeKey =
        match state.DisplayMode with
        | Normal -> "normal"
        | Fullscreen -> "fullscreen"

    [
        ([ "window-state"; modeKey ],
         fun (_dispatch: Msg -> unit) ->
             window.WindowState <-
                 (match state.DisplayMode with
                  | Fullscreen -> WindowState.FullScreen
                  | Normal -> WindowState.Normal)

             { new IDisposable with
                 member _.Dispose() = () })
    ]

// ========== Window ==========

type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "RawTandem"
        base.Width <- 900.0
        base.Height <- 640.0
        base.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        // Icône de la fenêtre — c'est elle que le gestionnaire de fenêtres
        // affiche dans la barre des tâches sous Linux ; l'exe Windows porte
        // la sienne via `<ApplicationIcon>`.
        base.Icon <- WindowIcon(new Bitmap(AssetLoader.Open(Uri("avares://RawTandem.UI/Assets/icon.png"))))

        Program.mkProgram init update view
        |> Program.withSubscription (fun state -> keyboardSubscriptions this state @ windowStateSubscriptions this state)
        |> Program.withHost this
        |> Program.runWithAvaloniaSyncDispatch ()
