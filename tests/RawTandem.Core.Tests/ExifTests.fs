module RawTandem.Core.ExifTests

open System
open System.IO
open Expecto
open RawTandem.Core.Exif

/// Fabrication de JPEG synthétiques pour la lecture du tag Orientation :
/// SOI, APP0 (JFIF), APP1 « Exif » ne portant que le tag Orientation, EOI.
/// Le lecteur réel (MetadataExtractor) ne voit que des octets — pas besoin
/// d'une vraie image pour couvrir la lecture de l'en-tête.
module private SyntheticJpeg =

    /// Segment APP1 « Exif » : en-tête "Exif\0\0", TIFF petit-boutiste (II),
    /// IFD d'une entrée (tag 0x0112 Orientation, type SHORT, count 1, valeur).
    let exifOrientationSegment (value: byte) : byte[] =
        let payload =
            [| yield! [| 0x45uy; 0x78uy; 0x69uy; 0x66uy; 0x00uy; 0x00uy |] // "Exif\0\0"
               yield! [| 0x49uy; 0x49uy |] // II (petit-boutiste)
               yield! [| 0x2Auy; 0x00uy |] // marqueur TIFF 42
               yield! [| 0x08uy; 0x00uy; 0x00uy; 0x00uy |] // IFD0 à l'octet 8
               yield! [| 0x01uy; 0x00uy |] // une entrée
               yield! [| 0x12uy; 0x01uy |] // tag 0x0112 Orientation
               yield! [| 0x03uy; 0x00uy |] // type SHORT
               yield! [| 0x01uy; 0x00uy; 0x00uy; 0x00uy |] // count 1
               yield! [| value; 0x00uy; 0x00uy; 0x00uy |] |] // valeur, alignée à gauche

        let body = Array.append payload [| 0x00uy; 0x00uy; 0x00uy; 0x00uy |] // pas d'IFD suivante
        let length = 2 + body.Length // le champ longueur se compte lui-même
        Array.append [| 0xFFuy; 0xE1uy; byte (length >>> 8); byte (length &&& 0xFF) |] body

    /// JPEG minimal : SOI + APP0 JFIF + APP1 (si un tag est donné) + EOI.
    let jpeg (orientation: byte option) : byte[] =
        let app0 =
            [| yield! [| 0xFFuy; 0xE0uy; 0x00uy; 0x10uy |]
               yield! [| 0x4Auy; 0x46uy; 0x49uy; 0x46uy; 0x00uy |] // "JFIF\0"
               yield! [| 0x01uy; 0x02uy; 0x00uy; 0x00uy; 0x01uy; 0x00uy; 0x01uy; 0x00uy; 0x00uy |] |]

        [ [| 0xFFuy; 0xD8uy |] // SOI
          app0
          match orientation with
          | Some value -> exifOrientationSegment value
          | None -> [||]
          [| 0xFFuy; 0xD9uy |] ] // EOI
        |> Array.concat

    /// Écrit le JPEG dans un fichier temporaire jetable, exécute, supprime.
    let withJpeg (bytes: byte[]) (run: string -> 'a) : 'a =
        let path = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-exif-%s.jpg" (Guid.NewGuid().ToString("N")))
        File.WriteAllBytes(path, bytes)

        try
            run path
        finally
            File.Delete path

[<Tests>]
let exifTests =

    testList
        "Exif"
        [ // ---------- displayTransform : les 8 valeurs de la norme ----------

          test "displayTransform maps the 8 EXIF orientation values" {
              Expect.equal (displayTransform 1uy) (Some Identity) "1 : aucune transformation"
              Expect.equal (displayTransform 2uy) (Some MirrorHorizontal) "2 : miroir gauche-droite"
              Expect.equal (displayTransform 3uy) (Some Rotate180) "3 : demi-tour"
              Expect.equal (displayTransform 4uy) (Some MirrorVertical) "4 : miroir haut-bas"
              Expect.equal (displayTransform 5uy) (Some MirrorHorizontalRotate270) "5 : transposée"
              Expect.equal (displayTransform 6uy) (Some Rotate90Clockwise) "6 : quart de tour horaire"
              Expect.equal (displayTransform 7uy) (Some MirrorHorizontalRotate90) "7 : anti-transposée"
              Expect.equal (displayTransform 8uy) (Some Rotate270Clockwise) "8 : quart de tour anti-horaire"
          }

          test "displayTransform rejects out-of-norm values" {
              Expect.equal (displayTransform 0uy) None "0 : hors norme"
              Expect.equal (displayTransform 9uy) None "9 : hors norme"
              Expect.equal (displayTransform 255uy) None "255 : hors norme"
          }

          // ---------- readOrientationTag : lecture réelle du fichier ----------

          test "readOrientationTag reads the tag from a JPEG with EXIF" {
              SyntheticJpeg.withJpeg
                  (SyntheticJpeg.jpeg (Some 6uy))
                  (fun path -> Expect.equal (readOrientationTag path) (Some 6uy) "le tag 6 est lu")
          }

          test "readOrientationTag returns None for a JPEG without EXIF" {
              SyntheticJpeg.withJpeg
                  (SyntheticJpeg.jpeg None)
                  (fun path -> Expect.equal (readOrientationTag path) None "aucun tag")
          }

          test "readOrientationTag returns None for an unreadable file" {
              let empty = Path.Combine(Path.GetTempPath(), sprintf "rawtandem-exif-%s.jpg" (Guid.NewGuid().ToString("N")))
              File.WriteAllText(empty, "")

              try
                  Expect.equal (readOrientationTag empty) None "fichier vide : aucune exception, None"
              finally
                  File.Delete empty

              Expect.equal (readOrientationTag "/dossier-fixture-inexistant/x.jpg") None "fichier absent : None"
          }

          // ---------- readDisplayTransform : le pipeline complet ----------

          test "readDisplayTransform returns the transform for a vertical photo" {
              SyntheticJpeg.withJpeg
                  (SyntheticJpeg.jpeg (Some 6uy))
                  (fun path -> Expect.equal (readDisplayTransform path) (Some Rotate90Clockwise) "tag 6 → quart de tour horaire")
          }

          test "readDisplayTransform returns None without EXIF" {
              SyntheticJpeg.withJpeg
                  (SyntheticJpeg.jpeg None)
                  (fun path -> Expect.equal (readDisplayTransform path) None "aucune transformation")
          } ]
