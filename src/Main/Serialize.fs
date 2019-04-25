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
open CWTools.Validation.CK2
open CWTools.Validation.IR
open CWTools.Rules
open CWTools.Games.Stellaris.STLLookup


let mkPickler (resolver : IPicklerResolver) =
    let arrayPickler = resolver.Resolve<Leaf array> ()
    let writer (w : WriteState) (ns : Lazy<Leaf array>) =
        arrayPickler.Write w "value" (ns.Force())
    let reader (r : ReadState) =
        let v = arrayPickler.Read r "value" in Lazy<Leaf array>.CreateFromValue v
    Pickler.FromPrimitives(reader, writer)
let registry = new CustomPicklerRegistry()
do registry.RegisterFactory mkPickler
registry.DeclareSerializable<FParsec.Position>()
let picklerCache = PicklerCache.FromCustomPicklerRegistry registry
let xmlSerializer = FsPickler.CreateXmlSerializer(picklerResolver = picklerCache)
let assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)


let serialize gameDirName scriptFolders cacheDirectory = ()
let serializeSTL folder cacheDirectory =
    let fileManager = FileManager(folder, Some "", FilesScope.Vanilla, STLConstants.scriptFolders, "stellaris", Encoding.UTF8, [])
    let files = fileManager.AllFilesByPath()
    let computefun : unit -> InfoService<STLConstants.Scope> option = (fun () -> (None))
    let resources = ResourceManager<STLComputedData>(Compute.STL.computeSTLData computefun, Compute.STL.computeSTLDataUpdate computefun, Encoding.UTF8, Encoding.GetEncoding(1252)).Api
    let entities =
        resources.UpdateFiles(files)
         |> List.choose (fun (r, e) -> e |> function |Some e2 -> Some (r, e2) |_ -> None)
         |> List.map (fun (r, (struct (e, _))) -> r, e)
    let files = resources.GetResources()
                |> List.choose (function |FileResource (_, r) -> Some (r.logicalpath, "")
                                         |FileWithContentResource (_, r) -> Some (r.logicalpath, r.filetext)
                                         |_ -> None)
    let data = { resources = entities; fileIndexTable = fileIndexTable; files = files; stringResourceManager = StringResource.stringManager}
    let pickle = xmlSerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "stl.cwb"), pickle)

let serializeEU4 folder cacheDirectory =
    let fileManager = FileManager(folder, Some "", FilesScope.Vanilla, EU4Constants.scriptFolders, "europa universalis iv", Encoding.UTF8, [])
    let files = fileManager.AllFilesByPath()
    let computefun : unit -> InfoService<EU4Constants.Scope> option = (fun () -> (None))
    let resources = ResourceManager<EU4ComputedData>(Compute.EU4.computeEU4Data computefun, Compute.EU4.computeEU4DataUpdate computefun, Encoding.GetEncoding(1252), Encoding.UTF8).Api
    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) -> e |> function |Some e2 -> Some (r, e2) |_ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)
    let files = resources.GetResources()
                |> List.choose (function |FileResource (_, r) -> Some (r.logicalpath, "")
                                         |FileWithContentResource (_, r) -> Some (r.logicalpath, r.filetext)
                                         |_ -> None)
    let data = { resources = entities; fileIndexTable = fileIndexTable; files = files; stringResourceManager = StringResource.stringManager}
    let pickle = xmlSerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "eu4.cwb"), pickle)
