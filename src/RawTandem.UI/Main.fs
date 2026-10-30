module Main

open System
open System.Reflection
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
///
/// Modalité UX : le sélecteur natif n'est pas modal sur tous les backends
/// (portail freedesktop sous Linux notamment) — la fenêtre principale est
/// désactivée le temps du dialogue, comme un dialogue modal le ferait :
/// aucune raison de sélectionner deux dossiers, et aucune action de l'écran
/// ne peut être démarrée pendant le choix. Affordance uniquement : la garde
/// vit dans update — une question peut s'afficher pendant que le dialogue
/// est ouvert (résultat d'arrière-plan), seul update peut refuser le
/// dossier choisi (RC-05).
let private pickFolder (source: obj) (dispatch: Msg -> unit) : Async<unit> =
    async {
        match source with
        | :? Control as control ->
            let topLevel = TopLevel.GetTopLevel control

            if not (isNull (box topLevel)) then
                match topLevel with
                | :? Window as window -> window.IsEnabled <- false
                | _ -> ()

                try
                    let! folders =
                        topLevel.StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(AllowMultiple = false))
                        |> Async.AwaitTask

                    match List.ofSeq folders |> List.tryHead with
                    | Some folder ->
                        let path = folder.TryGetLocalPath()

                        if not (isNull (box path)) then
                            dispatch (FolderSelected path)
                    | None -> ()
                finally
                    match topLevel with
                    | :? Window as window -> window.IsEnabled <- true
                    | _ -> ()
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
        // RC-06 : l'ouverture reste possible pendant une analyse ou une
        // réanalyse (exception 2), jamais pendant les autres vols.
        Button.isEnabled (not (isQuestionPending state) && canOpenFolder state)
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
    // La question vit dans le modèle de vue (`State.confirmationQuestion`,
    // pure et testée) : la vue n'en garde que le mapping vers des pixels.
    let question = confirmationQuestion action

    let confirmLabel, confirmMsg, cancelMsg =
        match action with
        | OrganizeFolder _ -> "Déplacer", OrganizeConfirmed, OrganizeCancelled
        | DeleteTandem _ -> "Supprimer", DeleteTandemConfirmed, DeleteTandemCancelled

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

/// Version de l'application, estampillée à la compilation : le défaut du
/// fsproj (0.0.0-dev en local), écrasé par le workflow de release à chaque
/// tag (-p:Version=x.y.z dans les binaires publiés). Affichée sur l'écran
/// d'accueil — information de support, discrète.
let private appVersion =
    match
        Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    with
    | null -> "?"
    | attr -> attr.InformationalVersion

let private homeView (dispatch: Msg -> unit) =
    DockPanel.create [
        DockPanel.children [
            // Pied discret : la version, information de support (petit
            // corps, couleur secondaire — même langage que le compteur
            // du tri). Dockée en bas du DockPanel racine de la vue.
            TextBlock.create [
                TextBlock.dock Dock.Bottom
                TextBlock.text (sprintf "Version %s" appVersion)
                TextBlock.fontSize 12.0
                TextBlock.foreground Brushes.DimGray
                TextBlock.horizontalAlignment HorizontalAlignment.Center
                TextBlock.margin (Thickness(0.0, 0.0, 0.0, 12.0))
            ]

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
        ]
    ]

