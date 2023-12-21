﻿
open System
open System.IO
open System.Diagnostics
open Fake.Core
open Fake.DotNet
open Fake.JavaScript
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

// open Fake.BuildServer

// BuildServer.install [ GitLab.Installer ]

let run (cmd : string) (args : string list) (dir : string) =
    if (Command.RawCommand (cmd, Arguments.OfArgs args)
    |> CreateProcess.fromCommand
    |> if not( String.isNullOrWhiteSpace dir) then CreateProcess.withWorkingDirectory dir else id
    |> CreateProcess.withTimeout System.TimeSpan.MaxValue
    |> Proc.run).ExitCode <> 0 then
        failwithf "Error while running '%s' with args: %A" cmd args

let platformTool tool path =
    match Environment.isUnix with
    | true -> tool
    | _ ->  match ProcessUtils.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")


let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/fsharp/FsAutoComplete/bin/release"

let releaseBinNetcore = releaseBin + "_netcore"
let fsacBinNetcore = fsacBin + "_netcore"

let cwtoolsPath = ""
let cwtoolsProjectName = "Main.fsproj"
let cwtoolsLinuxProjectName = "Main.Linux.fsproj"

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------
let initTargets () =

    Target.create "Clean" (fun _ ->
        Shell.cleanDir "./temp"
        Shell.cleanDir "./out/server"
        Shell.cleanDir "./out/client"
        // CopyFiles "release" ["README.md"; "LICENSE.md"]
        // CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
    )

    Target.create "YarnInstall" <| fun _ ->
        Npm.install id

    Target.create "DotNetRestore" <| fun _ ->
        DotNet.restore (fun p -> { p with Common = { p.Common with WorkingDirectory = "src/Main" }} ) cwtoolsProjectName
        DotNet.restore (fun p -> { p with Common = { p.Common with WorkingDirectory = "src/Main" }} ) cwtoolsLinuxProjectName

    let publishParams (framework : string) (release : bool) =
        (fun (p : DotNet.PublishOptions) ->
            { p with
                Common =
                    {
                        p.Common with
                            WorkingDirectory = "src/Main"
                            CustomParams = Some ("--self-contained true" + (if release then " /p:PublishReadyToRun=true" else " /p:LinkDuringPublish=false"))
                    }
                OutputPath = Some ("../../out/server/" + framework)
                Runtime = Some framework
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = { MSBuild.CliArguments.Create() with DisableInternalBinLog = true }
            })

    let buildParams (release : bool) (local : bool) =
        let args = ((if release then "" else " /p:LinkDuringPublish=false"))
        let args = if local then args + " /p:LocalPaket=True" else args
        (fun (b : DotNet.BuildOptions) ->
            { b with
                Common =
                    {
                        b.Common with
                            WorkingDirectory = "src/Main"
                            CustomParams = Some args
                    }
                OutputPath = Some ("../../out/server/local")
                Configuration = if release  then DotNet.BuildConfiguration.Release else DotNet.BuildConfiguration.Debug
                MSBuildParams = { MSBuild.CliArguments.Create() with DisableInternalBinLog = true }
            })

    Target.create "BuildDll" <| fun _ ->
        DotNet.build (buildParams true false) cwtoolsProjectName

    Target.create "BuildDllLocal" <| fun _ ->
        DotNet.build (buildParams true true) cwtoolsProjectName

    Target.create "BuildServer" <| fun _ ->
        match Environment.isWindows with
        |true -> DotNet.publish (publishParams "win-x64" false) cwtoolsProjectName
        |false -> DotNet.publish (publishParams "linux-x64" false) cwtoolsLinuxProjectName
        // DotNetCli.Publish (fun p -> {p with WorkingDir = "src/Main"; AdditionalArgs = ["--self-contained"; "true"; "/p:LinkDuringPublish=false"]; Output = "../../out/server/win-x64"; Runtime = "win-x64"; Configuration = "Release"})
        // DotNet.publish (publishParams "linux-x64" false) cwtoolsProjectName //(fun p -> {p with Common = { p.Common with WorkingDirectory = "src/Main"; CustomParams = Some "--self-contained true /p:LinkDuringPublish=false";}; OutputPath = Some "../../out/server/linux-x64"; Runtime =  Some "linux-x64"; Configuration = DotNet.BuildConfiguration.Release }) cwtoolsProjectName

    Target.create "PublishServer" <| fun _ ->
        DotNet.publish (publishParams "win-x64" true) cwtoolsProjectName
        DotNet.publish (publishParams "linux-x64" true) cwtoolsLinuxProjectName
        DotNet.publish (publishParams "osx-x64" true) cwtoolsProjectName

    let runTsc additionalArgs noTimeout =
        let cmd = "tsc"
        // let timeout = if noTimeout then System.TimeSpan.MaxValue else System.TimeSpan.FromMinutes 30.
        run cmd additionalArgs ""
    Target.create "RunScript" (fun _ ->
        match ProcessUtils.tryFindFileOnPath "npx" with
        |Some tsc ->
            CreateProcess.fromRawCommand tsc ["tsc"; "-p"; "./tsconfig.extension.json"]
            |> Proc.run
            |> (fun r -> if r.ExitCode <> 0 then failwith "tsc fail")
        // |Some tsc -> Process.directExec (fun (p : ProcStartInfo) -> p.WithFileName(tsc).WithArguments("tsc -p ./tsconfig.extension.json"))
        |_ -> failwith "didn't find tsc"
        match ProcessUtils.tryFindFileOnPath "npx" with
        |Some tsc ->
            CreateProcess.fromRawCommand tsc ["rollup"; "-c"; "-o"; "./out/client/webview/graph.js"]
            |> Proc.run
            |> (fun r -> if r.ExitCode <> 0 then failwith "rollup fail")
        |_ -> failwith "didn't find rollup"
        // Process.directExec (fun (p : ProcStartInfo) -> p.WithFileName("tsc").WithLoadUserProfile(true).WithUseShellExecute(false)) |> ignore
    )

    Target.create "CopyHtml" (fun _ ->
        !!("client/webview/*.css")
            |> Shell.copyFiles "out/client/webview"
    )

    // Target "Watch" (fun _ ->
    //     runFable "--watch" true
    // )

    // Target "CompileTypeScript" (fun _ ->
    //     // !! "**/*.ts"
    //     //     |> TypeScriptCompiler (fun p -> { p with OutputPath = "./out/client", Projec })
    //     //let cmd = "tsc -p ./"
    //     //DotNetCli.RunCommand id cmd
    //     ExecProcess (fun p -> p. <- "tsc" ;p.Arguments <- "-p ./") (TimeSpan.FromMinutes 5.0) |> ignore
    // )
    Target.create "PaketRestore" (fun _ ->
        Shell.replaceInFiles ["../cwtools",Path.getFullName("../cwtools")] ["paket.lock"]
        // match Environment.isWindows with
        // |true -> Paket.restore (fun _ -> Paket.PaketRestoreDefaults())
        // |_ -> Shell.Exec( "mono", @"./.paket/paket.exe restore") |> ignore
        Paket.restore (fun _ -> Paket.PaketRestoreDefaults())
        // Paket.PaketRestoreDefaults |> ignore
        Shell.replaceInFiles [Path.getFullName("../cwtools"),"../cwtools"] ["paket.lock"]
        )

    Target.create "CopyFSAC" (fun _ ->
        Directory.ensure releaseBin
        Shell.cleanDir releaseBin

        !!(fsacBin + "/*")
        |> Shell.copyFiles releaseBin
    )

    Target.create "CopyFSACNetcore" (fun _ ->
        Directory.ensure releaseBinNetcore
        Shell.cleanDir releaseBinNetcore

        Shell.copyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)
    )



    Target.create "InstallVSCE" ( fun _ ->
        Process.killAllByName "npm"
        run npmTool ["install"; "-g"; "vsce"] ""
    )


    Target.create "BuildPackage" ( fun _ ->
        Process.killAllByName "vsce"
        run vsceTool.Value ["package"] ""
        Process.killAllByName "vsce"
        !!("*.vsix")
        |> Seq.iter(Shell.moveFile "./temp/")
    )


    Target.create "PublishToGallery" ( fun _ ->
        let token =
            match Environment.environVarOrDefault "VSCE_TOKEN" System.String.Empty with
            | s when not (String.isNullOrWhiteSpace s) -> s
            | _ -> UserInput.getUserPassword "VSCE Token: "

        Process.killAllByName "vsce"
        run vsceTool.Value ["publish"; "patch"; "-p"; token] ""
    )



    // --------------------------------------------------------------------------------------
    // Run generator by default. Invoke 'build <Target>' to override
    // --------------------------------------------------------------------------------------

    Target.create "QuickBuild" ignore
    Target.create "QuickBuildLocal" ignore
    Target.create "Build" ignore
    Target.create "Release" ignore
    Target.create "DryRelease" ignore


