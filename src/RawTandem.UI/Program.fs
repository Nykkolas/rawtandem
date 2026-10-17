module Program

open Avalonia
open Avalonia.Skia

open App

[<EntryPoint>]
let main args =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .UseSkia()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args)
