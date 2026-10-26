module RawTandem.UI.ScenarioTests

open System
open System.IO
open System.Threading
open Expecto
open Elmish
open RawTandem.Core.Constants
open RawTandem.UI.State

/// Pas de scénario : traite un message utilisateur, puis exécute les
/// commandes que `update` planifie — le rôle du runtime Elmish — et renvoie
/// dans `update` les messages qu'elles dispatchent, cascade comprise.
///
/// Réalité vérifiée (Elmish 5.0.2) : `Cmd.OfAsync.either` exécute le workflow
/// sur le pool de threads — même sans point de suspension, le dispatch arrive
/// après le retour du Sub. Chaque Sub de ce projet dispatche exactement un
/// message (la branche succès ou erreur de `OfAsync.either`) ; step attend
/// donc chaque message avec un timeout, au lieu de le perdre.
let private step (msg: Msg) (state: State) : State =
    let runSubs (cmd: Cmd<Msg>) : Msg list =
        let subs = List.ofSeq cmd
        let subCount = List.length subs
        let dispatched = ResizeArray<Msg>()
        let allDispatched = new ManualResetEventSlim(false)

        let collect m =
            let isComplete =
                lock dispatched (fun () ->
                    dispatched.Add m
                    dispatched.Count >= subCount)

            if isComplete then allDispatched.Set()

        subs |> List.iter (fun sub -> sub collect)

        if subCount > 0 && not (allDispatched.Wait(TimeSpan.FromSeconds 5.0)) then
            failwithf "Commande sans retour : %d message(s) attendu(s), %d reçu(s)" subCount dispatched.Count

        List.ofSeq dispatched

    // Cascade : chaque message produit peut en recommander (ex. après une
    // suppression, le JPG courant est rechargé). Boucle bornée de sécurité.
    let rec run (s: State) (pending: Msg list) (fuel: int) =
        match pending, fuel with
        | [], _ -> s
        | m :: _, 0 -> failwithf "Cascade de commandes sans fin (dernier message : %A)" m
        | m :: rest, fuel ->
            let s', cmd' = update m s
            run s' (rest @ runSubs cmd') (fuel - 1)

    let state', cmd = update msg state
    run state' (runSubs cmd) 50

