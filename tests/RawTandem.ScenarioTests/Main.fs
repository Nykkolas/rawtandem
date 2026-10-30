module Main

open Expecto
open Specs
open Harness.Headless

// L'initialisation du harnais (session headless unique, Skia enregistré pour
// tout le processus, Avalonia pompé une fois) doit précéder le moindre test :
// dans l'ancien fichier unique, elle se produisait au chargement du module ;
// ici le point d'entrée la force explicitement — certains tests (RC-04 pixel)
// rendent des bitmaps sans jamais toucher la session, mais ils dépendent de
// Skia pour autant.
let _ = headlessSession

/// Assemblage des scénarios : l'ordre des testLists suit l'ordre des
/// documents — les UC puis les règles communes (RC-01 → RC-06), le même
/// ordre que docs/usecases/.
[<EntryPoint>]
let main args =
    runTestsWithCLIArgs
        []
        args
        (testList
            "RawTandem.ScenarioTests"
            [ Uc01OrganiserEtTrierDossier.tests
              Uc02TrierDossierDejaOrganise.tests
              Rc01InteractionsVuesDeTri.tests
              Rc04AffichageJpgRotationExif.tests
              Rc05BandeauJauneModal.tests
              Rc06ActionsAsynchrones.tests ])
