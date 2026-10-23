module RawTandem.Core.DeletionTests

open System.IO
open Expecto
open RawTandem.Core.Constants
open RawTandem.Core.TestHelpers
open RawTandem.Core.Tandems
open RawTandem.Core.FolderScan
open RawTandem.Core.Deletion

[<Tests>]
let deletionTests =

    testList
        "Deletion"
        [ let tandem stem = { Stem = stem; JpgFileName = stem + ".JPG"; RawFileName = stem + ".RW2" }

          // ---------- planDeletion ----------

          test "planDeletion plans the RAW at the root for a tandem to organize" {
              let folder = tempFolder ()

              let plan = planDeletion folder AtRoot (tandem "P1")

              Expect.equal
                  plan
                  { JpgPath = Path.Combine(folder, "P1.JPG"); RawPath = Path.Combine(folder, "P1.RW2") }
                  "les deux fichiers de la racine, chemins complets"
          }

          test "planDeletion plans the RAW in the subfolder for an organized tandem" {
              let folder = tempFolder ()

              let plan = planDeletion folder InRawSubfolder (tandem "P1")

              Expect.equal
                  plan
                  { JpgPath = Path.Combine(folder, "P1.JPG")
                    RawPath = Path.Combine(folder, RawSubfolderName, "P1.RW2") }
                  "le JPG reste à la racine, le RAW est dans RAW/"
          }

          // ---------- applyDeletion ----------

          test "applyDeletion sends both files to the OS trash" {
              // Vraie corbeille OS (gio trash) : les fichiers quittent le dossier
              // sans suppression définitive — la règle de sécurité du projet.
              let folder = tempFolder ()
              touch folder "P1.JPG"
              touch folder "P1.RW2"

              let outcome = applyDeletion (planDeletion folder AtRoot (tandem "P1"))

              Expect.equal
                  outcome.Trashed
                  [ Path.Combine(folder, "P1.JPG"); Path.Combine(folder, "P1.RW2") ]
                  "les deux fichiers sont partis à la corbeille"
              Expect.isEmpty outcome.Failed "aucun échec"
              Expect.isFalse (File.Exists(Path.Combine(folder, "P1.JPG"))) "le JPG a quitté le dossier"
              Expect.isFalse (File.Exists(Path.Combine(folder, "P1.RW2"))) "le RAW a quitté le dossier"
          }

          test "applyDeletion trashes the remaining file and reports the missing one" {
              // Le JPG a disparu entre le scan et la suppression : le RAW part
              // quand même, l'échec est signalé, rien ne crashe.
              let folder = tempFolder ()
              touch folder "P1.RW2"

              let outcome = applyDeletion (planDeletion folder AtRoot (tandem "P1"))

              Expect.equal outcome.Trashed [ Path.Combine(folder, "P1.RW2") ] "le RAW présent part à la corbeille"
              Expect.equal outcome.Failed.Length 1 "le JPG manquant est signalé"

              let failedPath, failedReason = List.head outcome.Failed

              Expect.equal failedPath (Path.Combine(folder, "P1.JPG")) "l'échec identifie le fichier"
              Expect.isNonEmpty failedReason "la raison de l'échec est visible"
              Expect.isFalse (File.Exists(Path.Combine(folder, "P1.RW2"))) "le RAW a quitté le dossier"
          }

          test "applyDeletion on a fully missing tandem reports both failures" {
              let folder = tempFolder ()

              let outcome = applyDeletion (planDeletion folder AtRoot (tandem "P9"))

              Expect.isEmpty outcome.Trashed "rien n'est parti à la corbeille"
              Expect.equal outcome.Failed.Length 2 "les deux fichiers manquants sont signalés"
          }

          // ---------- trashFile (branches d'erreur) ----------

          test "trashFile reports the exit code when the command fails without a message" {
              // La raison d'échec ne doit jamais être vide : elle alimente le
              // bandeau bas de l'UI. `false` sort en code 1 sans écrire sur
              // stderr (binaire standard sur Linux, y compris le runner CI).
              let result = trashFile "/usr/bin/false" (Path.Combine(tempFolder (), "P1.JPG"))

              match result with
              | Error reason -> Expect.stringContains reason "exited with code" "le code de sortie remplace le message manquant"
              | Ok() -> failtest "false ne peut pas réussir"
          }

          test "trashFile reports a missing command instead of crashing" {
              // Plateforme sans `gio` (ex. Windows avant l'évolution multi-plateforme) :
              // l'échec est propre et visible, jamais un crash de l'application.
              let result = trashFile "rawtandem-commande-inexistante" (Path.Combine(tempFolder (), "P1.JPG"))

              match result with
              | Error reason -> Expect.isNonEmpty reason "l'exception devient une raison visible"
              | Ok() -> failtest "une commande inexistante ne peut pas réussir"
          } ]
