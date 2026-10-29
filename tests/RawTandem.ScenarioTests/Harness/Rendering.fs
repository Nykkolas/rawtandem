module Harness.Rendering

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
open Harness.Headless

/// Boîte englobante (largeur, hauteur) en pixels de la photo rendue à
/// Photo rendue par la vraie vue : dimensions de la zone photo à l'écran et
/// position relative (0..1) du centre du quart rouge de la fixture. L'aspect
/// seul ne prouverait que « un quart de tour a été appliqué » ; le motif
/// orienté prouve aussi que c'est le **bon** sens (et verrouille l'identité :
/// pas de rotation du tout sans EXIF).
type internal RenderedPhoto =
    { Width: float
      Height: float
      RedCenterX: float
      RedCenterY: float }

/// Rend la vraie vue de l'application (Main.view) pour l'état donné et
/// analyse les pixels de la photo de fixture affichée : zone occupée à
/// l'écran et position du quart rouge (Firebrick 178,34,34) dans la photo
/// (reste RoyalBlue 65,105,225 ; tolérance pour la compression JPEG et les
/// arrondis de rendu). La vue est matérialisée par HostControl (FuncUI, sans
/// fenêtre ni Program Elmish), rendue par Skia dans un RenderTargetBitmap
/// sur le thread UI de la session headless. None : aucune photo rendue.
let internal renderPhoto (state: State) : RenderedPhoto option =
    headlessSession
        .Dispatch(
            Func<RenderedPhoto option>(fun () ->
                let host = HostControl()
                (host :> IViewHost).Update(Some(Main.view state ignore))

                // HostControl est un ContentControl : sans thème, il n'a pas de
                // template et n'attache jamais son contenu à l'arbre visuel —
                // on rend donc directement la racine que le patcher a posée.
                let root = host.Content :?> Avalonia.Controls.Control
                root.Measure(Size(900.0, 640.0))
                root.Arrange(Rect(0.0, 0.0, 900.0, 640.0))

                use target = new RenderTargetBitmap(PixelSize(900, 640), Vector(96.0, 96.0))
                target.Render root

                let w, h = 900, 640
                let size = w * h * 4
                let ptr = Marshal.AllocHGlobal size
                target.CopyPixels(PixelRect(0, 0, w, h), ptr, size, w * 4)

                let buffer = Array.create size 0uy
                Marshal.Copy(ptr, buffer, 0, size)
                Marshal.FreeHGlobal ptr

                let mutable photoMinX, photoMinY, photoMaxX, photoMaxY = Int32.MaxValue, Int32.MaxValue, Int32.MinValue, Int32.MinValue
                let mutable redMinX, redMinY, redMaxX, redMaxY = Int32.MaxValue, Int32.MaxValue, Int32.MinValue, Int32.MinValue
                let mutable found = false

                for y = 0 to h - 1 do
                    for x = 0 to w - 1 do
                        let i = (y * w + x) * 4
                        // BGRA. Fixture : quart rouge Firebrick, reste RoyalBlue.
                        let b, g, r = int buffer.[i], int buffer.[i + 1], int buffer.[i + 2]
                        let isRed = abs (r - 178) <= 25 && abs (g - 34) <= 25 && abs (b - 34) <= 25
                        let isBlue = abs (r - 65) <= 25 && abs (g - 105) <= 25 && abs (b - 225) <= 25

                        if isRed then
                            found <- true
                            redMinX <- min redMinX x; redMaxX <- max redMaxX x
                            redMinY <- min redMinY y; redMaxY <- max redMaxY y

                        if isRed || isBlue then
                            photoMinX <- min photoMinX x; photoMaxX <- max photoMaxX x
                            photoMinY <- min photoMinY y; photoMaxY <- max photoMaxY y

                if found then
                    let photoWidth = float (photoMaxX - photoMinX + 1)
                    let photoHeight = float (photoMaxY - photoMinY + 1)

                    // Position relative du centre du quart rouge dans la photo.
                    Some
                        { Width = photoWidth
                          Height = photoHeight
                          RedCenterX = (float (redMinX + redMaxX) / 2.0 - float photoMinX) / photoWidth
                          RedCenterY = (float (redMinY + redMaxY) / 2.0 - float photoMinY) / photoHeight }
                else
                    None),
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()

// ========== RC-05 : bandeau jaune modal (vraie vue rendue) ==========

/// Boutons de la vraie vue rendue pour l'état donné : (libellé, activé).
/// Même matérialisation que renderPhoto (HostControl + Skia headless) : le
/// test de RC-05 s'appuie sur la vue réellement rendue, pas sur un recalcul.
/// Les boutons sont cherchés dans l'arbre **logique** : le ScrollViewer de la
/// page d'organisation est un contrôle templaté — sans thème, le hôte headless
/// n'attache jamais son contenu à l'arbre visuel (même piège que HostControl,
/// voir renderPhoto) ; l'arbre logique, lui, porte tous les contrôles et leurs
/// propriétés (IsEnabled compris). Les boutons à icône (plein écran) n'ont
/// pas de libellé texte — c'est leur état d'activation qui compte, le nom du
/// type sert d'identifiant.
let internal renderedButtons (state: State) : (string * bool) list =
    headlessSession
        .Dispatch(
            Func<(string * bool) list>(fun () ->
                let host = HostControl()
                (host :> IViewHost).Update(Some(Main.view state ignore))

                let root = host.Content :?> Control
                root.Measure(Size(900.0, 640.0))
                root.Arrange(Rect(0.0, 0.0, 900.0, 640.0))

                root.GetLogicalDescendants()
                |> Seq.choose (fun descendant ->
                    match descendant with
                    | :? Button as button -> Some(string button.Content, button.IsEnabled)
                    | _ -> None)
                |> List.ofSeq),
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()

/// État d'affichage minimal : la vue ne lit pas le disque, l'état suffit
/// (la machine est testée ailleurs — ici on teste le mapping sémantique →
/// pixels du bandeau modal, RC-05).
let internal displayState (screen: Screen) : State =
    { Screen = screen
      FolderPath = Some "/dossier-fixture-rawtandem-inexistant"
      ScanRequestId = 1
      Scan =
        { emptyScan with
            TandemsToOrganize = [ { Stem = "P1"; JpgFileName = "P1.JPG"; RawFileName = "P1.RW2" } ] }
      CurrentIndex = 0
      DisplayMode = Normal
      CurrentJpg = None
      InFlight = None
      Status = "" }