open Fake.Core.TargetOperators
let buildTargetTree () =
    let (==>!) x y = x ==> y |> ignore
    "CopyHtml" ==>! "QuickBuild"
    "RunScript" ==>! "QuickBuild"
    "BuildDll" ==>! "QuickBuild"
    "CopyHtml" ==>! "QuickBuildLocal"
    "RunScript" ==>! "QuickBuildLocal"
    "BuildDllLocal" ==>! "QuickBuildLocal"

    "Clean"
    ==> "BuildServer"
    ==>! "Build"

    "Clean" ?=> "RunScript" |> ignore
    "Clean" ?=> "CopyHtml" |> ignore
    "Clean" ?=> "BuildDll" |> ignore
    "CopyHtml" ==>! "BuildPackage"
    "RunScript" ==>! "Build"
    "RunScript" ==>! "PublishServer"
    //"PaketRestore" ==> "BuildServer"
    //"PaketRestore" ==> "PublishServer"

    "Clean"
    ==> "PublishServer"
    ==> "BuildPackage"
    ==> "PublishToGallery"
    ==>! "Release"

    "PublishServer"
    ==>"BuildPackage"
    ==>! "DryRelease"
[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets()
    buildTargetTree()

    Target.runOrDefaultWithArguments "Build"
    0
