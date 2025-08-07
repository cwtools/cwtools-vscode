module Main.Serialize

open System.Text
open CWTools.Common
open System.IO
open CWTools.Parser
open CWTools
open FParsec
open System.Diagnostics.Tracing
open System.Reflection
open CWTools.Localisation
open CWTools.Localisation.STL
open CWTools.Games.Files
open CWTools.Common.STLConstants
open CWTools.Games.Compute
open CWTools.Games
open CWTools.Validation.Stellaris
open MBrace.FsPickler
open CWTools.Process
open CWTools.Utilities.Position
open CWTools.Utilities
open CWTools.Games.EU4
open CWTools.Validation.EU4
open CWTools.Validation
open CWTools.Validation.HOI4
open CWTools.Validation.VIC2
open CWTools.Rules
open CWTools.Games.Stellaris.STLLookup
open System.IO.Compression
open System.Collections.Generic
open System.Collections.Concurrent

let mkPickler (resolver: IPicklerResolver) =
    let arrayPickler = resolver.Resolve<Leaf array>()

    let writer (w: WriteState) (ns: Lazy<Leaf array>) =
        arrayPickler.Write w "value" (ns.Force())

    let reader (r: ReadState) =
        let v = arrayPickler.Read r "value" in Lazy<Leaf array>.CreateFromValue v

    Pickler.FromPrimitives(reader, writer)

let mkConcurrentDictionaryPickler<'a, 'b> (resolver: IPicklerResolver) =
    let dictionaryPickler = resolver.Resolve<KeyValuePair<_, _>[]>()

    let writer (w: WriteState) (dict: ConcurrentDictionary<'a, 'b>) =
        dictionaryPickler.Write w "value" (dict.ToArray())

    let reader (r: ReadState) =
        let v = dictionaryPickler.Read r "value" in new ConcurrentDictionary<_, _>(v)

    Pickler.FromPrimitives(reader, writer)

let registry = new CustomPicklerRegistry()
do registry.RegisterFactory mkPickler
do registry.RegisterFactory mkConcurrentDictionaryPickler<int, string>
do registry.RegisterFactory mkConcurrentDictionaryPickler<int, StringMetadata>
do registry.RegisterFactory mkConcurrentDictionaryPickler<string, StringTokens>
registry.DeclareSerializable<FParsec.Position>()
#nowarn "FS8989"
let picklerCache = PicklerCache.FromCustomPicklerRegistry registry

let binarySerializer =
    FsPickler.CreateBinarySerializer(picklerResolver = picklerCache)

let assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)

let addDLCs (workspaceDirectory: WorkspaceDirectory) =
    let dir = workspaceDirectory.path
    // eprintfn "ad %A" dir
    // eprintfn "ad2 %A" (Path.Combine [|dir; "dlc"|])
    if Directory.Exists(dir) && Directory.Exists(Path.Combine [| dir; "dlc" |]) then
        let dlcs = Directory.EnumerateDirectories(Path.Combine [| dir; "dlc" |])
        // eprintfn "ad3 %A" dlcs
        let createZippedDirectory (dlcDir: string) =
            // eprintfn "d1 %A" (Directory.EnumerateFiles dlcDir)
            match
                Directory.EnumerateFiles dlcDir
                |> Seq.tryFind (fun f -> (Path.GetExtension f) = ".zip")
            with
            | Some zip ->
                // eprintfn "d2 %A" zip
                try
                    use file = File.OpenRead(zip)
                    use zipFile = new ZipArchive(file, ZipArchiveMode.Read)

                    let files =
                        zipFile.Entries
                        |> Seq.map (fun e ->
                            Path.Combine([| "uri:"; zip; e.FullName.Replace("\\", "/") |]),
                            let sr = new StreamReader(e.Open()) in sr.ReadToEnd())
                        |> List.ofSeq
                    // eprintfn "%A" files
                    Some(
                        ZD
                            { ZippedDirectory.name = Path.GetFileName zip
                              path = zip.Replace("\\", "/")
                              files = files }
                    )
                with _ ->
                    None
            | None -> None

        dlcs |> Seq.choose createZippedDirectory |> List.ofSeq
    else
        []


let serialize gameDirName scriptFolders cacheDirectory = ()

let serializeSTL folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            STLConstants.scriptFolders,
            "stellaris",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<STLComputedData>(
            Compute.STL.computeSTLData computefun,
            Compute.STL.computeSTLDataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            true
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "stl.cwb"), pickle)

