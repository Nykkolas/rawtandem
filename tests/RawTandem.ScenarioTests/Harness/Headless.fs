module Harness.Headless

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

/// Point d'entrée de la session headless : un type avec BuildAvaloniaApp,
/// pour piloter le builder nous-mêmes. UseHeadlessDrawing = false : le mode
/// stub (vrai par défaut) rend du noir — Skia rend de vrais pixels.
type TestAppBuilder() =
    static member BuildAvaloniaApp() : AppBuilder =
        AppBuilder
            .Configure<App.App>()
            .UseSkia()
            .UseHeadless(AvaloniaHeadlessPlatformOptions(ShouldRenderOnUIThread = true, UseHeadlessDrawing = false))

/// Session headless unique du projet de tests (thread UI dédié qui pompe,
/// jamais disposée : le processus de test la vit jusqu'au bout).
/// - son initialisation enregistre Skia dans le processus : le décodage des
///   JPG dans `loadCurrentJpgCmd` fonctionne depuis n'importe quel thread —
///   sans elle, il lève (« No runtime platform services configured ») et
///   aucune photo ne se charge jamais ;
/// - les tests de rendu (RC-04) y dispatchent leurs actions sur le thread UI
///   (sinon, deadlock : le dispatcher ne pompe pas depuis les threads de
///   test).
/// Créée à l'initialisation de ce module (découverte des tests, avant tout
/// test), comme le faisait l'ancien SetupWithoutStarting.
let internal headlessSession =
    // Isolation PerAssembly (partagée) : les services Avalonia restent
    // enregistrés pour tout le processus. L'isolation PerTest (défaut) les
    // jetterait dans un scope de locator après chaque Dispatch — le décodage
    // des scénarios ne trouverait plus Skia.
    HeadlessUnitTestSession.StartNew(typeof<TestAppBuilder>, AvaloniaTestIsolationLevel.PerAssembly)

// L'initialisation d'Avalonia est différée au premier Dispatch
// (EnsureSharedApplication) : on la force tout de suite, sinon les scénarios
// qui décodent des JPG sans passer par la session tourneraient sans Skia
// enregistré (« Unable to locate IPlatformRenderInterface »).
headlessSession.Dispatch(Action ignore, CancellationToken.None).Wait()
