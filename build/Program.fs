open System
open System.IO
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Tools.Git
open Fake.Api
open System.Text.Json

// --------------------------------------------------------------------------------------
// Configuration
// --------------------------------------------------------------------------------------


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "cwtools"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "cwtools-vscode"


// Read additional information from the release notes document
let releaseNotesData = File.ReadAllLines "CHANGELOG.md" |> ReleaseNotes.parseAll

let release = List.head releaseNotesData

let githubToken = Environment.environVarOrNone "GITHUB_TOKEN"
// open Fake.BuildServer
let platformShortCode =
    match Environment.isWindows, Environment.isMacOS, Environment.isLinux with
    | false, false, true -> "linux-x64"
    | false, true, false -> "osx-x64"
    | _ -> "win-x64"

// BuildServer.install [ GitLab.Installer ]

let run cmd args dir =
    let parms =
        { ExecParams.Empty with
            Program = cmd
            WorkingDir = dir
            CommandLine = args }

    if Process.shellExec parms <> 0 then
        failwithf $"Error while running '%s{cmd}' with args: %s{args}"

let platformTool tool path =
    match Environment.isUnix with
    | true -> tool
    | _ ->
        match ProcessUtils.tryFindFileOnPath path with
        | None -> failwithf $"can't find tool %s{tool} on PATH"
        | Some v -> v

let npxTool = lazy (platformTool "npx" "npx.cmd")
let npmTool = lazy (platformTool "npm" "npm.cmd")

let cwtoolsProjectName = "Main.fsproj"
let cwtoolsProjectPath = "src/Main/Main.fsproj"
let releaseDir = "release"

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

let buildPackage dir =
    Process.killAllByName "npx"
    run npxTool.Value "--yes @vscode/vsce package" dir

    !! $"%s{dir}/*.vsix" |> Seq.iter (Shell.moveFile "./temp/")

let setPackageJsonField (name: string) (value: string) releaseDir =
    let fileName = $"./%s{releaseDir}/package.json"
    let content = File.readAsString fileName
    let jsonObj = JsonDocument.Parse content
    let node = System.Text.Json.Nodes.JsonObject.Create jsonObj.RootElement
    node[name] <- value
    let opts = JsonSerializerOptions(WriteIndented = true, AllowTrailingCommas = false)
    File.WriteAllText(fileName, node.ToJsonString(opts))

let setVersion (release: ReleaseNotes.ReleaseNotes) releaseDir =
    let versionString = $"%O{release.NugetVersion}"
    setPackageJsonField "version" versionString releaseDir

let publishToGallery releaseDir =
    let token =
        match Environment.environVarOrDefault "vsce-token" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "VSCE Token: "

    Process.killAllByName "npx"
    run npxTool.Value $"@vscode/vsce publish --pat %s{token}" releaseDir

let ensureGitUser user email =
    match CommandHelper.runGitCommand "." "config user.name" with
    | true, [ username ], _ when username = user -> ()
    | _, _, _ ->
        CommandHelper.directRunGitCommandAndFail "." $"config user.name %s{user}"
        CommandHelper.directRunGitCommandAndFail "." $"config user.email %s{email}"

let releaseGithub (release: ReleaseNotes.ReleaseNotes) =
    let user =
        match Environment.environVarOrDefault "github-user" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserInput "Username: "

    let email =
        match Environment.environVarOrDefault "user-email" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserInput "Email: "

    let remote =
        CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function
            | None -> gitHome + "/" + gitName
            | Some(s: string) -> s.Split().[0]

    Staging.stageAll ""
    ensureGitUser user email
    Commit.exec "." $"Bump version to %s{release.NugetVersion}"
    Branches.pushBranch "" remote "main"
    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion

    let files = !!("./temp" </> "*.vsix")

    let token =
        match githubToken with
        | Some s -> s
        | _ ->
            failwith
                "please set the github_token environment variable to a github personal access token with repo access."

    // release on github
    let cl =
        GitHub.createClientWithToken token
        |> GitHub.draftNewRelease
            gitOwner
            gitName
            release.NugetVersion
            (release.SemVer.PreRelease <> None)
            release.Notes

    (cl, files)
    ||> Seq.fold (fun acc e -> acc |> GitHub.uploadFile e)
    |> GitHub.publishDraft //releaseDraft
    |> Async.RunSynchronously

