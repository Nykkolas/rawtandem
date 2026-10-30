module Harness.Keyboard

open System
open System.Collections.Concurrent
open System.Threading
open Avalonia.Controls
open Avalonia.FuncUI.Hosts
open Avalonia.Headless
open RawTandem.UI.State
open Harness.Headless

/// L'application réelle en fenêtre headless : la vraie vue (`Main.view`)
/// rendue dans une vraie `Window`, la vraie subscription clavier
/// (`Main.keyboardSubscriptions` — route tunnel, celle du fix du 2026-10-26)
/// attachée à cette fenêtre. Le branchement testé est celui de l'application,
/// pas une copie du harnais.
///
/// Le test garde le rôle du runtime : `press` envoie une **vraie touche**
/// (`HeadlessWindowExtensions.KeyPress`) et rend les messages dispatchés dans
/// l'ordre d'arrivée — le test les traite ensuite via `step` (update +
/// cascade), comme tous les scénarios ; `refresh` remet la vue à jour pour
/// le nouvel état (le rôle du Program Elmish réel).
///
/// Non couvert, comme toujours : le sélecteur natif de dossier, le focus OS
/// réel d'un bureau.
type internal KeyboardWindow =
    { /// Presse une vraie touche sur la fenêtre ; rend les messages
      /// dispatchés par la subscription clavier, dans l'ordre.
      Press: Avalonia.Input.Key -> Msg list
      /// Remet la vue rendue à jour pour l'état donné.
      Refresh: State -> unit }

/// Ouvre la fenêtre clavier headless pour l'état initial, joue le corps,
/// puis referme tout (subscription, fenêtre) — le style `withPhotos` du
/// harnais. Tout ce qui touche aux contrôles vit sur le thread UI de la
/// session headless, création du HostControl comprise (comme `renderPhoto` :
/// un contrôle créé hors de ce thread fait échouer `VerifyAccess`).
let internal withKeyboardWindow (initialState: State) (body: KeyboardWindow -> unit) : unit =
    let dispatched = ConcurrentQueue<Msg>()

    let drain () =
        let msgs = ResizeArray()
        let mutable m = Unchecked.defaultof<Msg>

        while dispatched.TryDequeue(&m) do
            msgs.Add m

        List.ofSeq msgs

    let hostRef = ref Unchecked.defaultof<HostControl>
    let windowRef = ref Unchecked.defaultof<Window>
    let disposables = ref []

    let onUi (action: Action) = headlessSession.Dispatch(action, CancellationToken.None).Wait()

    onUi (
        Action(fun () ->
            let host = HostControl()
            (host :> IViewHost).Update(Some(Main.view initialState ignore))

            let window = Window(Content = host)
            window.Show()
            window.Focus() |> ignore

            hostRef := host
            windowRef := window

            // La vraie subscription, attachée à la vraie fenêtre.
            disposables :=
                Main.keyboardSubscriptions window initialState
                |> List.map (fun (_, start) -> start dispatched.Enqueue))
    )

    let press (key: Avalonia.Input.Key) : Msg list =
        onUi (
            Action(fun () ->
                let w = windowRef.Value

                w.KeyPress(
                    key,
                    Unchecked.defaultof<Avalonia.Input.RawInputModifiers>,
                    Avalonia.Input.PhysicalKey.None,
                    null
                ))
        )

        drain ()

    let refresh (state: State) =
        onUi (Action(fun () -> (hostRef.Value :> IViewHost).Update(Some(Main.view state ignore))))

    body { Press = press; Refresh = refresh }

    onUi (
        Action(fun () ->
            !disposables |> List.iter (fun (d: IDisposable) -> d.Dispose())
            windowRef.Value.Close())
    )