let serializeEU4 folder cacheDirectory =
    let rawdir =
        { WorkspaceDirectory.name = "vanilla"
          path = folder }

    let folders = (WD rawdir) :: (addDLCs rawdir)

    let fileManager =
        FileManager(folders, Some "", EU4Constants.scriptFolders, "europa universalis iv", Encoding.UTF8, [], 2)

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<EU4ComputedData>(
            Compute.EU4.computeEU4Data computefun,
            Compute.EU4.computeEU4DataUpdate computefun,
            Encoding.GetEncoding(1252),
            Encoding.UTF8,
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "eu4.cwb"), pickle)

let serializeHOI4 folder cacheDirectory =
    let rawdir =
        { WorkspaceDirectory.name = "vanilla"
          path = folder }

    let folders = (WD rawdir) :: (addDLCs rawdir)

    let fileManager =
        FileManager(folders, Some "", HOI4Constants.scriptFolders, "hearts of iron iv", Encoding.UTF8, [], 2)

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<HOI4ComputedData>(
            computeHOI4Data computefun,
            computeHOI4DataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "hoi4.cwb"), pickle)

let serializeCK2 folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            CK2Constants.scriptFolders,
            "crusader kings ii",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<CK2ComputedData>(
            computeCK2Data computefun,
            computeCK2DataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "ck2.cwb"), pickle)

let serializeIR folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            IRConstants.scriptFolders,
            "imperator",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<IRComputedData>(
            Compute.Jomini.computeJominiData computefun,
            Compute.Jomini.computeJominiDataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "ir.cwb"), pickle)

let serializeVIC2 folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            VIC2Constants.scriptFolders,
            "victoria 2",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<VIC2ComputedData>(
            computeVIC2Data computefun,
            computeVIC2DataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "vic2.cwb"), pickle)

let serializeCK3 folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            CK3Constants.scriptFolders,
            "crusader kings iii",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<CK3ComputedData>(
            Compute.Jomini.computeJominiData computefun,
            Compute.Jomini.computeJominiDataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "ck3.cwb"), pickle)


let serializeVIC3 folder cacheDirectory =
    let fileManager =
        FileManager(
            [ WD
                  { WorkspaceDirectory.name = "vanilla"
                    path = folder } ],
            Some "",
            VIC3Constants.scriptFolders,
            "Victoria 3",
            Encoding.UTF8,
            [],
            2
        )

    let files = fileManager.AllFilesByPath()
    let computefun: unit -> InfoService option = (fun () -> (None))

    let resources =
        ResourceManager<VIC3ComputedData>(
            Compute.Jomini.computeJominiData computefun,
            Compute.Jomini.computeJominiDataUpdate computefun,
            Encoding.UTF8,
            Encoding.GetEncoding(1252),
            false
        )
            .Api

    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) ->
            e
            |> function
                | Some e2 -> Some(r, e2)
                | _ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)

    let files =
        resources.GetResources()
        |> List.choose (function
            | FileResource(_, r) -> Some(r.logicalpath, "")
            | FileWithContentResource(_, r) -> Some(r.logicalpath, r.filetext)
            | _ -> None)

    let data =
        { resources = entities
          fileIndexTable = fileIndexTable
          files = files
          stringResourceManager = StringResource.stringManager }

    let pickle = binarySerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "vic3.cwb"), pickle)

let deserialize path =
    // registry.DeclareSerializable<System.LazyHelper>()
    // registry.DeclareSerializable<Lazy>()
    match File.Exists path with
    | true ->
        let cacheFile = File.ReadAllBytes(path)
        // let cacheFile = Assembly.GetEntryAssembly().GetManifestResourceStream("Main.files.pickled.cwb")
        //                 |> (fun f -> use ms = new MemoryStream() in f.CopyTo(ms); ms.ToArray())
        try
            let cached = binarySerializer.UnPickle<CachedResourceData> cacheFile
            fileIndexTable <- cached.fileIndexTable
            StringResource.stringManager <- cached.stringResourceManager
            cached.resources, cached.files
        with _ ->
            [], []

    | false -> [], []

// let deserializeEU4 path =
//     let cacheFile = File.ReadAllBytes(path)
//     let cached = binarySerializer.UnPickle<CachedResourceData> cacheFile
//     fileIndexTable <- cached.fileIndexTable
//     cached.resources
