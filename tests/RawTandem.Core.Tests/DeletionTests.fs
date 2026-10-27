module RawTandem.Core.DeletionTests

open System.IO
open System.Runtime.InteropServices
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
              // Vraie corbeille OS (RC-03, multi-plateforme) : les fichiers
              // quittent le dossier sans suppression définitive.
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

          // ---------- sendToTrash (RC-03 : corbeille système multi-plateforme) ----------

          test "sendToTrash trashes a real file on the current OS" {
              // Vraie corbeille OS (RC-03) : corbeille Windows sous Windows,
              // `gio trash` (freedesktop) ailleurs. Non couvert : la présence
              // effective du fichier dans la corbeille système (contrat de
              // l'OS, non vérifiable de façon fiable multi-plateforme).
              let folder = tempFolder ()
              touch folder "P1.JPG"

              let result = sendToTrash (Path.Combine(folder, "P1.JPG"))

              Expect.isOk result "le fichier part à la corbeille OS"
              Expect.isFalse (File.Exists(Path.Combine(folder, "P1.JPG"))) "le fichier a quitté le dossier"
          }

          test "sendToTrash reports a missing file instead of crashing" {
              let result = sendToTrash (Path.Combine(tempFolder (), "inconnu.JPG"))

              match result with
              | Error reason -> Expect.isNonEmpty reason "la raison de l'échec est visible"
              | Ok() -> failtest "un fichier inexistant ne peut pas partir à la corbeille"
          }

          // ---------- trashFile (mécanisme par commande, branches d'erreur) ----------

          test "trashFile reports the exit code when the command fails without a message" {
              // La raison d'échec ne doit jamais être vide : elle alimente le
              // bandeau bas de l'UI. `false` sort en code 1 sans écrire sur
              // stderr (binaire standard sur Linux, y compris le runner CI) ;
              // sous Windows, `cmd /c exit 1` fait de même.
              let command, args =
                  if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                      "cmd.exe", [ "/c"; "exit 1" ]
                  else
                      "/usr/bin/false", [ "trash"; Path.Combine(tempFolder (), "P1.JPG") ]

              let result = trashFile command args

              match result with
              | Error reason -> Expect.stringContains reason "exited with code" "le code de sortie remplace le message manquant"
              | Ok() -> failtest "une commande d'échec ne peut pas réussir"
          }

          test "trashFile reports a missing command instead of crashing" {
              // Plateforme sans la commande : l'échec est propre et visible,
              // jamais un crash de l'application.
              let result = trashFile "rawtandem-commande-inexistante" [ "trash"; "P1.JPG" ]

              match result with
              | Error reason -> Expect.isNonEmpty reason "l'exception devient une raison visible"
              | Ok() -> failtest "une commande inexistante ne peut pas réussir"
          } ]