/// Préconditions du UC : dossier « Pellicule », 3 couples JPG+RW2 appariés à
/// la racine, aucun sous-répertoire RAW. Fichiers vides créés par le test :
/// aucune photo, seuls les noms appariés comptent. Le dossier est un déchet de
/// test recréé à chaque lancement, détruit en finally (pas des photos).
let private withPellicule (run: string -> unit) =
    let folder = Path.Combine(Path.GetTempPath(), "rawtandem-scenario-Pellicule")

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    for stem in [ "P1"; "P2"; "P3" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(folder, stem + ".RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Préconditions du UC trier-dossier-deja-organise : dossier « Photos »,
/// 3 couples JPG+RW2 déjà organisés — JPG à la racine, RW2 dans RAW/,
/// aucune anomalie. Fichiers vides créés par le test : aucune photo, seuls
/// les noms appariés comptent. Le dossier est un déchet de test recréé à
/// chaque lancement, détruit en finally (pas des photos).
let private withPhotos (run: string -> unit) =
    let folder = Path.Combine(Path.GetTempPath(), "rawtandem-scenario-Photos")

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    for stem in [ "P1"; "P2"; "P3" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(rawDir, stem + ".RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Scénario exécutable de docs/usecases/organiser-et-trier-dossier.md.
///
/// Le test ne joue que les gestes de l'utilisateur (les étapes « utilisateur »
/// du scénario primaire) ; les étapes « application » transitent par les vraies
/// commandes Elmish que `step` exécute à la place du runtime, cascade comprise.
[<Tests>]
let scenarioTests =
    testList "ScenarioTests" [

        test "UC organiser-et-trier-dossier : organize a fresh dump then delete one unwanted tandem" {
            withPellicule (fun pellicule ->
                let stems = [ "P1"; "P2"; "P3" ]

                // UC-1 : ouverture du dossier. Le sélecteur natif n'est pas
                // testé ; le vrai scanCmd part et son FolderScanned revient.
                let s2 = init () |> fst |> step (FolderSelected pellicule)

                // UC-2 : l'application propose l'organisation (3 couples).
                Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                Expect.equal s2.Scan.TandemsToOrganize.Length 3 "3 couples appariés à organiser"
                Expect.equal s2.Scan.OrganizedTandems.Length 0 "aucun couple déjà organisé"

                // UC-3 : l'utilisateur demande l'organisation.
                // UC-4 : l'application demande confirmation, 3 RW2 annoncés.
                let s4 = s2 |> step OrganizeRequested
                Expect.equal s4.Screen (Confirming(Organizing, OrganizeFolder(3, 0))) "UC-4 : 3 fichiers déplacés annoncés"

                // UC-5 : l'utilisateur confirme.
                // UC-6 : l'application crée RAW/ et déplace les RW2 (vrais déplacements).
                let s6 = s4 |> step OrganizeConfirmed
                Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"
                let rawDir = Path.Combine(pellicule, RawSubfolderName)
                for stem in stems do
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, stem + ".RW2"))) "le RW2 est rangé dans RAW/"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, stem + ".JPG"))) "le JPG reste à la racine"

                // UC-7 : navigation vers la deuxième photo. L'affichage du JPG
                // n'est pas testé : fichiers vides, le chargement réel échoue.
                let s7 = s6 |> step NextClicked
                Expect.equal s7.CurrentIndex 1 "photo courante : P2"

                // UC-8 : l'utilisateur demande la suppression.
                // UC-9 : l'application affiche le nom du fichier à supprimer.
                let s8 = s7 |> step DeleteTandemRequested
                Expect.equal s8.Screen (Confirming(Triaging, DeleteTandem "P2")) "UC-9 : le nom du fichier est affiché"

                // UC-10 : l'utilisateur confirme.
                // UC-11 : le couple part à la corbeille (vrai gio trash).
                let s10 = s8 |> step DeleteTandemConfirmed
                Expect.equal (triableTandems s10).Length 2 "le tandem supprimé quitte le tri"
                Expect.isFalse (File.Exists(Path.Combine(pellicule, "P2.JPG"))) "le JPG est parti à la corbeille"
                Expect.isFalse (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "le RW2 est parti à la corbeille"

                // UC-12 : une seule itération de la boucle — la sortie (« plus
                // de photo à supprimer ») est une décision de l'utilisateur,
                // pas un état observable de la machine.

                // Garantie minimale : les 6 fichiers créés sont tous comptés,
                // aucun n'est perdu ni écrasé.
                let present =
                    [ yield! Directory.EnumerateFiles(pellicule)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 4 "P1 et P3 restent des deux côtés, seuls les fichiers de P2 sont partis")
        }

        test "UC trier-dossier-deja-organise : triage an already organized folder directly" {
            withPhotos (fun photos ->
                let rawDir = Path.Combine(photos, RawSubfolderName)

                // UC-1 : ouverture du dossier. Le sélecteur natif n'est pas
                // testé ; le vrai scanCmd part et son FolderScanned revient.
                let s2 = init () |> fst |> step (FolderSelected photos)

                // UC-2 : l'application affiche directement le mode tri — le
                // dossier est déjà organisé, aucune anomalie : rien à proposer.
                Expect.equal s2.Screen (Working Triaging) "UC-2 : le tri est affiché directement, sans passer par l'organisation"
                Expect.equal s2.Scan.TandemsToOrganize.Length 0 "aucun couple à organiser"
                Expect.equal s2.Scan.OrganizedTandems.Length 3 "3 couples déjà organisés"

                // UC-3 : navigation vers la deuxième photo. UC-4 : l'affichage
                // du JPG n'est pas testé : fichiers vides, le chargement réel échoue.
                let s3 = s2 |> step NextClicked
                Expect.equal s3.CurrentIndex 1 "photo courante : P2"

                // UC-5 : l'utilisateur demande la suppression.
                // UC-6 : l'application affiche le nom du fichier à supprimer.
                let s5 = s3 |> step DeleteTandemRequested
                Expect.equal s5.Screen (Confirming(Triaging, DeleteTandem "P2")) "UC-6 : le nom du fichier est affiché"

                // UC-7 : l'utilisateur confirme.
                // UC-8 : le couple part à la corbeille (vrai gio trash).
                let s7 = s5 |> step DeleteTandemConfirmed
                Expect.equal (triableTandems s7).Length 2 "le tandem supprimé quitte le tri"
                Expect.isFalse (File.Exists(Path.Combine(photos, "P2.JPG"))) "le JPG est parti à la corbeille"
                Expect.isFalse (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "le RW2 est parti à la corbeille"

                // UC-9 : une seule itération de la boucle — la sortie (« plus
                // de photo à supprimer ») est une décision de l'utilisateur,
                // pas un état observable de la machine.

                // Garantie de succès : les photos non supprimées restent
                // intactes, chacune des deux côtés.
                let present =
                    [ yield! Directory.EnumerateFiles(photos)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 4 "P1 et P3 restent des deux côtés, seuls les fichiers de P2 sont partis")
        } ]