let serializeHOI4 folder cacheDirectory =
    let fileManager = FileManager(folder, Some "", FilesScope.Vanilla, HOI4Constants.scriptFolders, "hearts of iron iv", Encoding.UTF8, [])
    let files = fileManager.AllFilesByPath()
    let computefun : unit -> InfoService<HOI4Constants.Scope> option = (fun () -> (None))
    let resources = ResourceManager<HOI4ComputedData>(computeHOI4Data computefun, computeHOI4DataUpdate computefun, Encoding.UTF8, Encoding.GetEncoding(1252)).Api
    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) -> e |> function |Some e2 -> Some (r, e2) |_ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)
    let files = resources.GetResources()
                |> List.choose (function |FileResource (_, r) -> Some (r.logicalpath, "")
                                         |FileWithContentResource (_, r) -> Some (r.logicalpath, r.filetext)
                                         |_ -> None)
    let data = { resources = entities; fileIndexTable = fileIndexTable; files = files; stringResourceManager = StringResource.stringManager}
    let pickle = xmlSerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "hoi4.cwb"), pickle)
let serializeCK2 folder cacheDirectory =
    let fileManager = FileManager(folder, Some "", FilesScope.Vanilla, CK2Constants.scriptFolders, "crusader kings ii", Encoding.UTF8, [])
    let files = fileManager.AllFilesByPath()
    let computefun : unit -> InfoService<CK2Constants.Scope> option = (fun () -> (None))
    let resources = ResourceManager<CK2ComputedData>(computeCK2Data computefun, computeCK2DataUpdate computefun, Encoding.UTF8, Encoding.GetEncoding(1252)).Api
    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) -> e |> function |Some e2 -> Some (r, e2) |_ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)
    let files = resources.GetResources()
                |> List.choose (function |FileResource (_, r) -> Some (r.logicalpath, "")
                                         |FileWithContentResource (_, r) -> Some (r.logicalpath, r.filetext)
                                         |_ -> None)
    let data = { resources = entities; fileIndexTable = fileIndexTable; files = files; stringResourceManager = StringResource.stringManager}
    let pickle = xmlSerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "ck2.cwb"), pickle)
let serializeIR folder cacheDirectory =
    let fileManager = FileManager(folder, Some "", FilesScope.Vanilla, IRConstants.scriptFolders, "imperator", Encoding.UTF8, [])
    let files = fileManager.AllFilesByPath()
    let computefun : unit -> InfoService<IRConstants.Scope> option = (fun () -> (None))
    let resources = ResourceManager<IRComputedData>(computeIRData computefun, computeIRDataUpdate computefun, Encoding.UTF8, Encoding.GetEncoding(1252)).Api
    let entities =
        resources.UpdateFiles(files)
        |> List.choose (fun (r, e) -> e |> function |Some e2 -> Some (r, e2) |_ -> None)
        |> List.map (fun (r, (struct (e, _))) -> r, e)
    let files = resources.GetResources()
                |> List.choose (function |FileResource (_, r) -> Some (r.logicalpath, "")
                                         |FileWithContentResource (_, r) -> Some (r.logicalpath, r.filetext)
                                         |_ -> None)
    let data = { resources = entities; fileIndexTable = fileIndexTable; files = files; stringResourceManager = StringResource.stringManager}
    let pickle = xmlSerializer.Pickle data
    File.WriteAllBytes(Path.Combine(cacheDirectory, "ir.cwb"), pickle)

let deserialize path =
    // registry.DeclareSerializable<System.LazyHelper>()
    // registry.DeclareSerializable<Lazy>()
    match File.Exists path with
    |true ->
        let cacheFile = File.ReadAllBytes(path)
        // let cacheFile = Assembly.GetEntryAssembly().GetManifestResourceStream("Main.files.pickled.cwb")
        //                 |> (fun f -> use ms = new MemoryStream() in f.CopyTo(ms); ms.ToArray())
        let cached = xmlSerializer.UnPickle<CachedResourceData> cacheFile
        fileIndexTable <- cached.fileIndexTable
        StringResource.stringManager <- cached.stringResourceManager
        cached.resources, cached.files
    |false -> [], []

// let deserializeEU4 path =
//     let cacheFile = File.ReadAllBytes(path)
//     let cached = xmlSerializer.UnPickle<CachedResourceData> cacheFile
//     fileIndexTable <- cached.fileIndexTable
//     cached.resources


