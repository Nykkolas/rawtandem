module Specs.TrierDossierDejaOrganise

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

/// UC trier-dossier-deja-organise — docs/usecases/trier-dossier-deja-organise.md.
/// 
/// Coverage : instancié — dossier déjà organisé : tri affiché directement,
/// navigation, suppression d'un tandem (vraie corbeille OS).
/// 
/// Non couvert (headless) : le sélecteur natif de dossier et le branchement
/// touche → message.

[<Tests>]
let tests =
    testList "UC trier-dossier-deja-organise" [

        test "UC trier-dossier-deja-organise : triage an already organized folder directly" {
            withPhotos (fun photos ->
                let rawDir = Path.Combine(photos, RawSubfolderName)

                // UC-1 : ouverture du dossier. Le sélecteur natif n'est pas
                // testé ; le vrai scanCmd part et son FolderScanned revient.
                let s2 = init () |> fst |> step (FolderSelected photos)

                // UC-2 : l'application affiche directement le mode tri — le
                // dossier est déjà organisé, aucune anomalie : rien à proposer.
                Expect.equal s2.Screen (Working Triaging) "UC-2 : le tri est affiché directement, sans passer par l'organisation"
                Expect.equal s2.Scan.TandemsToOrganize.Length 0 "aucun couple à organiser"
                Expect.equal s2.Scan.OrganizedTandems.Length 3 "3 couples déjà organisés"

                // UC-3 : navigation vers la deuxième photo. Le rendu visuel du
                // JPG n'est pas testé : fichiers vides, le chargement réel échoue.
                let s3 = s2 |> step NextClicked
                Expect.equal s3.CurrentIndex 1 "photo courante : P2"

                // UC-4 : l'application affiche la photo en cours et le nom du
                // fichier JPG (XXXX.JPG) — le nom exposé par l'état est testé,
                // le rendu (TextBlock) ne l'est pas.
                Expect.equal (currentJpgFileName s3) (Some "P2.JPG") "UC-4 : le nom du fichier JPG courant est affiché"

                // UC-5 : l'utilisateur demande la suppression.
                // UC-6 : l'application affiche le nom du fichier à supprimer.
                let s5 = s3 |> step DeleteTandemRequested
                Expect.equal s5.Screen (Confirming(Triaging, DeleteTandem "P2")) "UC-6 : le nom du fichier est affiché"

                // UC-7 : l'utilisateur confirme.
                // UC-8 : le couple part à la corbeille (vrai gio trash).
                let s7 = s5 |> step DeleteTandemConfirmed
                Expect.equal (triableTandems s7).Length 2 "le tandem supprimé quitte le tri"
                Expect.isFalse (File.Exists(Path.Combine(photos, "P2.JPG"))) "le JPG est parti à la corbeille"
                Expect.isFalse (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "le RW2 est parti à la corbeille"

                // UC-9 : une seule itération de la boucle — la sortie (« plus
                // de photo à supprimer ») est une décision de l'utilisateur,
                // pas un état observable de la machine.

                // Garantie de succès : les photos non supprimées restent
                // intactes, chacune des deux côtés.
                let present =
                    [ yield! Directory.EnumerateFiles(photos)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 4 "P1 et P3 restent des deux côtés, seuls les fichiers de P2 sont partis")
        }
    ]
