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
        [ let rawSubfolder (folder: string) =
              let raw = Path.Combine(folder, "RAW")
              Directory.CreateDirectory(raw) |> ignore
              raw

          let tandem stem = { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

          test "scanFolder on an empty folder returns an empty scan" {
              let scan = scanFolder (tempFolder ())

              Expect.isEmpty scan.TandemsToOrganize "aucun tandem à organiser"
              Expect.isEmpty scan.OrganizedTandems "aucun tandem déjà organisé"
              Expect.isEmpty scan.CrossedTandems "aucun tandem croisé"
              Expect.isEmpty scan.InvertedTandems "aucun tandem inversé"
              Expect.isEmpty scan.OrphanJpgs "aucun JPG orphelin"
              Expect.isEmpty scan.OrphanJpgsInRaw "aucun JPG orphelin dans RAW/"
              Expect.isEmpty scan.OrphanRawsToOrganize "aucun RAW orphelin à organiser"
              Expect.isEmpty scan.FiledOrphanRaws "aucun RAW orphelin rangé"
              Expect.isEmpty scan.DuplicateJpgs "aucun doublon JPG"
              Expect.isEmpty scan.DuplicateRaws "aucun doublon RAW"
              Expect.isNone scan.RawFolderIssue "aucune anomalie"
          }

          test "scanFolder classifies a nominal tandem as to organize" {
              let folder = tempFolder ()
              touch folder "P1230001.JPG"
              touch folder "P1230001.RW2"

              let scan = scanFolder folder

              Expect.equal scan.TandemsToOrganize [ tandem "P1230001" ] "tandem à organiser"
              Expect.isEmpty scan.OrganizedTandems "pas déjà organisé"
          }

          test "scanFolder classifies an already organized folder correctly" {
              let folder = tempFolder ()
              touch folder "P1230001.JPG"
              touch (rawSubfolder folder) "P1230001.RW2"

              let scan = scanFolder folder

              Expect.equal scan.OrganizedTandems [ tandem "P1230001" ] "tandem déjà organisé"
              Expect.isEmpty scan.TandemsToOrganize "rien à organiser"
              Expect.isEmpty scan.OrphanJpgs "le JPG n'est pas orphelin (bug itération 2 corrigé)"
          }

          test "scanFolder compares extensions case-insensitively everywhere" {
              let folder = tempFolder ()
              touch folder "P1230001.jpg"
              touch folder "P1230001.rw2"
              touch folder "P1230002.JPG"
              touch (rawSubfolder folder) "P1230002.rw2"

              let scan = scanFolder folder

              Expect.equal scan.TandemsToOrganize [ { tandem "P1230001" with JpgFileName = "P1230001.jpg"; RawFileName = "P1230001.rw2" } ] "apparié malgré la casse"
              Expect.equal scan.OrganizedTandems [ { tandem "P1230002" with RawFileName = "P1230002.rw2" } ] "organisé malgré la casse"
          }

          test "scanFolder reports a root JPG without RAW as an orphan" {
              let folder = tempFolder ()
              touch folder "P1230002.JPG"

              let scan = scanFolder folder

              Expect.equal scan.OrphanJpgs [ "P1230002.JPG" ] "JPG orphelin listé"
              Expect.isEmpty scan.TandemsToOrganize "pas de tandem sans RAW"
          }

          test "scanFolder reports a root RAW without JPG as an orphan to organize" {
              let folder = tempFolder ()
              touch folder "P1230003.RW2"

              let scan = scanFolder folder

              Expect.equal scan.OrphanRawsToOrganize [ "P1230003.RW2" ] "RAW orphelin à organiser"
          }

          test "scanFolder reports a RAW/ RAW without JPG as a filed orphan" {
              let folder = tempFolder ()
              touch (rawSubfolder folder) "P1230004.RW2"

              let scan = scanFolder folder

              Expect.equal scan.FiledOrphanRaws [ "P1230004.RW2" ] "RAW orphelin rangé, rien à organiser"
              Expect.isEmpty scan.OrphanRawsToOrganize "pas à la racine"
          }

          test "scanFolder reports a JPG alone in RAW/ as an orphan JPG in RAW" {
              let folder = tempFolder ()
              touch (rawSubfolder folder) "P1230005.JPG"

              let scan = scanFolder folder

              Expect.equal scan.OrphanJpgsInRaw [ "P1230005.JPG" ] "JPG orphelin dans RAW/"
          }

          test "scanFolder classifies a crossed tandem (JPG in RAW, RAW at root)" {
              let folder = tempFolder ()
              let raw = rawSubfolder folder
              touch raw "P1230006.JPG"
              touch folder "P1230006.RW2"

              let scan = scanFolder folder

              Expect.equal
                  scan.CrossedTandems
                  [ { Stem = "P1230006"; JpgFileName = "P1230006.JPG"; RawFileName = "P1230006.RW2" } ]
                  "tandem croisé"
              Expect.isEmpty scan.TandemsToOrganize "pas un tandem nominal"
          }

          test "scanFolder classifies an inverted tandem (both files in RAW/)" {
              let folder = tempFolder ()
              let raw = rawSubfolder folder
              touch raw "P1230007.JPG"
              touch raw "P1230007.RW2"

              let scan = scanFolder folder

              Expect.equal
                  scan.InvertedTandems
                  [ { Stem = "P1230007"; JpgFileName = "P1230007.JPG"; RawFileName = "P1230007.RW2" } ]
                  "tandem inversé"
          }

          test "scanFolder reports a RAW present at both locations as a duplicate plus an organized tandem" {
              let folder = tempFolder ()
              let raw = rawSubfolder folder
              touch folder "P1230008.JPG"
              touch folder "P1230008.RW2"
              touch raw "P1230008.RW2"

              let scan = scanFolder folder

              Expect.equal scan.DuplicateRaws [ "P1230008.RW2" ] "le RAW racine est le doublon"
              Expect.equal scan.OrganizedTandems [ tandem "P1230008" ] "le tandem compte organisé (sa copie rangée)"
          }

          test "scanFolder reports a JPG present at both locations as a duplicate plus a tandem to organize" {
              let folder = tempFolder ()
              let raw = rawSubfolder folder
              touch folder "P1230009.JPG"
              touch folder "P1230009.RW2"
              touch raw "P1230009.JPG"

              let scan = scanFolder folder

              Expect.equal scan.DuplicateJpgs [ "P1230009.JPG" ] "le JPG de RAW/ est le doublon (nom dans RAW/)"
              Expect.equal scan.TandemsToOrganize [ tandem "P1230009" ] "le tandem racine reste à organiser"
          }

          test "scanFolder ignores files that are neither JPG nor RW2 and other subfolders" {
              let folder = tempFolder ()
              touch folder "notes.txt"
              let other = Path.Combine(folder, " vacances 2026 ")
              Directory.CreateDirectory(other) |> ignore
              touch other "P1230001.JPG"

              let scan = scanFolder folder

              Expect.isEmpty scan.TandemsToOrganize "les autres sous-dossiers sont ignorés en silence"
              Expect.isNone scan.RawFolderIssue "pas d'anomalie signalée pour un dossier ordinaire"
          }

          test "scanFolder reports a RAW folder name with wrong case and ignores its files" {
              let folder = tempFolder ()
              let wrongCase = Path.Combine(folder, "raw")
              Directory.CreateDirectory(wrongCase) |> ignore
              touch wrongCase "P1230010.RW2"

              let scan = scanFolder folder

              Expect.equal scan.RawFolderIssue (Some(RawFolderCaseMismatch "raw")) "anomalie de casse signalée"
              Expect.isEmpty scan.FiledOrphanRaws "le contenu du dossier mal cassé est invisible tant qu'il n'est pas renommé"
          }

          test "scanFolder stays silent about a wrong-case folder when RAW exists" {
              let folder = tempFolder ()
              let wrongCase = Path.Combine(folder, "raw")
              Directory.CreateDirectory(wrongCase) |> ignore
              rawSubfolder folder |> ignore

              let scan = scanFolder folder

              Expect.isNone scan.RawFolderIssue "« raw » est un sous-dossier ordinaire quand « RAW » existe"
          }

          test "scanFolder reports RAW being a file, not a directory" {
              let folder = tempFolder ()
              touch folder "RAW"

              let scan = scanFolder folder

              Expect.equal scan.RawFolderIssue (Some RawFolderIsFile) "« RAW » fichier : erreur explicite"
              Expect.isEmpty scan.TandemsToOrganize "rien n'est organisable tant que l'anomalie n'est pas résolue"
          }

          test "scanFolder reports case twins of the same extension in one directory" {
              // Bug confirmé en itération 3 : P1.JPG + P1.jpg coexistent sur un
              // filesystem sensible à la casse ; aucun ne doit être avalé.
              let folder = tempFolder ()
              touch folder "P1.JPG"
              touch folder "P1.jpg"
              touch folder "P1.RW2"

              let scan = scanFolder folder

              Expect.equal
                  scan.TandemsToOrganize
                  [ { Stem = "P1"; JpgFileName = "P1.JPG"; RawFileName = "P1.RW2" } ]
                  "le premier nom en ordre ordinal gagne l'appariement"

              Expect.equal
                  scan.CaseCollisions
                  [ { FileName = "P1.jpg"; InRawSubfolder = false } ]
                  "le jumeau de casse est signalé, jamais avalé"
          }

          test "scanFolder reports case twins inside the RAW subfolder too" {
              let folder = tempFolder ()
              let raw = rawSubfolder folder
              touch raw "P1.RW2"
              touch raw "P1.rw2"

              let scan = scanFolder folder

              Expect.equal scan.FiledOrphanRaws [ "P1.RW2" ] "le gagnant est classé normalement"
              Expect.equal
                  scan.CaseCollisions
                  [ { FileName = "P1.rw2"; InRawSubfolder = true } ]
                  "le jumeau de RAW/ est signalé avec son emplacement"
          }

          test "scanFolder sorts every category by stem for a stable display" {
              let folder = tempFolder ()

              for stem in [ "P1230002"; "P1230001"; "P1230003" ] do
                  touch folder (stem + ".JPG")
                  touch folder (stem + ".RW2")

              let scan = scanFolder folder

              Expect.equal
                  (scan.TandemsToOrganize |> List.map (fun t -> t.Stem))
                  [ "P1230001"; "P1230002"; "P1230003" ]
                  "tri stable par stem"
          } ]
