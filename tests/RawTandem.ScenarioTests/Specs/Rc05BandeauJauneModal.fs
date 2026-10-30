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
open Avalonia.Input
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
open Harness.Keyboard

/// Règle RC-05 — docs/usecases/rc-05-bandeau-jaune-modal.md.
///
/// Coverage : générique — les boutons grisés pendant une question sont vérifiés
/// sur la vraie vue rendue (écran d'organisation, tri, retour après réponse) ;
/// la sélection de dossier pendant une question est refusée par la garde (filet
/// machine : le chemin utilisateur est fermé d'amont par RC-06, le picker ne
/// peut plus être ouvert pendant un vol — un message peut néanmoins arriver
/// d'une file ou d'un autre front sans fenêtre) ; les touches mappées pendant
/// une question sont jouées en VRAIES touches sur la vraie fenêtre et la vraie
/// vue (écrans de tri et d'organisation — le branchement clavier
/// Main.keyboardSubscriptions est couvert ici, pas rejoué).
///
/// Non couvert : le sélecteur natif de dossier, le focus OS réel d'un bureau.
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

        test "RC-05 common rule : organizing actions come back when no question is displayed" {
            // Santé du grisage : hors question, les actions ne doivent pas
            // rester grisées par erreur.
            let buttons = renderedButtons (displayState (Working Organizing))

            Expect.isTrue
                (buttons |> List.exists (fun (label, enabled) -> label.StartsWith "Ranger" && enabled))
                "Ranger les RAW est actif hors question"
        }

        /// RC-05, retour du grisage (tri) : hors question, les actions de la
        /// vue de tri ne doivent pas rester grisées par erreur — toutes
        /// réactivées. État réel (dossier de 3 photos organisées, photo
        /// courante P2) : la vue de tri n'existe qu'avec des photos triables
        /// et à P2 aucune action n'est désactivée pour une autre raison
        /// (flèche gauche active dès la deuxième photo).
        test "RC-05 common rule : triage actions come back when no question is displayed" {
            withPhotos (fun photos ->
                let s = init () |> fst |> step (FolderSelected photos) |> step NextClicked

                Expect.equal s.Screen (Working Triaging) "précondition : la vue de tri est affichée (P2), sans question"

                let buttons = renderedButtons s

                Expect.isFalse
                    (buttons |> List.exists (fun (_, enabled) -> not enabled))
                    "aucun bouton grisé : toutes les actions de tri sont actives"

                Expect.isTrue
                    (buttons |> List.exists (fun (label, enabled) -> label = "Supprimer le tandem" && enabled))
                    "Supprimer le tandem est actif hors question")
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

        // ---------- Le clavier pendant une question ----------

        /// RC-05, clavier : pendant la question de suppression, les touches
        /// mappées par l'application (flèches, Home/End, F/F11, Échap, Suppr
        /// redoublé, F5) n'ont aucun effet — ni l'écran, ni la photo courante,
        /// ni le statut ne bougent, aucune commande ne part, le disque reste
        /// intact. Vraies touches sur la vraie fenêtre et la vraie vue : le
        /// branchement clavier est couvert ici, pas rejoué ; la garde vit
        /// dans update, traitée par `step` (cascade comprise).
        test "RC-05 common rule : pressing mapped keys during a deletion question has no effect" {
            withPhotos (fun photos ->
                // Préparation réelle : dossier trié de 3 photos, fenêtre
                // headless vivante (vraie vue + vraie subscription clavier).
                let s0 = init () |> fst |> step (FolderSelected photos)

                Expect.equal s0.Screen (Working Triaging) "précondition : la vue de tri est affichée"

                withKeyboardWindow s0 (fun kb ->
                    // Preuve de harnais : une vraie flèche droite avance la
                    // photo courante (le branchement touche → message →
                    // update est vivant dans ce test)...
                    let s1 = kb.Press Key.Right |> List.fold (fun s m -> step m s) s0

                    Expect.equal s1.CurrentIndex 1 "la vraie flèche droite avance à P2"

                    // ...et Suppr ouvre la question RC-02 sur P2. On part de
                    // la photo 2 pour que « l'index n'a pas bougé » soit
                    // discriminant.
                    let confirming = kb.Press Key.Delete |> List.fold (fun s m -> step m s) s1

                    Expect.equal
                        confirming.Screen
                        (Confirming(Triaging, DeleteTandem "P2"))
                        "précondition : la question de suppression est affichée sur P2"

                    kb.Refresh confirming

                    // Le cœur : chaque touche mappée, une à une, depuis le
                    // même état Confirming — jamais l'une après l'autre,
                    // sinon un garde manquant masquerait le suivant.
                    let mappedKeys =
                        [ Key.Left
                          Key.Right
                          Key.Home
                          Key.End
                          Key.F
                          Key.F11
                          Key.Escape
                          Key.Delete
                          Key.F5 ]

                    for key in mappedKeys do
                        let attempt = kb.Press key |> List.fold (fun s m -> step m s) confirming

                        Expect.equal
                            attempt.Screen
                            confirming.Screen
                            (sprintf "%O : la question reste affichée, l'écran ne change pas" key)

                        Expect.equal
                            attempt.CurrentIndex
                            confirming.CurrentIndex
                            (sprintf "%O : la photo courante ne bouge pas" key)

                        Expect.equal
                            attempt.Status
                            confirming.Status
                            (sprintf "%O : aucun statut n'écrase la question" key)

                        Expect.equal attempt.Scan confirming.Scan (sprintf "%O : le scan ne bouge pas" key)

                        Expect.equal
                            attempt.FolderPath
                            confirming.FolderPath
                            (sprintf "%O : le dossier courant ne change pas" key)

                    // Le disque n'a pas bougé : le couple P2 est intact
                    // (aucun Suppr redoublé n'est parti à la corbeille).
                    Expect.isTrue
                        (File.Exists(Path.Combine(photos, "P2.JPG")))
                        "P2.JPG toujours là après toutes les touches"

                    Expect.isTrue
                        (File.Exists(Path.Combine(photos, "RAW", "P2.RW2")))
                        "P2.RW2 toujours là après toutes les touches"

                    // Le retour des raccourcis après la réponse vit dans son
                    // test dédié : « keyboard shortcuts come back … »
                    ))
        }

        /// RC-05, retour du clavier (tri) : la question tranchée, les
        /// raccourcis clavier de la vue de tri refonctionnent — vraies
        /// touches, dispatch et effet. Miroir du retour du grisage.
        test "RC-05 common rule : keyboard shortcuts come back once the deletion question is answered" {
            withPhotos (fun photos ->
                let s0 = init () |> fst |> step (FolderSelected photos)

                Expect.equal s0.Screen (Working Triaging) "précondition : la vue de tri est affichée"

                withKeyboardWindow s0 (fun kb ->
                    // Suppr ouvre la question sur P1, Annuler la tranche.
                    let confirming = kb.Press Key.Delete |> List.fold (fun s m -> step m s) s0

                    Expect.equal
                        confirming.Screen
                        (Confirming(Triaging, DeleteTandem "P1"))
                        "précondition : la question de suppression est affichée"

                    let answered = step DeleteTandemCancelled confirming

                    Expect.equal answered.Screen (Working Triaging) "la question est tranchée (refusée)"

                    kb.Refresh answered

                    // La flèche droite refonctionne : dispatch et effet.
                    let moved = kb.Press Key.Right |> List.fold (fun s m -> step m s) answered

                    Expect.equal
                        moved.CurrentIndex
                        1
                        "la vraie flèche droite avance à P2 (raccourci réactivé)"

                    // Et Suppr rouvre une question sur la photo courante :
                    // le raccourci destructif est actif de nouveau.
                    let askedAgain = kb.Press Key.Delete |> List.fold (fun s m -> step m s) moved

                    Expect.equal
                        askedAgain.Screen
                        (Confirming(Triaging, DeleteTandem "P2"))
                        "la vraie touche Suppr rouvre une question sur la photo courante (raccourci réactivé)"))
        }

        /// RC-05, clavier, écran d'organisation : pendant la question de
        /// rangement, les touches mappées (flèches, Home/End, F/F11, Échap,
        /// Suppr, F5/Réanalyser) n'ont aucun effet — la question reste
        /// entière, ni le statut ni le scan ne bougent, aucune commande ne
        /// part, et le disque reste intact (aucun rangement parti tout
        /// seul). Même modèle que la vue de tri : vraies touches sur la
        /// vraie fenêtre et la vraie vue, la garde dans update.
        test "RC-05 common rule : pressing mapped keys during an organizing question has no effect" {
            withPellicule (fun pellicule ->
                // Préparation réelle : 3 couples à organiser à la racine,
                // écran d'organisation, fenêtre headless vivante.
                let s0 = init () |> fst |> step (FolderSelected pellicule)

                Expect.equal s0.Screen (Working Organizing) "précondition : l'écran d'organisation est affiché"

                withKeyboardWindow s0 (fun kb ->
                    // Preuve de harnais : la vraie touche F5 dispatch
                    // Réanalyser — la seule touche agissante de cet écran,
                    // silencieuse par design (aucun observable ne bouge),
                    // c'est donc le message dispatché qui prouve que le
                    // branchement est vivant.
                    let proof = kb.Press Key.F5

                    Expect.equal proof [ RescanRequested ] "la vraie touche F5 dispatch Réanalyser (branchement vivant)"

                    let s1 = proof |> List.fold (fun s m -> step m s) s0

                    Expect.equal s1.Screen (Working Organizing) "la réanalyse reste sur l'écran d'organisation"

                    // La question de rangement s'ouvre par le bouton
                    // « Ranger les RAW » (pas de raccourci clavier pour
                    // l'ouvrir) : 3 RAW annoncés.
                    let confirming = step OrganizeRequested s1

                    Expect.equal
                        confirming.Screen
                        (Confirming(Organizing, OrganizeFolder 3))
                        "précondition : la question de rangement est affichée (3 RAW)"

                    kb.Refresh confirming

                    // Le cœur : chaque touche mappée, une à une, depuis le
                    // même état Confirming — jamais l'une après l'autre,
                    // sinon un garde manquant masquerait le suivant.
                    let mappedKeys =
                        [ Key.Left
                          Key.Right
                          Key.Home
                          Key.End
                          Key.F
                          Key.F11
                          Key.Escape
                          Key.Delete
                          Key.F5 ]

                    for key in mappedKeys do
                        let attempt = kb.Press key |> List.fold (fun s m -> step m s) confirming

                        Expect.equal
                            attempt.Screen
                            confirming.Screen
                            (sprintf "%O : la question reste affichée, l'écran ne change pas" key)

                        Expect.equal
                            attempt.Status
                            confirming.Status
                            (sprintf "%O : aucun statut n'écrase la question" key)

                        Expect.equal attempt.Scan confirming.Scan (sprintf "%O : le scan ne bouge pas" key)

                        Expect.equal
                            attempt.ScanRequestId
                            confirming.ScanRequestId
                            (sprintf "%O : aucune nouvelle demande de scan" key)

                        Expect.equal
                            attempt.FolderPath
                            confirming.FolderPath
                            (sprintf "%O : le dossier courant ne change pas" key)

                    // Le disque n'a pas bougé : aucun rangement parti tout
                    // seul — pas de dossier « RAW » créé, les 3 RAW toujours
                    // à la racine.
                    Expect.isFalse
                        (Directory.Exists(Path.Combine(pellicule, "RAW")))
                        "aucun dossier « RAW » n'a été créé pendant la question"

                    for stem in [ "P1"; "P2"; "P3" ] do
                        Expect.isTrue
                            (File.Exists(Path.Combine(pellicule, stem + ".RW2")))
                            (sprintf "%s.RW2 toujours à la racine" stem)

                    // Le retour des raccourcis après la réponse vit dans son
                    // test dédié : « keyboard shortcuts come back … »
                    ))
        }

        /// RC-05, retour du clavier (organisation) : la question tranchée,
        /// la vraie touche F5 dispatch de nouveau — pas de sur-blocage.
        /// Miroir du retour du grisage.
        test "RC-05 common rule : keyboard shortcuts come back once the organizing question is answered" {
            withPellicule (fun pellicule ->
                let s0 = init () |> fst |> step (FolderSelected pellicule)

                Expect.equal s0.Screen (Working Organizing) "précondition : l'écran d'organisation est affiché"

                let confirming = step OrganizeRequested s0

                Expect.equal
                    confirming.Screen
                    (Confirming(Organizing, OrganizeFolder 3))
                    "précondition : la question de rangement est affichée"

                let answered = step OrganizeCancelled confirming

                Expect.equal answered.Screen (Working Organizing) "la question est tranchée (refusée)"

                withKeyboardWindow answered (fun kb ->
                    let msgs = kb.Press Key.F5

                    Expect.equal
                        msgs
                        [ RescanRequested ]
                        "la vraie touche F5 dispatch Réanalyser après la réponse (raccourci réactivé)"))
        }
    ]
    
