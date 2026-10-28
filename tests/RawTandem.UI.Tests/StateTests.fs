module RawTandem.UI.StateTests

open Expecto
open Avalonia
open Avalonia.Media
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Organization
open RawTandem.Core.Deletion
open RawTandem.UI.State
open RawTandem.UI.ScenarioTests

/// Image factice pour les tests : seul le fait qu'elle soit l'image courante
/// (ou une obsolète) compte, jamais les pixels.
type private FakeImage() =
    interface IImage with
        member _.Size = Size(10.0, 10.0)
        member _.Draw(_, _, _) = ()

let update' (msg: Msg) (state: State) : State =
    update msg state |> fst

let init' () : State =
    init () |> fst

// ========== Fixtures (données pures, aucun fichier n'est touché) ==========

/// Chemin factice : le plan ne trouve jamais de cible existante.
let private fixtureFolderPath = "/dossier-fixture-rawtandem-inexistant"

/// Second dossier factice, pour un changement de dossier pendant qu'une
/// opération tourne en arrière-plan (stems identiques : même appareil photo).
let private otherFolderPath = "/autre-dossier-fixture-rawtandem-inexistant"

let private tandem (stem: string) =
    { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

let private fixtureStems = [ "P1"; "P2"; "P3" ]

let private fixtureScan = { emptyScan with TandemsToOrganize = fixtureStems |> List.map tandem }

let private rawMoveFixture stem =
    { SourcePath = sprintf "%s/%s.RW2" fixtureFolderPath stem
      TargetPath = sprintf "%s/RAW/%s.RW2" fixtureFolderPath stem
      TargetRenamed = false }

let private planFixture =
    { RawDirectoryPath = sprintf "%s/RAW" fixtureFolderPath
      Moves = fixtureStems |> List.map rawMoveFixture }

/// Écran d'organisation obtenu en sélectionnant un dossier puis en recevant son scan.
let organizing' () : State =
    init' ()
    |> update' (FolderSelected fixtureFolderPath)
    |> update' (FolderScanned(1, fixtureFolderPath, fixtureScan))

/// Écran de confirmation de l'organisation (demande + plan calculé).
let confirming' () : State =
    organizing' ()
    |> update' OrganizeRequested
    |> update' (OrganizePlanned(fixtureFolderPath, planFixture))

/// Écran de tri atteint sans organisation.
let triaging' () : State =
    organizing' () |> update' StartTriagingClicked

let private outcomeAllMoved = { Moved = planFixture.Moves; Failed = [] }

/// Suppression complète du tandem : les deux fichiers partis à la corbeille.
let private deletionTrashed (stem: string) =
    { Trashed =
        [ sprintf "%s/%s.JPG" fixtureFolderPath stem
          sprintf "%s/%s.RW2" fixtureFolderPath stem ]
      Failed = [] }

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
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))
                    Expect.stringContains state.Status "déjà organisé" "message dédié"
                }

                test "FolderScanned enters the Triaging screen directly when the folder has nothing to fix" {
                    // UC trier-dossier-deja-organise : un dossier déjà organisé,
                    // sans aucune anomalie, n'a rien à proposer à l'écran
                    // d'organisation — le tri est affiché directement.
                    let scan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))

                    Expect.equal state.Screen (Working Triaging) "l'écran d'organisation est sauté : tri direct"
                    Expect.equal state.CurrentIndex 0 "index initial"
                    Expect.stringContains state.Status "déjà organisé" "le bandeau garde le bilan du scan"
                }

                test "FolderScanned still offers the Organizing screen when an anomaly remains" {
                    // Une anomalie corrigeable (JPG orphelin rangé dans RAW/)
                    // garde l'écran d'organisation, seul endroit où la
                    // résoudre — même si tous les couples sont organisés.
                    let scan =
                        { emptyScan with
                            OrganizedTandems = [ tandem "P1" ]
                            OrphanJpgsInRaw = [ "PX.JPG" ] }

                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))

                    Expect.equal state.Screen (Working Organizing) "une anomalie garde l'écran d'organisation"
                }

                test "FolderScanned reports a RAW folder anomaly" {
                    let scan = { emptyScan with RawFolderIssue = Some RawFolderIsFile }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))
                    Expect.stringContains state.Status "RAW" "l'anomalie est visible"
                }

                test "FolderScanFailed stays on Home and reports the error" {
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanFailed(1, fixtureFolderPath, "acces refuse"))

                    Expect.equal state.Screen (Working Home) "retour accueil"
                    Expect.stringContains state.Status "acces refuse" "message d'erreur visible"
                }

                test "FolderSelected from Triaging keeps the screen until the new scan completes" {
                    // Parcours UI : l'utilisateur, en train de trier, clique
                    // « Ouvrir un autre dossier… » et en choisit un. Le bandeau passe
                    // à « Analyse du dossier… » mais l'écran de tri reste affiché le
                    // temps de l'analyse ; c'est le scan complété qui ramène à
                    // l'écran d'organisation du nouveau dossier.
                    let state = triaging' () |> update' (FolderSelected fixtureFolderPath)

                    Expect.equal state.Screen (Working Triaging) "l'écran courant reste pendant l'analyse"
                    Expect.stringContains state.Status "Analyse" "analyse annoncée dans le bandeau"

                    let scanned = state |> update' (FolderScanned(2, fixtureFolderPath, fixtureScan))

                    Expect.equal scanned.Screen (Working Organizing) "le nouveau dossier s'ouvre à l'organisation"
                } ]

          testList
              "folder rescan"
              [ test "FolderRescanned on the current folder replaces the stale scan and nothing else" {
                    // Bug du scan périmé après organisation (2026-10-24) : les
                    // RAW déplacés sont encore notés à la racine. Le rafraîchissement
                    // remplace le scan — et rien d'autre : ni l'écran (une question
                    // affichée n'est jamais refermée), ni le bandeau du bilan.
                    let staleScan = { emptyScan with TandemsToOrganize = [ tandem "P1" ] }
                    let freshScan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let state = { triaging' () with Scan = staleScan }

                    let refreshed = state |> update' (FolderRescanned(1, fixtureFolderPath, freshScan))

                    Expect.equal refreshed.Scan freshScan "le scan périmé est remplacé"
                    Expect.equal refreshed.Screen (Working Triaging) "l'écran ne bouge pas"
                    Expect.equal refreshed.Status (triaging' ()).Status "le bandeau ne bouge pas"
                }

                test "FolderRescanned keeps a pending question open" {
                    let staleScan = { emptyScan with TandemsToOrganize = fixtureStems |> List.map tandem }
                    let freshScan = { emptyScan with OrganizedTandems = fixtureStems |> List.map tandem }
                    let state =
                        { triaging' () with Scan = staleScan }
                        |> update' DeleteTandemRequested

                    let refreshed = state |> update' (FolderRescanned(1, fixtureFolderPath, freshScan))

                    Expect.equal
                        refreshed.Screen
                        (Confirming(Triaging, DeleteTandem fixtureStems.[0]))
                        "la question en attente reste affichée"
                    Expect.equal refreshed.Scan freshScan "le scan est rafraîchi sous la question"
                }

                test "FolderRescanned from another folder is ignored" {
                    let staleScan = { emptyScan with TandemsToOrganize = [ tandem "P1" ] }
                    let state = { triaging' () with Scan = staleScan }
                    let freshScan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }

                    let refreshed = state |> update' (FolderRescanned(1, otherFolderPath, freshScan))

                    Expect.equal refreshed.Scan staleScan "le scan d'un autre dossier n'écrase rien"
                }

                test "FolderRescanFailed reports in the status without changing screen" {
                    let state = triaging' () |> update' (FolderRescanFailed(1, fixtureFolderPath, "acces refuse"))

                    Expect.equal state.Screen (Working Triaging) "l'écran ne bouge pas"
                    Expect.isTrue (state.Status.StartsWith ErrorStatusPrefix) "l'échec est visible dans le bandeau"
                }

                test "RescanRequested refreshes the current folder without changing anything else" {
                    // Geste « Réanalyser » (UC organiser-et-trier-dossier 2a/2b) :
                    // une commande de rescan part, ni l'écran, ni le scan, ni le
                    // bandeau ne bougent — seul le résultat remplacera le scan.
                    let state = organizing' ()
                    let next, cmd = update RescanRequested state

                    Expect.equal next.Screen (Working Organizing) "l'écran ne bouge pas"
                    Expect.equal next.Scan state.Scan "le scan attend le résultat du rafraîchissement"
                    Expect.equal next.Status state.Status "le bandeau ne bouge pas"
                    Expect.isNonEmpty (List.ofSeq cmd) "une commande de rescan est lancée"
                }

                test "RescanRequested is ignored while a question is displayed" {
                    // RC-05 : le geste est grisé dans la vue, la garde dans
                    // update reste seule autorité — une question affichée
                    // n'est jamais refermée ni par un geste ni par son résultat.
                    let expected = confirming' ()
                    let state, cmd = update RescanRequested expected

                    Expect.equal state expected "rien ne bouge pendant une question"
                    Expect.isEmpty (List.ofSeq cmd) "aucune commande lancée pendant une question"
                }

                test "RescanRequested without a folder reports instead of crashing" {
                    let state = { organizing' () with FolderPath = None } |> update' RescanRequested

                    Expect.equal state.Screen (Working Organizing) "l'écran ne bouge pas"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                } ]

          testList
              "scan request races"
              [ // Courses sur les résultats de scan (audit 2026-10-28) : un
                // résultat d'arrière-plan n'appartient qu'à la demande la plus
                // récente. Les identifiants sont déterministes : init à 0,
                // +1 à chaque FolderSelected — la demande 1 précède la 2.

                test "a late scan result of a previous folder is ignored" {
                    // Parcours UI : l'utilisateur ouvre A (analyse lente),
                    // s'impatiente, ouvre B (analyse rapide) : le scan de B
                    // arrive, puis celui de A. Le scan de A ne doit ni
                    // remplacer celui de B ni toucher l'écran : ses stems
                    // seraient associés aux chemins de B, et une suppression
                    // au tri partirait dans le mauvais dossier.
                    let scanA = { emptyScan with TandemsToOrganize = [ tandem "A1"; tandem "A2" ] }
                    let scanB = { emptyScan with TandemsToOrganize = [ tandem "B1" ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath) // demande 1 : dossier A
                        |> update' (FolderSelected otherFolderPath) // demande 2 : dossier B
                        |> update' (FolderScanned(2, otherFolderPath, scanB)) // B arrive en premier
                        |> update' (FolderScanned(1, fixtureFolderPath, scanA)) // A arrive en retard

                    Expect.equal state.FolderPath (Some otherFolderPath) "le dossier courant reste B"
                    Expect.equal state.Scan scanB "le scan retardé de A n'écrase pas celui de B"
                    Expect.equal state.Screen (Working Organizing) "l'écran ouvert pour B reste"
                    Expect.stringContains state.Status "1 tandems à organiser" "le bandeau parle de B, pas de A"
                }

                test "a late scan failure of a previous folder does not go Home" {
                    // L'échec tardif de A ne ramène pas à l'accueil : c'est
                    // l'écran de B, analysé avec succès, qui est affiché.
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath) // demande 1 : dossier A, échouera
                        |> update' (FolderSelected otherFolderPath) // demande 2 : dossier B
                        |> update' (FolderScanned(2, otherFolderPath, fixtureScan))
                        |> update' (FolderScanFailed(1, fixtureFolderPath, "acces refuse")) // échec tardif de A

                    Expect.equal state.Screen (Working Organizing) "l'échec de A ne referme pas le dossier B"
                    Expect.equal state.FolderPath (Some otherFolderPath) "le dossier courant reste B"
                    Expect.stringContains state.Status "tandems à organiser" "le bilan de B reste affiché"
                }

                test "a double scan of the same folder applies only the latest request" {
                    // Le chemin seul ne le voit pas : deux demandes du même
                    // dossier, la première arrivée en retard ne doit pas
                    // écraser le scan frais de la seconde.
                    let firstScan = { emptyScan with TandemsToOrganize = [ tandem "P1" ] }
                    let secondScan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath) // demande 1
                        |> update' (FolderSelected fixtureFolderPath) // demande 2 : même dossier
                        |> update' (FolderScanned(2, fixtureFolderPath, secondScan)) // résultat frais d'abord
                        |> update' (FolderScanned(1, fixtureFolderPath, firstScan)) // résultat périmé ensuite

                    Expect.equal state.Scan secondScan "seule la demande la plus récente s'applique"
                }

                test "a fresh scan result while a question is displayed updates the scan without closing it" {
                    // Le scan du dossier courant arrive pendant qu'une question
                    // est affichée : scan et statut mis à jour, écran préservé
                    // — un résultat d'arrière-plan ne referme jamais une
                    // question (VISION), la réussite pas plus que l'échec.
                    let question = triaging' () |> update' DeleteTandemRequested
                    let freshScan = { emptyScan with OrganizedTandems = fixtureStems |> List.map tandem }

                    let state = question |> update' (FolderScanned(1, fixtureFolderPath, freshScan))

                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem "P1")) "la question reste affichée"
                    Expect.equal state.Scan freshScan "le scan frais se met à jour sous la question"
                    Expect.stringContains state.Status "déjà organisé" "le statut du scan frais remplace l'ancien"
                }

                test "a stale scan result while a question is displayed changes nothing" {
                    // Résultat d'une demande dépassée pendant une question :
                    // ni l'écran ni le scan ni le bandeau ne bougent.
                    let question = triaging' () |> update' DeleteTandemRequested
                    let staleScan = { emptyScan with TandemsToOrganize = [ tandem "PX" ] }

                    let state = question |> update' (FolderScanned(0, fixtureFolderPath, staleScan))

                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem "P1")) "la question reste affichée"
                    Expect.equal state.Scan (triaging' ()).Scan "le scan ne bouge pas"
                    Expect.equal state.Status question.Status "le bandeau ne bouge pas"
                }

                test "a late rescan result of an older request of the same folder is ignored" {
                    // Le rafraîchissement suit la même règle que l'ouverture :
                    // une demande dépassée du même dossier ne doit pas écraser
                    // le scan frais (le chemin seul ne le voit pas).
                    let freshScan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let staleScan = { emptyScan with TandemsToOrganize = [ tandem "P1" ] }

                    let state =
                        triaging' () // demande 1
                        |> update' (FolderSelected fixtureFolderPath) // demande 2 : même dossier ré-ouvert
                        |> update' (FolderRescanned(2, fixtureFolderPath, freshScan)) // rafraîchissement frais
                        |> update' (FolderRescanned(1, fixtureFolderPath, staleScan)) // retard de la demande 1

                    Expect.equal state.Scan freshScan "le résultat périmé de la demande 1 n'écrase rien"
                }

                test "a late rescan failure of an older request does not overwrite the status" {
                    let state =
                        triaging' () // demande 1
                        |> update' (FolderSelected fixtureFolderPath) // demande 2 : analyse en cours
                        |> update' (FolderRescanFailed(1, fixtureFolderPath, "acces refuse")) // échec tardif de la demande 1

                    Expect.equal state.Status "Analyse du dossier…" "l'échec dépassé n'écrase pas le statut d'attente"
                } ]

          testList
              "operation result races"
              [ // Courses sur les résultats d'opérations (audit 2026-10-28,
                // critique 2) : un résultat d'opération embarque le dossier pour
                // lequel il a été calculé. Arrivé après un changement de dossier,
                // il ne touche ni l'écran, ni le scan, ni l'index du dossier
                // courant : seul le bandeau bas reflète l'effet réel (jamais
                // avalé, réussite comme échec). L'organisation de B ci-dessous
                // exige un scan B porteur : demande 2 après FolderSelected.

                let scanOfFolderB = { emptyScan with TandemsToOrganize = [ tandem "B1" ] }

                /// Dossier B ouvert et scanné (demande 2) pendant que
                /// l'opération lancée sur A tourne encore en arrière-plan.
                let folderBDisplayed () : State =
                    init' ()
                    |> update' (FolderSelected fixtureFolderPath) // demande 1 : dossier A
                    |> update' (FolderScanned(1, fixtureFolderPath, fixtureScan))
                    |> update' (FolderSelected otherFolderPath) // demande 2 : dossier B
                    |> update' (FolderScanned(2, otherFolderPath, scanOfFolderB))

                test "an organize completion from a previous folder does not switch screens" {
                    // Parcours UI : l'organisation de A tourne, l'utilisateur
                    // ouvre B et attend son scan. La réussite de A ne doit ni
                    // passer B au tri ni toucher son scan : seul le bandeau
                    // reflète l'effet réel sur A.
                    let state =
                        folderBDisplayed ()
                        |> update' (OrganizeCompleted(fixtureFolderPath, outcomeAllMoved)) // A réussit en retard

                    Expect.equal state.Screen (Working Organizing) "l'écran de B ne bouge pas"
                    Expect.equal state.Scan scanOfFolderB "le scan de B ne bouge pas"
                    Expect.stringContains state.Status SuccessStatusPrefix "l'effet réel de A reste visible dans le bandeau"
                    Expect.stringContains state.Status "3 RAW déplacés" "le bilan de A est annoncé"
                }

                test "an organize failure from a previous folder does not pull the user back to Organizing" {
                    // L'échec de A ne doit pas éjecter l'utilisateur du tri de B
                    // vers l'écran d'organisation : le bandeau seul le signale.
                    let state =
                        (folderBDisplayed () |> update' StartTriagingClicked) // il trie B
                        |> update' (OrganizeFailed(fixtureFolderPath, "acces refuse")) // A échoue en retard

                    Expect.equal state.Screen (Working Triaging) "le tri de B n'est pas interrompu"
                    Expect.stringContains state.Status "acces refuse" "l'échec de A reste visible dans le bandeau"
                }

                test "an organize plan from a previous folder does not open a confirmation" {
                    // Le plan de A arrive après le changement de dossier : aucune
                    // question ne s'ouvre — elle porterait sur des fichiers que
                    // l'utilisateur ne regarde plus.
                    let state =
                        (organizing' () |> update' OrganizeRequested) // préparation du plan de A en arrière-plan
                        |> update' (FolderSelected otherFolderPath) // demande 2 : dossier B
                        |> update' (FolderScanned(2, otherFolderPath, scanOfFolderB))
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture)) // le plan de A arrive

                    Expect.equal state.Screen (Working Organizing) "aucune confirmation pour un plan d'un autre dossier"
                }

                test "a plan preparation failure from a previous folder is announced in the status only" {
                    let state =
                        (organizing' () |> update' OrganizeRequested) // préparation du plan de A en arrière-plan
                        |> update' (FolderSelected otherFolderPath) // demande 2 : dossier B
                        |> update' (FolderScanned(2, otherFolderPath, scanOfFolderB))
                        |> update' (OrganizePlanningFailed(fixtureFolderPath, "acces refuse")) // A échoue en retard

                    Expect.equal state.Screen (Working Organizing) "l'écran de B ne bouge pas"
                    Expect.stringContains state.Status "acces refuse" "l'échec de A reste visible dans le bandeau"
                }

                test "an organize nothing-to-move from a previous folder is announced in the status only" {
                    let state =
                        (folderBDisplayed () |> update' StartTriagingClicked) // il trie B
                        |> update' (OrganizeNothingToMove fixtureFolderPath) // A n'avait plus rien à déplacer

                    Expect.equal state.Screen (Working Triaging) "l'écran de B ne bouge pas"
                    Expect.stringContains state.Status "Rien à déplacer" "le résultat réel de A reste visible dans le bandeau"
                } ]

          testList
              "organize"
              [ test "OrganizeRequested prepares the plan asynchronously" {
                    let state = organizing' () |> update' OrganizeRequested
                    Expect.equal state.Screen (Working Organizing) "l'écran attend le plan"
                    Expect.stringContains state.Status "Préparation" "préparation annoncée"
                }

                test "OrganizeRequested with nothing to fix reports immediately" {
                    // État hors d'atteinte par l'UI depuis le tri direct (le
                    // scan « rien à faire » mène au tri), mais le garde reste :
                    // un rafraîchissement d'arrière-plan peut rendre le scan
                    // vide pendant que l'écran d'organisation est affiché.
                    let scan = { emptyScan with OrganizedTandems = [ tandem "P1" ] }
                    let state =
                        { organizing' () with Scan = scan }
                        |> update' OrganizeRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Rien" "rien à remettre en ordre"
                }

                test "OrganizeRequested with a RAW-as-file anomaly refuses to plan" {
                    let scan = { fixtureScan with RawFolderIssue = Some RawFolderIsFile }
                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))
                        |> update' OrganizeRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "RAW" "l'anomalie bloque la planification"
                }

                test "OrganizeRequested with a wrong-cased raw folder refuses to plan" {
                    // Parcours UI : l'utilisateur ouvre un dossier ./P1.JPG ./P1.RW2
                    // ./raw/P9.RW2 (dossier « RAW » mal cassé). L'écran d'organisation
                    // affiche la ligne orange « Dossier « raw » mal cassé : « RAW »
                    // n'existe pas » et le bouton Organiser grisé. Ce test couvre le
                    // filet de sécurité derrière la vue : si un clic (ou un futur
                    // raccourci clavier) produit OrganizeRequested tant que l'anomalie
                    // est là, la planification est refusée. Organiser maintenant
                    // créerait un second dossier « RAW/ » à côté de « raw », et le
                    // contenu de « raw » deviendrait invisible au scan suivant.
                    // Le seul chemin vers l'organisation passe par le bouton
                    // « Renommer « raw » en « RAW » ».
                    let scan =
                        { emptyScan with
                            TandemsToOrganize = [ tandem "P1" ]
                            RawFolderIssue = Some(RawFolderCaseMismatch "raw") }

                    // L'utilisateur ouvre le dossier : l'écran d'organisation
                    // affiche l'anomalie en orange.
                    let organizingScreen =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))

                    Expect.equal organizingScreen.Screen (Working Organizing) "écran d'organisation"
                    Expect.stringContains organizingScreen.Status "mal cass" "la ligne orange signale l'anomalie"

                    // Malgré le bouton grisé, un message OrganizeRequested arrive :
                    // refus explicite, aucune préparation de plan.
                    let state = organizingScreen |> update' OrganizeRequested

                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "renommez" "le statut demande de renommer avant d'organiser"
                }

                test "OrganizeRequested does nothing outside the Organizing screen" {
                    let state = init' () |> update' OrganizeRequested
                    Expect.equal state.Screen (Working Home) "aucun changement d'écran"
                }

                test "OrganizePlanned with moves opens the Confirming screen with the RAW count" {
                    let state = organizing' () |> update' (OrganizePlanned(fixtureFolderPath, planFixture))
                    Expect.equal state.Screen (Confirming(Organizing, OrganizeFolder 3)) "confirmation avec le compte de RAW appariés"
                }

                test "OrganizePlanned without moves reports and stays" {
                    let emptyPlan = { RawDirectoryPath = ""; Moves = [] }
                    let state = organizing' () |> update' (OrganizePlanned(fixtureFolderPath, emptyPlan))
                    Expect.equal state.Screen (Working Organizing) "pas d'écran de confirmation"
                    Expect.stringContains state.Status "Rien" "message explicite"
                }

                test "OrganizePlanned outside the Organizing screen is ignored" {
                    let expected = triaging' ()
                    let state = expected |> update' (OrganizePlanned(fixtureFolderPath, planFixture))
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


                test "OrganizePlanningFailed stays on Organizing and reports the error" {
                    let state =
                        organizing' ()
                        |> update' OrganizeRequested
                        |> update' (OrganizePlanningFailed(fixtureFolderPath, "acces refuse"))

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "acces refuse" "erreur visible"
                }

                test "OrganizeConfirmed switches to Organizing while the moves run" {
                    let state = confirming' () |> update' OrganizeConfirmed
                    Expect.equal state.Screen (Working Organizing) "plus d'écran de confirmation pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "exécution annoncée"
                }

                test "OrganizeCompleted with all moves goes to Triaging and confirms success" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeCompleted(fixtureFolderPath, outcomeAllMoved))
                    Expect.equal state.Screen (Working Triaging) "passage au tri après succès"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status "3" "les 3 RAW déplacés sont comptés"
                }

                test "OrganizeCompleted with failures stays on Organizing and reports them" {
                    let outcome =
                        { Moved = [ rawMoveFixture "P1" ]
                          Failed = [ (rawMoveFixture "P2", "permission refusee") ] }

                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeCompleted(fixtureFolderPath, outcome))

                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "P2.RW2" "l'échec identifie le fichier"
                    Expect.stringContains state.Status "permission refusee" "le message d'erreur est visible"
                }

                test "OrganizeNothingToMove stays on Organizing and explains" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeNothingToMove fixtureFolderPath)
                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "Rien" "le dossier a changé entre-temps"
                }

                test "OrganizeFailed stays on Organizing and reports the error" {
                    let state = confirming' () |> update' OrganizeConfirmed |> update' (OrganizeFailed(fixtureFolderPath, "disque plein"))
                    Expect.equal state.Screen (Working Organizing) "on reste à l'organisation"
                    Expect.stringContains state.Status "disque plein" "erreur visible"
                }

                test "OrganizeConfirmed without a matching pending action is reported, not swallowed" {
                    let state = organizing' () |> update' OrganizeConfirmed
                    Expect.equal state.Screen (Working Organizing) "l'écran ne change pas"
                    Expect.stringContains state.Status "Confirmation inattendue" "l'incohérence est signalée"
                }

                test "OrganizeFailed keeps the pending confirmation on screen" {
                    // Parcours UI : l'utilisateur confirme « Déplacer 3 fichiers
                    // .RW2 », puis, pendant que l'organisation tourne en arrière-plan,
                    // redemande le rangement : une nouvelle question s'affiche.
                    // L'organisation échoue en arrière-plan (ex. dossier cible en
                    // lecture seule) : la question ne doit pas disparaître, seul le
                    // bandeau bas passe au rouge.
                    let scan = { fixtureScan with DuplicateRaws = [ "P9.RW2" ]; DuplicateJpgs = [ "P8.JPG" ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))
                        |> update' OrganizeRequested
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture)) // « Déplacer » : organisation en arrière-plan
                        |> update' OrganizeConfirmed
                        |> update' OrganizeRequested // il redemande le rangement pendant ce temps
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture)) // nouvelle question affichée
                        |> update' (OrganizeFailed(fixtureFolderPath, "acces refuse")) // échec de l'organisation en arrière-plan

                    Expect.equal state.Screen (Confirming(Organizing, OrganizeFolder 3)) "la question posée reste affichée"
                    Expect.stringContains state.Status "acces refuse" "l'erreur d'arrière-plan est visible dans le bandeau bas"
                }

                test "OrganizeCompleted with failures keeps the pending confirmation on screen" {
                    // Parcours UI : « Déplacer 3 fichiers .RW2 » confirmé, l'organisation
                    // tourne et échouera (cible en lecture seule). Pendant ce temps
                    // l'utilisateur redemande le rangement : une nouvelle question
                    // s'affiche. L'organisation se termine en échec : la question ne
                    // doit pas disparaître, seul le bandeau bas passe au rouge.
                    let scan = { fixtureScan with DuplicateRaws = [ "P9.RW2" ]; DuplicateJpgs = [ "P8.JPG" ] }

                    let outcome = { Moved = []; Failed = [ (rawMoveFixture "P1", "acces refuse") ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))
                        |> update' OrganizeRequested
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture))
                        |> update' OrganizeConfirmed // organisation en arrière-plan
                        |> update' OrganizeRequested // il redemande le rangement
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture)) // nouvelle question affichée
                        |> update' (OrganizeCompleted(fixtureFolderPath, outcome)) // se termine en échec

                    Expect.equal state.Screen (Confirming(Organizing, OrganizeFolder 3)) "la question posée reste affichée"
                    Expect.stringContains state.Status "acces refuse" "l'échec d'arrière-plan est visible dans le bandeau bas"
                }

                test "OrganizeCompleted with success keeps the pending confirmation on screen" {
                    // Parcours UI : « Déplacer 3 fichiers .RW2 » confirmé, l'organisation
                    // tourne. Pendant ce temps l'utilisateur clique « Résoudre les
                    // 2 doublons » : la question s'affiche. L'organisation réussit :
                    // la question ne doit pas disparaître non plus — la réussite n'a
                    // pas plus le droit d'annuler une action en attente. Le passage
                    // automatique au tri ne se fait que depuis l'écran d'organisation.
                    let scan = { fixtureScan with DuplicateRaws = [ "P9.RW2" ]; DuplicateJpgs = [ "P8.JPG" ] }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))
                        |> update' OrganizeRequested
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture))
                        |> update' OrganizeConfirmed // organisation en arrière-plan
                        |> update' OrganizeRequested // il redemande le rangement
                        |> update' (OrganizePlanned(fixtureFolderPath, planFixture)) // nouvelle question affichée
                        |> update' (OrganizeCompleted(fixtureFolderPath, outcomeAllMoved)) // réussite

                    Expect.equal state.Screen (Confirming(Organizing, OrganizeFolder 3)) "la question posée reste affichée"
                    Expect.stringContains state.Status SuccessStatusPrefix "la réussite reste visible dans le bandeau bas"
                }

                test "OrganizeCancelled returns to the Organizing screen" {
                    let state = confirming' () |> update' OrganizeCancelled
                    Expect.equal state.Screen (Working Organizing) "retour à l'organisation"
                    Expect.stringContains state.Status "annul" "annulation signalée"
                }

                test "OrganizePlanningFailed outside the Organizing screen is ignored" {
                    let expected = init' ()
                    let state = expected |> update' (OrganizePlanningFailed(fixtureFolderPath, "acces refuse"))
                    Expect.equal state expected "rien ne change hors de l'écran d'organisation"
                }

                test "OrganizeNothingToMove outside the Organizing screen is ignored" {
                    let expected = init' ()
                    let state = expected |> update' (OrganizeNothingToMove fixtureFolderPath)
                    Expect.equal state expected "rien ne change hors de l'écran d'organisation"
                }

                test "OrganizeCancelled without a matching pending action is ignored" {
                    let expected = init' ()
                    let state = expected |> update' OrganizeCancelled
                    Expect.equal state expected "rien à annuler, aucun changement d'écran"
                }

                test "OrganizeCompleted counts every moved RAW file in the summary" {
                    // Le plan d'option A ne déplace que des RAW appariés :
                    // le bilan les compte tous, pluriel régulier compris.
                    let state =
                        organizing' () |> update' (OrganizeCompleted(fixtureFolderPath, outcomeAllMoved))

                    Expect.equal state.Screen (Working Triaging) "passage au tri après succès"
                    Expect.stringContains state.Status "3 RAW déplacés" "tous les RAW déplacés sont comptés"
                } ]

          testList
              "triage"
              [ test "StartTriagingClicked skips to Triaging without organizing" {
                    let state = organizing' () |> update' StartTriagingClicked
                    Expect.equal state.Screen (Working Triaging) "écran de tri"
                }

                test "StartTriagingClicked outside the Organizing screen is ignored" {
                    let expected = init' ()
                    let state = expected |> update' StartTriagingClicked
                    Expect.equal state expected "rien ne change depuis l'accueil"
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

                test "PreviousClicked does nothing outside the Triaging screen" {
                    let state = organizing' () |> update' PreviousClicked
                    Expect.equal state.CurrentIndex 0 "index inchangé"
                    Expect.equal state.Screen (Working Organizing) "écran inchangé"
                }

                test "navigation messages do nothing outside the Triaging screen" {
                    let state = organizing' () |> update' NextClicked
                    Expect.equal state.CurrentIndex 0 "index inchangé"
                    Expect.equal state.Screen (Working Organizing) "écran inchangé"
                }

                test "FirstPhotoRequested jumps to the first photo" {
                    let last = fixtureStems.Length - 1
                    let state = { triaging' () with CurrentIndex = last } |> update' FirstPhotoRequested
                    Expect.equal state.CurrentIndex 0 "index ramené au premier"
                }

                test "LastPhotoRequested jumps to the last photo" {
                    let last = fixtureStems.Length - 1
                    let state = triaging' () |> update' LastPhotoRequested
                    Expect.equal state.CurrentIndex last "index porté au dernier"
                }

                test "FirstPhotoRequested and LastPhotoRequested do nothing outside the Triaging screen" {
                    let expected = organizing' ()
                    Expect.equal (expected |> update' FirstPhotoRequested) expected "rien ne change hors du tri"
                    Expect.equal (expected |> update' LastPhotoRequested) expected "rien ne change hors du tri"
                } ]

          testList
              "fullscreen (RC-01)"
              [ test "FullscreenRequested enters fullscreen on the Triaging screen" {
                    let state = triaging' () |> update' FullscreenRequested
                    Expect.equal state.DisplayMode Fullscreen "mode plein écran"
                }

                test "FullscreenRequested toggles back to the normal display" {
                    let state = { triaging' () with DisplayMode = Fullscreen } |> update' FullscreenRequested
                    Expect.equal state.DisplayMode Normal "retour à l'affichage normal"
                }

                test "FullscreenExitRequested leaves fullscreen" {
                    let state = { triaging' () with DisplayMode = Fullscreen } |> update' FullscreenExitRequested
                    Expect.equal state.DisplayMode Normal "affichage normal"
                }

                test "FullscreenExitRequested does nothing in the normal display" {
                    let expected = triaging' ()
                    Expect.equal (expected |> update' FullscreenExitRequested) expected "déjà en affichage normal"
                }

                test "fullscreen messages do nothing outside the Triaging screen" {
                    let expected = organizing' ()
                    Expect.equal (expected |> update' FullscreenRequested) expected "pas de plein écran hors du tri"
                    Expect.equal (expected |> update' FullscreenExitRequested) expected "rien à quitter hors du tri"
                }

                test "Echap does not leave fullscreen while a confirmation is pending" {
                    // RC-01 : une confirmation présentée prime sur la sortie du
                    // plein écran — F comme Echap n'ont aucun effet tant que la
                    // question n'est pas tranchée.
                    let state =
                        { triaging' () with DisplayMode = Fullscreen }
                        |> update' DeleteTandemRequested

                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem "P1")) "la confirmation est présentée"
                    let after = state |> update' FullscreenExitRequested
                    Expect.equal after.DisplayMode Fullscreen "on reste en plein écran"
                    Expect.equal after.Screen state.Screen "la confirmation reste présentée"
                    Expect.equal (state |> update' FullscreenRequested) state "F non plus ne bascule pendant la confirmation"
                }

                test "StatusHidden clears the status in fullscreen" {
                    // RC-01 : en plein écran, un statut s'affiche brièvement
                    // puis disparaît — la barre reste cachée le reste du temps.
                    let state = { { triaging' () with DisplayMode = Fullscreen } with Status = "✓ message" }
                    Expect.equal (state |> update' StatusHidden).Status "" "le statut est effacé en plein écran"
                }

                test "StatusHidden does nothing in the normal display" {
                    // En vue normale, le statut est persistant (règle RC-02) :
                    // le minuteur est annulé à la sortie du plein écran, et un
                    // StatusHidden tardif ne doit rien effacer.
                    let state = { triaging' () with Status = "✓ message" }
                    Expect.equal (state |> update' StatusHidden).Status "✓ message" "le statut persiste en vue normale"
                }

                test "a status appearing in fullscreen schedules the auto-hide command" {
                    // Vue de tri sans photo : DeleteTandemRequested pose un
                    // statut explicite ; la machine doit planifier l'effacement.
                    let empty = { triaging' () with Scan = emptyScan; CurrentIndex = 0; DisplayMode = Fullscreen }
                    let _, cmd = update DeleteTandemRequested empty
                    Expect.isNonEmpty (List.ofSeq cmd) "la commande d'effacement temporisé est planifiée"
                }

                test "a status appearing in the normal display schedules nothing" {
                    let empty = { triaging' () with Scan = emptyScan; CurrentIndex = 0 }
                    let _, cmd = update DeleteTandemRequested empty
                    Expect.isEmpty (List.ofSeq cmd) "en vue normale le statut persiste, rien à planifier"
                }

                test "entering fullscreen with a status also schedules the auto-hide" {
                    // Un statut déjà affiché avant la bascule devient éphémère
                    // dès l'entrée en plein écran (« cachée le reste du temps »).
                    let state = { { triaging' () with Scan = emptyScan; CurrentIndex = 0 } with Status = "✓ message" }
                    let _, cmd = update FullscreenRequested state
                    Expect.isNonEmpty (List.ofSeq cmd) "le statut déjà présent devient éphémère en entrant en plein écran"
                } ]

          testList
              "view model"
              [ // Fonctions pures State -> sémantique de présentation (TODO
                // « modèle de vue ») : la vue ne garde que le mapping
                // sémantique -> pixels, le testable vit ici.
                test "isQuestionPending is true only while a question is displayed" {
                    // Bandeau modal : les gestes de l'écran sous-jacent sont
                    // sans effet tant que la question n'est pas tranchée —
                    // la vue s'en sert pour griser les actions.
                    let working = organizing' ()
                    let question = confirming' ()
                    let answered = question |> update' OrganizeCancelled

                    Expect.isFalse (isQuestionPending working) "pas de question : actions libres"
                    Expect.isTrue (isQuestionPending question) "question affichée : actions grisées"
                    Expect.isFalse (isQuestionPending answered) "question tranchée : les actions reviennent"
                }

                test "confirmationQuestion announces exactly the paired RAW moves (option A)" {
                    // Le bandeau de confirmation ne ment jamais : le plan ne
                    // déplace que les RAW des couples appariés (option A),
                    // la question ne promet donc ni les orphelins ni d'autres
                    // fichiers (audit 2026-10-28 — le texte annonçait à tort
                    // « RAW sans JPG inclus »).
                    let question = confirmationQuestion (OrganizeFolder 3)

                    Expect.equal
                        question
                        "Déplacer 3 RAW appariés de la racine vers le sous-dossier « RAW » ?"
                        "exactement le périmètre du plan, pas plus"

                    Expect.isFalse (question.Contains "sans JPG") "aucune promesse sur les RAW orphelins"
                }

                test "confirmationQuestion singularizes one paired RAW move" {
                    Expect.equal
                        (confirmationQuestion (OrganizeFolder 1))
                        "Déplacer 1 RAW apparié de la racine vers le sous-dossier « RAW » ?"
                        "pluriel français régulier"
                }

                test "confirmationQuestion names both files of the tandem to delete" {
                    let question = confirmationQuestion (DeleteTandem "P1")

                    Expect.equal
                        question
                        "Supprimer le tandem P1.JPG + P1.RW2 ? Les deux fichiers iront dans la corbeille (récupérables)."
                        "la suppression annonce les deux fichiers du couple"
                }

                test "currentJpgFileName exposes the scanned name of the current tandem" {
                    let state = triaging' () |> update' NextClicked
                    Expect.equal (currentJpgFileName state) (Some "P2.JPG") "nom du fichier JPG courant, index 1"
                }

                test "currentJpgFileName respects the original casing from the scan" {
                    // Le scan respecte la casse des noms sur disque : le nom
                    // affiché est celui qui existe, pas une reconstruction.
                    // Dossier déjà organisé : le tri est affiché directement.
                    let scan = { emptyScan with OrganizedTandems = [ { Stem = "P2"; JpgFileName = "p2.JPG"; RawFileName = "P2.RW2" } ] }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))

                    Expect.equal state.Screen (Working Triaging) "tri direct : le dossier n'a rien à organiser"
                    Expect.equal (currentJpgFileName state) (Some "p2.JPG") "la casse du scan est préservée"
                }

                test "currentJpgFileName is None when no tandem is triable" {
                    let scan = { emptyScan with OrphanJpgs = [ "PX.JPG" ] }
                    let state = init' () |> update' (FolderSelected fixtureFolderPath) |> update' (FolderScanned(1, fixtureFolderPath, scan))

                    Expect.equal state.Screen (Working Triaging) "tri direct : rien à remettre en ordre"
                    Expect.isNone (currentJpgFileName state) "aucune photo courante"
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

                test "DeleteTandemConfirmed switches back to Triaging while the deletion runs" {
                    // Parcours UI : l'utilisateur confirme « Supprimer le tandem
                    // P1.JPG + P1.RW2 ? ». La suppression (corbeille OS) tourne en
                    // arrière-plan : retour immédiat au tri, plus d'écran de
                    // confirmation — aucune double confirmation possible.
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed

                    Expect.equal state.Screen (Working Triaging) "retour au tri pendant l'exécution"
                    Expect.stringContains state.Status "en cours" "suppression annoncée"
                }

                test "DeleteTandemConfirmed without a folder reports instead of crashing" {
                    let state =
                        { (triaging' () |> update' DeleteTandemRequested) with
                            FolderPath = None }
                        |> update' DeleteTandemConfirmed

                    Expect.equal state.Screen (Working Triaging) "retour au tri sans exécution"
                    Expect.stringContains state.Status "Aucun dossier" "l'état incohérent est signalé, pas de crash"
                }

                test "DeleteTandemConfirmed on a tandem that left the scan reports it" {
                    // Le dossier a changé entre la demande et la confirmation :
                    // le tandem n'est plus dans le scan, rien n'est exécuté.
                    let stale =
                        { (triaging' () |> update' DeleteTandemRequested) with
                            Scan = emptyScan }

                    let state = stale |> update' DeleteTandemConfirmed

                    Expect.equal state.Screen (Working Triaging) "retour au tri sans exécution"
                    Expect.stringContains state.Status "n'est plus dans le dossier" "le tandem disparu est signalé"
                }

                test "DeleteTandemCompleted removes the photo and mentions both files" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, fixtureStems.[0], deletionTrashed fixtureStems.[0]))

                    Expect.equal state.Scan.TandemsToOrganize.Length (fixtureStems.Length - 1) "une photo de moins"
                    Expect.equal state.Screen (Working Triaging) "l'écran de tri reste"
                    Expect.stringContains state.Status SuccessStatusPrefix "préfixe de succès"
                    Expect.stringContains state.Status (fixtureStems.[0] + ".JPG") "JPG mentionné"
                    Expect.stringContains state.Status (fixtureStems.[0] + ".RW2") "RAW mentionné"
                    Expect.stringContains state.Status "corbeille" "suppression via la corbeille"
                }

                test "DeleteTandemCompleted adjusts the index when removing the last photo" {
                    let last = fixtureStems.Length - 1
                    let state =
                        { triaging' () with CurrentIndex = last }
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, fixtureStems.[last], deletionTrashed fixtureStems.[last]))

                    Expect.equal state.CurrentIndex (last - 1) "index ramené au nouveau dernier"
                }

                test "DeleteTandemCompleted on an organized tandem keeps the index on the visible list" {
                    // 1 tandem à organiser + 3 organisés : le courant est le dernier
                    // visible (un organisé). L'index doit reculer au nouveau dernier.
                    let scan =
                        { emptyScan with
                            TandemsToOrganize = [ tandem "P1" ]
                            OrganizedTandems = [ "P2"; "P3"; "P4" ] |> List.map tandem }

                    let state =
                        init' ()
                        |> update' (FolderSelected fixtureFolderPath)
                        |> update' (FolderScanned(1, fixtureFolderPath, scan))
                        |> update' StartTriagingClicked
                        |> (fun s -> { s with CurrentIndex = 3 })
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, "P4", deletionTrashed "P4"))

                    Expect.equal state.Screen (Working Triaging) "retour au tri"
                    Expect.equal state.CurrentIndex 2 "l'index recule au nouveau dernier, pas plus tôt"
                    Expect.equal state.Scan.OrganizedTandems.Length 2 "le tandem organisé est retiré"
                }

                test "DeleteTandemCompleted with a partial failure keeps the tandem and reports it" {
                    // Le JPG part à la corbeille mais le RAW échoue (ex. verrouillé) :
                    // le tandem reste dans le scan — il est toujours visible au tri —
                    // et l'échec identifie le fichier.
                    let outcome =
                        { Trashed = [ sprintf "%s/%s.JPG" fixtureFolderPath "P1" ]
                          Failed = [ (sprintf "%s/%s.RW2" fixtureFolderPath "P1", "fichier verrouille") ] }

                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, "P1", outcome))

                    Expect.equal state.Scan.TandemsToOrganize.Length fixtureStems.Length "le tandem reste dans le scan"
                    Expect.equal state.Screen (Working Triaging) "l'écran de tri reste"
                    Expect.stringContains state.Status ErrorStatusPrefix "préfixe d'erreur"
                    Expect.stringContains state.Status "P1.RW2" "l'échec identifie le fichier"
                    Expect.stringContains state.Status "fichier verrouille" "la raison est visible"
                }

                test "DeleteTandemFailed reports the error and keeps the triage screen" {
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed
                        |> update' (DeleteTandemFailed(fixtureFolderPath, fixtureStems.[0], "corbeille indisponible"))

                    Expect.equal state.Scan.TandemsToOrganize.Length fixtureStems.Length "rien n'est supprimé"
                    Expect.equal state.Screen (Working Triaging) "l'écran de tri reste"
                    Expect.stringContains state.Status "corbeille indisponible" "erreur visible"
                }

                test "DeleteTandemCompleted keeps a pending confirmation on screen" {
                    // Parcours UI : la suppression de P1 tourne en arrière-plan.
                    // Pendant ce temps l'utilisateur navigue vers P2 et demande sa
                    // suppression : la question « Supprimer P2 ? » s'affiche. La
                    // suppression de P1 réussit : la question ne doit pas disparaître
                    // — la réussite n'a pas plus le droit d'annuler une action en
                    // attente qu'un échec. Seul le bandeau bas confirme P1.
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested // « Supprimer P1 ? »
                        |> update' DeleteTandemConfirmed // suppression de P1 en arrière-plan
                        |> update' NextClicked // il navigue vers P2
                        |> update' DeleteTandemRequested // « Supprimer P2 ? » : question affichée
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, fixtureStems.[0], deletionTrashed fixtureStems.[0]))

                    Expect.equal state.Screen (Confirming(Triaging, DeleteTandem "P2")) "la question posée reste affichée"
                    Expect.stringContains state.Status SuccessStatusPrefix "la réussite de P1 est visible dans le bandeau bas"
                    Expect.equal state.Scan.TandemsToOrganize.Length (fixtureStems.Length - 1) "P1 est bien retiré du scan"
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
                }

                test "DeleteTandemCancelled without a matching pending action is ignored" {
                    let expected = init' ()
                    let state = expected |> update' DeleteTandemCancelled
                    Expect.equal state expected "rien à annuler, aucun changement d'écran"
                }

                test "DeleteTandemCompleted while back on the Organizing screen still updates the scan" {
                    // L'utilisateur ré-ouvre le même dossier pendant que la
                    // suppression tourne : l'écran est celui de l'organisation
                    // à l'arrivée du résultat. Le scan doit quand même refléter
                    // la suppression réelle (mêmes fichiers, même dossier),
                    // sans charger de photo hors du tri.
                    let state =
                        triaging' ()
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed // suppression en arrière-plan
                        |> update' (FolderSelected fixtureFolderPath) // le même dossier est ré-ouvert
                        |> update' (FolderScanned(2, fixtureFolderPath, fixtureScan)) // retour à l'écran d'organisation
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, fixtureStems.[0], deletionTrashed fixtureStems.[0]))

                    Expect.equal state.Screen (Working Organizing) "l'écran d'organisation reste"
                    Expect.equal state.Scan.TandemsToOrganize.Length (fixtureStems.Length - 1) "le scan reflète la suppression réelle"
                    Expect.stringContains state.Status "corbeille" "le résultat reste visible dans le bandeau bas"
                } ]

          testList
              "photo display"
              [ test "JpgLoaded stores the image of the current photo" {
                    let state = triaging' () |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage()))
                    Expect.isSome state.CurrentJpg "l'image courante est affichable"
                }

                test "JpgLoaded replaces the previous image when navigating" {
                    let firstImage = FakeImage()

                    let state =
                        triaging' ()
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], firstImage))
                        |> update' NextClicked
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[1], FakeImage()))

                    match state.CurrentJpg with
                    | Some image ->
                        Expect.isFalse (obj.ReferenceEquals(image, firstImage)) "la nouvelle image remplace l'ancienne, sans cumul"
                    | None -> failtest "l'image suivante doit être affichable"
                }

                test "JpgLoaded ignores a stale image that arrives after navigating" {
                    // Parcours UI : l'utilisateur navigue pendant que le JPG de
                    // la photo précédente se décode encore. Quand l'image lente
                    // arrive, elle ne doit pas écraser la photo courante.
                    let state =
                        triaging' ()
                        |> update' NextClicked // courant : P2
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage())) // arrivée tardive de P1

                    Expect.isNone state.CurrentJpg "l'image obsolète est ignorée, pas affichée"
                }

                test "JpgLoadFailed on the current photo reports the error in the status" {
                    let state = triaging' () |> update' (JpgLoadFailed(fixtureFolderPath, fixtureStems.[0], "fichier verrouille"))

                    Expect.equal state.Screen (Working Triaging) "l'écran ne change pas"
                    Expect.stringContains state.Status ErrorStatusPrefix "préfixe d'erreur"
                    Expect.stringContains state.Status (fixtureStems.[0] + ".JPG") "le fichier en échec est identifié"
                    Expect.stringContains state.Status "fichier verrouille" "la raison est visible"
                }

                test "JpgLoadFailed on the current photo clears the previous image" {
                    // Audit itération 4 (Majeur 2) : l'échec de chargement ne
                    // doit pas laisser la photo du tandem précédent affichée
                    // sous le compteur du courant — une décision de suppression
                    // ne se prend jamais sur une image qui ment.
                    let state =
                        triaging' ()
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage()))
                        |> update' NextClicked
                        |> update' (JpgLoadFailed(fixtureFolderPath, fixtureStems.[1], "fichier verrouille"))

                    Expect.isNone state.CurrentJpg "aucune image ne reste affichée pour un tandem non chargé"
                    Expect.stringContains state.Status "fichier verrouille" "l'échec reste signalé dans le bandeau bas"
                }

                test "JpgLoadFailed for a stale photo does not overwrite the status" {
                    // Un échec de chargement obsolète (photo déjà quittée)
                    // n'a pas le droit d'écraser un bandeau bas valide.
                    let withStatus = { triaging' () with Status = SuccessStatusPrefix + "Tandem supprimé." }

                    let state =
                        withStatus
                        |> update' NextClicked
                        |> update' (JpgLoadFailed(fixtureFolderPath, fixtureStems.[0], "fichier verrouille"))

                    Expect.equal state.Status (SuccessStatusPrefix + "Tandem supprimé.") "le statut n'est pas écrasé"
                }

                test "FolderScanned clears the current image" {
                    let state =
                        triaging' ()
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage()))
                        |> update' (FolderScanned(1, fixtureFolderPath, fixtureScan))

                    Expect.isNone state.CurrentJpg "le nouveau dossier ne garde pas l'ancienne image en mémoire"
                }

                test "FolderScanFailed clears the current image" {
                    let state =
                        triaging' ()
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage()))
                        |> update' (FolderScanFailed(1, fixtureFolderPath, "acces refuse"))

                    Expect.isNone state.CurrentJpg "aucune image ne survit à un changement de dossier"
                }

                test "JpgLoaded on an empty scan is ignored" {
                    // Plus aucun tandem visible : l'image décodée n'a pas de
                    // photo courante à rejoindre.
                    let state =
                        { triaging' () with Scan = emptyScan }
                        |> update' (JpgLoaded(fixtureFolderPath, "P1", FakeImage()))

                    Expect.isNone state.CurrentJpg "rien à afficher sans tandem courant"
                } ]

          testList
              "organization problems"
              [ // Modèle de vue du panneau « À corriger vous-même » (option A) :
                // signaler, jamais réparer. Une ligne par catégorie réellement
                // présente dans le scan, panneau absent si la liste est vide.

                test "a clean scan signals nothing" {
                    Expect.isEmpty (signaledProblems emptyScan) "scan vide : aucun problème signalé"
                    Expect.isEmpty (signaledProblems fixtureScan) "un dossier sain ne signale rien"
                }

                test "each present category signals one line with its files" {
                    let scan =
                        { emptyScan with
                            InvertedTandems = [ tandem "P3" ]
                            OrphanJpgs = [ "P5.JPG" ]
                            RawFolderIssue = Some(RawFolderCaseMismatch "raw") }

                    let problems = signaledProblems scan

                    Expect.equal problems.Length 3 "une ligne par catégorie réellement présente"
                    Expect.stringContains problems.[0].Sentence "tous les deux dans RAW/" "le couple inversé est expliqué"
                    Expect.equal problems.[0].FileNames [ "P3.JPG (dans RAW/)"; "P3.RW2 (dans RAW/)" ] "les fichiers concernés sont listés avec leur emplacement"
                    Expect.stringContains problems.[1].Sentence "JPG sans RAW" "l'orphelin est expliqué"
                    Expect.equal problems.[1].FileNames [ "P5.JPG (à la racine)" ] "l'orphelin est listé"
                    Expect.stringContains problems.[2].Sentence "renommez-le" "le dossier mal cassé explique le geste"
                    Expect.isEmpty problems.[2].FileNames "l'anomalie de dossier n'a pas de liste de fichiers"
                }

                test "counts are pluralized, never photo(s)" {
                    let two = signaledProblems { emptyScan with InvertedTandems = [ tandem "P3"; tandem "P4" ] }
                    Expect.stringContains two.[0].Sentence "2 photos" "pluriel réel"

                    let one = signaledProblems { emptyScan with InvertedTandems = [ tandem "P3" ] }
                    Expect.stringContains one.[0].Sentence "1 photo" "singulier réel"
                }

                test "every category of problem signals its own line" {
                    // Une ligne par catégorie réellement présente — le panneau
                    // ne fusionne ni n'oublie aucune sorte de problème.
                    let scan =
                        { emptyScan with
                            InvertedTandems = [ tandem "P3" ]
                            CrossedTandems = [ tandem "P4" ]
                            DuplicateRaws = [ "P9.RW2" ]
                            DuplicateJpgs = [ "P8.JPG" ]
                            CaseCollisions = [ { FileName = "P1.jpg"; InRawSubfolder = false } ]
                            OrphanJpgs = [ "P5.JPG" ]
                            OrphanRawsToOrganize = [ "P6.RW2" ]
                            FiledOrphanRaws = [ "P7.RW2" ]
                            OrphanJpgsInRaw = [ "P10.JPG" ] }

                    let problems = signaledProblems scan

                    Expect.equal problems.Length 9 "neuf catégories présentes, neuf lignes"

                    let sentences = problems |> List.map (fun p -> p.Sentence)

                    for expected in
                        [ "tous les deux dans RAW/" // inversés
                          "JPG dans RAW/, RAW à la racine" // croisés
                          "le même RW2" // doublon RAW
                          "le même JPG" // doublon JPG
                          "ne diffèrent que par la casse" // jumeaux de casse
                          "JPG sans RAW à la racine" // orphelin JPG racine
                          "RAW sans JPG à la racine" // orphelin RAW racine
                          "RAW sans JPG déjà rangés" // orphelin RAW rangé
                          "JPG isolés dans RAW/" ] do
                        Expect.isTrue
                            (sentences |> List.exists (fun (s: string) -> s.Contains expected))
                            (sprintf "ligne présente : %s" expected)
                }

                test "a RAW entry that is a file is signaled" {
                    let problems = signaledProblems { emptyScan with RawFolderIssue = Some RawFolderIsFile }

                    Expect.equal problems.Length 1 "une ligne pour l'anomalie"
                    Expect.stringContains problems.[0].Sentence "fichier nommé" "le cas « RAW » fichier est expliqué"
                } ]

          testList
              "stale folder results"
              [ // Parcours UI : les résultats d'arrière-plan (décodage, suppression)
                // identifient leur tandem par stem seul. Si l'utilisateur change
                // de dossier pendant l'opération et que le nouveau dossier a les
                // mêmes stems (même appareil), le résultat de l'ancien dossier ne
                // doit toucher ni l'image ni le scan du nouveau. Le bandeau bas,
                // lui, garde le droit de refléter le résultat (règle VISION).

                let scanOfOtherFolder () =
                    triaging' ()
                    |> update' (FolderSelected otherFolderPath)
                    |> update' (FolderScanned(2, otherFolderPath, fixtureScan)) // mêmes stems, dossier différent

                test "JpgLoaded from the previous folder is ignored after another folder is scanned" {
                    let state =
                        scanOfOtherFolder ()
                        |> update' (JpgLoaded(fixtureFolderPath, fixtureStems.[0], FakeImage())) // décodage lent de l'ancien dossier

                    Expect.isNone state.CurrentJpg "la photo affichée appartient au dossier courant, pas au précédent"
                }

                test "JpgLoadFailed from the previous folder does not overwrite the new folder status" {
                    let state =
                        scanOfOtherFolder ()
                        |> update' (JpgLoadFailed(fixtureFolderPath, fixtureStems.[0], "fichier verrouille"))

                    Expect.stringContains state.Status "tandems à organiser" "le statut du dossier courant tient toujours"
                    Expect.isFalse (state.Status.StartsWith ErrorStatusPrefix) "l'échec de l'ancien dossier n'enduit pas le nouveau"
                }

                test "DeleteTandemCompleted from the previous folder leaves the new folder scan alone" {
                    let state =
                        { triaging' () with FolderPath = Some fixtureFolderPath }
                        |> update' DeleteTandemRequested
                        |> update' DeleteTandemConfirmed // suppression de l'ancien dossier, tourne en arrière-plan
                        |> update' (FolderSelected otherFolderPath)
                        |> update' (FolderScanned(2, otherFolderPath, fixtureScan)) // le nouveau dossier a aussi un P1
                        |> update' (DeleteTandemCompleted(fixtureFolderPath, fixtureStems.[0], deletionTrashed fixtureStems.[0]))

                    Expect.equal state.Scan.TandemsToOrganize.Length fixtureStems.Length "le tandem du nouveau dossier n'est pas retiré : ses fichiers existent toujours"
                    Expect.stringContains state.Status "corbeille" "le résultat réel de l'ancien dossier reste visible dans le bandeau bas"
                } ] ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args (testList "RawTandem.UI tests" [ stateTests; scenarioTests ])
