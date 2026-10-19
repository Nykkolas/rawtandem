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

/// Chemin factice : le plan ne trouve jamais de cible existante.
let private fixtureFolderPath = "/dossier-fixture-rawtandem-inexistant"

let private tandem (stem: string) =
    { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

let private fixtureStems = [ "P1"; "P2"; "P3" ]

let private fixtureScan = { emptyScan with TandemsToOrganize = fixtureStems |> List.map tandem }

let private rawMoveFixture stem =
    { Kind = RawToSubfolder
      SourcePath = sprintf "%s/%s.RW2" fixtureFolderPath stem
      TargetPath = sprintf "%s/RAW/%s.RW2" fixtureFolderPath stem
      TargetRenamed = false }

let private planFixture =
    { RawDirectoryPath = sprintf "%s/RAW" fixtureFolderPath
      Moves = fixtureStems |> List.map rawMoveFixture }

/// Écran d'organisation obtenu en sélectionnant un dossier puis en recevant son scan.
let organizing' () : State =
    init' ()
    |> update' (FolderSelected fixtureFolderPath)
    |> update' (FolderScanned fixtureScan)

/// Écran de confirmation de l'organisation (demande + plan calculé).
let confirming' () : State =
    organizing' ()
    |> update' OrganizeRequested
    |> update' (OrganizePlanned planFixture)

/// Écran de tri atteint sans organisation.
let triaging' () : State =
    organizing' () |> update' StartTriagingClicked

let private outcomeAllMoved = { Moved = planFixture.Moves; Failed = [] }

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
                    Expect.equal state.Scan.TandemsToOrganize.Length 3 "tandems du scan"
                    Expect.equal state.CurrentIndex 0 "index initial"
                }

                test "FolderScanned announces an already organized folder" {
                    let scan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned scan)
                    Expect.stringContains state.Status "déjà organisé" "message dédié"
                }

                test "FolderScanned reports a RAW folder anomaly" {
                    let scan = { emptyScan with RawFolderIssue = Some RawFolderIsFile }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned scan)
                    Expect.stringContains state.Status "RAW" "l'anomalie est visible"
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
              "organize"
              [ test "OrganizeRequested prepares the plan asynchronously" {
                    let state = organizing' () |> update' OrganizeRequested
                    Expect.equal state.Screen (Working Organizing) "l'écran attend le plan"
                    Expect.stringContains state.Status "Préparation" "préparation annoncée"
                }

                test "OrganizeRequested with nothing to fix reports immediately" {
                    let scan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned scan)
                        |> update' OrganizeRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Rien" "rien à remettre en ordre"
                }

                test "OrganizeRequested with a RAW-as-file anomaly refuses to plan" {
                    let scan = { fixtureScan with RawFolderIssue = Some RawFolderIsFile }
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned scan)
                        |> update' OrganizeRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "RAW" "l'anomalie bloque la planification"
                }

                test "OrganizeRequested does nothing outside the Organizing screen" {
                    let state = init' () |> update' OrganizeRequested
                    Expect.equal state.Screen (Working Home) "aucun changement d'écran"
                }

                test "OrganizePlanned with moves opens the Confirming screen with counts by kind" {
                    let state = organizing' () |> update' (OrganizePlanned planFixture)
                    Expect.equal state.Screen (Confirming(Organizing, OrganizeFolder(3, 0))) "confirmation avec comptes RAW/JPG"
                }

                test "OrganizePlanned without moves reports and stays" {
                    let emptyPlan = { RawDirectoryPath = ""; Moves = [] }
                    let state = organizing' () |> update' (OrganizePlanned emptyPlan)
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Rien" "message explicite"
                }

                test "OrganizePlanned outside the Organizing screen is ignored" {
                    let expected = triaging' ()
                    let state = expected |> update' (OrganizePlanned planFixture)
                    Expect.equal state expected "rien ne change : le plan est obsolète"
                }

                test "OrganizeRequested without a folder reports instead of crashing" {
                    let state = { organizing' () with FolderPath = None } |> update' OrganizeRequested
                    Expect.equal state.Screen (Working Organizing) "l'écran ne change pas"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                }

                test "OrganizeConfirmed without a folder reports instead of crashing" {
                    let state =
                        { confirming' () with FolderPath = None }
                        |> update' OrganizeConfirmed

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation sans exécution"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                }

                test "ResolveDuplicatesConfirmed without a folder reports instead of crashing" {
                    let confirmingDups =
                        { organizing' () with
                            Scan = { emptyScan with DuplicateRaws = [ "P9.RW2" ] } }
                        |> update' ResolveDuplicatesRequested

                    let state = { confirmingDups with FolderPath = None } |> update' ResolveDuplicatesConfirmed

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation sans exécution"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                }

                test "NormalizeRawFolderConfirmed without a folder reports instead of crashing" {
                    let confirmingRename =
                        { organizing' () with Scan = { emptyScan with RawFolderIssue = Some(RawFolderCaseMismatch "raw") } }
                        |> update' NormalizeRawFolderRequested

                    let state = { confirmingRename with FolderPath = None } |> update' NormalizeRawFolderConfirmed

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation sans exécution"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                }

                test "OrganizePlanningFailed stays on Organizing and reports the error" {
                    let state =
                        organizing' ()
                        |> update' OrganizeRequested
                        |> update' (OrganizePlanningFailed "acces refuse")

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "acces refuse" "erreur visible"
                }

                test "OrganizeConfirmed switches to Organizing while the moves run" {
                    let state = confirming' () |> update' OrganizeConfirmed
                    Expect.equal state.Screen (Working Organizing) "plus d'écran de confirmation pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "exécution annoncée"
                }

                test "OrganizeCompleted with all moves goes to Triaging and confirms success" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeCompleted outcomeAllMoved)
                    Expect.equal state.Screen (Working Triaging) "passage au tri après succès"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status "3" "les 3 RAW déplacés sont comptés"
                }

                test "OrganizeCompleted with failures stays on Organizing and reports them" {
                    let outcome =
                        { Moved = [ rawMoveFixture "P1" ]
                          Failed = [ (rawMoveFixture "P2", "permission refusee") ] }

                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeCompleted outcome)

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "P2.RW2" "l'échec identifie le fichier"
                    Expect.stringContains state.Status "permission refusee" "le message d'erreur est visible"
                }

                test "OrganizeNothingToMove stays on Organizing and explains" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' OrganizeNothingToMove
                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "Rien" "le dossier a changé entre-temps"
                }

                test "OrganizeFailed stays on Organizing and reports the error" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeFailed "disque plein")
                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "disque plein" "erreur visible"
                }

                test "OrganizeConfirmed without a matching pending action is reported, not swallowed" {
                    let state = organizing' () |> update' OrganizeConfirmed
                    Expect.equal state.Screen (Working Organizing) "l'écran ne change pas"
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                }

                test "OrganizeCancelled returns to the Organizing screen" {
                    let state = confirming' () |> update' OrganizeCancelled
                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation"
                    Expect.stringContains state.Status "annul" "annulation signalée"
                } ]

          testList
              "duplicate resolution"
              [ let dupScan = { emptyScan with DuplicateRaws = [ "P9.RW2" ]; DuplicateJpgs = [ "P8.JPG" ] }

                let organizingWithDups () =
                    init' ()
                    |> update' (FolderSelected fixtureFolderPath)
                    |> update' (FolderScanned dupScan)

                test "ResolveDuplicatesRequested opens a Confirming screen with the duplicate count" {
                    let state = organizingWithDups () |> update' ResolveDuplicatesRequested
                    Expect.equal state.Screen (Confirming(Organizing, ResolveDuplicates 2)) "confirmation pour les 2 doublons"
                }

                test "ResolveDuplicatesRequested without duplicates reports it" {
                    let state = organizing' () |> update' ResolveDuplicatesRequested
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun doublon" "message explicite"
                }

                test "ResolveDuplicatesConfirmed runs while back on Organizing" {
                    let state =
                        organizingWithDups ()
                        |> update' ResolveDuplicatesRequested
                        |> update' ResolveDuplicatesConfirmed

                    Expect.equal state.Screen (Working Organizing) "plus d'écran de confirmation pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "exécution annoncée"
                }

                test "ResolveDuplicatesCompleted stays on Organizing and confirms renames" {
                    let move =
                        { Kind = RawToSubfolder
                          SourcePath = sprintf "%s/P9.RW2" fixtureFolderPath
                          TargetPath = sprintf "%s/RAW/P9 (1).RW2" fixtureFolderPath
                          TargetRenamed = true }

                    let state =
                        organizingWithDups ()
                        |> update' ResolveDuplicatesRequested
                        |> update' ResolveDuplicatesConfirmed
                        |> update' (ResolveDuplicatesCompleted { Moved = [ move ]; Failed = [] })

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation après une résolution"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status "renomm" "le renommage est confirmé"
                }

                test "ResolveDuplicatesFailed reports the error" {
                    let state =
                        organizingWithDups ()
                        |> update' ResolveDuplicatesRequested
                        |> update' ResolveDuplicatesConfirmed
                        |> update' (ResolveDuplicatesFailed "lecture seule")

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "lecture seule" "erreur visible"
                }

                test "ResolveDuplicatesCancelled returns to the Organizing screen" {
                    let state =
                        organizingWithDups ()
                        |> update' ResolveDuplicatesRequested
                        |> update' ResolveDuplicatesCancelled

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation"
                    Expect.stringContains state.Status "annul" "annulation signalée"
                }

                test "ResolveDuplicatesRequested counts case twins too" {
                    let twinScan = { emptyScan with CaseCollisions = [ { FileName = "P1.jpg"; InRawSubfolder = false } ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned twinScan)
                        |> update' ResolveDuplicatesRequested

                    Expect.equal state.Screen (Confirming(Organizing, ResolveDuplicates 1)) "le jumeau compte comme anomalie à résoudre"
                }

                test "ResolveDuplicatesConfirmed without a matching pending action is reported" {
                    let state = organizing' () |> update' ResolveDuplicatesConfirmed
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                } ]

          testList
              "raw folder name normalization"
              [ let caseMismatchScan = { emptyScan with RawFolderIssue = Some(RawFolderCaseMismatch "raw") }

                let organizingWithCaseMismatch () =
                    init' ()
                    |> update' (FolderSelected fixtureFolderPath)
                    |> update' (FolderScanned caseMismatchScan)

                test "NormalizeRawFolderRequested opens a Confirming screen with the found name" {
                    let state = organizingWithCaseMismatch () |> update' NormalizeRawFolderRequested
                    Expect.equal state.Screen (Confirming(Organizing, NormalizeRawFolder "raw")) "confirmation du renommage"
                }

                test "NormalizeRawFolderRequested without the anomaly reports it" {
                    let state = organizing' () |> update' NormalizeRawFolderRequested
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Aucun dossier" "message explicite"
                }

                test "NormalizeRawFolderConfirmed runs while back on Organizing" {
                    let state =
                        organizingWithCaseMismatch ()
                        |> update' NormalizeRawFolderRequested
                        |> update' NormalizeRawFolderConfirmed

                    Expect.equal state.Screen (Working Organizing) "plus d'écran de confirmation pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "exécution annoncée"
                }

                test "NormalizeRawFolderCompleted confirms the rename" {
                    let state =
                        organizingWithCaseMismatch ()
                        |> update' NormalizeRawFolderRequested
                        |> update' NormalizeRawFolderConfirmed
                        |> update' NormalizeRawFolderCompleted

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status "RAW" "le nouveau nom est confirmé"
                }

                test "NormalizeRawFolderFailed reports the error" {
                    let state =
                        organizingWithCaseMismatch ()
                        |> update' NormalizeRawFolderRequested
                        |> update' NormalizeRawFolderConfirmed
                        |> update' (NormalizeRawFolderFailed "permission refusee")

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "permission refusee" "erreur visible"
                }

                test "NormalizeRawFolderCancelled returns to the Organizing screen" {
                    let state =
                        organizingWithCaseMismatch ()
                        |> update' NormalizeRawFolderRequested
                        |> update' NormalizeRawFolderCancelled

                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation"
                    Expect.stringContains state.Status "annul" "annulation signalée"
                }

                test "NormalizeRawFolderConfirmed without a matching pending action is reported" {
                    let state = organizing' () |> update' NormalizeRawFolderConfirmed
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                } ]

          testList
              "triage"
              [ test "StartTriagingClicked skips to Triaging without organizing" {
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

                    Expect.equal state.Scan.TandemsToOrganize.Length (fixtureStems.Length - 1) "une photo de moins"
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

                test "DeleteTandemConfirmed on an organized tandem keeps the index on the visible list" {
                    // 1 tandem à organiser + 3 organisés : le courant est le dernier
                    // visible (un organisé). L'index doit reculer au nouveau dernier.
                    let scan =
                        { emptyScan with
                            TandemsToOrganize = [ tandem "P1" ]
                            OrganizedTandems = [ "P2"; "P3"; "P4" ] |> List.map tandem }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned scan)
                        |> update' StartTriagingClicked
                        |> (fun s -> { s with CurrentIndex = 3 })

                    let confirmed = state |> update' DeleteTandemRequested |> update' DeleteTandemConfirmed

                    Expect.equal confirmed.Screen (Working Triaging) "retour au tri"
                    Expect.equal confirmed.CurrentIndex 2 "l'index recule au nouveau dernier, pas plus tôt"
                    Expect.equal confirmed.Scan.OrganizedTandems.Length 2 "le tandem organisé est retiré"
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

                    Expect.equal state.Scan.TandemsToOrganize.Length fixtureStems.Length "rien n'est supprimé"
                    Expect.equal state.Screen (Working Triaging) "retour au tri"
                    Expect.stringContains state.Status "annul" "annulation signalée"
                } ] ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args stateTests
