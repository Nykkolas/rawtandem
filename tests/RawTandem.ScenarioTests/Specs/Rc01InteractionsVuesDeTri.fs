module Specs.Rc01InteractionsVuesDeTri

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

/// Règle RC-01 — docs/usecases/rc-01-interactions-vues-de-tri.md.
/// 
/// Coverage : générique — tests dédiés valables pour toute vue de tri :
/// clavier (flèches, Home/End, Suppr), plein écran (F/F11/double-clic/Echap),
/// statut éphémère en plein écran et persistant en affichage normal.
/// 
/// Non couvert (headless) : le sélecteur natif et le branchement touche →
/// message ; le rendu fin des superpositions (bandeau, barre de statut) n'est
/// pas vérifié pixel par pixel.

[<Tests>]
let tests =
    testList "RC-01 : interactions dans les vues de tri" [

        /// Scénario exécutable de docs/usecases/rc-01-interactions-vues-de-tri.md.
        /// Coverage générique : un seul test pour toutes les vues de tri.
        ///
        /// Le test ne joue que les gestes de l'utilisateur. Le mapping clavier
        /// → message vit dans la vue (Main.fs, KeyDown) : la touche et le bouton
        /// dispatchent le même message (règle RC-01 « identiques selon le
        /// mapping »). Ce branchement vue n'est pas testable en headless —
        /// non couvert, comme le sélecteur natif de dossier.
        test "RC-01 common rule : keyboard interactions in the triaging view" {
            withPhotos (fun photos ->
                // Précondition RC-01 : une vue de tri (dossier organisé, 3 photos).
                let s = init () |> fst |> step (FolderSelected photos)
                Expect.equal s.Screen (Working Triaging) "la vue de tri est affichée"

                // RC-01 : flèche gauche sur la première photo → arrêt.
                let sLeft = s |> step PreviousClicked
                Expect.equal sLeft.CurrentIndex 0 "arrêt à la première photo"

                // RC-01 : flèche droite jusqu'à la dernière → arrêt.
                let s1 = s |> step NextClicked
                Expect.equal s1.CurrentIndex 1 "flèche droite : photo courante P2"
                let s2 = s1 |> step NextClicked
                Expect.equal s2.CurrentIndex 2 "flèche droite : photo courante P3"
                let s3 = s2 |> step NextClicked
                Expect.equal s3.CurrentIndex 2 "arrêt à la dernière photo"

                // RC-01 : Home → première photo, End → dernière photo.
                let sHome = s3 |> step FirstPhotoRequested
                Expect.equal sHome.CurrentIndex 0 "Home : photo courante P1"
                Expect.equal (currentJpgFileName sHome) (Some "P1.JPG") "RC-01 observable effect : le nom affiché suit la sélection"
                let sEnd = sHome |> step LastPhotoRequested
                Expect.equal sEnd.CurrentIndex 2 "End : photo courante P3"
                Expect.equal (currentJpgFileName sEnd) (Some "P3.JPG") "RC-01 observable effect : le nom affiché suit la sélection"

                // RC-01 : Suppr → l'écran de confirmation RC-02 s'affiche.
                let sDel = sEnd |> step DeleteTandemRequested
                Expect.equal sDel.Screen (Confirming(Triaging, DeleteTandem "P3")) "Suppr : la confirmation RC-02 s'affiche pour la photo courante")
        }

        /// Scénario exécutable de docs/usecases/rc-01-interactions-vues-de-tri.md
        /// (plein écran). Coverage générique, suite du test clavier ci-dessus.
        ///
        /// Le test ne joue que les gestes. F, F11, le double-clic et le bouton
        /// d'icône sont le même geste (mapping RC-01) : un seul message. Le
        /// branchement vue (touches réelles, double-clic, rendu sur fond noir,
        /// bandeau et barre de statut superposés) n'est pas testable en
        /// headless — non couvert, comme le sélecteur natif de dossier.
        test "RC-01 common rule : fullscreen interactions in the triaging view" {
            withPhotos (fun photos ->
                // Précondition RC-01 : une vue de tri (dossier organisé, 3 photos).
                let s = init () |> fst |> step (FolderSelected photos)
                Expect.equal s.DisplayMode Normal "affichage normal au départ"

                // RC-01 : F → plein écran, la photo courante ne change pas.
                let sF = s |> step FullscreenRequested
                Expect.equal sF.DisplayMode Fullscreen "F : la photo passe en plein écran"
                Expect.equal (currentJpgFileName sF) (Some "P1.JPG") "la photo affichée ne change pas"

                // RC-01 : en plein écran, le fonctionnement des touches reste valable.
                let s1 = sF |> step NextClicked
                Expect.equal s1.DisplayMode Fullscreen "on reste en plein écran en naviguant"
                Expect.equal s1.CurrentIndex 1 "les flèches naviguent toujours"

                // RC-01 : Echap → retour à l'affichage normal.
                let sBack = s1 |> step FullscreenExitRequested
                Expect.equal sBack.DisplayMode Normal "Echap : la photo quitte le plein écran"

                // RC-01 : F11 et F basculent (même geste que le double-clic).
                let sF11 = sBack |> step FullscreenRequested
                Expect.equal sF11.DisplayMode Fullscreen "F11 : plein écran"
                let sToggle = sF11 |> step FullscreenRequested
                Expect.equal sToggle.DisplayMode Normal "F en plein écran : retour à l'affichage normal"

                // RC-01 : en plein écran, une confirmation s'affiche par-dessus
                // l'image (RC-02 reste actif), et Echap ne quitte pas le plein
                // écran tant qu'elle est présentée à l'utilisateur.
                let sFull = sToggle |> step FullscreenRequested
                let sDel = sFull |> step DeleteTandemRequested
                Expect.equal sDel.Screen (Confirming(Triaging, DeleteTandem "P2")) "Suppr : la confirmation s'affiche par-dessus l'image"
                let sEchap = sDel |> step FullscreenExitRequested
                Expect.equal sEchap.DisplayMode Fullscreen "Echap ne quitte pas le plein écran pendant une confirmation"
                Expect.equal sEchap.Screen (Confirming(Triaging, DeleteTandem "P2")) "la confirmation reste présentée"

                // La question tranchée, Echap redevient actif.
                let sCancelled = sEchap |> step DeleteTandemCancelled
                Expect.equal sCancelled.Screen (Working Triaging) "la confirmation est annulée"
                let sNormalAgain = sCancelled |> step FullscreenExitRequested
                Expect.equal sNormalAgain.DisplayMode Normal "Echap : retour à l'affichage normal")
        }

        /// Scénario exécutable de docs/usecases/rc-01-interactions-vues-de-tri.md :
        /// en plein écran, la barre de statut n'apparaît que brièvement puis se
        /// cache « le reste du temps ». La machine programme l'effacement
        /// (commande temporisée → StatusHidden) : le pas de scénario exécute
        /// la commande réellement et attend son message — le délai réel est
        /// attendu, pas simulé.
        test "RC-01 common rule : ephemeral status in fullscreen, persistent in normal display" {
            withPhotos (fun photos ->
                // Vue normale : le rappel de suppression (RC-02) persiste.
                let sNormal = init () |> fst |> step (FolderSelected photos)
                let sDeletedNormal = sNormal |> step DeleteTandemRequested |> step DeleteTandemConfirmed
                Expect.isNonEmpty sDeletedNormal.Status "en vue normale, le statut persiste"

                // En plein écran : même parcours, le statut disparaît seul
                // après le délai (StatusHidden, garde plein écran).
                let sFull = init () |> fst |> step (FolderSelected photos) |> step FullscreenRequested
                let sDeletedFull = sFull |> step DeleteTandemRequested |> step DeleteTandemConfirmed
                Expect.equal sDeletedFull.Status "" "en plein écran, le statut est éphémère")
        }
    ]