/// Page d'organisation (option A, PROPOSITION-organisation.md) : une action
/// primaire (« Ranger les RAW »), un panneau « À corriger vous-même »
/// signal-only (une ligne par problème réellement présent, absent sinon) et
/// le geste « Réanalyser » au plus près du besoin. Boutons réels : Ranger
/// les RAW, Passer au tri, Ouvrir un autre dossier, Réanalyser (UC 2a/2b).
let private organizingView (state: State) (dispatch: Msg -> unit) =
    let scan = state.Scan
    let photosToRange = scan.TandemsToOrganize.Length

    // Bandeau modal (RC-05) : pendant une question, toute action de l'écran
    // est un no-op gardé dans update — elle se voit grisée, jamais de clic
    // mort en silence. RC-06 : idem pendant une opération en vol.
    let questionPending = isQuestionPending state
    let operationInFlight = isBlockingOperationInFlight state

    let organizeDisabled =
        photosToRange = 0
        || scan.RawFolderIssue.IsSome
        || operationInFlight

    /// Ligne du panneau « À corriger vous-même » : phrase visible (quoi +
    /// geste attendu) et, quand des fichiers sont concernés, le détail
    /// replié — libellé concret avec compte (« Voir les 2 fichiers »),
    /// jamais de « Plus de détails » (information scent).
    let problemLine (problem: SignaledProblem) : Types.IView =
        StackPanel.create [
            StackPanel.spacing 4.0
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text ("• " + problem.Sentence)
                    TextBlock.textWrapping TextWrapping.Wrap
                ]
                (if List.isEmpty problem.FileNames then
                     TextBlock.create [ TextBlock.text "" ] :> Types.IView
                 else
                     Expander.create [
                         Expander.header (
                             TextBlock.create [
                                 TextBlock.text (
                                     if List.length problem.FileNames = 1 then
                                         "Voir le fichier"
                                     else
                                         sprintf "Voir les %d fichiers" problem.FileNames.Length
                                 )
                             ]
                         )
                         Expander.content (
                             StackPanel.create [
                                 StackPanel.margin (Thickness(0.0, 4.0, 0.0, 0.0))
                                 StackPanel.children (
                                     problem.FileNames
                                     |> List.map (fun fileName ->
                                         TextBlock.create [
                                             TextBlock.text fileName
                                             TextBlock.foreground Brushes.DimGray
                                         ]
                                         :> Types.IView)
                                 )
                             ]
                         )
                     ] :> Types.IView)
            ]
        ] :> Types.IView

    /// Panneau « À corriger vous-même » : une seule sévérité (orange), le
    /// périmètre dans le titre, le geste de rafraîchissement au pied, au plus
    /// près du besoin (« Corrections faites ? »). Absent s'il n'y a rien à
    /// signaler (pas d'UI morte).
    let anomaliesPanel : Types.IView list =
        let problems = signaledProblems scan

        if List.isEmpty problems then
            []
        else
            let footer : Types.IView =
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.text "Corrections faites dans l'explorateur ?"
                            TextBlock.verticalAlignment VerticalAlignment.Center
                        ]
                        Button.create [
                            Button.content "Réanalyser (F5)"
                            Button.tip "Réanalyser le dossier (F5)"
                            Button.isEnabled (not questionPending && not operationInFlight)
                            Button.onClick (fun _ -> dispatch RescanRequested)
                        ]
                    ]
                ] :> Types.IView

            let header : Types.IView =
                TextBlock.create [
                    TextBlock.text "⚠ À corriger vous-même — l'application ne gère pas ces cas"
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.foreground Brushes.DarkOrange
                ] :> Types.IView

            let lines : Types.IView list = problems |> List.map problemLine

            [ Border.create [
                  Border.borderBrush Brushes.DarkOrange
                  Border.borderThickness (Thickness(1.0))
                  Border.cornerRadius (CornerRadius(6.0))
                  Border.padding (Thickness(14.0))
                  Border.child (
                      StackPanel.create [
                          StackPanel.spacing 10.0
                          StackPanel.children ((header :: lines) @ [ footer ])
                      ]
                  )
              ] :> Types.IView ]

    /// Carte principale : une seule action primaire par écran — le geste
    /// fréquent a la grosse cible (Fitts), le compte annonce ce qui va se
    /// passer avant la confirmation.
    let mainCard : Types.IView =
        Border.create [
            Border.background Brushes.Gainsboro
            Border.cornerRadius (CornerRadius(6.0))
            Border.padding (Thickness(16.0))
            Border.child (
                Grid.create [
                    Grid.columnDefinitions "*,Auto"
                    Grid.children [
                        StackPanel.create [
                            Grid.column 0
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text (sprintf "%s à ranger" (pluralized photosToRange "photo"))
                                    TextBlock.fontSize 18.0
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBlock.create [
                                    TextBlock.text (
                                        if photosToRange = 1 then
                                            sprintf "1 fichier RAW ira dans le dossier « %s »" RawSubfolderName
                                        else
                                            sprintf "%d fichiers RAW iront dans le dossier « %s »" photosToRange RawSubfolderName
                                    )
                                    TextBlock.foreground Brushes.DimGray
                                ]
                            ]
                        ] :> Types.IView
                        Button.create [
                            Grid.column 1
                            Button.content "Ranger les RAW"
                            Button.fontSize 16.0
                            Button.padding (Thickness(16.0, 8.0))
                            Button.isEnabled (not organizeDisabled && not questionPending)
                            Button.onClick (fun _ -> dispatch OrganizeRequested)
                        ] :> Types.IView
                    ]
                ]
            )
        ] :> Types.IView

    /// Geste discret de contournement : trier sans ranger. Taille standard
    /// (un lien trop petit est une cible ratée, Fitts), aligné à droite en
    /// pied de page : même colonne d'actions que « Ranger les RAW », position
    /// « continuer » en bas-droite comme la bascule plein écran du tri.
    let goToTriageButton : Types.IView =
        Button.create [
            Button.content "Passer au tri →"
            Button.horizontalAlignment HorizontalAlignment.Right
            Button.isEnabled (not questionPending && not operationInFlight)
            Button.onClick (fun _ -> dispatch StartTriagingClicked)
        ] :> Types.IView

    // Ascenseur : le panneau « À corriger vous-même » peut être long (une
    // ligne par catégorie + listes de fichiers) — la page défile, le bas de
    // la fenêtre reste atteignable (recette 06-melange).
    ScrollViewer.create [
        ScrollViewer.content (
            StackPanel.create [
                StackPanel.margin (Thickness(20.0))
                StackPanel.spacing 16.0
                StackPanel.children (
            [ TextBlock.create [
                  TextBlock.text "Dossier chargé :"
                  TextBlock.fontWeight FontWeight.Bold
              ] :> Types.IView
              TextBox.create [
                  TextBox.isReadOnly true
                  TextBox.text (defaultArg state.FolderPath "")
              ] :> Types.IView
              StackPanel.create [
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 8.0
                  StackPanel.children [
                      openAnotherFolderButton state dispatch
                      Button.create [
                          Button.content "Réanalyser (F5)"
                          Button.tip "Réanalyser le dossier (F5)"
                          Button.isEnabled (not questionPending && not operationInFlight)
                          Button.onClick (fun _ -> dispatch RescanRequested)
                      ]
                  ]
              ] :> Types.IView
              mainCard ]
            @ anomaliesPanel
            @ [ goToTriageButton ]
                )
            ]
        )
    ]

