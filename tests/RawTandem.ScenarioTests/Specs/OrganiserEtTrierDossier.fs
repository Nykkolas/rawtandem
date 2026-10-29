module Specs.OrganiserEtTrierDossier

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

/// UC organiser-et-trier-dossier — docs/usecases/organiser-et-trier-dossier.md.
/// 
/// Coverage : instancié — le scénario primaire (étapes 1 à 13) et les extensions
/// 2a (problèmes d'organisation) et 2b (dossier « raw » mal cassé, test ignoré
/// sur un filesystem insensible à la casse pour raison erronée jamais silencieuse)
/// y sont rejoués : vrais fichiers temporaires, vraie corbeille OS, vraies
/// commandes Elmish en cascade.
/// 
/// Non couvert (headless) : le sélecteur natif de dossier (dialogue OS) et le
/// branchement touche → message (KeyDown vit dans la vue).

[<Tests>]
let tests =
    testList "UC organiser-et-trier-dossier" [

        test "UC organiser-et-trier-dossier : organize a fresh dump then delete one unwanted tandem" {
            withPellicule (fun pellicule ->
                let stems = [ "P1"; "P2"; "P3" ]

                // UC-1 : ouverture du dossier. Le sélecteur natif n'est pas
                // testé ; le vrai scanCmd part et son FolderScanned revient.
                let s2 = init () |> fst |> step (FolderSelected pellicule)

                // UC-2 : l'application propose l'organisation (3 couples).
                Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                Expect.equal s2.Scan.TandemsToOrganize.Length 3 "3 couples appariés à organiser"
                Expect.equal s2.Scan.OrganizedTandems.Length 0 "aucun couple déjà organisé"

                // UC-3 : l'utilisateur demande l'organisation.
                // UC-4 : l'application demande confirmation, 3 RW2 annoncés.
                let s4 = s2 |> step OrganizeRequested
                Expect.equal s4.Screen (Confirming(Organizing, OrganizeFolder 3)) "UC-4 : 3 fichiers déplacés annoncés"

                // UC-5 : l'utilisateur confirme.
                // UC-6 : l'application crée RAW/ et déplace les RW2 (vrais déplacements).
                let s6 = s4 |> step OrganizeConfirmed
                Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"
                let rawDir = Path.Combine(pellicule, RawSubfolderName)
                for stem in stems do
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, stem + ".RW2"))) "le RW2 est rangé dans RAW/"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, stem + ".JPG"))) "le JPG reste à la racine"

                // UC-7 : navigation vers la deuxième photo. Le rendu visuel du
                // JPG n'est pas testé : fichiers vides, le chargement réel échoue.
                let s7 = s6 |> step NextClicked
                Expect.equal s7.CurrentIndex 1 "photo courante : P2"

                // UC-8 : l'application affiche la photo en cours et le nom du
                // fichier JPG (XXXX.JPG) — le nom exposé par l'état est testé,
                // le rendu (TextBlock) ne l'est pas.
                Expect.equal (currentJpgFileName s7) (Some "P2.JPG") "UC-8 : le nom du fichier JPG courant est affiché"

                // UC-8 : l'utilisateur demande la suppression.
                // UC-9 : l'application affiche le nom du fichier à supprimer.
                let s8 = s7 |> step DeleteTandemRequested
                Expect.equal s8.Screen (Confirming(Triaging, DeleteTandem "P2")) "UC-9 : le nom du fichier est affiché"

                // UC-10 : l'utilisateur confirme.
                // UC-11 : le couple part à la corbeille (vrai gio trash).
                let s10 = s8 |> step DeleteTandemConfirmed
                Expect.equal (triableTandems s10).Length 2 "le tandem supprimé quitte le tri"
                Expect.isFalse (File.Exists(Path.Combine(pellicule, "P2.JPG"))) "le JPG est parti à la corbeille"
                Expect.isFalse (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "le RW2 est parti à la corbeille"

                // UC-12 : une seule itération de la boucle — la sortie (« plus
                // de photo à supprimer ») est une décision de l'utilisateur,
                // pas un état observable de la machine.

                // Garantie minimale : les 6 fichiers créés sont tous comptés,
                // aucun n'est perdu ni écrasé.
                let present =
                    [ yield! Directory.EnumerateFiles(pellicule)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 4 "P1 et P3 restent des deux côtés, seuls les fichiers de P2 sont partis")
        }

        /// Scénario exécutable de docs/usecases/organiser-et-trier-dossier.md,
        /// extension 2a. Un seul dossier « mélange » : le flux est prouvé
        /// (lister → corriger à la main → réanalyser → interface à jour →
        /// reprise du primaire), la matrice des catégories de problèmes reste
        /// en tests unitaires (FolderScanTests).
        test "UC organiser-et-trier-dossier 2a : fix problems manually, reanalyze, then organize" {
            withPelliculeAProblemes (fun pellicule ->
                let rawDir = Path.Combine(pellicule, RawSubfolderName)

                // 2a-1 : ouverture du dossier — l'application propose
                // l'organisation et liste les problèmes d'organisation.
                let s2 = init () |> fst |> step (FolderSelected pellicule)

                Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                Expect.equal s2.Scan.TandemsToOrganize.Length 2 "2 couples à organiser (P1, P2)"
                Expect.equal s2.Scan.OrganizedTandems.Length 1 "1 couple déjà organisé (P4)"
                Expect.equal s2.Scan.InvertedTandems.Length 1 "problème listé : P3 inversé"
                Expect.equal s2.Scan.DuplicateRaws.Length 1 "problème listé : doublon de P4"
                Expect.equal s2.Scan.OrphanJpgs.Length 1 "problème listé : P5 orphelin"

                // 2a-2 : correction manuelle dans l'explorateur (hors de
                // l'application) : le JPG de P3 revient à la racine (le couple
                // inversé devient un couple organisé), le doublon de P4 part.
                // P5 n'est pas corrigé : l'utilisateur est satisfait (2a-5) —
                // il organisera malgré le signal.
                File.Move(Path.Combine(rawDir, "P3.JPG"), Path.Combine(pellicule, "P3.JPG"))
                File.Delete(Path.Combine(pellicule, "P4.RW2"))

                // 2a-3 : l'utilisateur réanalyse le dossier.
                // 2a-4 : l'interface se met entièrement à jour (comptes du scan).
                let s4 = s2 |> step RescanRequested

                Expect.equal s4.Scan.TandemsToOrganize.Length 2 "toujours 2 couples à organiser (P1, P2)"
                Expect.equal s4.Scan.OrganizedTandems.Length 2 "P3 corrigé devient un couple organisé"
                Expect.equal s4.Scan.InvertedTandems.Length 0 "plus de couple inversé"
                Expect.equal s4.Scan.DuplicateRaws.Length 0 "plus de doublon"
                Expect.equal s4.Scan.OrphanJpgs.Length 1 "P5 reste signalé, l'utilisateur a choisi de le garder"

                // Seuls les couples correctement appariés s'affichent au
                // tri : P3 devenu apparié y entre, l'orphelin P5 n'y entre
                // jamais (signalé, pas géré).
                Expect.equal
                    (s4 |> triableTandems |> List.map (fun t -> t.Stem))
                    [ "P1"; "P2"; "P3"; "P4" ]
                    "la liste de tri ne montre que les couples appariés, jamais P5"

                // 2a-5 : l'utilisateur est satisfait, le scénario principal
                // reprend — il organise (vrais déplacements sur le disque).
                let s5 = s4 |> step OrganizeRequested
                Expect.equal s5.Screen (Confirming(Organizing, OrganizeFolder 2)) "UC-4 : 2 RW2 appariés annoncés"

                let s6 = s5 |> step OrganizeConfirmed
                Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"

                // Le rangement remet le scan à jour (bug du scan périmé,
                // 2026-10-24, corrigé par le rescan en cascade) : les 4
                // couples sont organisés et leurs RW2 sont cherchés dans
                // RAW/ — un plan de suppression ne peut plus viser la racine.
                Expect.equal s6.Scan.TandemsToOrganize.Length 0 "plus rien à organiser"
                Expect.equal s6.Scan.OrganizedTandems.Length 4 "les 4 couples sont organisés"

                for stem in [ "P1"; "P2"; "P3"; "P4" ] do
                    match findTriableTandem s6.Scan stem with
                    | Some(_, InRawSubfolder) -> ()
                    | _ -> failtestf "le RW2 de %s doit être cherché dans RAW/ après le rangement" stem

                Expect.equal
                    (s6 |> triableTandems |> List.map (fun t -> t.Stem))
                    [ "P1"; "P2"; "P3"; "P4" ]
                    "la liste de tri montre les 4 couples appariés rangés"

                for stem in [ "P1"; "P2" ] do
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, stem + ".RW2"))) "le RW2 du couple apparié est rangé"

                for stem in [ "P1"; "P2"; "P3"; "P4" ] do
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, stem + ".JPG"))) "le JPG reste à la racine"

                // Signal-only : l'orphelin P5 n'est jamais touché par
                // l'application — il reste exactement où il est.
                Expect.isTrue (File.Exists(Path.Combine(pellicule, "P5.JPG"))) "l'orphelin signalé n'est pas déplacé"

                // Garantie minimale : 9 fichiers présents après correction (le
                // doublon supprimé par l'utilisateur en moins des 10 créés),
                // aucun n'est perdu ni écrasé par l'application.
                let present =
                    [ yield! Directory.EnumerateFiles(pellicule)
                      yield! Directory.EnumerateFiles(rawDir) ]
                Expect.equal present.Length 9 "aucun fichier perdu ni écrasé")
        }

        /// Scénario exécutable de docs/usecases/organiser-et-trier-dossier.md,
        /// extension 2b : le répertoire « raw » mal cassé bloque le rangement
        /// (refus gardé dans update, déjà testé en unitaire — le scénario ne
        /// peut pas cliquer un bouton grisé) ; correction manuelle, réanalyse,
        /// retour au primaire.
        testScenarioCaseSensitiveOnly
            "UC organiser-et-trier-dossier 2b : wrong-cased raw folder blocks organize until fixed and reanalyzed"
            (fun () ->
                withDossierRawMalCassee (fun pellicule ->
                    let rawDir = Path.Combine(pellicule, RawSubfolderName)
    
                    // 2b-1 : ouverture — l'anomalie est signalée (même panneau que
                    // 2a-1) et le contenu de « raw » est invisible au scan.
                    let s2 = init () |> fst |> step (FolderSelected pellicule)
    
                    Expect.equal s2.Screen (Working Organizing) "l'écran d'organisation est proposé"
                    Expect.equal s2.Scan.TandemsToOrganize.Length 1 "1 couple à organiser (P1)"
                    Expect.equal s2.Scan.RawFolderIssue (Some(RawFolderCaseMismatch "raw")) "l'anomalie du dossier « raw » est signalée"
                    Expect.stringContains s2.Status "mal cass" "le signal est visible dans le bandeau"
    
                    // 2b-1 : « l'application ne permet pas de ranger ». Le bouton
                    // est grisé (affordance, non observable ici) : on vérifie le
                    // filet de sécurité — même si le message de rangement arrive
                    // (futur raccourci clavier), la machine refuse, et le disque
                    // ne bouge pas. Garde aussi couverte en unitaire ; rejouée
                    // ici parce que le refus est une garantie de l'extension.
                    let sRefusal = s2 |> step OrganizeRequested
    
                    Expect.equal sRefusal.Screen (Working Organizing) "le rangement est refusé : aucune confirmation ouverte"
                    Expect.stringContains sRefusal.Status "renommez" "le refus explique quoi faire"
                    Expect.isFalse (Directory.Exists rawDir) "aucun dossier « RAW » n'est créé pendant l'anomalie"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, "P1.RW2"))) "aucun fichier déplacé pendant l'anomalie"
    
                    // 2b-2 : correction manuelle dans l'explorateur : « raw »
                    // redevient « RAW ».
                    Directory.Move(Path.Combine(pellicule, "raw"), rawDir)
    
                    // 2b-3 : l'utilisateur réanalyse.
                    // 2b-4 : retour à l'étape 2 du primaire — l'anomalie a
                    // disparu et le contenu du dossier renommé devient visible.
                    let s4 = sRefusal |> step RescanRequested
    
                    Expect.equal s4.Scan.RawFolderIssue None "l'anomalie a disparu après correction et réanalyse"
                    Expect.equal s4.Scan.TandemsToOrganize.Length 1 "le couple P1 est toujours proposé"
                    Expect.equal s4.Scan.FiledOrphanRaws.Length 1 "le RW2 de « raw » est maintenant visible (orphelin rangé)"
    
                    // « Le bouton Ranger est de nouveau accessible » : l'état
                    // grisé d'un bouton n'est pas observable ici (sémantique de
                    // vue), la preuve fonctionnelle est le geste joué — la
                    // demande de rangement aboutit désormais à la confirmation
                    // (elle était refusée s2 → sRefusal, elle passe s4 → s5).
                    let s5 = s4 |> step OrganizeRequested
                    Expect.equal s5.Screen (Confirming(Organizing, OrganizeFolder 1)) "UC-4 : le rangement redevient possible, 1 RW2 apparié annoncé"
    
                    let s6 = s5 |> step OrganizeConfirmed
                    Expect.equal s6.Screen (Working Triaging) "passage automatique au tri après l'organisation"
    
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, "P1.RW2"))) "le RW2 du couple apparié est rangé"
                    Expect.isTrue (File.Exists(Path.Combine(pellicule, "P1.JPG"))) "le JPG reste à la racine"
    
                    // Signal-only : l'orphelin rangé P2, devenu visible à la
                    // réanalyse, n'est pas touché par le rangement.
                    Expect.isTrue (File.Exists(Path.Combine(rawDir, "P2.RW2"))) "l'orphelin rangé n'est pas déplacé"
    
                    // Garantie minimale : les 3 fichiers créés sont tous comptés.
                    let present =
                        [ yield! Directory.EnumerateFiles(pellicule)
                          yield! Directory.EnumerateFiles(rawDir) ]
                    Expect.equal present.Length 3 "aucun fichier perdu ni écrasé"))
    ]
