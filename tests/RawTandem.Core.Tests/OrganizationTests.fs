module RawTandem.Core.OrganizationTests

open System
open System.IO
open Expecto
open RawTandem.Core.Constants
open RawTandem.Core.TestHelpers
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Organization

[<Tests>]
let organizationTests =

    testList
        "Organization"
        [ // ---------- planOrganization ----------

          let rawDir (folder: string) =
              let raw = Path.Combine(folder, RawSubfolderName)
              Directory.CreateDirectory(raw) |> ignore
              raw

          test "planOrganization moves the RAW of a nominal tandem to the subfolder" {
              let folder = tempFolder ()

              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          TandemsToOrganize =
                              [ { Stem = "P1"; JpgFileName = "P1.JPG"; RawFileName = "P1.RW2" } ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P1.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P1.RW2")
                      TargetRenamed = false } ]
                  "un déplacement RAW, sans renommage"

              Expect.equal plan.RawDirectoryPath (Path.Combine(folder, RawSubfolderName)) "chemin du sous-dossier"
          }

          test "planOrganization also moves orphan RAWs from the root" {
              let folder = tempFolder ()

              let plan =
                  planOrganization folder { emptyScan with OrphanRawsToOrganize = [ "P2.RW2" ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P2.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P2.RW2")
                      TargetRenamed = false } ]
                  "le RAW orphelin rejoint le sous-dossier"
          }

          test "planOrganization moves JPGs from the subfolder back to the root" {
              let folder = tempFolder ()

              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          InvertedTandems = [ { Stem = "P3"; JpgFileName = "P3.JPG"; RawFileName = "P3.RW2" } ]
                          CrossedTandems = [ { Stem = "P4"; JpgFileName = "P4.JPG"; RawFileName = "P4.RW2" } ]
                          OrphanJpgsInRaw = [ "P5.JPG" ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = JpgToRoot
                      SourcePath = Path.Combine(folder, RawSubfolderName, "P4.JPG")
                      TargetPath = Path.Combine(folder, "P4.JPG")
                      TargetRenamed = false }
                    { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P4.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P4.RW2")
                      TargetRenamed = false }
                    { Kind = JpgToRoot
                      SourcePath = Path.Combine(folder, RawSubfolderName, "P3.JPG")
                      TargetPath = Path.Combine(folder, "P3.JPG")
                      TargetRenamed = false }
                    { Kind = JpgToRoot
                      SourcePath = Path.Combine(folder, RawSubfolderName, "P5.JPG")
                      TargetPath = Path.Combine(folder, "P5.JPG")
                      TargetRenamed = false } ]
                  "croisés d'abord (JPG → racine puis RAW → RAW/), puis inversés et orphelins JPG → racine"
          }

          test "planOrganization on an already organized scan plans nothing" {
              let folder = tempFolder ()

              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          OrganizedTandems = [ { Stem = "P6"; JpgFileName = "P6.JPG"; RawFileName = "P6.RW2" } ]
                          FiledOrphanRaws = [ "P7.RW2" ]
                          OrphanJpgs = [ "P8.JPG" ] }

              Expect.isEmpty plan.Moves "rien à déplacer : tout est déjà en place"
          }

          test "planOrganization renames a target that unexpectedly exists, following the system convention" {
              let folder = tempFolder ()
              let raw = rawDir folder
              touch folder "P9.RW2"
              touch raw "P9.RW2"
              touch raw "P9 (1).RW2"

              // Doublon statique à part, on force un plan sur un tandem à organiser
              // dont la cible est déjà prise par un fichier imprévu.
              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          TandemsToOrganize = [ { Stem = "P9"; JpgFileName = "P9.JPG"; RawFileName = "P9.RW2" } ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P9.RW2")
                      TargetPath = Path.Combine(raw, "P9 (2).RW2")
                      TargetRenamed = true } ]
                  "jamais d'écrasement : renommage « stem (n).ext » jusqu'au premier libre"
          }

          // ---------- applyOrganization ----------

          test "applyOrganization moves the planned files and creates the RAW subfolder only when needed" {
              let folder = tempFolder ()
              touch folder "P1.RW2"

              let rawOnlyFolder = tempFolder ()
              touch rawOnlyFolder "P2.RW2"

              let plan =
                  { RawDirectoryPath = Path.Combine(folder, RawSubfolderName)
                    Moves =
                      [ { Kind = RawToSubfolder
                          SourcePath = Path.Combine(folder, "P1.RW2")
                          TargetPath = Path.Combine(folder, RawSubfolderName, "P1.RW2")
                          TargetRenamed = false } ] }

              let outcome = applyOrganization plan

              Expect.equal outcome.Moved.Length 1 "le RAW est déplacé"
              Expect.isEmpty outcome.Failed "aucun échec"
              Expect.isTrue (File.Exists(Path.Combine(folder, RawSubfolderName, "P1.RW2"))) "la cible existe"
              Expect.isFalse (File.Exists(Path.Combine(folder, "P1.RW2"))) "la source n'est plus à la racine"

              // Plan sans déplacement vers le sous-dossier : le dossier RAW
              // ne doit pas être créé inutilement.
              let jpgOnly =
                  { RawDirectoryPath = Path.Combine(rawOnlyFolder, RawSubfolderName)
                    Moves =
                      [ { Kind = JpgToRoot
                          SourcePath = Path.Combine(rawOnlyFolder, RawSubfolderName, "P2.RW2")
                          TargetPath = Path.Combine(rawOnlyFolder, "P2.RW2")
                          TargetRenamed = false } ] }

              let jpgOutcome = applyOrganization jpgOnly

              Expect.equal jpgOutcome.Failed.Length 1 "le plan incohérent échoue proprement"
              Expect.isFalse (Directory.Exists(Path.Combine(rawOnlyFolder, RawSubfolderName))) "aucun dossier créé inutilement"
          }

          test "applyOrganization never overwrites and reports a partial failure" {
              let folder = tempFolder ()
              touch folder "P1.RW2"
              touch folder "P2.RW2"
              let raw = rawDir folder
              File.WriteAllText(Path.Combine(raw, "P1.RW2"), "ancien")

              // P1 : cible apparue après le plan ; P2 : nominal.
              let plan =
                  { RawDirectoryPath = raw
                    Moves =
                      [ { Kind = RawToSubfolder
                          SourcePath = Path.Combine(folder, "P1.RW2")
                          TargetPath = Path.Combine(raw, "P1.RW2")
                          TargetRenamed = false }
                        { Kind = RawToSubfolder
                          SourcePath = Path.Combine(folder, "P2.RW2")
                          TargetPath = Path.Combine(raw, "P2.RW2")
                          TargetRenamed = false } ] }

              let outcome = applyOrganization plan

              Expect.equal outcome.Moved.Length 1 "le déplacement sain est effectué"
              Expect.equal outcome.Failed.Length 1 "l'échec est signalé"
              Expect.equal
                  (File.ReadAllText(Path.Combine(raw, "P1.RW2")))
                  "ancien"
                  "le fichier existant n'est jamais écrasé"
          }

          test "applyOrganization on an empty plan does nothing" {
              let folder = tempFolder ()

              let outcome =
                  applyOrganization { RawDirectoryPath = Path.Combine(folder, RawSubfolderName); Moves = [] }

              Expect.isEmpty outcome.Moved "aucun déplacement"
              Expect.isEmpty outcome.Failed "aucun échec"
              Expect.isFalse (Directory.Exists(Path.Combine(folder, RawSubfolderName))) "aucun dossier créé"
          }

          // ---------- planDuplicateResolution ----------

          test "planDuplicateResolution renames the out-of-place RAW copy into the subfolder" {
              let folder = tempFolder ()
              let raw = rawDir folder
              touch folder "P1.RW2"
              touch raw "P1.RW2"

              let plan = planDuplicateResolution folder { emptyScan with DuplicateRaws = [ "P1.RW2" ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P1.RW2")
                      TargetPath = Path.Combine(raw, "P1 (1).RW2")
                      TargetRenamed = true } ]
                  "le doublon racine rejoint le sous-dossier, renommé selon la convention système"
          }

          test "planDuplicateResolution renames the out-of-place JPG copy back to the root" {
              let folder = tempFolder ()
              let raw = rawDir folder
              touch folder "P1.JPG"
              touch raw "P1.JPG"

              let plan = planDuplicateResolution folder { emptyScan with DuplicateJpgs = [ "P1.JPG" ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = JpgToRoot
                      SourcePath = Path.Combine(raw, "P1.JPG")
                      TargetPath = Path.Combine(folder, "P1 (1).JPG")
                      TargetRenamed = true } ]
                  "le doublon de RAW/ rejoint la racine, renommé"
          }

          test "planDuplicateResolution renames a case twin within its directory" {
              let folder = tempFolder ()
              touch folder "P1.JPG"
              touch folder "P1.jpg"

              let plan =
                  planDuplicateResolution folder { emptyScan with CaseCollisions = [ { FileName = "P1.jpg"; InRawSubfolder = false } ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RenameInPlace
                      SourcePath = Path.Combine(folder, "P1.jpg")
                      TargetPath = Path.Combine(folder, "P1 (1).jpg")
                      TargetRenamed = true } ]
                  "le jumeau est renommé selon la convention système, dans son répertoire"
          }

          test "planDuplicateResolution without duplicates plans nothing" {
              let folder = tempFolder ()
              let plan = planDuplicateResolution folder emptyScan
              Expect.isEmpty plan.Moves "aucun doublon, aucun déplacement"
          }

          // ---------- normalizeRawFolderName ----------

          test "normalizeRawFolderName renames a wrong-case folder to RAW" {
              let folder = tempFolder ()
              let wrongCase = Path.Combine(folder, "raw")
              Directory.CreateDirectory(wrongCase) |> ignore
              touch wrongCase "P1.RW2"

              normalizeRawFolderName folder "raw"

              Expect.isTrue (Directory.Exists(Path.Combine(folder, RawSubfolderName))) "le dossier est renommé en RAW"
              Expect.isFalse (Directory.Exists(wrongCase)) "l'ancien nom a disparu"
              Expect.isTrue (File.Exists(Path.Combine(folder, RawSubfolderName, "P1.RW2"))) "le contenu suit"
          }

          test "normalizeRawFolderName refuses to run when RAW already exists" {
              let folder = tempFolder ()
              let wrongCase = Path.Combine(folder, "raw")
              Directory.CreateDirectory(wrongCase) |> ignore
              rawDir folder |> ignore

              Expect.throws
                  (fun () -> normalizeRawFolderName folder "raw")
                  "le renommage est refusé si « RAW » existe déjà"
          } ]
