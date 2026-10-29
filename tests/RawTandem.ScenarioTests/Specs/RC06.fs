module Specs.RC06

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Expecto
open Elmish
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.Hosts
open Avalonia.Headless
open Avalonia.Media.Imaging
open Avalonia.LogicalTree
open SkiaSharp
open RawTandem.Core.Constants
open RawTandem.Core.Exif
open RawTandem.Core.FolderScan
open RawTandem.Core.Tandems
open RawTandem.UI.State

open Harness.Steps
open Harness.Fixtures
open Harness.Rendering

/// Règle RC-06 — docs/usecases/regles-communes.md.
/// 
/// Coverage : générique — pendant chaque opération (analyse, plan, rangement,
/// réanalyses, suppression), les gestes ne passent pas ; les boutons sont
/// vérifiés grisés sur la vraie vue rendue ; ce que la règle autorise toujours
/// (affichage d'une image, ouverture de dossier pendant une analyse ou une
/// réanalyse) est verrouillé pour interdire tout sur-blocage.
/// 
/// Non couvert (headless) : le sélecteur natif ; la modalité du picker
/// (fenêtre désactivée pendant le dialogue) est une affordance de la vue
/// (Main.fs), pas une garde testable.

[<Tests>]
let tests =
    testList "RC-06 : actions asynchrones justes nécessaires" [

        // ---------- Pendant l'analyse d'un dossier ----------

        /// RC-06, garde : pendant l'analyse d'un dossier demandé depuis le
        /// tri (« Analyse du dossier… »), les gestes du tri sont des
        /// no-ops — ils agiraient sur le scan périmé du dossier quitté.
        /// L'ouverture d'un autre dossier reste permise (exception 2),
        /// verrouillée par le test « opening a folder during an analysis or
        /// rescan supersedes it ». Rouge aujourd'hui : l'analyse ne porte
        /// pas de drapeau.
        test "RC-06 common rule : triage gestures are no-ops while a folder analysis is in flight" {
            withPhotos (fun photos ->
                withPellicule (fun pellicule ->
                    let triage = init () |> fst |> step (FolderSelected photos)

                    Expect.equal triage.Screen (Working Triaging) "précondition : vue de tri sur le dossier organisé"

                    // Décollé : un autre dossier est ouvert depuis le tri —
                    // l'analyse tourne, l'écran reste celui du tri.
                    let flight = takeOff (FolderSelected pellicule) triage

                    Expect.equal (fst flight).Screen (Working Triaging) "l'écran reste le tri pendant l'analyse"

                    Expect.stringContains (fst flight).Status "Analyse du dossier" "le vol est visible dans le bandeau bas"

                    // RC-06 : la navigation n'agit pas sur le scan périmé.
                    let navigated = step NextClicked (fst flight)

                    Expect.equal navigated.CurrentIndex (fst flight).CurrentIndex "la navigation est sans effet pendant l'analyse"

                    // RC-06 : Suppr ne peut pas partir sur le scan périmé.
                    let askedDuring = step DeleteTandemRequested (fst flight)
                    Expect.equal askedDuring (fst flight) "Suppr est sans effet pendant l'analyse"

                    // RC-06 : le plein écran non plus.
                    let fullscreen = step FullscreenRequested (fst flight)
                    Expect.equal fullscreen.DisplayMode Normal "le plein écran est sans effet pendant l'analyse"

                    // Atterrissage : le nouveau dossier s'affiche, la main
                    // est rendue — le rangement demande passe.
                    let landed = landFlight flight
                    Expect.equal landed.Screen (Working Organizing) "le dossier analysé s'affiche"
                    Expect.equal landed.Scan.TandemsToOrganize.Length 3 "le scan affiché est celui du dossier ouvert"

                    let askedAfter = step OrganizeRequested landed

                    Expect.equal
                        askedAfter.Screen
                        (Confirming(Organizing, OrganizeFolder 3))
                        "la main rendue : le rangement demande passe"))
        }

        // ---------- Pendant le calcul du plan ----------

        /// RC-06, garde : pendant le calcul du plan (« Préparation du
        /// plan… »), aucun geste de l'écran d'organisation ne passe —
        /// c'est le seul vol où l'ouverture de dossier est refusée aussi
        /// (aucune exception de la règle ne s'applique : ni affichage
        /// d'image, ni analyse, ni réanalyse). Ce vol atterrit sur le
        /// bandeau de question : la main passe par elle. Rouge
        /// aujourd'hui : le vol du plan ne porte pas de drapeau.
        test "RC-06 common rule : organizing gestures are no-ops while the organize plan is in flight" {
            withPellicule (fun pellicule ->
                let organizing = init () |> fst |> step (FolderSelected pellicule)

                Expect.equal organizing.Screen (Working Organizing) "précondition : écran d'organisation, 3 RAW à ranger"

                // Décollé : le plan part (« Préparation du plan… »), le
                // bandeau de question ne s'affiche qu'à son retour.
                let flight = takeOff OrganizeRequested organizing

                Expect.equal (fst flight).Screen (Working Organizing) "aucune question avant le retour du plan"

                Expect.stringContains (fst flight).Status "Préparation du plan" "le vol est visible dans le bandeau bas"

                // RC-06 : « Passer au tri » ne passe pas pendant le vol.
                let goToTriage = step StartTriagingClicked (fst flight)
                Expect.equal goToTriage.Screen (Working Organizing) "Passer au tri est sans effet pendant le vol du plan"

                // RC-06 : recliquer « Ranger » (double-clic) ne relance rien.
                let again = step OrganizeRequested (fst flight)
                Expect.equal again.Screen (Working Organizing) "un second rangement est sans effet pendant le vol du plan"
                Expect.equal again.Status (fst flight).Status "le statut ne bouge pas non plus"

                // RC-06 : Réanalyser ne lance rien pendant le vol (l'égalité
                // d'état seule serait un faux vert — une réanalyse du même
                // dossier rend un scan identique : la preuve observable est
                // qu'aucune commande n'est planifiée).
                let rescanAttempt = takeOff RescanRequested (fst flight)
                Expect.equal (fst rescanAttempt) (fst flight) "Réanalyser est sans effet pendant le vol du plan"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd rescanAttempt))) "Réanalyser ne lance aucune commande pendant le vol du plan"

                // RC-06 : l'ouverture de dossier est refusée aussi — le seul
                // vol sans exception.
                let openAttempt = takeOff (FolderSelected pellicule) (fst flight)
                Expect.equal (fst openAttempt) (fst flight) "ouvrir un dossier est sans effet pendant le vol du plan"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd openAttempt))) "aucune nouvelle analyse n'est lancée pendant le vol du plan"

                // Atterrissage : le vol du plan atterrit sur le bandeau de
                // question — la main passe par elle.
                let landed = landFlight flight
                Expect.equal landed.Screen (Confirming(Organizing, OrganizeFolder 3)) "le plan atterrit : la question s'affiche"

                let answered = landed |> step OrganizeCancelled
                Expect.equal answered.Screen (Working Organizing) "question tranchée : retour à l'organisation"

                // La main est rendue : les gestes repassent.
                let goToTriageAfter = step StartTriagingClicked answered
                Expect.equal goToTriageAfter.Screen (Working Triaging) "Passer au tri repasse après l'atterrissage")
        }

        // ---------- Pendant le rangement des RAW ----------

        /// RC-06, garde : pendant l'exécution du rangement, aucun geste de
        /// l'écran d'organisation ne passe. Rouge aujourd'hui : « Passer au
        /// tri » et un second rangement agissent pendant le vol.
        test "RC-06 common rule : organizing gestures are no-ops while the organize execution is in flight" {
            withPellicule (fun pellicule ->
                // Précondition : dossier ouvert, rangement demandé, question
                // affichée (RC-05), geste de confirmation joué.
                let confirming =
                    init ()
                    |> fst
                    |> step (FolderSelected pellicule)
                    |> step OrganizeRequested

                Expect.equal confirming.Screen (Confirming(Organizing, OrganizeFolder 3)) "précondition : 3 RAW annoncés"

                // Décollé : l'exécution part (vrais déplacements), son
                // résultat n'est pas encore revenu.
                let flight = takeOff OrganizeConfirmed confirming
                Expect.equal (fst flight).Screen (Working Organizing) "l'exécution laisse l'écran d'organisation"
                Expect.stringContains (fst flight).Status "Organisation en cours" "le vol est visible dans le bandeau"

                // RC-06 : « Passer au tri » ne passe pas pendant le vol.
                let goToTriage = step StartTriagingClicked (fst flight)
                Expect.equal goToTriage.Screen (Working Organizing) "Passer au tri est sans effet pendant le rangement"

                // RC-06 : un second rangement ne rouvre pas de question.
                let again = step OrganizeRequested (fst flight)
                Expect.equal again.Screen (Working Organizing) "un second rangement est sans effet pendant le vol"
                Expect.equal again.Status (fst flight).Status "le statut ne bouge pas non plus"

                // RC-06 : « Réanalyser » ne passe pas pendant le vol. L'égalité
                // d'état seule serait un faux vert (une réanalyse du même
                // dossier rend un scan identique) : la preuve observable est
                // qu'aucune commande n'est planifiée.
                let rescanAttempt = takeOff RescanRequested (fst flight)
                Expect.equal (fst rescanAttempt) (fst flight) "Réanalyser est sans effet sur l'état pendant le vol"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd rescanAttempt))) "Réanalyser ne lance aucune commande pendant le vol"

                // RC-06 : ouvrir un dossier ne passe pas non plus —
                // l'exception 2 ne couvre que l'analyse et la réanalyse.
                let openAttempt = takeOff (FolderSelected pellicule) (fst flight)
                Expect.equal (fst openAttempt) (fst flight) "ouvrir un dossier est sans effet pendant le rangement"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd openAttempt))) "aucune nouvelle analyse n'est lancée pendant le rangement"

                // Atterrissage : déplacements, réanalyse en cascade, main
                // rendue — le tri s'ouvre sur un scan frais.
                let landed = landFlight flight
                Expect.equal landed.Screen (Working Triaging) "le tri s'ouvre une fois tout terminé"
                Expect.equal landed.Scan.TandemsToOrganize.Length 0 "le scan est frais à l'arrivée au tri"

                // La main est rendue : la navigation repasse.
                let navigated = step NextClicked landed
                Expect.equal navigated.CurrentIndex 1 "la navigation repasse après l'atterrissage")
        }

        // ---------- Après le rangement : la réanalyse d'arrière-plan ----------

        /// RC-06, garde : le rangement déclenche une réanalyse automatique —
        /// l'utilisateur attend dans la vue tri et la main n'est rendue
        /// qu'à l'arrivée du scan frais. Rouge aujourd'hui : la suppression
        /// part pendant la réanalyse, c'est la course du « tandem fantôme »
        /// (le rescan, cliché pris avant, réafficherait une photo supprimée).
        test "RC-06 common rule : the hand is not returned until the post-organize rescan completes" {
            withPellicule (fun pellicule ->
                let confirming =
                    init ()
                    |> fst
                    |> step (FolderSelected pellicule)
                    |> step OrganizeRequested

                // Première vague d'atterrissage : les déplacements sont faits,
                // l'écran bascule au tri — mais la réanalyse que le rangement
                // déclenche tourne encore.
                let waiting = landOneWave (takeOff OrganizeConfirmed confirming)

                Expect.equal (fst waiting).Screen (Working Triaging) "l'utilisateur attend dans la vue tri pendant la réanalyse"

                // RC-06 : Suppr n'ouvre aucune confirmation pendant la réanalyse.
                let askedDuring = step DeleteTandemRequested (fst waiting)
                Expect.equal askedDuring (fst waiting) "Suppr est sans effet pendant la réanalyse"

                // RC-06 : la navigation non plus.
                let navigated = step NextClicked (fst waiting)
                Expect.equal navigated.CurrentIndex (fst waiting).CurrentIndex "la navigation est sans effet pendant la réanalyse"

                // RC-06 : le plein écran non plus (geste de vue, mais geste).
                let fullscreen = step FullscreenRequested (fst waiting)
                Expect.equal fullscreen.DisplayMode Normal "le plein écran est sans effet pendant la réanalyse"

                // Atterrissage de la réanalyse : le scan est frais, la main
                // est rendue — la suppression repasse.
                let landed = landFlight waiting
                Expect.equal landed.Scan.TandemsToOrganize.Length 0 "le scan est frais après la réanalyse"

                let askedAfter = step DeleteTandemRequested landed
                Expect.equal askedAfter.Screen (Confirming(Triaging, DeleteTandem "P1")) "la main rendue : la suppression repasse")
        }

        // ---------- Pendant une réanalyse demandée (F5) ----------

        /// RC-06, garde : pendant une réanalyse demandée (F5, écran
        /// d'organisation), les gestes sont des no-ops. L'ouverture d'un
        /// autre dossier reste permise (exception 2), verrouillée par le
        /// test « opening a folder during an analysis or rescan supersedes
        /// it ». La réanalyse est silencieuse (ni écran ni bandeau ne
        /// bougent) : le vol n'est observable que par sa commande en
        /// attente. Rouge aujourd'hui : la réanalyse demandée ne porte pas
        /// de drapeau.
        test "RC-06 common rule : organizing gestures are no-ops while a user rescan is in flight" {
            withPellicule (fun pellicule ->
                let organizing = init () |> fst |> step (FolderSelected pellicule)

                // Décollé : F5, la réanalyse part — silencieuse pour
                // l'utilisateur : l'écran, le bandeau et le scan ne bougent
                // pas, seule la commande en attente prouve le vol (le
                // drapeau RC-06 posé par update n'est pas un observable
                // utilisateur — c'est lui qui bloque les gestes).
                let flight = takeOff RescanRequested organizing

                Expect.equal (fst flight).Screen organizing.Screen "la réanalyse est silencieuse : l'écran ne bouge pas"
                Expect.equal (fst flight).Status organizing.Status "le bandeau bas ne bouge pas"
                Expect.equal (fst flight).Scan organizing.Scan "le scan ne bouge pas"

                Expect.isFalse
                    (List.isEmpty (List.ofSeq (snd flight)))
                    "précondition : une réanalyse est bien en vol"

                // RC-06 : demander le rangement ne passe pas pendant le vol.
                let organizeAttempt = takeOff OrganizeRequested (fst flight)
                Expect.equal (fst organizeAttempt) (fst flight) "Ranger est sans effet pendant la réanalyse"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd organizeAttempt))) "aucun plan n'est lancé pendant la réanalyse"

                // RC-06 : « Passer au tri » non plus.
                let goToTriage = step StartTriagingClicked (fst flight)
                Expect.equal goToTriage.Screen (Working Organizing) "Passer au tri est sans effet pendant la réanalyse"

                // Atterrissage : la main est rendue — le rangement demande
                // passe de nouveau.
                let landed = landFlight flight
                Expect.equal landed.Screen (Working Organizing) "la réanalyse laisse l'écran d'organisation"

                let askedAfter = step OrganizeRequested landed

                Expect.equal
                    askedAfter.Screen
                    (Confirming(Organizing, OrganizeFolder 3))
                    "la main rendue : le rangement demande passe")
        }

        // ---------- Pendant une suppression ----------

        /// RC-06, garde : pendant l'exécution d'une suppression, les gestes
        /// du tri sont des no-ops — un second Suppr ne rouvre pas de question.
        /// Rouge aujourd'hui : tous ces gestes agissent pendant le vol.
        test "RC-06 common rule : triage gestures are no-ops while a tandem deletion is in flight" {
            withPhotos (fun photos ->
                let rawDir = Path.Combine(photos, RawSubfolderName)

                let confirming =
                    init ()
                    |> fst
                    |> step (FolderSelected photos)
                    |> step DeleteTandemRequested

                Expect.equal confirming.Screen (Confirming(Triaging, DeleteTandem "P1")) "précondition : suppression de P1 confirmable"

                let flight = takeOff DeleteTandemConfirmed confirming
                Expect.stringContains (fst flight).Status "Suppression en cours" "le vol est visible dans le bandeau"

                // RC-06 : un second Suppr ne rouvre pas de question.
                let doubleDelete = step DeleteTandemRequested (fst flight)
                Expect.equal doubleDelete (fst flight) "un second Suppr est sans effet pendant la suppression"

                // RC-06 : la navigation non plus.
                let navigated = step NextClicked (fst flight)
                Expect.equal navigated.CurrentIndex 0 "la navigation est sans effet pendant la suppression"

                // RC-06 : le plein écran non plus.
                let fullscreen = step FullscreenRequested (fst flight)
                Expect.equal fullscreen.DisplayMode Normal "le plein écran est sans effet pendant la suppression"

                // RC-06 : ouvrir un dossier ne passe pas non plus —
                // l'exception 2 ne couvre que l'analyse et la réanalyse.
                let openAttempt = takeOff (FolderSelected photos) (fst flight)
                Expect.equal (fst openAttempt) (fst flight) "ouvrir un dossier est sans effet pendant la suppression"
                Expect.isTrue (List.isEmpty (List.ofSeq (snd openAttempt))) "aucune nouvelle analyse n'est lancée pendant la suppression"

                // Atterrissage : le tandem est parti (vraie corbeille OS),
                // la main est rendue.
                let landed = landFlight flight
                Expect.isFalse (File.Exists(Path.Combine(photos, "P1.JPG"))) "le JPG est parti à la corbeille"
                Expect.isFalse (File.Exists(Path.Combine(rawDir, "P1.RW2"))) "le RW2 est parti à la corbeille"
                Expect.equal (triableTandems landed).Length 2 "le tandem supprimé quitte le tri"

                let askedAfter = step DeleteTandemRequested landed
                Expect.equal askedAfter.Screen (Confirming(Triaging, DeleteTandem "P2")) "la main rendue : la suppression repasse pour la photo courante")
        }

        // ---------- Les boutons pendant les opérations ----------

        /// RC-06, affordance : pendant un vol, les widgets d'action sont
        /// grisés dans la vraie vue, l'ouverture de dossier comprise —
        /// sauf pendant une analyse ou une réanalyse, où l'exception 2 la
        /// laisse active. Rouge aujourd'hui : rien n'est grisé pendant les vols.
        test "RC-06 common rule : action widgets are grayed while an operation is in flight, and come back after" {
            withPellicule (fun pellicule ->
                let organizing = init () |> fst |> step (FolderSelected pellicule)

                // Vol de l'exécution du rangement (écran d'organisation) :
                // tout est grisé, l'ouverture comprise.
                let organizeFlight = organizing |> step OrganizeRequested |> takeOff OrganizeConfirmed

                let organizeButtons = renderedButtons (fst organizeFlight)

                Expect.isFalse (organizeButtons |> List.exists snd) "toutes les actions sont grisées pendant le rangement, ouverture comprise"

                // Vol du plan (« Préparation du plan… », écran
                // d'organisation) : tout est grisé, ouverture comprise —
                // aucune exception ne s'applique à ce vol.
                let planFlight = takeOff OrganizeRequested organizing
                let planButtons = renderedButtons (fst planFlight)

                Expect.isFalse (planButtons |> List.exists snd) "toutes les actions sont grisées pendant le vol du plan, ouverture comprise"

                // Vol d'une réanalyse demandée (F5) : l'exception 2 laisse
                // l'ouverture de dossier active, elle seule.
                let rescanFlight = takeOff RescanRequested organizing
                let rescanButtons = renderedButtons (fst rescanFlight)

                Expect.isTrue
                    (rescanButtons |> List.exists (fun (label, enabled) -> label = "Ouvrir un autre dossier…" && enabled))
                    "l'ouverture de dossier reste active pendant la réanalyse demandée (exception 2)"

                Expect.equal (rescanButtons |> List.filter snd |> List.map fst) [ "Ouvrir un autre dossier…" ] "seule l'ouverture de dossier reste active pendant la réanalyse demandée"

                // Vol de la réanalyse post-rangement (vue tri) : l'exception 2
                // laisse l'ouverture de dossier active, elle seule.
                let waiting = landOneWave organizeFlight
                let triageButtons = renderedButtons (fst waiting)

                Expect.isTrue
                    (triageButtons |> List.exists (fun (label, enabled) -> label = "Ouvrir un autre dossier…" && enabled))
                    "l'ouverture de dossier reste active pendant la réanalyse (exception 2)"

                Expect.equal (triageButtons |> List.filter snd |> List.map fst) [ "Ouvrir un autre dossier…" ] "seule l'ouverture de dossier reste active pendant la réanalyse"

                // Vol d'une suppression (vue tri) : tout est grisé, ouverture
                // comprise ; puis santé du grisage : une fois atterri, les
                // actions reviennent.
                withPhotos (fun photos ->
                    // Vol d'une analyse demandée depuis le tri : l'exception 2
                    // laisse l'ouverture de dossier active, elle seule.
                    let triage = init () |> fst |> step (FolderSelected photos)
                    let analysisFlight = takeOff (FolderSelected pellicule) triage
                    let analysisButtons = renderedButtons (fst analysisFlight)

                    Expect.isTrue
                        (analysisButtons |> List.exists (fun (label, enabled) -> label = "Ouvrir un autre dossier…" && enabled))
                        "l'ouverture de dossier reste active pendant l'analyse (exception 2)"

                    Expect.equal (analysisButtons |> List.filter snd |> List.map fst) [ "Ouvrir un autre dossier…" ] "seule l'ouverture de dossier reste active pendant l'analyse"

                    let deleteFlight =
                        init ()
                        |> fst
                        |> step (FolderSelected photos)
                        |> step DeleteTandemRequested
                        |> takeOff DeleteTandemConfirmed

                    let deleteButtons = renderedButtons (fst deleteFlight)

                    Expect.isFalse (deleteButtons |> List.exists snd) "toutes les actions sont grisées pendant la suppression, ouverture comprise"

                    let landedButtons = renderedButtons (landFlight deleteFlight)

                    Expect.isTrue
                        (landedButtons |> List.exists (fun (label, enabled) -> label = "Supprimer le tandem" && enabled))
                        "Supprimer le tandem revient actif après la suppression"

                    Expect.isTrue
                        (landedButtons |> List.exists (fun (label, enabled) -> label = "Ouvrir un autre dossier…" && enabled))
                        "l'ouverture de dossier revient active après la suppression"))
        }

        // ---------- Ce que la règle autorise toujours ----------

        /// RC-06, exception 1 : l'affichage de l'image dans la vue tri ne
        /// bloque jamais les autres actions — navigation, suppression, ouverture
        /// de dossier. Vert aujourd'hui : le test verrouille l'exception pour
        /// que l'implémentation de la règle ne sur-bloque pas.
        test "RC-06 exception : image display never blocks the triage view" {
            withPhotos (fun photos ->
                let triage = init () |> fst |> step (FolderSelected photos)
                Expect.equal triage.Screen (Working Triaging) "précondition : vue de tri"

                // Décollé : la photo suivante est demandée, son JPG décode
                // encore (commande en vol).
                let loadFlight = takeOff NextClicked triage
                Expect.equal (fst loadFlight).CurrentIndex 1 "la navigation change immédiatement, sans attendre le JPG"

                // Pendant le vol du chargement, la navigation continue : Next
                // et Previous passent, l'affichage ne bloque jamais les gestes.
                let forwarded = step NextClicked (fst loadFlight)
                Expect.equal forwarded.CurrentIndex 2 "Next passe pendant le chargement"
                let back = step PreviousClicked forwarded
                Expect.equal back.CurrentIndex 1 "Previous passe pendant le chargement"

                // Pendant le vol du chargement, la suppression reste possible.
                let asked = step DeleteTandemRequested (fst loadFlight)
                Expect.equal asked.Screen (Confirming(Triaging, DeleteTandem "P2")) "la suppression reste possible pendant le chargement"

                // Exception 1, exemple explicite de la règle : ouvrir un
                // dossier reste possible pendant le chargement d'une image.
                let openDuringLoad = takeOff (FolderSelected photos) (fst loadFlight)

                Expect.stringContains (fst openDuringLoad).Status "Analyse du dossier"
                    "l'ouverture reste possible pendant le chargement d'une image"

                Expect.isFalse (List.isEmpty (List.ofSeq (snd openDuringLoad)))
                    "une nouvelle analyse part pendant le chargement d'une image"

                let cancelled = step DeleteTandemCancelled asked
                Expect.equal cancelled.Screen (Working Triaging) "retour au tri après refus"

                // Le chargement atterrit sur l'état courant : échec attendu,
                // les JPG de fixture sont vides.
                let landed = landFlight (cancelled, snd loadFlight)
                Expect.stringContains landed.Status "Impossible d'afficher" "le chargement atterrit pendant que le tri a continué")
        }

        /// RC-06, exception 2 : l'ouverture d'un dossier est possible pendant
        /// une analyse ou une réanalyse — y compris la réanalyse post-rangement,
        /// le vol suivi par le drapeau où l'utilisateur attend dans le tri. La
        /// dernière demande gagne, le résultat dépassé est ignoré (garde
        /// ScanRequestId) et son drapeau se lève. Vert aujourd'hui : le test
        /// verrouille l'exception.
        test "RC-06 exception : opening a folder during an analysis or rescan supersedes it" {
            withPellicule (fun pellicule ->
                withPhotos (fun photos ->
                    // Vol d'analyse de pellicule, interrompu par l'ouverture
                    // de photos : la dernière demande gagne.
                    let pelliculeFlight = init () |> fst |> takeOff (FolderSelected pellicule)
                    let photosFlight = takeOff (FolderSelected photos) (fst pelliculeFlight)

                    let photosLanded = landFlight photosFlight
                    Expect.equal photosLanded.Screen (Working Triaging) "le dossier ouvert en dernier s'affiche"
                    Expect.equal photosLanded.Scan.OrganizedTandems.Length 3 "le scan affiché est celui de photos"

                    // Le résultat retardé de pellicule atterrit après : ignoré.
                    let afterStale = landFlight (photosLanded, snd pelliculeFlight)
                    Expect.equal afterStale.Screen (Working Triaging) "le résultat dépassé ne change pas l'écran"
                    Expect.equal afterStale.Scan.OrganizedTandems.Length 3 "le résultat dépassé ne change pas le scan"

                    // Vol de réanalyse demandée (F5) depuis l'écran
                    // d'organisation de pellicule, interrompu par l'ouverture
                    // de photos : même supersede.
                    let pelliculeFlight2 = takeOff (FolderSelected pellicule) afterStale
                    let pelliculeLanded = landFlight pelliculeFlight2

                    Expect.equal pelliculeLanded.Screen (Working Organizing) "pellicule propose son organisation"
                    Expect.equal pelliculeLanded.Scan.TandemsToOrganize.Length 3 "le scan affiché est celui de pellicule"

                    let rescanFlight = takeOff RescanRequested pelliculeLanded
                    let photosFlight2 = takeOff (FolderSelected photos) (fst rescanFlight)

                    let photosLanded2 = landFlight photosFlight2

                    Expect.equal photosLanded2.Screen (Working Triaging) "photos s'affiche à la place"
                    Expect.equal photosLanded2.Scan.OrganizedTandems.Length 3 "le scan affiché est celui de photos"

                    let afterStaleRescan = landFlight (photosLanded2, snd rescanFlight)

                    Expect.equal afterStaleRescan.Scan.OrganizedTandems.Length 3
                        "le résultat dépassé de la réanalyse ne change pas le scan"

                    // Vol de la réanalyse post-rangement — le vol suivi par
                    // le drapeau RC-06 où l'utilisateur attend dans le tri :
                    // l'ouverture reste sa porte de sortie (exception 2).
                    let organizeConfirming =
                        init ()
                        |> fst
                        |> step (FolderSelected pellicule)
                        |> step OrganizeRequested

                    let waiting = landOneWave (takeOff OrganizeConfirmed organizeConfirming)

                    Expect.equal
                        (fst waiting).Screen
                        (Working Triaging)
                        "précondition : attente dans la vue tri pendant la réanalyse post-rangement"

                    let escapeRescan = takeOff (FolderSelected photos) (fst waiting)

                    Expect.stringContains
                        (fst escapeRescan).Status
                        "Analyse du dossier"
                        "l'ouverture reste possible pendant la réanalyse suivie (exception 2)"

                    let photosAfterEscape = landFlight escapeRescan

                    Expect.equal photosAfterEscape.Screen (Working Triaging) "le dossier ouvert pendant la réanalyse s'affiche"
                    Expect.equal photosAfterEscape.Scan.OrganizedTandems.Length 3 "le scan affiché est celui de photos"

                    // La réanalyse dépassée atterrit : ignorée, le scan de
                    // photos reste — et son drapeau se lève, la main revient.
                    let afterStaleRescan2 = landFlight (photosAfterEscape, snd waiting)

                    Expect.equal afterStaleRescan2.Scan.OrganizedTandems.Length 3
                        "le résultat dépassé de la réanalyse ne change pas le scan"

                    let navigated = step NextClicked afterStaleRescan2

                    Expect.equal navigated.CurrentIndex 1
                        "le drapeau de la réanalyse dépassée est levé : la navigation repasse"))
        }
    ]
