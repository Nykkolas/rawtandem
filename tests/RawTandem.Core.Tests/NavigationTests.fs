module RawTandem.Core.NavigationTests

open Expecto
open RawTandem.Core.Navigation

[<Tests>]
let navigationTests =

    testList
        "Navigation"
        [ test "next moves forward and clamps at the last item" {
              Expect.equal (next 12 4) 5 "avance d'un cran"
              Expect.equal (next 12 11) 11 "borné au dernier"
          }

          test "previous moves backward and clamps at the first item" {
              Expect.equal (previous 12 4) 3 "recule d'un cran"
              Expect.equal (previous 12 0) 0 "borné au premier"
          }

          test "navigation on an empty list stays at zero" {
              Expect.equal (next 0 0) 0 "liste vide : reste à 0"
              Expect.equal (previous 0 0) 0 "liste vide : reste à 0"
          }

          test "removeItem removes the element and keeps the index in bounds" {
              let items = [ "P1230001"; "P1230002"; "P1230003" ]
              let remaining, index = removeItem "P1230002" items 1
              Expect.equal remaining [ "P1230001"; "P1230003" ] "l'élément est retiré"
              Expect.equal index 1 "l'index reste dans les bornes"
          }

          test "removeItem of the last element pulls the index back" {
              let items = [ "P1230001"; "P1230002" ]
              let remaining, index = removeItem "P1230002" items 1
              Expect.equal remaining [ "P1230001" ] "l'élément est retiré"
              Expect.equal index 0 "l'index recule au nouveau dernier"
          }

          test "removeItem of an absent element changes nothing" {
              let items = [ "P1230001"; "P1230002" ]
              let remaining, index = removeItem "P9999999" items 1
              Expect.equal remaining items "rien n'est retiré"
              Expect.equal index 1 "l'index est inchangé"
          } ]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args navigationTests
