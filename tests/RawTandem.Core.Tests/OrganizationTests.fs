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

          test "planOrganization leaves orphan RAWs at the root (signaled, never moved)" {
              // Option A : signaler, jamais réparer — le panneau « À corriger
              // vous-même » annonce que l'application ne touchera pas les
              // fichiers non appariés ; le plan doit tenir cette promesse.
              let folder = tempFolder ()

              let plan =
                  planOrganization folder { emptyScan with OrphanRawsToOrganize = [ "P2.RW2" ] }

              Expect.isEmpty plan.Moves "le RAW orphelin reste à la racine : signalé, jamais déplacé"
          }

          test "planOrganization moves only the RAWs of appaired tandems" {
              let folder = tempFolder ()

              let tandem stem : Tandem = { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          TandemsToOrganize = [ tandem "P1"; tandem "P2" ]
                          OrphanRawsToOrganize = [ "P9.RW2" ]
                          InvertedTandems = [ tandem "P3" ]
                          CrossedTandems = [ tandem "P4" ] }

              Expect.equal
                  plan.Moves
                  [ { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P1.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P1.RW2")
                      TargetRenamed = false }
                    { Kind = RawToSubfolder
                      SourcePath = Path.Combine(folder, "P2.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P2.RW2")
                      TargetRenamed = false } ]
                  "seuls les RAW des couples appariés sont déplacés"
          }

          test "planOrganization leaves crossed, inverted and orphan JPGs in place (signaled, never moved)" {
              let folder = tempFolder ()

              let plan =
                  planOrganization
                      folder
                      { emptyScan with
                          InvertedTandems = [ { Stem = "P3"; JpgFileName = "P3.JPG"; RawFileName = "P3.RW2" } ]
                          CrossedTandems = [ { Stem = "P4"; JpgFileName = "P4.JPG"; RawFileName = "P4.RW2" } ]
                          OrphanJpgsInRaw = [ "P5.JPG" ] }

              Expect.isEmpty plan.Moves "croisés, inversés et orphelins JPG : signalés, jamais déplacés"
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
        ]