let private triagingView (state: State) (dispatch: Msg -> unit) =
    let visibleTandems = triableTandems state
    let photoCount = visibleTandems.Length
    // Bandeau modal : pendant une question, toute action de l'écran est un
    // no-op gardé dans update — elle se voit grisée, jamais de clic mort.
    // RC-06 : idem pendant une opération en vol.
    let questionPending = isQuestionPending state
    let operationInFlight = isBlockingOperationInFlight state
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
                        Button.isEnabled (not (isQuestionPending state) && canOpenFolder state)
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
                        Button.isEnabled (photoCount > 0 && not questionPending && not operationInFlight)
                        Button.onClick (fun _ -> dispatch DeleteTandemRequested)
                    ]
                    Button.create [
                        Grid.column 2
                        Button.horizontalAlignment HorizontalAlignment.Right
                        Button.verticalAlignment VerticalAlignment.Center
                        Button.margin (Thickness(0.0, 0.0, 20.0, 12.0))
                        Button.padding (Thickness(10.0))
                        Button.tip "Plein écran (F)"
                        Button.isEnabled (not questionPending && not operationInFlight)
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
                Button.isEnabled (state.CurrentIndex > 0 && not questionPending && not operationInFlight)
                Button.onClick (fun _ -> dispatch PreviousClicked)
            ]
            Button.create [
                Button.dock Dock.Right
                Button.verticalAlignment VerticalAlignment.Center
                Button.margin (Thickness(0.0, 0.0, 20.0, 0.0))
                Button.fontSize 22.0
                Button.content "►"
                Button.tip "Photo suivante (→)"
                Button.isEnabled (state.CurrentIndex < photoCount - 1 && not questionPending && not operationInFlight)
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

/// Mapping touche → message de geste (RC-01, docs/usecases/rc-01-interactions-vues-de-tri.md
/// ; Échap/Entrée : RC-05, docs/usecases/rc-05-bandeau-jaune-modal.md).
/// La vue traduit les touches, la garde d'écran vit dans `update` : tout
/// producteur de messages (clavier, bouton, autre front-end) est couvert.
/// Les touches non mappées (Tab, Espace…) restent aux contrôles.
/// Entrée n'y figure pas : son traitement dépend du focus (bouton du bandeau
/// prioritaire), géré dans la subscription.
let private keyToMsg (key: Key) : Msg option =
    match key with
    | Key.Left -> Some PreviousClicked
    | Key.Right -> Some NextClicked
    | Key.Home -> Some FirstPhotoRequested
    | Key.End -> Some LastPhotoRequested
    | Key.Delete -> Some DeleteTandemRequested
    | Key.F5 -> Some RescanRequested
    | Key.F
    | Key.F11 -> Some FullscreenRequested
    | Key.Escape -> Some EscapePressed
    | _ -> None

/// RC-05 : le bouton actif actuellement focusé, s'il y en a un. Pendant une
/// question, seuls les boutons du bandeau peuvent être à la fois actifs et
/// focusés (tout le reste est grisé) — un bouton grisé focusé ne compte
/// pas, l'acceptation par Entrée doit rester possible.
let private focusedEnabledButton (window: Window) : Button option =
    let fm = window.FocusManager

    if isNull (box fm) then
        None
    else
        match box (fm.GetFocusedElement()) with
        | :? Button as b when b.IsEnabled -> Some b
        | _ -> None

/// Subscription clavier. Le gestionnaire est posé en **tunnel** sur la
/// fenêtre pour voir les touches AVANT la navigation de focus d'Avalonia :
/// en bubble, le KeyboardNavigationHandler du TopLevel captait la première
/// flèche (focus donné au premier contrôle focalisable, événement marqué
/// géré) et le raccourci ne jouait qu'à partir de la deuxième pression
/// (bug confirmé 2026-10-26).
/// Internal (pas private) : le harnais des scénarios (RawTandem.ScenarioTests)
/// attache la VRAIE subscription à une vraie fenêtre headless — les tests de
/// clavier couvrent le branchement réel, pas une copie.
let internal keyboardSubscriptions (window: Window) (_state: State) =
    [
        ([ "keyboard-shortcuts" ],
         fun (dispatch: Msg -> unit) ->
             let handler =
                 EventHandler<KeyEventArgs>(fun _ args ->
                     match args.Key with
                     // RC-05 : Entrée tranche la question affichée. Un bouton
                     // actif a le focus (pendant une question : un bouton du
                     // bandeau, seuls boutons actifs — tout le reste est grisé)
                     // : c'est LUI qu'Entrée active (convention Windows), par
                     // son propre clic — pas de double tranchage. Sans bouton
                     // focusé, le raccourci accepte ; hors question, la garde
                     // machine rend QuestionAccepted inoffensif (no-op).
                     | Key.Enter ->
                         args.Handled <- true

                         match focusedEnabledButton window with
                         | Some button -> button.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
                         | None -> dispatch QuestionAccepted
                     | key ->
                         match keyToMsg key with
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
