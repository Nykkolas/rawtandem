module RawTandem.Core.FolderScanTests

open System
open System.IO
open Expecto
open RawTandem.Core.TestHelpers
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan

[<Tests>]
let folderScanTests =

    testList
        "FolderScan"
        [ test "scanFolder on an empty folder returns an empty scan" {
              let folder = tempFolder ()
              let scan = scanFolder folder

              Expect.isEmpty scan.Tandems "aucun tandem"
              Expect.isEmpty scan.OrphanJpgs "aucun JPG orphelin"
              Expect.isEmpty scan.OrphanRaws "aucun RAW orphelin"
          }

          test "scanFolder pairs a JPG and a RAW sharing the same stem" {
              let folder = tempFolder ()
              touch folder "P1230001.JPG"
              touch folder "P1230001.RW2"

              let scan = scanFolder folder

              Expect.equal
                  scan.Tandems
                  [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" } ]
                  "un tandem, stem sans extension"

              Expect.isEmpty scan.OrphanJpgs "le JPG est apparié"
              Expect.isEmpty scan.OrphanRaws "le RAW est apparié"
          }

          test "scanFolder compares extensions case-insensitively" {
              let folder = tempFolder ()
              touch folder "P1230001.jpg"
              touch folder "P1230001.rw2"

              let scan = scanFolder folder

              Expect.equal scan.Tandems.Length 1 "P1230001.jpg + P1230001.rw2 forment un tandem"
              Expect.isEmpty scan.OrphanJpgs "aucun orphelin malgré la casse"
              Expect.isEmpty scan.OrphanRaws "aucun orphelin malgré la casse"
          }

          test "scanFolder reports a JPG without RAW as an orphan, not a tandem" {
              let folder = tempFolder ()
              touch folder "P1230002.JPG"

              let scan = scanFolder folder

              Expect.isEmpty scan.Tandems "pas de tandem sans RAW"
              Expect.equal scan.OrphanJpgs [ "P1230002.JPG" ] "JPG orphelin listé"
              Expect.isEmpty scan.OrphanRaws "aucun RAW dans le dossier"
          }

          test "scanFolder reports a RAW without JPG as an orphan, not a tandem" {
              let folder = tempFolder ()
              touch folder "P1230003.RW2"

              let scan = scanFolder folder

              Expect.isEmpty scan.Tandems "pas de tandem sans JPG"
              Expect.isEmpty scan.OrphanJpgs "aucun JPG dans le dossier"
              Expect.equal scan.OrphanRaws [ "P1230003.RW2" ] "RAW orphelin listé"
          }

          test "scanFolder ignores files that are neither JPG nor RW2" {
              let folder = tempFolder ()
              touch folder "notes.txt"
              touch folder "video.MP4"
              touch folder "P1230001.JPG"
              touch folder "P1230001.RW2"

              let scan = scanFolder folder

              Expect.equal scan.Tandems.Length 1 "seuls JPG et RW2 comptent"
              Expect.isEmpty scan.OrphanJpgs "les fichiers non-photos sont ignorés"
              Expect.isEmpty scan.OrphanRaws "les fichiers non-photos sont ignorés"
          }

          test "scanFolder reads only the folder root, not subfolders" {
              let folder = tempFolder ()

              let raw = Path.Combine(folder, "RAW")
              Directory.CreateDirectory(raw) |> ignore
              touch folder "P1230001.JPG"
              touch raw "P1230001.RW2"

              let scan = scanFolder folder

              // Bug confirmé en itération 2 : le RAW du sous-dossier n'est pas
              // apparié (voir docs/TODO.md). Ce test fige le comportement actuel,
              // il changera avec la reconnaissance des dossiers déjà organisés.
              Expect.isEmpty scan.Tandems "le RAW du sous-dossier n'est pas apparié"
              Expect.equal scan.OrphanJpgs [ "P1230001.JPG" ] "le JPG racine est un orphelin"
              Expect.isEmpty scan.OrphanRaws "aucun RAW à la racine"
          }

          test "scanFolder returns tandems sorted by stem" {
              let folder = tempFolder ()

              for stem in [ "P1230002"; "P1230001"; "P1230003" ] do
                  touch folder (stem + ".JPG")
                  touch folder (stem + ".RW2")

              let scan = scanFolder folder

              Expect.equal
                  (scan.Tandems |> List.map (fun t -> t.Stem))
                  [ "P1230001"; "P1230002"; "P1230003" ]
                  "tri stable par stem"
          } ]
