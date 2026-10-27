module RawTandem.Core.TestHelpers

open System
open System.IO
open Expecto

/// Crée un dossier temporaire unique par test (les tests Expecto tournent
/// en parallèle). Le dossier existe au retour.
let tempFolder () =
    let path =
        Path.Combine(Path.GetTempPath(), "rawtandem-tests", Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(path) |> ignore
    path

/// Crée un fichier d'une ligne dans le dossier donné.
let touch (folder: string) (fileName: string) =
    File.WriteAllText(Path.Combine(folder, fileName), "fixture")

/// Indique si le filesystem du dossier temporaire distingue la casse des
/// noms : vrai sous Linux (ext4), faux sous Windows (NTFS). Sonde de
/// capacité réelle, pas test d'OS : un NTFS peut être configuré sensible,
/// un volume peut venir d'un autre système — c'est le FS qui décide.
/// Leçon transverse : voir docs/ERREURS-GENERIC.md.
let isCaseSensitiveFileSystem () =
    let folder = tempFolder ()
    let probe = Path.Combine(folder, "sonde.tmp")
    File.WriteAllText(probe, "1")

    // FS insensible : « SONDE.tmp » rouvre le même fichier et écrase le
    // contenu « 1 » ; la relecture après écriture distingue les deux cas.
    File.WriteAllText(Path.Combine(folder, "SONDE.tmp"), "2")
    let sensitive = File.ReadAllText probe = "1"

    Directory.Delete(folder, true)
    sensitive

/// Test joué uniquement si le filesystem distingue la casse. Les états
/// décrits par ces tests (jumeaux « P1.JPG » + « P1.jpg », dossier « raw »
/// distinct de « RAW ») sont impossibles à créer sur un FS insensible :
/// l'OS refuse la coexistence. Le test y serait vert ou rouge pour une
/// raison erronée : il est ignoré (compté « ignoré »), jamais silencieusement
/// absent ni silencieusement vert.
let testCaseSensitiveOnly (name: string) (assertions: unit -> unit) : Expecto.Test =
    if isCaseSensitiveFileSystem () then
        Expecto.Tests.testCase name assertions
    else
        Expecto.Tests.ptestCase name assertions
