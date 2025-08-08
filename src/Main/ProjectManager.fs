namespace Main

open System
open System.IO
open System.Collections.Generic
open System.Xml
open FSharp.Data
open FSharp.Data.JsonExtensions

type CompilerOptions =
    { sources: list<FileInfo>
      projectReferences: list<FileInfo>
      references: list<FileInfo> }

module ProjectManagerUtils =
    type Dependency =
        { _type: string; compile: list<string> }

    type Library = { path: string }

    type ProjectAssets =
        { targets: Map<string, Map<string, Dependency>>
          libraries: Map<string, Library>
          packageFolders: list<string> }

    let private fixPath (path: string) : string =
        path.Replace('\\', Path.DirectorySeparatorChar)

    let keys (record: JsonValue) : list<string> =
        List.ofSeq (
            seq {
                for key, _ in record.Properties do
                    yield key
            }
        )

    let private parseDependency (info: JsonValue) : Dependency =
        let compile = info.TryGetProperty("compile") |> Option.map keys

        { _type = info?``type``.AsString()
          compile = defaultArg compile [] }

    let private parseDependencies (dependencies: JsonValue) : Map<string, Dependency> =
        Map.ofSeq (
            seq {
                for name, info in dependencies.Properties do
                    yield name, parseDependency info
            }
        )

    let private parseTargets (targets: JsonValue) : Map<string, Map<string, Dependency>> =
        Map.ofSeq (
            seq {
                for target, dependencies in targets.Properties do
                    yield target, parseDependencies dependencies
            }
        )

    let private parseLibrary (library: JsonValue) : Library = { path = library?path.AsString() }

    let private parseLibraries (libraries: JsonValue) : Map<string, Library> =
        Map.ofSeq (
            seq {
                for dependency, info in libraries.Properties do
                    yield dependency, parseLibrary info
            }
        )

    let parseAssetsJson (text: string) : ProjectAssets =
        let json = JsonValue.Parse text

        { targets = parseTargets json?targets
          libraries = parseLibraries json?libraries
          packageFolders = keys json?packageFolders }

    let private parseAssets (path: FileInfo) : ProjectAssets =
        let text = File.ReadAllText path.FullName
        parseAssetsJson text
    // Find all dlls in project.assets.json
    let private references (assets: ProjectAssets) : list<FileInfo> =
        let resolveInPackageFolders (dependencyPath: string) : option<FileInfo> =
            seq {
                for packageFolder in assets.packageFolders do
                    let absolutePath = Path.Combine(packageFolder, dependencyPath)

                    if File.Exists absolutePath then
                        yield FileInfo(absolutePath)
            }
            |> Seq.tryHead

        let resolveInLibrary (library: string) (dll: string) : option<FileInfo> =
            let libraryPath = assets.libraries[library].path
            let dependencyPath = Path.Combine(libraryPath, dll) |> fixPath
            resolveInPackageFolders dependencyPath

        List.ofSeq (
            seq {
                for target in assets.targets do
                    for dependency in target.Value do
                        if
                            dependency.Value._type = "package"
                            && Map.containsKey dependency.Key assets.libraries
                        then
                            for dll in dependency.Value.compile do
                                let resolved = resolveInLibrary dependency.Key dll

                                if resolved.IsSome then
                                    yield resolved.Value
                                else
                                    let packageFolders = String.concat ", " assets.packageFolders
                                    eprintfn $"Couldn't find %s{dll} in %s{packageFolders}"
            }
        )
    // Parse fsproj
    let private parseProject (fsproj: FileInfo) : XmlElement =
        let text = File.ReadAllText fsproj.FullName
        let doc = XmlDocument()
        doc.LoadXml text
        doc.DocumentElement
    // Parse fsproj and fsproj/../obj/project.assets.json
    let parseBoth (path: FileInfo) : CompilerOptions =
        let project = parseProject path
        // Find all <Compile Include=?> elements in fsproj
        let sources (fsproj: XmlNode) : list<FileInfo> =
            List.ofSeq (
                seq {
                    for n in fsproj.SelectNodes "//Compile[@Include]" do
                        let relativePath = n.Attributes["Include"].Value |> fixPath
                        let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                        yield FileInfo(absolutePath)
                }
            )
        // Find all <ProjectReference Include=?> elements in fsproj
        let projectReferences (fsproj: XmlNode) : list<FileInfo> =
            List.ofSeq (
                seq {
                    for n in fsproj.SelectNodes "//ProjectReference[@Include]" do
                        let relativePath = n.Attributes["Include"].Value |> fixPath
                        let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                        yield FileInfo(absolutePath)
                }
            )

        let assetsFile =
            Path.Combine(path.DirectoryName, "obj", "project.assets.json") |> FileInfo

        if assetsFile.Exists then
            let assets = parseAssets assetsFile

            { sources = sources project
              projectReferences = projectReferences project
              references = references assets }
        else
            { sources = sources project
              projectReferences = projectReferences project
              references = [] }

open ProjectManagerUtils

type ProjectManager() =
    let cache = new Dictionary<DirectoryInfo, CompilerOptions>()

    let addToCache (projectFile: FileInfo) : CompilerOptions =
        let parsed = parseBoth projectFile
        cache[projectFile.Directory] <- parsed
        parsed
    // Scan the parent directories looking for a file *.fsproj
    let findProjectFileInParents (sourceFile: FileInfo) : option<FileInfo> =
        seq {
            let mutable dir = sourceFile.Directory

            while dir <> dir.Root do
                for proj in dir.GetFiles("*.fsproj") do
                    yield proj

                dir <- dir.Parent
        }
        |> Seq.tryHead

    let tryFindAndCache (sourceFile: FileInfo) : option<CompilerOptions> =
        match findProjectFileInParents sourceFile with
        | None ->
            eprintfn $"No project file for %s{sourceFile.Name}"
            None
        | Some projectFile ->
            eprintfn $"Found project file %s{projectFile.FullName} for %s{sourceFile.Name}"
            Some(addToCache projectFile)

    member this.UpdateProjectFile(project: Uri) : unit =
        let file = FileInfo(project.AbsolutePath)
        addToCache file |> ignore

    member this.FindProjectOptions(sourceFile: Uri) : option<CompilerOptions> =
        let file = FileInfo(sourceFile.AbsolutePath)

        match tryFindAndCache file with
        | Some cachedProject -> Some cachedProject
        | None -> tryFindAndCache file
