module RawTandem.UI.StateTests

open Expecto
open RawTandem.UI.State

let update' (msg: Msg) (state: State) : State =
    update msg state |> fst

let init' () : State =
    init () |> fst

/// Écran d'organisation obtenu en chargeant le dossier de démonstration.
let organizing' () : State =
    init' () |> update' DemoFolderLoaded

/// Écran de tri obtenu en confirmant le déplacement des RAW.
let triaging' () : State =
    organizing' ()
    |> update' MoveRawsRequested
    |> update' MoveRawsConfirmed

[<Tests>]
let stateTests =

    testList
        "State"
        [ testList
              "init"
              [ test "starts on the Home screen with no photos" {
                    let state = init' ()
                    Expect.equal state.Screen (Working Home) "écran initial"
                    Expect.isNone state.FolderPath "aucun dossier"
                    Expect.isEmpty state.Photos "aucune photo"
                    Expect.equal state.RawCountAtRoot 0 "aucun RAW à la racine"
                } ]

          testList
              "folder loading"
              [ test "DemoFolderLoaded switches to the Organizing screen with demo data" {
                    let state = organizing' ()
                    Expect.equal state.Screen (Working Organizing) "écran après chargement"
                    Expect.equal state.FolderPath (Some demoFolderPath) "chemin du dossier"
                    Expect.equal state.CurrentIndex 0 "index initial"
                    Expect.equal state.Photos demoPhotoStems "photos de démonstration"
                    Expect.equal state.RawCountAtRoot demoPhotoStems.Length "RAW à la racine"
                }

                test "OpenFolderClicked only reports the would-be action" {
                    let state = init' () |> update' OpenFolderClicked
                    Expect.equal state.Screen (Working Home) "l'écran ne change pas"
                    Expect.stringContains state.Status "serait" "l'action est un stub"
                } ]

          testList
              "RAW move"
              [ test "MoveRawsRequested opens a Confirming screen with the RAW count" {
                    let state = organizing' () |> update' MoveRawsRequested
                    Expect.equal state.Screen (Confirming(Organizing, MoveRawFiles demoPhotoStems.Length)) "écran de confirmation"
                }

                test "MoveRawsRequested does nothing outside the Organizing screen" {
                    let state = init' () |> update' MoveRawsRequested
                    Expect.equal state.Screen (Working Home) "aucun changement d'écran"
                }

                test "MoveRawsRequested reports when there is no RAW at root" {
                    let state =
                        { organizing' () with RawCountAtRoot = 0 }
                        |> update' MoveRawsRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun RAW" "message explicite"
                }

                test "MoveRawsConfirmed switches to Triaging and mentions the subfolder" {
                    let state = triaging' ()
                    Expect.equal state.Screen (Working Triaging) "écran de tri"
                    Expect.stringContains state.Status "RAW" "le sous-dossier est nommé"
                    Expect.stringContains state.Status "serait" "l'action réelle est un stub"
                }

                test "MoveRawsConfirmed without a matching pending action is reported, not swallowed" {
                    let state = organizing' () |> update' MoveRawsConfirmed
                    Expect.equal state.Screen (Working Organizing) "l'écran ne change pas"
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                }

                test "MoveRawsCancelled returns to the Organizing screen" {
                    let state =
                        organizing' ()
                        |> update' MoveRawsRequested
                        |> update' MoveRawsCancelled

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation"
                    Expect.stringContains state.Status "annulé" "annulation signalée"
                }

                test "StartTriagingClicked skips to Triaging without moving" {
                    let state = organizing' () |> update' StartTriagingClicked
                    Expect.equal state.Screen (Working Triaging) "écran de tri"
                } ]

          testList
              "navigation"
              [ test "NextClicked increments the index on the Triaging screen" {
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
                }

                test "navigation messages do nothing outside the Triaging screen" {
                    let state = organizing' () |> update' NextClicked
                    Expect.equal state.CurrentIndex 0 "index inchangé"
                    Expect.equal state.Screen (Working Organizing) "écran inchangé"
                } ]

          testList
              "tandem deletion"
              [ test "DeleteTandemRequested opens a Confirming screen for the current photo" {
                    let state = triaging' () |> update' DeleteTandemRequested
                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem demoPhotoStems.[0])) "confirmation pour le tandem courant"
                }

                test "DeleteTandemRequested does nothing outside the Triaging screen" {
                    let state = organizing' () |> update' DeleteTandemRequested
                    Expect.equal state.Screen (Working Organizing) "aucune confirmation hors du tri"
                }

                test "DeleteTandemRequested on an empty folder reports it" {
                    let state = { triaging' () with Photos = [] } |> update' DeleteTandemRequested
                    Expect.equal state.Screen (Working Triaging) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucune photo" "message explicite"
                }

                test "DeleteTandemConfirmed removes the photo and mentions both files" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed

                    Expect.equal state.Photos.Length (demoPhotoStems.Length - 1) "une photo de moins"
                    Expect.isFalse (List.contains demoPhotoStems.[0] state.Photos) "le tandem n'est plus listé"
                    Expect.equal state.Screen (Working Triaging) "retour au tri"
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

                    Expect.equal state.CurrentIndex (last - 1) "index ramené au nouveau dernier"
                }

                test "DeleteTandemConfirmed without a matching pending action is reported, not swallowed" {
                    let state = triaging' () |> update' DeleteTandemConfirmed
                    Expect.equal state.Screen (Working Triaging) "l'écran ne change pas"
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                }

                test "DeleteTandemCancelled keeps all photos and returns to Triaging" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemCancelled

                    Expect.equal state.Photos demoPhotoStems "rien n'est supprimé"
                    Expect.equal state.Screen (Working Triaging) "retour au tri"
                    Expect.stringContains state.Status "annulée" "annulation signalée"
                } ] ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args stateTests
