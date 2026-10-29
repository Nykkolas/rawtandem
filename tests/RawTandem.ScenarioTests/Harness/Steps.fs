module Harness.Steps

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

/// Exécute les commandes que `update` planifie — le rôle du runtime Elmish —
/// et renvoie les messages qu'elles dispatchent, cascade comprise.
///
/// Réalité vérifiée (Elmish 5.0.2) : `Cmd.OfAsync.either` exécute le workflow
/// sur le pool de threads — même sans point de suspension, le dispatch arrive
/// après le retour du Sub. Chaque Sub de ce projet dispatche exactement un
/// message (la branche succès ou erreur de `OfAsync.either`) ; runSubs attend
/// donc chaque message avec un timeout, au lieu de le perdre.
let private runSubs (cmd: Cmd<Msg>) : Msg list =
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

/// Cascade : chaque message produit peut en recommander (ex. après une
/// suppression, le JPG courant est rechargé). Boucle bornée de sécurité.
let private runCascade (s: State) (pending0: Msg list) (fuel0: int) : State =
    let rec go s pending fuel =
        match pending, fuel with
        | [], _ -> s
        | m :: _, 0 -> failwithf "Cascade de commandes sans fin (dernier message : %A)" m
        | m :: rest, fuel ->
            let s', cmd' = update m s
            go s' (rest @ runSubs cmd') (fuel - 1)

    go s pending0 fuel0

/// Pas de scénario : traite un message utilisateur, puis exécute les
/// commandes que `update` planifie et traite leurs messages, cascade comprise.
let internal step (msg: Msg) (state: State) : State =
    let state', cmd = update msg state
    runCascade state' (runSubs cmd) 50

// ========== RC-06 : pas « en vol » (docs/usecases/regles-communes.md) ==========
//
// `step` attend la fin de toutes les commandes : il ne peut jamais observer
// un état « pendant » une opération asynchrone. RC-06 interdit à l'utilisateur
// de lancer une nouvelle action tant que la précédente n'est pas terminée —
// pour jouer « l'utilisateur clique pendant que ça tourne », trois pas
// supplémentaires séparent le décollage de l'atterrissage.

/// Pas « décollé » : traite le geste utilisateur sans exécuter les commandes
/// qu'il planifie — l'opération asynchrone part, son résultat n'est pas
/// encore revenu. Rend l'état en vol et les commandes en attente.
let internal takeOff (msg: Msg) (state: State) : State * Cmd<Msg> = update msg state

/// Atterrit un vol : exécute les commandes en attente, traite leurs messages
/// et leur cascade — l'opération est terminée, la main est rendue.
let internal landFlight (flight: State * Cmd<Msg>) : State =
    runCascade (fst flight) (runSubs (snd flight)) 50

/// Atterrit une seule vague d'un vol : les commandes s'exécutent, leurs
/// messages sont traités, mais les commandes que ceux-ci planifient restent
/// en attente — le moment intermédiaire de RC-06 (ex. le rangement est fait,
/// la réanalyse qu'il déclenche tourne encore : l'utilisateur attend dans la
/// vue tri). Rend l'état intermédiaire et les commandes encore en vol.
let internal landOneWave (flight: State * Cmd<Msg>) : State * Cmd<Msg> =
    let rec go s pending acc =
        match pending with
        | [] -> s, acc
        | m :: rest ->
            let s', c' = update m s
            go s' rest (acc @ c')

    go (fst flight) (runSubs (snd flight)) Cmd.none
