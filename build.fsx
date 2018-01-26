open Fake.ProcessHelper
open Fake
// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I "packages/build/FAKE/tools"
#r "FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.ProcessHelper
open Fake.ReleaseNotesHelper
open Fake.NpmHelper
open Fake.ZipHelper
open TypeScript

let run cmd args dir =
    if execProcess( fun info ->
        info.FileName <- cmd
        if not( String.IsNullOrWhiteSpace dir) then
            info.WorkingDirectory <- dir
        info.Arguments <- args
    ) System.TimeSpan.MaxValue = false then
        failwithf "Error while running '%s' with args: %s" cmd args


let platformTool tool path =
    match isUnix with
    | true -> tool
    | _ ->  match ProcessHelper.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")


let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/fsharp/FsAutoComplete/bin/release"

let releaseBinNetcore = releaseBin + "_netcore"
let fsacBinNetcore = fsacBin + "_netcore"

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CleanDir "./out/server"
    // CopyFiles "release" ["README.md"; "LICENSE.md"]
    // CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target "YarnInstall" <| fun () ->
    Npm (fun p -> { p with Command = Install Standard })

Target "DotNetRestore" <| fun () ->
    DotNetCli.Restore (fun p -> { p with WorkingDir = "src/Main" } )


Target "BuildServer" <| fun () ->
    DotNetCli.Publish (fun p -> {p with WorkingDir = "src/Main"; AdditionalArgs = ["--self-contained"; "true"; "/p:LinkDuringPublish=false"]; Output = "../../out/server/win-x64"; Runtime = "win-x64"; Configuration = "Debug"})

Target "PublishServer" <| fun () ->
    DotNetCli.Publish (fun p -> {p with WorkingDir = "src/Main"; AdditionalArgs = ["--self-contained"]; Output = "../../out/server/win-x64"; Runtime = "win-x64"; Configuration = "Debug"})
    DotNetCli.Publish (fun p -> {p with WorkingDir = "src/Main"; AdditionalArgs = ["--self-contained"]; Output = "../../out/server/linux-x64"; Runtime = "linux-x64"; Configuration = "Debug"})
    //DotNetCli.Publish (fun p -> {p with Output = "../../out/server"; Configuration = "Debug";})

let runTsc additionalArgs noTimeout =
    let cmd = "tsc"
    let timeout = if noTimeout then TimeSpan.MaxValue else TimeSpan.FromMinutes 30.
    run cmd additionalArgs ""
Target "RunScript" (fun _ ->
    // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
    Shell.Exec @"C:\Users\Thomas\AppData\Roaming\npm\tsc.cmd" |> ignore
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
Target "PaketRestore" (fun _ ->
    Paket.PaketRestoreDefaults |> ignore)

Target "CopyFSAC" (fun _ ->
    ensureDirectory releaseBin
    CleanDir releaseBin

    !! (fsacBin + "/*")
    |> CopyFiles releaseBin
)

Target "CopyFSACNetcore" (fun _ ->
    ensureDirectory releaseBinNetcore
    CleanDir releaseBinNetcore

    CopyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)
)



Target "InstallVSCE" ( fun _ ->
    killProcess "npm"
    run npmTool "install -g vsce" ""
)


Target "BuildPackage" ( fun _ ->
    killProcess "vsce"
    run vsceTool.Value "package" ""
    killProcess "vsce"
    !! "*.vsix"
    |> Seq.iter(MoveFile "./temp/")
)


Target "PublishToGallery" ( fun _ ->
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "

    killProcess "vsce"
    run vsceTool.Value (sprintf "publish patch --pat %s" token) ""
)



// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target "Build" DoNothing
Target "Release" DoNothing
Target "DryRelease" DoNothing

//"YarnInstall" ?=> "RunScript"
//"DotNetRestore" ?=> "RunScript"

// "Clean"
// //==> "RunScript"
// ==> "Default"

"Clean"
//==> "RunScript"
//==> "CopyFSAC"
//==> "CopyFSACNetcore"
//==> "CopyForge"
//==> "CopyGrammar"
//==> "CopySchemas"
==> "BuildServer"
==> "Build"

"RunScript" ==> "Build"
"RunScript" ==> "PublishServer"
"PaketRestore" ==> "BuildServer"
"PaketRestore" ==> "PublishServer"
// "CompileTypeScript" ==> "Build"
//"DotNetRestore" ==> "BuildServer"
//"DotNetRestore" ==> "Build"

"Clean"
//==> "SetVersion"
// ==> "InstallVSCE"
==> "PublishServer"
==> "BuildPackage"
//==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"

"BuildPackage"
==> "DryRelease"

RunTargetOrDefault "Build"