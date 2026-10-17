module RawTandem.Core.Tests

open Expecto
open RawTandem.Core.App

let update' (msg: Msg) (state: State) : State =
    update msg state |> fst

let init' () : State =
    init () |> fst

/// État de départ de la phase de tri, obtenu en chargeant le dossier de
/// démonstration puis en confirmant le déplacement des RAW.
let triaging' () : State =
    init' ()
    |> update' DemoFolderLoaded
    |> update' MoveRawsRequested
    |> update' MoveRawsConfirmed

[<Tests>]
let appTests =

    let organizing () =
        init' () |> update' DemoFolderLoaded

    testList
        "App"
        [ testList
              "init"
              [ test "starts on the Home phase with no photos" {
                    let state = init' ()
                    Expect.equal state.Phase Home "phase initiale"
                    Expect.isNone state.FolderPath "aucun dossier"
                    Expect.isEmpty state.Photos "aucune photo"
                    Expect.isNone state.PendingAction "aucune action en attente"
                } ]

          testList
              "folder loading"
              [ test "DemoFolderLoaded switches to Organizing with demo data" {
                    let state = organizing ()
                    Expect.equal state.Phase Organizing "phase après chargement"
                    Expect.equal state.FolderPath (Some demoFolderPath) "chemin du dossier"
                    Expect.equal state.CurrentIndex 0 "index initial"
                    Expect.equal state.Photos demoPhotoStems "photos de démonstration"
                    Expect.equal state.Photos.Length state.RawCountAtRoot "chaque tandem a son RAW à la racine"
                }

                test "OpenFolderClicked only reports the would-be action" {
                    let state = init' () |> update' OpenFolderClicked
                    Expect.equal state.Phase Home "la phase ne change pas"
                    Expect.stringContains state.Status "serait" "l'action est un stub"
                } ]

          testList
              "RAW move"
              [ test "MoveRawsRequested asks for confirmation with the RAW count" {
                    let state = organizing () |> update' MoveRawsRequested
                    Expect.equal state.PendingAction (Some(MoveRawFiles demoPhotoStems.Length)) "action en attente"
                }

                test "MoveRawsRequested does nothing when there is no RAW at root" {
                    let state =
                        { organizing () with RawCountAtRoot = 0 }
                        |> update' MoveRawsRequested

                    Expect.isNone state.PendingAction "aucune action en attente"
                    Expect.stringContains state.Status "Aucun RAW" "message explicite"
                }

                test "MoveRawsConfirmed switches to Triaging and mentions the subfolder" {
                    let state =
                        organizing ()
                        |> update' MoveRawsRequested
                        |> update' MoveRawsConfirmed

                    Expect.equal state.Phase Triaging "phase après confirmation"
                    Expect.isNone state.PendingAction "action consommée"
                    Expect.stringContains state.Status RawSubfolderName "le sous-dossier est nommé"
                    Expect.stringContains state.Status "serait" "l'action réelle est un stub"
                }

                test "MoveRawsCancelled keeps the Organizing phase" {
                    let state =
                        organizing ()
                        |> update' MoveRawsRequested
                        |> update' MoveRawsCancelled

                    Expect.equal state.Phase Organizing "phase inchangée"
                    Expect.isNone state.PendingAction "action annulée"
                }

                test "StartTriagingClicked skips to Triaging without moving" {
                    let state = organizing () |> update' StartTriagingClicked
                    Expect.equal state.Phase Triaging "phase de tri"
                    Expect.isNone state.PendingAction "aucune action en attente"
                } ]

          testList
              "navigation"
              [ test "NextClicked increments the index" {
                    let state = triaging' () |> update' NextClicked
                    Expect.equal state.CurrentIndex 1 "index avancé"
                }

                test "NextClicked clamps at the last photo" {
                    let last = demoPhotoStems.Length - 1
                    let state = { triaging' () with CurrentIndex = last } |> update' NextClicked
                    Expect.equal state.CurrentIndex last "borné au dernier"
                }

                test "PreviousClicked clamps at the first photo" {
                    let state = triaging' () |> update' PreviousClicked
                    Expect.equal state.CurrentIndex 0 "borné au premier"
                } ]

          testList
              "tandem deletion"
              [ test "DeleteTandemRequested asks confirmation for the current photo" {
                    let state = triaging' () |> update' DeleteTandemRequested
                    Expect.equal state.PendingAction (Some(DeleteTandem demoPhotoStems.[0])) "tandem courant en attente"
                }

                test "DeleteTandemConfirmed removes the photo and mentions both files" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed

                    Expect.equal state.Photos.Length (demoPhotoStems.Length - 1) "une photo de moins"
                    Expect.isFalse (List.contains demoPhotoStems.[0] state.Photos) "le tandem supprimé n'est plus listé"
                    Expect.isNone state.PendingAction "action consommée"
                    Expect.stringContains state.Status (demoPhotoStems.[0] + ".JPG") "JPG mentionné"
                    Expect.stringContains state.Status (demoPhotoStems.[0] + ".RW2") "RAW mentionné"
                    Expect.stringContains state.Status "corbeille" "suppression via la corbeille"
                }

                test "DeleteTandemConfirmed adjusts the index when removing the last photo" {
                    let last = demoPhotoStems.Length - 1
                    let state =
                        { triaging' () with CurrentIndex = last }
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                    Expect.equal state.Photos.Length (demoPhotoStems.Length - 1) "une photo de moins"
                    Expect.equal state.CurrentIndex (last - 1) "index ramené au nouveau dernier"
                }

                test "DeleteTandemCancelled keeps all photos" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemCancelled

                    Expect.equal state.Photos demoPhotoStems "rien n'est supprimé"
                    Expect.isNone state.PendingAction "action annulée"
                }

                test "DeleteTandemRequested on an empty folder reports it" {
                    let state = { triaging' () with Photos = [] } |> update' DeleteTandemRequested
                    Expect.isNone state.PendingAction "aucune action en attente"
                    Expect.stringContains state.Status "Aucune photo" "message explicite"
                } ] ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args appTests
