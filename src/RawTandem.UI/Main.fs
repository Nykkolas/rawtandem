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
    let question, confirmMsg, cancelMsg =
        match action with
        | MoveRawFiles count ->
            sprintf "Déplacer %d RAW vers le sous-dossier « %s » ?" count RawSubfolderName,
            MoveRawsConfirmed,
            MoveRawsCancelled
        | DeleteTandem stem ->
            sprintf "Supprimer le tandem %s.JPG + %s.RW2 ?" stem stem,
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
                                Button.content "Confirmer"
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
    let tandemsCount = scan.Tandems.Length

    let orphanLines =
        [ if not (List.isEmpty scan.OrphanJpgs) then
              sprintf "%d JPG sans RAW : %s" scan.OrphanJpgs.Length (String.concat ", " scan.OrphanJpgs)
          if not (List.isEmpty scan.OrphanRaws) then
              sprintf "%d RAW sans JPG : %s" scan.OrphanRaws.Length (String.concat ", " scan.OrphanRaws) ]

    let headerViews : Types.IView list =
        [ TextBlock.create [
              TextBlock.text "Dossier chargé :"
              TextBlock.fontWeight FontWeight.Bold
          ]
          TextBox.create [
              TextBox.isReadOnly true
              TextBox.text (defaultArg state.FolderPath "")
          ]
          TextBlock.create [
              TextBlock.text (sprintf "%d tandems détectés, %d RAW à la racine." tandemsCount tandemsCount)
          ] ]

    let orphanViews : Types.IView list =
        orphanLines
        |> List.map (fun line ->
            TextBlock.create [
                TextBlock.text line
                TextBlock.foreground Brushes.DarkOrange
            ] :> Types.IView)

    let actionViews : Types.IView list =
        [ Button.create [
              Button.content (
                  sprintf "Déplacer les %d RAW dans le sous-dossier « %s »" tandemsCount RawSubfolderName
              )
              Button.isEnabled (tandemsCount > 0)
              Button.onClick (fun _ -> dispatch MoveRawsRequested)
          ]
          Button.create [
              Button.content "Passer directement au tri"
              Button.onClick (fun _ -> dispatch StartTriagingClicked)
          ] ]

    StackPanel.create [
        StackPanel.margin (Thickness(20.0))
        StackPanel.spacing 12.0
        StackPanel.children (headerViews @ orphanViews @ actionViews)
    ]

let private triagingView (state: State) (dispatch: Msg -> unit) =
    let photoCount = state.Scan.Tandems.Length
    let currentStem =
        state.Scan.Tandems
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
