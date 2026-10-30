module Specs.Rc05BandeauJauneModal

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

/// Règle RC-05 — docs/usecases/rc-05-bandeau-jaune-modal.md.
/// 
/// Coverage : générique — les boutons grisés pendant une question sont vérifiés
/// sur la vraie vue rendue (écran d'organisation, tri, retour après réponse) ;
/// la sélection de dossier pendant une question est refusée par la garde (filet
/// machine : le chemin utilisateur est fermé d'amont par RC-06, le picker ne
/// peut plus être ouvert pendant un vol — un message peut néanmoins arriver
/// d'une file ou d'un autre front sans fenêtre).
/// 
/// Les actions du bandeau (Déplacer/Supprimer, Annuler) sont exercées en
/// permanence par les scénarios des UC.

[<Tests>]
let tests =
    testList "RC-05 : bandeau jaune modal" [

        // ---------- Les boutons pendant une question ----------

        test "RC-05 common rule : organizing actions are grayed while the question is displayed" {
            // Pendant une question, tout geste de l'écran sous-jacent est un
            // no-op gardé dans update — il doit aussi se voir : bouton grisé,
            // jamais de clic mort en silence. Seuls Déplacer / Annuler restent
            // actifs. Les boutons-maquettes du prototype (Voir les fichiers,
            // Réanalyser) suivent la même règle.
            let buttons = renderedButtons (displayState (Confirming(Organizing, OrganizeFolder 1)))

            let outsideQuestion =
                buttons
                |> List.filter (fun (label, _) -> label <> "Déplacer" && label <> "Annuler")

            Expect.isFalse
                (outsideQuestion |> List.exists snd)
                "aucune action hors question n'est active (Ranger, Voir les fichiers, Réanalyser, Passer au tri, dossier)"

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label = "Déplacer" && enabled))
                "Déplacer reste actif : la question doit pouvoir être acceptée"

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label = "Annuler" && enabled))
                "Annuler reste actif : la question doit pouvoir être refusée"
        }

        test "RC-05 common rule : triage actions are grayed while a deletion question is displayed" {
            let buttons = renderedButtons (displayState (Confirming(Triaging, DeleteTandem "P1")))

            let outsideQuestion =
                buttons
                |> List.filter (fun (label, _) -> label <> "Supprimer" && label <> "Annuler")

            Expect.isFalse
                (outsideQuestion |> List.exists snd)
                "aucune action hors question n'est active (flèches, suppression, plein écran, dossier)"

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label = "Supprimer" && enabled))
                "Supprimer reste actif"

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label = "Annuler" && enabled))
                "Annuler reste actif"
        }

        test "RC-05 common rule : actions come back when no question is displayed" {
            // Santé du grisage : hors question, les actions ne doivent pas
            // rester grisées par erreur.
            let buttons = renderedButtons (displayState (Working Organizing))

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label.StartsWith "Ranger" && enabled))
                "Ranger les RAW est actif hors question"
        }

        // ---------- La sélection de dossier pendant une question ----------

        /// RC-05, garde : un dossier choisi pendant la question de rangement
        /// est refusé — la question reste entière, le dossier courant et son
        /// scan ne changent pas, aucune analyse ne part. No-op silencieux,
        /// comme les autres gardes de question : c'est un filet technique
        /// (le chemin utilisateur est fermé d'amont par RC-06), l'action
        /// visible est celle du bandeau.
        test "RC-05 common rule : folder selection during an organizing question is refused" {
            withPellicule (fun pellicule ->
                withPhotos (fun photos ->
                    let confirming =
                        init ()
                        |> fst
                        |> step (FolderSelected pellicule)
                        |> step OrganizeRequested

                    Expect.equal
                        confirming.Screen
                        (Confirming(Organizing, OrganizeFolder 3))
                        "précondition : la question de rangement est affichée"

                    let attempt = takeOff (FolderSelected photos) confirming

                    Expect.equal (fst attempt).Screen confirming.Screen "la question reste affichée, l'écran ne change pas"

                    Expect.equal (fst attempt).FolderPath confirming.FolderPath "le dossier courant ne change pas"

                    Expect.equal (fst attempt).ScanRequestId confirming.ScanRequestId "aucune nouvelle demande de scan"

                    Expect.isTrue
                        (List.isEmpty (List.ofSeq (snd attempt)))
                        "aucune analyse n'est lancée pendant la question"))
        }

        /// RC-05, garde : même refus pendant la question de suppression —
        /// sinon la suppression confirmée s'exécuterait sur le scan du
        /// dossier choisi, pas celui que la question annonçait.
        test "RC-05 common rule : folder selection during a deletion question is refused" {
            withPhotos (fun photos ->
                withPellicule (fun pellicule ->
                    let confirming =
                        init ()
                        |> fst
                        |> step (FolderSelected photos)
                        |> step DeleteTandemRequested

                    Expect.equal
                        confirming.Screen
                        (Confirming(Triaging, DeleteTandem "P1"))
                        "précondition : la question de suppression est affichée"

                    let attempt = takeOff (FolderSelected pellicule) confirming

                    Expect.equal
                        (fst attempt).Screen
                        (Confirming(Triaging, DeleteTandem "P1"))
                        "la question reste affichée, l'écran ne change pas"

                    Expect.equal (fst attempt).FolderPath confirming.FolderPath "le dossier courant ne change pas"

                    Expect.equal (fst attempt).ScanRequestId confirming.ScanRequestId "aucune nouvelle demande de scan"

                    Expect.isTrue
                        (List.isEmpty (List.ofSeq (snd attempt)))
                        "aucune analyse n'est lancée pendant la question"))
        }

        /// RC-05, santé de la garde : la question tranchée, la sélection de
        /// dossier fonctionne de nouveau. Vert aujourd'hui — il verrouille
        /// le retour pour que la garde ne sur-bloque pas.
        test "RC-05 common rule : folder selection comes back once the question is answered" {
            withPhotos (fun photos ->
                let answered =
                    init ()
                    |> fst
                    |> step (FolderSelected photos)
                    |> step DeleteTandemRequested
                    |> step DeleteTandemCancelled

                Expect.equal answered.Screen (Working Triaging) "précondition : la question est tranchée (refusée)"

                let reopened = takeOff (FolderSelected photos) answered

                Expect.stringContains
                    (fst reopened).Status
                    "Analyse du dossier"
                    "la sélection de dossier fonctionne de nouveau après la réponse"

                Expect.isFalse
                    (List.isEmpty (List.ofSeq (snd reopened)))
                    "une nouvelle analyse part après la réponse")
        }
    ]