let initTargets () =

    Target.create "Clean" (fun _ ->
        Shell.cleanDir "./temp"
        Shell.cleanDir "./release/bin"
        Shell.copyFiles "release" [ "README.md"; "LICENSE.md" ]
        Shell.copyFile "release/CHANGELOG.md" "CHANGELOG.md")

    Target.create "NpmInstall" <| fun _ -> run npmTool.Value "install" "."

    Target.create "PackageNpmInstall"
    <| fun _ -> run npmTool.Value "install" "release"

    Target.create "CopyDocs" (fun _ ->
        Shell.copyFiles "release" [ "README.md"; "LICENSE.md" ]
        Shell.copyFile "release/CHANGELOG.md" "CHANGELOG.md")

    let publishParams (framework: string) =
        fun (p: DotNet.PublishOptions) ->
            { p with
                Common =
                    { p.Common with
                        CustomParams = Some "--self-contained true /p:PublishReadyToRun=true /p:UseLocalCwtools=False" }
                OutputPath = Some(releaseDir </> "bin/server" </> framework)
                Runtime = Some framework
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams =
                    { MSBuild.CliArguments.Create() with
                        DisableInternalBinLog = true } }

    let buildParams (release: bool) =
        fun (b: DotNet.BuildOptions) ->
            { b with
                OutputPath = Some(releaseDir </> "bin/server" </> platformShortCode)
                Configuration =
                    if release then
                        DotNet.BuildConfiguration.Release
                    else
                        DotNet.BuildConfiguration.Debug
                MSBuildParams =
                    { MSBuild.CliArguments.Create() with
                        DisableInternalBinLog = true } }

    Target.create "BuildServer"
    <| fun _ ->
        if File.exists (releaseDir </> "bin/server" </> platformShortCode </> "hostfxr.dll") then
            Shell.cleanDir "./release/bin"
        else
            ()

        DotNet.build (buildParams true) cwtoolsProjectPath

    Target.create "BuildServerDebug"
    <| fun _ ->
        if File.exists (releaseDir </> "bin/server" </> platformShortCode </> "hostfxr.dll") then
            Shell.cleanDir "./release/bin"
        else
            ()

        DotNet.build (buildParams false) cwtoolsProjectPath

    Target.create "PublishServer"
    <| fun _ ->
        DotNet.publish (publishParams "win-x64") cwtoolsProjectPath
        DotNet.publish (publishParams "linux-x64") cwtoolsProjectPath
        DotNet.publish (publishParams "osx-x64") cwtoolsProjectPath

    Target.create "BuildClient" (fun _ ->
        match ProcessUtils.tryFindFileOnPath "npx" with
        | Some tsc ->
            CreateProcess.fromRawCommand tsc [ "tsc"; "-p"; "./tsconfig.extension.json" ]
            |> Proc.run
            |> (fun r ->
                if r.ExitCode <> 0 then
                    failwith "tsc fail")
        | _ -> failwith "didn't find tsc"

        match ProcessUtils.tryFindFileOnPath "npx" with
        | Some tsc ->
            CreateProcess.fromRawCommand tsc [ "rollup"; "-c"; "-o"; "./release/bin/client/webview/graph.js" ]
            |> Proc.run
            |> (fun r ->
                if r.ExitCode <> 0 then
                    failwith "rollup fail")
        | _ -> failwith "didn't find rollup")

    Target.create "CopyHtml" (fun _ -> !!("client/webview/*.css") |> Shell.copyFiles "release/bin/client/webview")


    Target.create "BuildPackage" (fun _ -> buildPackage "release")

    Target.create "SetVersion" (fun _ -> setVersion release "release")

    Target.create "PublishToGallery" (fun _ -> publishToGallery "release")

    Target.create "ReleaseGitHub" (fun _ -> releaseGithub release)


    Target.description "Assemble the extension"
    Target.create "PrePackage" ignore

    Target.create "PrepareClient" ignore

    // --------------------------------------------------------------------------------------
    // Run generator by default. Invoke 'build <Target>' to override
    // --------------------------------------------------------------------------------------
    Target.description "Build the requirements to run the extension locally"
    Target.create "QuickBuild" ignore
    Target.description "Build the requirements to run the extension locally, in debug mode"
    Target.create "QuickBuildDebug" ignore
    Target.description "Package into the vsix, but don't publish it"
    Target.create "DryRelease" ignore
    Target.description "Package into the vsix, and publish it"
    Target.create "Release" ignore


let buildTargetTree () =
    let (==>!) x y = x ==> y |> ignore

    //Clean only if we care about final output, so clean if DryRelease or Release

    //BuildClient doesn't change, and needs
    //PrepareClient gets everything up to date for the clientside and needs
    //- NpmInstall if deps have changed?
    //- BuildClient
    //-CopyDocs, CopyHtml

    //BuildServer is non-self-contained, using remote cwtools folder
    //BuildServerLocal is non-self-contained, using local cwtools folder
    //PublishServer is self-contained, all platforms

    //PrePackage copies client/server bin to extension dir

    //Release needs PublishServer


    "Clean" ?=> "NpmInstall"
    ==> "BuildClient"
    ==> "CopyDocs"
    ==> "CopyHtml"
    ==> "PrepareClient"
    ==> "PrePackage"
    ==>! "BuildPackage"

    "PublishServer" ?=> "PrePackage" |> ignore
    "BuildServer" ?=> "PrePackage" |> ignore
    "BuildServerDebug" ?=> "PrePackage" |> ignore

    // "Format" ==>
    "Clean"
    ==> "PublishServer"
    ==> "SetVersion"
    ==> "PackageNpmInstall"
    ==> "BuildPackage"
    ==> "ReleaseGitHub"
    ==> "PublishToGallery"
    ==>! "Release"

    "Clean" ==> "BuildPackage" ==>! "DryRelease"

    "BuildServer" ==>! "QuickBuild"

    "PrePackage" ==>! "QuickBuild"

    "BuildServerDebug" ==>! "QuickBuildDebug"

    "PrePackage" ==>! "QuickBuildDebug"

[<EntryPoint>]
let main argv =
    // Microsoft.Build.Logging.StructuredLogger.Strings.Initialize()
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    buildTargetTree ()

    Target.runOrDefaultWithArguments "QuickBuild"
    0
