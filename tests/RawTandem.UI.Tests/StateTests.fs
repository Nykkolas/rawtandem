module RawTandem.UI.StateTests

open Expecto
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Organization
open RawTandem.UI.State

let update' (msg: Msg) (state: State) : State =
    update msg state |> fst

let init' () : State =
    init () |> fst

// ========== Fixtures (données pures, aucun fichier n'est touché) ==========

/// Chemin factice : le plan de déplacement ne trouve jamais de collision
/// (File.Exists renvoie faux sur un dossier inexistant).
let private fixtureFolderPath = "/dossier-fixture-rawtandem-inexistant"

let private tandem (stem: string) =
    { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

let private fixtureStems = [ "P1230001"; "P1230002"; "P1230003" ]

let private fixtureScan =
    { Tandems = fixtureStems |> List.map tandem
      OrphanJpgs = []
      OrphanRaws = [] }

/// Écran d'organisation obtenu en sélectionnant un dossier puis en recevant
/// le résultat de son scan.
let organizing' () : State =
    init' ()
    |> update' (FolderSelected fixtureFolderPath)
    |> update' (FolderScanned fixtureScan)

/// Écran de tri atteint sans déplacement.
let triaging' () : State =
    organizing' () |> update' StartTriagingClicked

/// Résultat de déplacement entièrement réussi pour le plan de la fixture.
let private movedOutcome =
    { Moved =
        fixtureStems
        |> List.map (fun s ->
            { SourcePath = sprintf "%s/%s.RW2" fixtureFolderPath s
              TargetPath = sprintf "%s/RAW/%s.RW2" fixtureFolderPath s })
      Failed = [] }

let private moveFixture (stem: string) =
    { SourcePath = sprintf "%s/%s.RW2" fixtureFolderPath stem
      TargetPath = sprintf "%s/RAW/%s.RW2" fixtureFolderPath stem }

let private planFixture =
    { RawDirectoryPath = sprintf "%s/RAW" fixtureFolderPath
      Moves = fixtureStems |> List.map moveFixture
      Collisions = [] }

let private planWithCollisions =
    { planFixture with
        Moves = [ moveFixture fixtureStems.[0] ]
        Collisions = [ "P1230009.RW2"; "P1230010.RW2" ] }

let private planWithOnlyCollisions =
    { planFixture with
        Moves = []
        Collisions = [ "P1230009.RW2"; "P1230010.RW2" ] }

let private planEmpty =
    { RawDirectoryPath = sprintf "%s/RAW" fixtureFolderPath
      Moves = []
      Collisions = [] }

/// Écran de confirmation du déplacement des RAW (demande + plan calculé).
let confirming' () : State =
    organizing' ()
    |> update' MoveRawsRequested
    |> update' (MoveRawsPlanned planFixture)

[<Tests>]
let stateTests =

    testList
        "State"
        [ testList
              "init"
              [ test "starts on the Home screen with an empty scan" {
                    let state = init' ()
                    Expect.equal state.Screen (Working Home) "écran initial"
                    Expect.isNone state.FolderPath "aucun dossier"
                    Expect.equal state.Scan emptyScan "scan vide"
                } ]

          testList
              "folder loading"
              [ test "FolderSelected keeps the Home screen and records the path while scanning" {
                    let state = init' () |> update' (FolderSelected fixtureFolderPath)
                    Expect.equal state.Screen (Working Home) "l'écran attend le résultat du scan"
                    Expect.equal state.FolderPath (Some fixtureFolderPath) "chemin enregistré"
                }

                test "FolderScanned switches to the Organizing screen with the scan result" {
                    let state = organizing' ()
                    Expect.equal state.Screen (Working Organizing) "écran après analyse"
                    Expect.equal state.Scan.Tandems.Length 3 "tandems du scan"
                    Expect.equal state.CurrentIndex 0 "index initial"
                }

                test "FolderScanned mentions the orphans in the status" {
                    let scan = { fixtureScan with OrphanJpgs = [ "P1230009.JPG" ]; OrphanRaws = [ "P1230010.RW2" ] }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned scan)
                    Expect.stringContains state.Status "P1230009.JPG" "JPG orphelin signalé"
                    Expect.stringContains state.Status "P1230010.RW2" "RAW orphelin signalé"
                }

                test "FolderScanFailed stays on Home and reports the error" {
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanFailed "acces refuse")

                    Expect.equal state.Screen (Working Home) "retour accueil"
                    Expect.stringContains state.Status "acces refuse" "message d'erreur visible"
                } ]

          testList
              "RAW move"
              [ test "MoveRawsRequested with no tandem reports immediately without planning" {
                    let state =
                        { organizing' () with Scan = emptyScan }
                        |> update' MoveRawsRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun RAW" "message explicite"
                }

                test "MoveRawsRequested prepares the confirmation asynchronously" {
                    let state = organizing' () |> update' MoveRawsRequested
                    Expect.equal state.Screen (Working Organizing) "l'écran attend le résultat du plan"
                    Expect.stringContains state.Status "Préparation" "préparation annoncée"
                }

                test "MoveRawsRequested does nothing outside the Organizing screen" {
                    let state = init' () |> update' MoveRawsRequested
                    Expect.equal state.Screen (Working Home) "aucun changement d'écran"
                }

                test "MoveRawsPlanned with moves opens the Confirming screen with the plannable count" {
                    let state = organizing' () |> update' (MoveRawsPlanned planFixture)
                    Expect.equal state.Screen (Confirming(Organizing, MoveRawFiles 3)) "écran de confirmation"
                    Expect.equal state.Status "" "aucune collision à signaler"
                }

                test "MoveRawsPlanned with collisions warns they will be ignored" {
                    let state = organizing' () |> update' (MoveRawsPlanned planWithCollisions)
                    Expect.equal state.Screen (Confirming(Organizing, MoveRawFiles 1)) "seul le RAW plannifiable compte"
                    Expect.stringContains state.Status "2 RAW déjà présents" "les collisions sont signalées"
                    Expect.stringContains state.Status "ignorés" "jamais écrasés"
                }

                test "MoveRawsPlanned with only collisions reports nothing to move" {
                    let state = organizing' () |> update' (MoveRawsPlanned planWithOnlyCollisions)
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun RAW à déplacer" "message explicite"
                    Expect.stringContains state.Status "2 fichiers déjà présents" "la raison est donnée"
                }

                test "MoveRawsPlanned empty without collisions reports nothing to move" {
                    let state = organizing' () |> update' (MoveRawsPlanned planEmpty)
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun RAW à déplacer" "message explicite"
                }

                test "MoveRawsPlanned outside the Organizing screen is ignored" {
                    let expected = triaging' ()
                    let state = expected |> update' (MoveRawsPlanned planFixture)
                    Expect.equal state expected "rien ne change : le plan est obsolète"
                }

                test "MoveRawsPlanningFailed stays on Organizing and reports the error" {
                    let state =
                        organizing' ()
                        |> update' MoveRawsRequested
                        |> update' (MoveRawsPlanningFailed "acces refuse")

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "acces refuse" "erreur visible"
                }

                test "MoveRawsConfirmed switches to Organizing while the move is in flight" {
                    let state = confirming' () |> update' MoveRawsConfirmed
                    Expect.equal state.Screen (Working Organizing) "plus d'écran de confirmation pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "exécution annoncée"
                }

                test "MoveRawsNothingToMove at confirm stays on Organizing and reports collisions" {
                    let state =
                        confirming' ()
                        |> update' MoveRawsConfirmed
                        |> update' (MoveRawsNothingToMove 2)

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "2 fichiers déjà présents" "les collisions sont expliquées"
                }

                test "MoveRawsCompleted with all moves goes to Triaging and confirms success" {
                    let state =
                        confirming' ()
                        |> update' MoveRawsConfirmed
                        |> update' (MoveRawsCompleted movedOutcome)

                    Expect.equal state.Screen (Working Triaging) "passage au tri après succès"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status "3" "les 3 RAW déplacés sont comptés"
                    Expect.stringContains state.Status "RAW" "le sous-dossier est nommé"
                }

                test "MoveRawsCompleted with failures stays on Organizing and reports them" {
                    let outcome =
                        { Moved = [ { SourcePath = "/s/P1230001.RW2"; TargetPath = "/s/RAW/P1230001.RW2" } ]
                          Failed = [ ({ SourcePath = "/s/P1230002.RW2"; TargetPath = "/s/RAW/P1230002.RW2" }, "permission refusee") ] }

                    let state =
                        confirming' ()
                        |> update' MoveRawsConfirmed
                        |> update' (MoveRawsCompleted outcome)

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "P1230002" "l'échec identifie le fichier"
                    Expect.stringContains state.Status "permission refusee" "le message d'erreur est visible"
                }

                test "MoveRawsFailed stays on Organizing and reports the error" {
                    let state =
                        confirming' ()
                        |> update' MoveRawsConfirmed
                        |> update' (MoveRawsFailed "disque plein")

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "disque plein" "erreur visible"
                }

                test "MoveRawsConfirmed without a matching pending action is reported, not swallowed" {
                    let state = organizing' () |> update' MoveRawsConfirmed
                    Expect.equal state.Screen (Working Organizing) "l'écran ne change pas"
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                }

                test "MoveRawsCancelled returns to the Organizing screen" {
                    let state = confirming' () |> update' MoveRawsCancelled
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
                    let last = fixtureStems.Length - 1
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
                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem fixtureStems.[0])) "confirmation pour le tandem courant"
                }

                test "DeleteTandemRequested does nothing outside the Triaging screen" {
                    let state = organizing' () |> update' DeleteTandemRequested
                    Expect.equal state.Screen (Working Organizing) "aucune confirmation hors du tri"
                }

                test "DeleteTandemRequested on an empty folder reports it" {
                    let state = { triaging' () with Scan = emptyScan } |> update' DeleteTandemRequested
                    Expect.equal state.Screen (Working Triaging) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucune photo" "message explicite"
                }

                test "DeleteTandemConfirmed removes the photo and mentions both files" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed

                    Expect.equal state.Scan.Tandems.Length (fixtureStems.Length - 1) "une photo de moins"
                    Expect.isFalse (state.Scan.Tandems |> List.exists (fun t -> t.Stem = fixtureStems.[0])) "le tandem n'est plus listé"
                    Expect.equal state.Screen (Working Triaging) "retour au tri"
                    Expect.stringContains state.Status (fixtureStems.[0] + ".JPG") "JPG mentionné"
                    Expect.stringContains state.Status (fixtureStems.[0] + ".RW2") "RAW mentionné"
                    Expect.stringContains state.Status "corbeille" "suppression via la corbeille"
                }

                test "DeleteTandemConfirmed adjusts the index when removing the last photo" {
                    let last = fixtureStems.Length - 1
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

                    Expect.equal state.Scan.Tandems.Length fixtureStems.Length "rien n'est supprimé"
                    Expect.equal state.Screen (Working Triaging) "retour au tri"
                    Expect.stringContains state.Status "annulée" "annulation signalée"
                } ] ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args stateTests
