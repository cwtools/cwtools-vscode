#r "paket:
    nuget Fake.Core
    nuget Fake.Core.Target
    nuget Fake.IO.FileSystem
    nuget Fake.DotNet.Cli
    nuget Fake.DotNet.Paket
    nuget Fake.JavaScript.Npm
    nuget Fake.Core.UserInput //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.JavaScript
open Fake.IO
open Fake.IO.Globbing.Operators


Target.create "BuildPackage" ( fun _ ->
    Process.killAllByName "vsce"
    run vsceTool.Value "package" ""
    Process.killAllByName "vsce"
    !!("*.vsix")
    |> Seq.iter(Shell.moveFile "./temp/")
)
