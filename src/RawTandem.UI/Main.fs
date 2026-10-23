module Main

open Elmish
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform.Storage
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Elmish

open RawTandem.Core.Constants
open RawTandem.Core.FolderScan
open RawTandem.UI.State

// ========== Dispatch (branchement temporaire des raccourcis clavier) ==========
// Maquette : la vue capture le dispatch pour que la fenêtre puisse traduire
// les touches en messages. Les messages de navigation et de suppression ne
// s'appliquent qu'à l'écran de tri : la garde est dans `update`, donc tout
// producteur de messages (clavier, bouton, autre front-end) est couvert.
// À remplacer par une subscription Elmish.

let mutable private currentDispatch : Msg -> unit = ignore

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
/// l'utilisateur, cf. règle de VISION sur les confirmations).
let private canOpenAnotherFolder (state: State) =
    match state.Screen with
    | Confirming _ -> false
    | Working _ -> true

/// Bouton standard « Ouvrir un autre dossier… », identique sur tous les écrans.
let private openAnotherFolderButton (state: State) (dispatch: Msg -> unit) : Types.IView =
    Button.create [
        Button.content "Ouvrir un autre dossier…"
        Button.isEnabled (canOpenAnotherFolder state)
        Button.onClick (fun args -> pickFolder args.Source dispatch |> Async.StartImmediate)
    ] :> Types.IView

let private statusView (state: State) =
    let statusBrush =
        if state.Status.StartsWith SuccessStatusPrefix then Brushes.Green
        elif state.Status.StartsWith ErrorStatusPrefix then Brushes.Red
        else Brushes.Black

    Border.create [
        Border.dock Dock.Bottom
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

    let renameButton : Types.IView list =
        match scan.RawFolderIssue with
        | Some (RawFolderCaseMismatch foundName) ->
            [ Button.create [
                  Button.content (sprintf "Renommer « %s » en « %s »" foundName RawSubfolderName)
                  Button.onClick (fun _ -> dispatch NormalizeRawFolderRequested)
              ] :> Types.IView ]
        | _ -> []

    let actionViews : Types.IView list =
        ([ Button.create [
              Button.content (sprintf "Organiser (%d déplacements)" plannedCount)
              Button.isEnabled (not organizeDisabled)
              Button.onClick (fun _ -> dispatch OrganizeRequested)
           ] :> Types.IView
           Button.create [
               Button.content (sprintf "Résoudre les %d doublons et jumeaux de casse (renommage)" duplicates)
               Button.isEnabled (duplicates > 0)
               Button.onClick (fun _ -> dispatch ResolveDuplicatesRequested)
           ] :> Types.IView ]
         @ renameButton
         @ [ Button.create [
                Button.content "Passer directement au tri"
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
    let currentStem =
        visibleTandems
        |> List.tryItem state.CurrentIndex
        |> Option.map (fun tandem -> tandem.Stem)

    let viewerText =
        match currentStem with
        | Some stem -> sprintf "[ Visionneuse JPG — %s.JPG ]" stem
        | None -> "Aucune photo dans ce dossier."

    let counter =
        if photoCount = 0 then "-"
        else sprintf "%d / %d" (state.CurrentIndex + 1) photoCount

    DockPanel.create [
        DockPanel.children [
            Button.create [
                Button.dock Dock.Top
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.margin (Thickness(20.0, 12.0, 0.0, 0.0))
                Button.isEnabled (canOpenAnotherFolder state)
                Button.onClick (fun args -> pickFolder args.Source dispatch |> Async.StartImmediate)
                Button.content "Ouvrir un autre dossier…"
            ]
            Button.create [
                Button.dock Dock.Bottom
                Button.content "Supprimer le tandem"
                Button.foreground Brushes.Red
                Button.fontSize 16.0
                Button.padding (Thickness(16.0, 8.0))
                Button.horizontalAlignment HorizontalAlignment.Center
                Button.isEnabled (photoCount > 0)
                Button.onClick (fun _ -> dispatch DeleteTandemRequested)
            ]
            StackPanel.create [
                StackPanel.dock Dock.Bottom
                StackPanel.orientation Orientation.Horizontal
                StackPanel.horizontalAlignment HorizontalAlignment.Center
                StackPanel.spacing 12.0
                StackPanel.margin (Thickness(0.0, 12.0, 0.0, 8.0))
                StackPanel.children [
                    Button.create [
                        Button.content "Précédent"
                        Button.isEnabled (state.CurrentIndex > 0)
                        Button.onClick (fun _ -> dispatch PreviousClicked)
                    ]
                    TextBlock.create [
                        TextBlock.text counter
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                    Button.create [
                        Button.content "Suivant"
                        Button.isEnabled (state.CurrentIndex < photoCount - 1)
                        Button.onClick (fun _ -> dispatch NextClicked)
                    ]
                ]
            ]
            Border.create [
                Border.margin (Thickness(20.0))
                Border.borderBrush Brushes.Gray
                Border.borderThickness (Thickness(1.0))
                Border.child (
                    TextBlock.create [
                        TextBlock.text viewerText
                        TextBlock.fontSize 18.0
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                )
            ]
        ]
    ]

// ========== View ==========

let view (state: State) (dispatch: Msg -> unit) =
    currentDispatch <- dispatch

    // L'écran de confirmation se superpose à l'écran de travail d'origine :
    // on affiche la phase sous-jacente plus la bannière si nécessaire.
    let phase, (confirmation: Types.IView list) =
        match state.Screen with
        | Working phase -> phase, []
        | Confirming(phase, action) -> phase, [ confirmationView action dispatch ]

    let content : Types.IView =
        match phase with
        | Home -> homeView dispatch
        | Organizing -> organizingView state dispatch
        | Triaging -> triagingView state dispatch

    DockPanel.create [
        DockPanel.children ([ statusView state :> Types.IView ] @ confirmation @ [ content ])
    ]

// ========== Window ==========

type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "RawTandem"
        base.Width <- 900.0
        base.Height <- 640.0
        base.WindowStartupLocation <- WindowStartupLocation.CenterScreen

        this.KeyDown.Add(fun args ->
            match args.Key with
            | Key.Left ->
                args.Handled <- true
                currentDispatch PreviousClicked
            | Key.Right ->
                args.Handled <- true
                currentDispatch NextClicked
            | Key.Delete ->
                args.Handled <- true
                currentDispatch DeleteTandemRequested
            | _ -> ())

        Program.mkProgram init update view
        |> Program.withHost this
        |> Program.runWithAvaloniaSyncDispatch ()
