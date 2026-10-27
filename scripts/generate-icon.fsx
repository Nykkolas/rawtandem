#r "nuget: SkiaSharp"
#r "nuget: SkiaSharp.NativeAssets.Linux"

// Génération de l'icône de l'application (design « Le tandem », style iOS) :
// tuile khaki unie à coins arrondis (le jaune du bandeau de confirmation de
// l'app), glyphe bleu nuit — l'encadrement derrière (contour) et la photo devant
// (carte pleine), deux cartes paysage chevauchées.
//
// Exécution : dotnet fsi scripts/generate-icon.fsx
// Sortie : src/RawTandem.UI/Assets/icon.png (256 px, ressource embarquée)
//          src/RawTandem.UI/Assets/icon.ico (16→256 px, icône de l'exe Windows)
//
// Attention (leçon 2026-10-27) : SKColor(red, green, blue, alpha) — l'ordre
// des canaux a été inversé ici une première fois, produisant une tuile
// « noire » en fait rouge translucide. Toujours vérifier les pixels du
// résultat, pas seulement qu'un fichier sort.

open System
open System.IO
open SkiaSharp

let repoRoot = __SOURCE_DIRECTORY__ + "/.."
let assetsDir = Path.Combine(repoRoot, "src/RawTandem.UI/Assets")
Directory.CreateDirectory(assetsDir) |> ignore

// Palette de l'application. SKColor(r, g, b, a).
let Khaki = SKColor(0xF0uy, 0xE6uy, 0x8Cuy, 0xFFuy)
let Night = SKColor(0x2Buy, 0x39uy, 0x67uy, 0xFFuy)

/// Rend le design en PNG de `size` px de côté. Le dessin est défini pour
/// 512 px puis le canvas est mis à l'échelle : traits et rayons suivent,
/// net à toutes les tailles.
let renderPng (size: int) : byte[] =
    let info = SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul)
    use surface = SKSurface.Create(info)
    let canvas = surface.Canvas
    canvas.Scale(float32 size / 512.f)

    // Tuile khaki arrondie (22 %) — style iPhone.
    let tile = SKRoundRect()
    tile.SetRect(SKRect.Create(0.f, 0.f, 512.f, 512.f), 512.f * 0.22f, 512.f * 0.22f)
    use tilePaint = new SKPaint(Color = Khaki, IsAntialias = true)
    canvas.DrawRoundRect(tile, tilePaint)

    // L'encadrement derrière (RAW) : carte en contour.
    use stroke = new SKPaint(Color = Night, Style = SKPaintStyle.Stroke, StrokeWidth = 24.f, IsAntialias = true)
    let back = SKRoundRect()
    back.SetRect(SKRect.Create(81.f, 121.f, 240.f, 190.f), 28.f, 28.f)
    canvas.DrawRoundRect(back, stroke)

    // La photo devant (JPG) : carte pleine.
    use fill = new SKPaint(Color = Night, IsAntialias = true)
    let front = SKRoundRect()
    front.SetRect(SKRect.Create(191.f, 201.f, 240.f, 190.f), 28.f, 28.f)
    canvas.DrawRoundRect(front, fill)

    use image = surface.Snapshot()
    use png = image.Encode(SKEncodedImageFormat.Png, 100)
    png.ToArray()

/// Conteneur ICO avec PNG embarqués (format Vista+ standard, lu partout).
let buildIco (pngBySize: (int * byte[]) list) : byte[] =
    use ms = new MemoryStream()
    use w = new BinaryWriter(ms)
    w.Write(0us) // réservé
    w.Write(1us) // type : icône
    w.Write(uint16 pngBySize.Length)

    let mutable offset = 6 + 16 * List.length pngBySize
    let mutable blobs = []

    for size, data in pngBySize do
        let dimension = if size >= 256 then 0uy else byte size
        w.Write(dimension) // largeur
        w.Write(dimension) // hauteur
        w.Write(0uy) // palette
        w.Write(0uy) // réservé
        w.Write(1us) // plans
        w.Write(32us) // bits par pixel
        w.Write(uint32 data.Length)
        w.Write(uint32 offset)
        offset <- offset + data.Length
        blobs <- data :: blobs

    for data in List.rev blobs do
        w.Write(data)

    ms.ToArray()

// --- Génération ---

let sizes = [ 16; 32; 48; 64; 128; 256; 512 ]
let pngs = sizes |> List.map (fun size -> size, renderPng size)

File.WriteAllBytes(Path.Combine(assetsDir, "icon.png"), pngs |> List.find (fun (s, _) -> s = 256) |> snd)

let icoSizes = [ 16; 32; 48; 64; 128; 256 ]
File.WriteAllBytes(Path.Combine(assetsDir, "icon.ico"), buildIco (pngs |> List.filter (fun (s, _) -> List.contains s icoSizes)))

printfn "Icônes générées dans %s" assetsDir

// --- Garde-fou : les pixels du résultat (l'ordre des canaux de SKColor a
// déjà menti une fois — une icône fausse ne doit jamais repasser inaperçue).

let sample (path: string) (x: int) (y: int) =
    use stream = File.OpenRead(path)
    use bmp = SKBitmap.Decode(stream)
    let p = bmp.GetPixel(x, y)
    (int p.Red, int p.Green, int p.Blue, int p.Alpha)

// Coordonnées d'échantillon dans le PNG 256 px (le dessin 512 est réduit
// de moitié) : haut de tuile au centre, centre de la carte avant.
let (tileR, tileG, tileB, tileA) = sample (Path.Combine(assetsDir, "icon.png")) 128 20
let (cardR, cardG, cardB, cardA) = sample (Path.Combine(assetsDir, "icon.png")) 150 150

if tileR <> 240 || tileG <> 230 || tileB <> 140 || tileA <> 255 then
    failwithf "couleur de tuile inattendue : %d %d %d a=%d (khaki opaque attendu)" tileR tileG tileB tileA

if cardR <> 43 || cardG <> 57 || cardB <> 103 || cardA <> 255 then
    failwithf "couleur de carte inattendue : %d %d %d a=%d (bleu nuit opaque attendu)" cardR cardG cardB cardA

printfn "Pixels vérifiés : tuile khaki (%d,%d,%d), carte bleu nuit (%d,%d,%d)" tileR tileG tileB cardR cardG cardB
