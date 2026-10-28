module RawTandem.UI.ScenarioTests

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
let private headlessSession =
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
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Pellicule-%s" (Guid.NewGuid().ToString("N")))

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
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Photos-%s" (Guid.NewGuid().ToString("N")))

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

/// Préconditions de l'extension 2a : dossier « Pellicule » avec des problèmes
/// d'organisation — 2 bons couples à organiser (P1, P2), 1 couple inversé
/// (P3 : JPG et RW2 tous les deux dans RAW/), 1 doublon (P4 : couple organisé
/// + un RW2 en trop à la racine) et 1 JPG orphelin à la racine (P5). Fichiers
/// vides créés par le test, dossier détruit en finally (pas des photos).
let private withPelliculeAProblemes (run: string -> unit) =
    let folder =
        Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Pellicule-2a-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore
    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    for stem in [ "P1"; "P2" ] do
        File.WriteAllText(Path.Combine(folder, stem + ".JPG"), "")
        File.WriteAllText(Path.Combine(folder, stem + ".RW2"), "")

    // P3 : couple inversé — les deux fichiers dans RAW/.
    File.WriteAllText(Path.Combine(rawDir, "P3.JPG"), "")
    File.WriteAllText(Path.Combine(rawDir, "P3.RW2"), "")

    // P4 : couple organisé + doublon (le RW2 hors de sa place à la racine).
    File.WriteAllText(Path.Combine(folder, "P4.JPG"), "")
    File.WriteAllText(Path.Combine(folder, "P4.RW2"), "")
    File.WriteAllText(Path.Combine(rawDir, "P4.RW2"), "")

    // P5 : JPG orphelin — signalé, jamais déplacé par l'application.
    File.WriteAllText(Path.Combine(folder, "P5.JPG"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Indique si le filesystem du dossier temporaire distingue la casse des
/// noms. Sonde de capacité réelle, pas test d'OS (règle AGENTS/ERREURS-
/// GENERIC) — même sonde que `RawTandem.Core.Tests/TestHelpers.fs`,
/// dupliquée ici car les projets de tests ne se référencent pas.
let private isCaseSensitiveFileSystem () =
    let folder = Path.Combine(Path.GetTempPath(), "rawtandem-scenario-case-probe", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    let probe = Path.Combine(folder, "sonde.tmp")
    File.WriteAllText(probe, "1")
    File.WriteAllText(Path.Combine(folder, "SONDE.tmp"), "2")
    let sensitive = File.ReadAllText probe = "1"
    Directory.Delete(folder, true)
    sensitive

/// Scénario joué uniquement si le filesystem distingue la casse. L'état « raw »
/// distinct de « RAW » (UC organiser-et-trier-dossier 2b) est impossible à
/// créer sur un FS insensible (NTFS fusionne les deux noms : le scan lit
/// « raw » comme « RAW », l'anomalie n'existe pas) — le scénario y serait
/// rouge pour une raison erronée : il est ignoré (compté « ignoré », jamais
/// silencieusement absent ni silencieusement vert).
let private testScenarioCaseSensitiveOnly (name: string) (scenario: unit -> unit) : Expecto.Test =
    if isCaseSensitiveFileSystem () then
        Expecto.Tests.testCase name scenario
    else
        Expecto.Tests.ptestCase name scenario

/// Préconditions de l'extension 2b : dossier avec un tandem à organiser (P1)
/// et un répertoire « raw » en minuscules contenant un RW2 (P2) — invisible au
/// scan, qui ne lit que « RAW ». Fichiers vides, dossier détruit en finally.
let private withDossierRawMalCassee (run: string -> unit) =
    let folder =
        Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-raw-2b-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore
    File.WriteAllText(Path.Combine(folder, "P1.JPG"), "")
    File.WriteAllText(Path.Combine(folder, "P1.RW2"), "")

    Directory.CreateDirectory(Path.Combine(folder, "raw")) |> ignore
    File.WriteAllText(Path.Combine(folder, "raw", "P2.RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Segment APP1 « Exif » minimal ne portant que le tag Orientation (RC-04),
/// inséré dans un JPEG encodé : en-tête "Exif\0\0", TIFF petit-boutiste (II),
/// IFD d'une entrée (tag 0x0112, type SHORT, count 1, valeur `value`).
let private exifOrientationSegment (value: byte) : byte[] =
    let payload =
        // "Exif\0\0" : en F#, \0 n'est pas une séquence d'échappement (il
        // deviendrait backslash + '0' littéraux) — octets explicites.
        [| yield! [| 0x45uy; 0x78uy; 0x69uy; 0x66uy; 0x00uy; 0x00uy |]
           yield! [| 0x49uy; 0x49uy |] // II (petit-boutiste)
           yield! [| 0x2Auy; 0x00uy |] // marqueur TIFF 42
           yield! [| 0x08uy; 0x00uy; 0x00uy; 0x00uy |] // IFD0 à l'octet 8
           yield! [| 0x01uy; 0x00uy |] // une entrée
           yield! [| 0x12uy; 0x01uy |] // tag 0x0112 Orientation
           yield! [| 0x03uy; 0x00uy |] // type SHORT
           yield! [| 0x01uy; 0x00uy; 0x00uy; 0x00uy |] // count 1
           yield! [| value; 0x00uy; 0x00uy; 0x00uy |] |] // valeur, alignée à gauche

    let nextIfdOffset = [| 0x00uy; 0x00uy; 0x00uy; 0x00uy |] // pas d'IFD suivante
    let body = Array.append payload nextIfdOffset
    let length = 2 + body.Length // le champ longueur se compte lui-même

    Array.append [| 0xFFuy; 0xE1uy; byte (length >>> 8); byte (length &&& 0xFF) |] body

/// Position d'insertion du segment APP1 : après le SOI et l'éventuel APP0
/// (JFIF) — l'ordre APP0 puis APP1 est la convention EXIF 2.2.
let private app1InsertPosition (jpg: byte[]) =
    if jpg.Length > 5 && jpg.[2] = 0xFFuy && jpg.[3] = 0xE0uy then
        2 + 2 + (int jpg.[4] * 256 + int jpg.[5]) // fin du segment APP0
    else
        2 // juste après le SOI

/// Fixture RC-04 : un JPG **paysage** (320×240, largeur > hauteur) à motif
/// orienté, encodé par Skia, accompagné ou non du tag EXIF Orientation. Le
/// quart haut-gauche est rouge (Firebrick), le reste bleu (RoyalBlue) : seule
/// la métadonnée dit comment afficher la photo — exactement le fichier que
/// produit un appareil photo tenu à la verticale (tag 6 : le capteur stocke
/// en paysage, l'EXIF dit « à afficher en portrait, quart de tour horaire »).
/// Le motif rend le **sens** de la rotation observable dans les pixels : une
/// simple comparaison hauteur/largeur ne distinguerait pas une rotation juste
/// d'une rotation à l'envers, ni ne verrait jamais un miroir.
let private landscapeJpeg (exifOrientation: byte option) : byte[] =
    let info = SKImageInfo(320, 240, SKColorType.Bgra8888)
    use surface = SKSurface.Create(info)
    let canvas = surface.Canvas
    canvas.Clear(SKColors.RoyalBlue)

    use paint = new SKPaint(Color = SKColors.Firebrick)
    canvas.DrawRect(SKRect.Create(0.0f, 0.0f, 160.0f, 120.0f), paint)

    use image = surface.Snapshot()
    use data = image.Encode(SKEncodedImageFormat.Jpeg, 90)
    let jpg = data.ToArray()

    match exifOrientation with
    | None -> jpg
    | Some value ->
        let at = app1InsertPosition jpg
        let segment = exifOrientationSegment value
        Array.concat [ jpg.[0 .. at - 1]; segment; jpg.[at ..] ]

/// Préconditions de la règle RC-04 : dossier « Verticales » organisé (JPG à la
/// racine, RW2 dans RAW/), 2 couples. P1 : JPG paysage sans EXIF. P2 : JPG
/// paysage avec EXIF Orientation 6 (photo « prise à la verticale »). Déchets
/// de test recréés à chaque lancement, détruits en finally.
let private withVerticales (run: string -> unit) =
    // Dossier unique : Expecto exécute les tests en parallèle, deux scénarios
    // ne doivent pas partager leurs déchets.
    let folder = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-scenario-Verticales-%s" (Guid.NewGuid().ToString("N")))

    if Directory.Exists folder then Directory.Delete(folder, true)

    Directory.CreateDirectory folder |> ignore

    let rawDir = Path.Combine(folder, RawSubfolderName)
    Directory.CreateDirectory rawDir |> ignore

    File.WriteAllBytes(Path.Combine(folder, "P1.JPG"), landscapeJpeg None)
    File.WriteAllBytes(Path.Combine(folder, "P2.JPG"), landscapeJpeg (Some 6uy))
    File.WriteAllText(Path.Combine(rawDir, "P1.RW2"), "")
    File.WriteAllText(Path.Combine(rawDir, "P2.RW2"), "")

    try
        run folder
    finally
        Directory.Delete(folder, true)

/// Boîte englobante (largeur, hauteur) en pixels de la photo rendue à
/// Photo rendue par la vraie vue : dimensions de la zone photo à l'écran et
/// position relative (0..1) du centre du quart rouge de la fixture. L'aspect
/// seul ne prouverait que « un quart de tour a été appliqué » ; le motif
/// orienté prouve aussi que c'est le **bon** sens (et verrouille l'identité :
/// pas de rotation du tout sans EXIF).
type private RenderedPhoto =
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
let private renderPhoto (state: State) : RenderedPhoto option =
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
let private renderedButtons (state: State) : (string * bool) list =
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
let private displayState (screen: Screen) : State =
    { Screen = screen
      FolderPath = Some "/dossier-fixture-rawtandem-inexistant"
      ScanRequestId = 1
      Scan =
        { emptyScan with
            TandemsToOrganize = [ { Stem = "P1"; JpgFileName = "P1.JPG"; RawFileName = "P1.RW2" } ] }
      CurrentIndex = 0
      DisplayMode = Normal
      CurrentJpg = None
      Status = "" }

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

                // UC-7 : navigation vers la deuxième photo. Le rendu visuel du
                // JPG n'est pas testé : fichiers vides, le chargement réel échoue.
                let s7 = s6 |> step NextClicked
                Expect.equal s7.CurrentIndex 1 "photo courante : P2"

                // UC-8 : l'application affiche la photo en cours et le nom du
                // fichier JPG (XXXX.JPG) — le nom exposé par l'état est testé,
                // le rendu (TextBlock) ne l'est pas.
                Expect.equal (currentJpgFileName s7) (Some "P2.JPG") "UC-8 : le nom du fichier JPG courant est affiché"

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

                // UC-3 : navigation vers la deuxième photo. Le rendu visuel du
                // JPG n'est pas testé : fichiers vides, le chargement réel échoue.
                let s3 = s2 |> step NextClicked
                Expect.equal s3.CurrentIndex 1 "photo courante : P2"

                // UC-4 : l'application affiche la photo en cours et le nom du
                // fichier JPG (XXXX.JPG) — le nom exposé par l'état est testé,
                // le rendu (TextBlock) ne l'est pas.
                Expect.equal (currentJpgFileName s3) (Some "P2.JPG") "UC-4 : le nom du fichier JPG courant est affiché"

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
        }

        /// Scénario exécutable de docs/usecases/organiser-et-trier-dossier.md,
        /// extension 2a. Un seul dossier « mélange » : le flux est prouvé
        /// (lister → corriger à la main → réanalyser → interface à jour →
        /// reprise du primaire), la matrice des catégories de problèmes reste
        /// en tests unitaires (FolderScanTests).
        test "UC organiser-et-trier-dossier 2a : fix problems manually, reanalyze, then organize" {
            withPelliculeAProblemes (fun pellicule ->
                let rawDir = Path.Combine(pellicule, RawSubfolderName)

                // 2a-1 : ouverture du dossier — l'application propose
                // l'organisation et liste les problèmes d'organisation.
                let s2 = init () |> fst |> step (FolderSelected pellicule)

                Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                Expect.equal s2.Scan.TandemsToOrganize.Length 2 "2 couples à organiser (P1, P2)"
                Expect.equal s2.Scan.OrganizedTandems.Length 1 "1 couple déjà organisé (P4)"
                Expect.equal s2.Scan.InvertedTandems.Length 1 "problème listé : P3 inversé"
                Expect.equal s2.Scan.DuplicateRaws.Length 1 "problème listé : doublon de P4"
                Expect.equal s2.Scan.OrphanJpgs.Length 1 "problème listé : P5 orphelin"

                // 2a-2 : correction manuelle dans l'explorateur (hors de
                // l'application) : le JPG de P3 revient à la racine (le couple
                // inversé devient un couple organisé), le doublon de P4 part.
                // P5 n'est pas corrigé : l'utilisateur est satisfait (2a-5) —
                // il organisera malgré le signal.
                File.Move(Path.Combine(rawDir, "P3.JPG"), Path.Combine(pellicule, "P3.JPG"))
                File.Delete(Path.Combine(pellicule, "P4.RW2"))

                // 2a-3 : l'utilisateur réanalyse le dossier.
                // 2a-4 : l'interface se met entièrement à jour (comptes du scan).
                let s4 = s2 |> step RescanRequested

                Expect.equal s4.Scan.TandemsToOrganize.Length 2 "toujours 2 couples à organiser (P1, P2)"
                Expect.equal s4.Scan.OrganizedTandems.Length 2 "P3 corrigé devient un couple organisé"
                Expect.equal s4.Scan.InvertedTandems.Length 0 "plus de couple inversé"
                Expect.equal s4.Scan.DuplicateRaws.Length 0 "plus de doublon"
                Expect.equal s4.Scan.OrphanJpgs.Length 1 "P5 reste signalé, l'utilisateur a choisi de le garder"

                // Seuls les couples correctement appariés s'affichent au
                // tri : P3 devenu apparié y entre, l'orphelin P5 n'y entre
                // jamais (signalé, pas géré).
                Expect.equal
                    (s4 |> triableTandems |> List.map (fun t -> t.Stem))
                    [ "P1"; "P2"; "P3"; "P4" ]
                    "la liste de tri ne montre que les couples appariés, jamais P5"

                // 2a-5 : l'utilisateur est satisfait, le scénario principal
                // reprend — il organise (vrais déplacements sur le disque).
                let s5 = s4 |> step OrganizeRequested
                Expect.equal s5.Screen (Confirming(Organizing, OrganizeFolder(2, 0))) "UC-4 : 2 RW2 appariés annoncés"

                let s6 = s5 |> step OrganizeConfirmed
                Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"

                // Le rangement remet le scan à jour (bug du scan périmé,
                // 2026-10-24, corrigé par le rescan en cascade) : les 4
                // couples sont organisés et leurs RW2 sont cherchés dans
                // RAW/ — un plan de suppression ne peut plus viser la racine.
                Expect.equal s6.Scan.TandemsToOrganize.Length 0 "plus rien à organiser"
                Expect.equal s6.Scan.OrganizedTandems.Length 4 "les 4 couples sont organisés"

                for stem in [ "P1"; "P2"; "P3"; "P4" ] do
                    match findTriableTandem s6.Scan stem with
                    | Some(_, InRawSubfolder) -> ()
                    | _ -> failtestf "le RW2 de %s doit être cherché dans RAW/ après le rangement" stem

                Expect.equal
                    (s6 |> triableTandems |> List.map (fun t -> t.Stem))
                    [ "P1"; "P2"; "P3"; "P4" ]
                    "la liste de tri montre les 4 couples appariés rangés"

                for stem in [ "P1"; "P2" ] do
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, stem + ".RW2"))) "le RW2 du couple apparié est rangé"

                for stem in [ "P1"; "P2"; "P3"; "P4" ] do
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, stem + ".JPG"))) "le JPG reste à la racine"

                // Signal-only : l'orphelin P5 n'est jamais touché par
                // l'application — il reste exactement où il est.
                Expect.isTrue (File.Exists(Path.Combine(pellicule, "P5.JPG"))) "l'orphelin signalé n'est pas déplacé"

                // Garantie minimale : 9 fichiers présents après correction (le
                // doublon supprimé par l'utilisateur en moins des 10 créés),
                // aucun n'est perdu ni écrasé par l'application.
                let present =
                    [ yield! Directory.EnumerateFiles(pellicule)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 9 "aucun fichier perdu ni écrasé")
        }

        /// Scénario exécutable de docs/usecases/organiser-et-trier-dossier.md,
        /// extension 2b : le répertoire « raw » mal cassé bloque le rangement
        /// (refus gardé dans update, déjà testé en unitaire — le scénario ne
        /// peut pas cliquer un bouton grisé) ; correction manuelle, réanalyse,
        /// retour au primaire.
        testScenarioCaseSensitiveOnly
            "UC organiser-et-trier-dossier 2b : wrong-cased raw folder blocks organize until fixed and reanalyzed"
            (fun () ->
                withDossierRawMalCassee (fun pellicule ->
                    let rawDir = Path.Combine(pellicule, RawSubfolderName)
    
                    // 2b-1 : ouverture — l'anomalie est signalée (même panneau que
                    // 2a-1) et le contenu de « raw » est invisible au scan.
                    let s2 = init () |> fst |> step (FolderSelected pellicule)
    
                    Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                    Expect.equal s2.Scan.TandemsToOrganize.Length 1 "1 couple à organiser (P1)"
                    Expect.equal s2.Scan.RawFolderIssue (Some(RawFolderCaseMismatch "raw")) "l'anomalie du dossier « raw » est signalée"
                    Expect.stringContains s2.Status "mal cass" "le signal est visible dans le bandeau"
    
                    // 2b-1 : « l'application ne permet pas de ranger ». Le bouton
                    // est grisé (affordance, non observable ici) : on vérifie le
                    // filet de sécurité — même si le message de rangement arrive
                    // (futur raccourci clavier), la machine refuse, et le disque
                    // ne bouge pas. Garde aussi couverte en unitaire ; rejouée
                    // ici parce que le refus est une garantie de l'extension.
                    let sRefusal = s2 |> step OrganizeRequested
    
                    Expect.equal sRefusal.Screen (Working Organizing) "le rangement est refusé : aucune confirmation ouverte"
                    Expect.stringContains sRefusal.Status "renommez" "le refus explique quoi faire"
                    Expect.isFalse (Directory.Exists rawDir) "aucun dossier « RAW » n'est créé pendant l'anomalie"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, "P1.RW2"))) "aucun fichier déplacé pendant l'anomalie"
    
                    // 2b-2 : correction manuelle dans l'explorateur : « raw »
                    // redevient « RAW ».
                    Directory.Move(Path.Combine(pellicule, "raw"), rawDir)
    
                    // 2b-3 : l'utilisateur réanalyse.
                    // 2b-4 : retour à l'étape 2 du primaire — l'anomalie a
                    // disparu et le contenu du dossier renommé devient visible.
                    let s4 = sRefusal |> step RescanRequested
    
                    Expect.equal s4.Scan.RawFolderIssue None "l'anomalie a disparu après correction et réanalyse"
                    Expect.equal s4.Scan.TandemsToOrganize.Length 1 "le couple P1 est toujours proposé"
                    Expect.equal s4.Scan.FiledOrphanRaws.Length 1 "le RW2 de « raw » est maintenant visible (orphelin rangé)"
    
                    // « Le bouton Ranger est de nouveau accessible » : l'état
                    // grisé d'un bouton n'est pas observable ici (sémantique de
                    // vue), la preuve fonctionnelle est le geste joué — la
                    // demande de rangement aboutit désormais à la confirmation
                    // (elle était refusée s2 → sRefusal, elle passe s4 → s5).
                    let s5 = s4 |> step OrganizeRequested
                    Expect.equal s5.Screen (Confirming(Organizing, OrganizeFolder(1, 0))) "UC-4 : le rangement redevient possible, 1 RW2 apparié annoncé"
    
                    let s6 = s5 |> step OrganizeConfirmed
                    Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"
    
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, "P1.RW2"))) "le RW2 du couple apparié est rangé"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, "P1.JPG"))) "le JPG reste à la racine"
    
                    // Signal-only : l'orphelin rangé P2, devenu visible à la
                    // réanalyse, n'est pas touché par le rangement.
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "l'orphelin rangé n'est pas déplacé"
    
                    // Garantie minimale : les 3 fichiers créés sont tous comptés.
                    let present =
                        [ yield! Directory.EnumerateFiles(pellicule)
                          yield! Directory.EnumerateFiles(rawDir) ]
                    Expect.equal present.Length 3 "aucun fichier perdu ni écrasé"))

        /// Scénario exécutable de docs/usecases/regles-communes.md, règle RC-01.
        /// Coverage générique : un seul test pour toutes les vues de tri.
        ///
        /// Le test ne joue que les gestes de l'utilisateur. Le mapping clavier
        /// → message vit dans la vue (Main.fs, KeyDown) : la touche et le bouton
        /// dispatchent le même message (règle RC-01 « identiques selon le
        /// mapping »). Ce branchement vue n'est pas testable en headless —
        /// non couvert, comme le sélecteur natif de dossier.
        test "RC-01 common rule : keyboard interactions in the triaging view" {
            withPhotos (fun photos ->
                // Précondition RC-01 : une vue de tri (dossier organisé, 3 photos).
                let s = init () |> fst |> step (FolderSelected photos)
                Expect.equal s.Screen (Working Triaging) "la vue de tri est affichée"

                // RC-01 : flèche gauche sur la première photo → arrêt.
                let sLeft = s |> step PreviousClicked
                Expect.equal sLeft.CurrentIndex 0 "arrêt à la première photo"

                // RC-01 : flèche droite jusqu'à la dernière → arrêt.
                let s1 = s |> step NextClicked
                Expect.equal s1.CurrentIndex 1 "flèche droite : photo courante P2"
                let s2 = s1 |> step NextClicked
                Expect.equal s2.CurrentIndex 2 "flèche droite : photo courante P3"
                let s3 = s2 |> step NextClicked
                Expect.equal s3.CurrentIndex 2 "arrêt à la dernière photo"

                // RC-01 : Home → première photo, End → dernière photo.
                let sHome = s3 |> step FirstPhotoRequested
                Expect.equal sHome.CurrentIndex 0 "Home : photo courante P1"
                Expect.equal (currentJpgFileName sHome) (Some "P1.JPG") "RC-01 observable effect : le nom affiché suit la sélection"
                let sEnd = sHome |> step LastPhotoRequested
                Expect.equal sEnd.CurrentIndex 2 "End : photo courante P3"
                Expect.equal (currentJpgFileName sEnd) (Some "P3.JPG") "RC-01 observable effect : le nom affiché suit la sélection"

                // RC-01 : Suppr → l'écran de confirmation RC-02 s'affiche.
                let sDel = sEnd |> step DeleteTandemRequested
                Expect.equal sDel.Screen (Confirming(Triaging, DeleteTandem "P3")) "Suppr : la confirmation RC-02 s'affiche pour la photo courante")
        }

        /// Scénario exécutable de docs/usecases/regles-communes.md, règle RC-01
        /// (plein écran). Coverage générique, suite du test clavier ci-dessus.
        ///
        /// Le test ne joue que les gestes. F, F11, le double-clic et le bouton
        /// d'icône sont le même geste (mapping RC-01) : un seul message. Le
        /// branchement vue (touches réelles, double-clic, rendu sur fond noir,
        /// bandeau et barre de statut superposés) n'est pas testable en
        /// headless — non couvert, comme le sélecteur natif de dossier.
        test "RC-01 common rule : fullscreen interactions in the triaging view" {
            withPhotos (fun photos ->
                // Précondition RC-01 : une vue de tri (dossier organisé, 3 photos).
                let s = init () |> fst |> step (FolderSelected photos)
                Expect.equal s.DisplayMode Normal "affichage normal au départ"

                // RC-01 : F → plein écran, la photo courante ne change pas.
                let sF = s |> step FullscreenRequested
                Expect.equal sF.DisplayMode Fullscreen "F : la photo passe en plein écran"
                Expect.equal (currentJpgFileName sF) (Some "P1.JPG") "la photo affichée ne change pas"

                // RC-01 : en plein écran, le fonctionnement des touches reste valable.
                let s1 = sF |> step NextClicked
                Expect.equal s1.DisplayMode Fullscreen "on reste en plein écran en naviguant"
                Expect.equal s1.CurrentIndex 1 "les flèches naviguent toujours"

                // RC-01 : Echap → retour à l'affichage normal.
                let sBack = s1 |> step FullscreenExitRequested
                Expect.equal sBack.DisplayMode Normal "Echap : la photo quitte le plein écran"

                // RC-01 : F11 et F basculent (même geste que le double-clic).
                let sF11 = sBack |> step FullscreenRequested
                Expect.equal sF11.DisplayMode Fullscreen "F11 : plein écran"
                let sToggle = sF11 |> step FullscreenRequested
                Expect.equal sToggle.DisplayMode Normal "F en plein écran : retour à l'affichage normal"

                // RC-01 : en plein écran, une confirmation s'affiche par-dessus
                // l'image (RC-02 reste actif), et Echap ne quitte pas le plein
                // écran tant qu'elle est présentée à l'utilisateur.
                let sFull = sToggle |> step FullscreenRequested
                let sDel = sFull |> step DeleteTandemRequested
                Expect.equal sDel.Screen (Confirming(Triaging, DeleteTandem "P2")) "Suppr : la confirmation s'affiche par-dessus l'image"
                let sEchap = sDel |> step FullscreenExitRequested
                Expect.equal sEchap.DisplayMode Fullscreen "Echap ne quitte pas le plein écran pendant une confirmation"
                Expect.equal sEchap.Screen (Confirming(Triaging, DeleteTandem "P2")) "la confirmation reste présentée"

                // La question tranchée, Echap redevient actif.
                let sCancelled = sEchap |> step DeleteTandemCancelled
                Expect.equal sCancelled.Screen (Working Triaging) "la confirmation est annulée"
                let sNormalAgain = sCancelled |> step FullscreenExitRequested
                Expect.equal sNormalAgain.DisplayMode Normal "Echap : retour à l'affichage normal")
        }

        /// Scénario exécutable de docs/usecases/regles-communes.md, règle RC-01 :
        /// en plein écran, la barre de statut n'apparaît que brièvement puis se
        /// cache « le reste du temps ». La machine programme l'effacement
        /// (commande temporisée → StatusHidden) : le pas de scénario exécute
        /// la commande réellement et attend son message — le délai réel est
        /// attendu, pas simulé.
        test "RC-01 common rule : ephemeral status in fullscreen, persistent in normal display" {
            withPhotos (fun photos ->
                // Vue normale : le rappel de suppression (RC-02) persiste.
                let sNormal = init () |> fst |> step (FolderSelected photos)
                let sDeletedNormal = sNormal |> step DeleteTandemRequested |> step DeleteTandemConfirmed
                Expect.isNonEmpty sDeletedNormal.Status "en vue normale, le statut persiste"

                // En plein écran : même parcours, le statut disparaît seul
                // après le délai (StatusHidden, garde plein écran).
                let sFull = init () |> fst |> step (FolderSelected photos) |> step FullscreenRequested
                let sDeletedFull = sFull |> step DeleteTandemRequested |> step DeleteTandemConfirmed
                Expect.equal sDeletedFull.Status "" "en plein écran, le statut est éphémère")
        }

        /// Scénario exécutable de docs/usecases/regles-communes.md, règle RC-04 :
        /// le JPG affiché est tourné selon son tag EXIF Orientation. Coverage
        /// générique : un seul test pour toutes les vues affichant une photo.
        ///
        /// Deux niveaux d'observation, l'un ne pouvant pas mentir pour l'autre :
        /// - machine : `currentJpgIsPortrait` lit le bitmap réellement décodé
        ///   (`CurrentJpg`) — la rotation doit se voir sur le bitmap, pas
        ///   seulement dans l'intention ;
        /// - rendu : la vraie vue de l'application (`Main.view`), matérialisée
        ///   par HostControl et rendue par Skia en headless, est vérifiée
        ///   pixel par pixel. La fixture est un motif orienté (quart
        ///   haut-gauche rouge) : le test vérifie le SENS de la rotation, pas
        ///   seulement l'aspect — une rotation à l'envers ou un miroir
        ///   parasite échouerait ici alors que l'aspect resterait correct.
        ///   Ce niveau couvre le mapping vue (`Image.source`) — plafond
        ///   headless levé pour l'affichage de la photo. Les tags miroirs
        ///   (2, 4, 5, 7) restent couverts par les tests unitaires miroir de
        ///   la fonction pure de mapping tag → transformation.
        /// La fixture fabrique de vrais JPG (Skia en headless) : P1 sans EXIF,
        /// P2 avec le tag 6 (capteur paysage, affichage portrait) — c'est le
        /// fichier que produit un appareil photo tenu à la verticale. Le
        /// sélecteur natif de dossier reste non testé.
        test "RC-04 common rule : EXIF-oriented photos display upright" {
            withVerticales (fun folder ->
                // Précondition RC-04 : vue de tri, photo courante P1 (paysage,
                // sans EXIF). Le dossier organisé est trié directement.
                let s = init () |> fst |> step (FolderSelected folder)
                Expect.equal s.Screen (Working Triaging) "la vue de tri est affichée"
                Expect.equal (currentJpgFileName s) (Some "P1.JPG") "photo courante P1"

                // RC-04 : sans tag EXIF, aucune rotation — le paysage reste
                // paysage et le motif reste dans son coin d'origine. Cette
                // identité verrouille aussi l'absence de rotation parasite
                // (180°, miroir) sur un fichier sans métadonnée.
                Expect.equal (currentJpgIsPortrait s) (Some false) "sans EXIF, la photo décodée reste horizontale"

                match renderPhoto s with
                | Some photo ->
                    Expect.isGreaterThan photo.Width photo.Height "sans EXIF, la photo rendue à l'écran reste horizontale"
                    Expect.isLessThan photo.RedCenterX 0.5 "sans EXIF, le quart rouge du motif reste à gauche : aucune rotation"
                    Expect.isLessThan photo.RedCenterY 0.5 "sans EXIF, le quart rouge du motif reste en haut : aucune rotation"
                | None -> failtest "la photo P1 n'est pas rendue (pipeline de rendu headless cassé ?)"

                // RC-04 : photo suivante, P2 — même cliché paysage, tag
                // Orientation 6 : l'appareil était tenu à la verticale.
                let s2 = s |> step NextClicked
                Expect.equal (currentJpgFileName s2) (Some "P2.JPG") "photo courante P2"

                // RC-04 observable effect : la photo prise à la verticale
                // s'affiche à la verticale — bitmap décodé et pixels rendus.
                Expect.equal (currentJpgIsPortrait s2) (Some true) "avec le tag EXIF Orientation 6, la photo décodée est verticale"

                // Le tag 6 impose un quart de tour HORAIRE : le quart rouge
                // (haut-gauche du capteur) doit se retrouver en haut-droite.
                // Le sens est vérifié par pixels : une rotation dans l'autre
                // sens afficherait une photo verticale… mais couchée.
                match renderPhoto s2 with
                | Some photo ->
                    Expect.isGreaterThan photo.Height photo.Width "avec le tag EXIF Orientation 6, la photo rendue à l'écran est verticale"
                    Expect.isGreaterThan photo.RedCenterX 0.5 "tag 6 : le quart rouge est rendu en haut à DROITE (quart de tour horaire)"
                    Expect.isLessThan photo.RedCenterY 0.5 "tag 6 : le quart rouge est rendu EN HAUT (quart de tour horaire)"
                | None -> failtest "la photo P2 n'est pas rendue (pipeline de rendu headless cassé ?)")
        }

        /// RC-04, niveau unitaire pixels : les 8 transformations EXIF déplacent
        /// les pixels exactement comme la norme le prescrit. La fixture est
        /// un bitmap 4×2 à quatre quadrants (haut-gauche rouge, haut-droite
        /// bleu, bas-gauche vert, bas-droite jaune) : on lit la position du
        /// rouge et du bleu après transformation et les dimensions du résultat
        /// — le SENS de chaque rotation et chaque miroir est ainsi vérifié,
        /// pas seulement l'aspect. Le scénario E2E ci-dessus couvre le tag 6
        /// de bout en bout ; ce test couvre les 8 valeurs au niveau pixels.
        test "RC-04 unit : the 8 EXIF display transforms move pixels as the norm prescribes" {
            let sourceBitmap () : Bitmap =
                let bmp = new WriteableBitmap(PixelSize(4, 2), Vector(96.0, 96.0))

                (
                    use fb = bmp.Lock()

                    let setPixel (x: int) (y: int) (r, g, b) =
                        Marshal.WriteInt32(
                            fb.Address + nativeint (y * fb.RowBytes + x * 4),
                            (255 <<< 24) ||| (r <<< 16) ||| (g <<< 8) ||| b
                        )

                    for y = 0 to 1 do
                        for x = 0 to 3 do
                            let color =
                                if x < 2 && y = 0 then (255, 0, 0) // haut-gauche rouge
                                elif x >= 2 && y = 0 then (0, 0, 255) // haut-droite bleu
                                elif x < 2 then (0, 255, 0) // bas-gauche vert
                                else (255, 255, 0) // bas-droite jaune

                            setPixel x y color
                )

                bmp :> Bitmap

            // Pour chaque tag : quadrant d'arrivée du rouge (haut-gauche au
            // départ) puis du bleu (haut-droite au départ), et dimensions
            // échangées ou non (source 4×2).
            let expectations : (DisplayTransform * string * string * bool) list =
                [ Identity, "LT", "RT", false
                  MirrorHorizontal, "RT", "LT", false
                  Rotate180, "RB", "LB", false
                  MirrorVertical, "LB", "RB", false
                  MirrorHorizontalRotate270, "LT", "LB", true
                  Rotate90Clockwise, "RT", "RB", true
                  MirrorHorizontalRotate90, "RB", "RT", true
                  Rotate270Clockwise, "LB", "LT", true ]

            for transform, expectedRed, expectedBlue, swaps in expectations do
                let source = sourceBitmap ()
                let result = applyDisplayTransform transform source
                let w, h = result.PixelSize.Width, result.PixelSize.Height
                let expectedSize = if swaps then (2, 4) else (4, 2)
                Expect.equal (w, h) expectedSize (sprintf "%A : dimensions du résultat" transform)

                // Lecture des pixels (BGRA) et localisation du rouge et du bleu
                // (tolérance large : les bords interpolés ne comptent pas,
                // seul le quadrant du centre de la masse compte).
                let size = w * h * 4
                let ptr = Marshal.AllocHGlobal size
                result.CopyPixels(PixelRect(0, 0, w, h), ptr, size, w * 4)
                let buffer = Array.create size 0uy
                Marshal.Copy(ptr, buffer, 0, size)
                Marshal.FreeHGlobal ptr

                let massCenter (r0, g0, b0) =
                    let mutable minX, minY, maxX, maxY = Int32.MaxValue, Int32.MaxValue, Int32.MinValue, Int32.MinValue
                    let mutable found = false

                    for y = 0 to h - 1 do
                        for x = 0 to w - 1 do
                            let i = (y * w + x) * 4
                            let b, g, r = int buffer.[i], int buffer.[i + 1], int buffer.[i + 2]

                            if abs (r - r0) <= 60 && abs (g - g0) <= 60 && abs (b - b0) <= 60 then
                                found <- true
                                minX <- min minX x
                                minY <- min minY y
                                maxX <- max maxX x
                                maxY <- max maxY y

                    if found then
                        let cx = (minX + maxX) / 2
                        let cy = (minY + maxY) / 2

                        (if cx < w / 2 then "L" else "R")
                        + (if cy < h / 2 then "T" else "B")
                    else
                        "?"

                Expect.equal (massCenter (255, 0, 0)) expectedRed (sprintf "%A : quadrant du rouge" transform)
                Expect.equal (massCenter (0, 0, 255)) expectedBlue (sprintf "%A : quadrant du bleu" transform)

                result.Dispose()

                if not (Object.ReferenceEquals(source, result)) then
                    source.Dispose()
        }

        // ---------- RC-05 : bandeau jaune modal (docs/usecases/regles-communes.md) ----------

        test "RC-05 common rule : organizing actions are grayed while the question is displayed" {
            // Pendant une question, tout geste de l'écran sous-jacent est un
            // no-op gardé dans update — il doit aussi se voir : bouton grisé,
            // jamais de clic mort en silence. Seuls Déplacer / Annuler restent
            // actifs. Les boutons-maquettes du prototype (Voir les fichiers,
            // Réanalyser) suivent la même règle.
            let buttons = renderedButtons (displayState (Confirming(Organizing, OrganizeFolder(1, 0))))

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
        } ]
