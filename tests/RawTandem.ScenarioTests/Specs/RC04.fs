module Specs.RC04

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

/// Règle RC-04 — docs/usecases/regles-communes.md.
/// 
/// Coverage : générique — deux niveaux d'observation, l'un ne pouvant pas mentir
/// pour l'autre : la vraie vue de tri est rendue et vérifiée pixel par pixel (ce
/// que l'utilisateur voit — une rotation à l'envers ou un miroir parasite se
/// verraient à l'écran), et les 8 transformations de la norme sont vérifiées sur
/// leurs pixels pour être précises sur le SENS de chaque rotation.
/// 
/// Non couvert (headless) : le sélecteur natif de dossier.

[<Tests>]
let tests =
    testList "RC-04 : affichage des JPG orienté selon la rotation EXIF" [

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
    ]
