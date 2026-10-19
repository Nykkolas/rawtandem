module RawTandem.Core.OrganizationTests

open System
open System.IO
open Expecto
open RawTandem.Core.Constants
open RawTandem.Core.TestHelpers
open RawTandem.Core.Tandems
open RawTandem.Core.Organization

[<Tests>]
let organizationTests =

    testList
        "Organization"
        [ // ---------- planRawMoves ----------

          test "planRawMoves maps each tandem RAW to the RAW subfolder, keeping its file name" {
              let folder = tempFolder ()
              touch folder "P1230001.JPG"
              touch folder "P1230001.RW2"

              let plan =
                  planRawMoves folder [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" } ]

              Expect.equal
                  plan.Moves
                  [ { SourcePath = Path.Combine(folder, "P1230001.RW2")
                      TargetPath = Path.Combine(folder, RawSubfolderName, "P1230001.RW2") } ]
                  "source à la racine, cible dans le sous-dossier"

              Expect.equal plan.RawDirectoryPath (Path.Combine(folder, RawSubfolderName)) "chemin du sous-dossier"
              Expect.isEmpty plan.Collisions "aucune collision à priori"
          }

          test "planRawMoves reports a collision when the target already exists in RAW" {
              let folder = tempFolder ()
              touch folder "P1230001.JPG"
              touch folder "P1230001.RW2"

              let rawDir = Path.Combine(folder, RawSubfolderName)
              Directory.CreateDirectory(rawDir) |> ignore
              touch rawDir "P1230001.RW2"

              let plan =
                  planRawMoves folder [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" } ]

              Expect.isEmpty plan.Moves "le RAW en collision n'est pas planifié"
              Expect.equal plan.Collisions [ "P1230001.RW2" ] "la collision est signalée, jamais écrasée"
          }

          test "planRawMoves with no tandem plans nothing" {
              let folder = tempFolder ()
              let plan = planRawMoves folder []

              Expect.isEmpty plan.Moves "aucun déplacement"
              Expect.isEmpty plan.Collisions "aucune collision"
          }

          // ---------- applyRawMovePlan ----------

          test "applyRawMovePlan moves the RAW files and creates the RAW subfolder" {
              let folder = tempFolder ()
              touch folder "P1230001.RW2"
              touch folder "P1230002.RW2"

              let plan =
                  planRawMoves
                      folder
                      [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" }
                        { Stem = "P1230002"; JpgFileName = "P1230002.JPG"; RawFileName = "P1230002.RW2" } ]

              let outcome = applyRawMovePlan plan

              Expect.equal outcome.Moved.Length 2 "les deux RAW sont déplacés"
              Expect.isEmpty outcome.Failed "aucun échec"

              Expect.isFalse (File.Exists(Path.Combine(folder, "P1230001.RW2"))) "la source n'est plus à la racine"
              Expect.isTrue (File.Exists(Path.Combine(folder, RawSubfolderName, "P1230001.RW2"))) "la cible existe"
              Expect.isTrue (File.Exists(Path.Combine(folder, RawSubfolderName, "P1230002.RW2"))) "la cible existe"
          }

          test "applyRawMovePlan never overwrites an existing target and reports it as a failure" {
              let folder = tempFolder ()
              touch folder "P1230001.RW2"

              let rawDir = Path.Combine(folder, RawSubfolderName)
              Directory.CreateDirectory(rawDir) |> ignore
              File.WriteAllText(Path.Combine(rawDir, "P1230001.RW2"), "ancien contenu")

              let plan =
                  planRawMoves folder [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" } ]

              // Le plan n'a pas vu la collision : on simule une cible apparue
              // entre le plan et l'exécution pour vérifier la garde d'exécution.
              let sourcePath = Path.Combine(folder, "P1230001.RW2")
              let targetPath = Path.Combine(rawDir, "P1230001.RW2")
              let plan = { plan with Moves = [ { SourcePath = sourcePath; TargetPath = targetPath } ] }

              let outcome = applyRawMovePlan plan

              Expect.equal outcome.Moved.Length 0 "rien n'est déplacé"
              Expect.equal outcome.Failed.Length 1 "l'échec est signalé"
              Expect.equal
                  (File.ReadAllText(Path.Combine(rawDir, "P1230001.RW2")))
                  "ancien contenu"
                  "le fichier existant n'est jamais écrasé"
          }

          test "applyRawMovePlan reports a partial failure and keeps the other moves" {
              let folder = tempFolder ()
              touch folder "P1230001.RW2"
              touch folder "P1230002.RW2"

              let plan =
                  planRawMoves
                      folder
                      [ { Stem = "P1230001"; JpgFileName = "P1230001.JPG"; RawFileName = "P1230001.RW2" }
                        { Stem = "P1230002"; JpgFileName = "P1230002.JPG"; RawFileName = "P1230002.RW2" } ]

              // La source du premier disparaît entre le plan et l'exécution.
              File.Delete(Path.Combine(folder, "P1230001.RW2"))

              let outcome = applyRawMovePlan plan

              Expect.equal outcome.Moved.Length 1 "le RAW présent est déplacé"
              Expect.equal outcome.Failed.Length 1 "le RAW absent est signalé"
              Expect.isTrue ((fst outcome.Failed.Head).SourcePath.EndsWith("P1230001.RW2")) "l'échec porte sur le bon fichier"
          }

          test "applyRawMovePlan on an empty plan creates nothing and moves nothing" {
              let folder = tempFolder ()
              let plan = planRawMoves folder []

              let outcome = applyRawMovePlan plan

              Expect.isEmpty outcome.Moved "aucun déplacement"
              Expect.isEmpty outcome.Failed "aucun échec"
              Expect.isFalse (Directory.Exists(Path.Combine(folder, RawSubfolderName))) "aucun dossier créé inutilement"
          } ]
