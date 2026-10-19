module RawTandem.Core.TestHelpers

open System
open System.IO

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
