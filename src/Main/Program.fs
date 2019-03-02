module Main.Program

open LSP
open LSP.Types
open System
open System.IO
open CWTools.Parser
open CWTools.Parser.EU4Parser
open CWTools.Parser.CK2Parser
open CWTools.Parser.STLParser
open CWTools.Parser.Types
open CWTools.Common
open CWTools.Common.STLConstants
open CWTools.Games
open FParsec
open System.Threading.Tasks
open System.Text
open System.Reflection
open System.IO
open System.Runtime.InteropServices
open FSharp.Data
open LSP
open CWTools.Validation.ValidationCore
open System
open CWTools.Validation.Rules
open System.Xml.Schema
open CWTools.Games.Files
open LSP.Json.Ser
open System.ComponentModel
open CWTools.Games.Stellaris
open CWTools.Games.Stellaris.STLLookup
open MBrace.FsPickler
open CWTools.Process
open CWTools.Utilities.Position
open CWTools.Games.EU4
open Main.Serialize
open Main.Git
open FSharp.Data
open System.Diagnostics
open Main.Lang

let private TODO() = raise (Exception "TODO")

[<assembly: AssemblyDescription("CWTools language server for PDXScript")>]
do()

type LintRequestMsg =
    | UpdateRequest of VersionedTextDocumentIdentifier * bool
    | WorkComplete of DateTime

type GameLanguage = |STL |HOI4 |EU4 |CK2
type Server(client: ILanguageClient) =
    let docs = DocumentStore()
    let projects = ProjectManager()
    let notFound (doc: Uri) (): 'Any =
        raise (Exception (sprintf "%s does not exist" (doc.ToString())))
    let mutable docErrors : DocumentHighlight list = []

    let mutable activeGame = STL
    let mutable isVanillaFolder = false
    let mutable gameObj : option<IGame> = None
    let mutable stlGameObj : option<IGame<STLComputedData, STLConstants.Scope, STLConstants.Modifier>> = None
    let mutable hoi4GameObj : option<IGame<HOI4ComputedData, HOI4Constants.Scope, HOI4Constants.Modifier>> = None
    let mutable eu4GameObj : option<IGame<EU4ComputedData, EU4Constants.Scope, EU4Constants.Modifier>> = None
    let mutable ck2GameObj : option<IGame<CK2ComputedData, CK2Constants.Scope, CK2Constants.Modifier>> = None

    let mutable languages : Lang list = []
    let mutable rootUri : Uri option = None
    let mutable cachePath : string option = None
    let mutable stlVanillaPath : string option = None
    let mutable hoi4VanillaPath : string option = None
    let mutable eu4VanillaPath : string option = None
    let mutable ck2VanillaPath : string option = None
    let mutable stellarisCacheVersion : string option = None
    let mutable eu4CacheVersion : string option = None
    let mutable hoi4CacheVersion : string option = None
    let mutable ck2CacheVersion : string option = None
    let mutable remoteRepoPath : string option = None

    let mutable rulesChannel : string = "stable"
    let mutable manualRulesFolder : string option = None
    let mutable useEmbeddedRules : bool = false
    let mutable useManualRules : bool = false
    let mutable validateVanilla : bool = false
    let mutable experimental : bool = false

    let mutable ignoreCodes : string list = []
    let mutable ignoreFiles : string list = []
    let mutable dontLoadPatterns : string list = []
    let mutable locCache : Map<string, CWError list> = Map.empty

    let (|TrySuccess|TryFailure|) tryResult =
        match tryResult with
        | true, value -> TrySuccess value
        | _ -> TryFailure
    let sevToDiagSev =
        function
        | Severity.Error -> DiagnosticSeverity.Error
        | Severity.Warning -> DiagnosticSeverity.Warning
        | Severity.Information -> DiagnosticSeverity.Information
        | Severity.Hint -> DiagnosticSeverity.Hint

    let convRangeToLSPRange (range : range) =
        {
            start = {
                line = (int range.StartLine - 1)
                character = (int range.StartColumn)
            }
            ``end`` = {
                    line = (int range.EndLine - 1)
                    character = (int range.EndColumn)
            }
        }
    let parserErrorToDiagnostics e =
        let code, sev, file, error, (position : range), length = e
        let startC, endC = match length with
        | 0 -> 0,( int position.StartColumn)
        | x ->(int position.StartColumn),(int position.StartColumn) + length
        let result = {
                        range = {
                                start = {
                                        line = (int position.StartLine - 1)
                                        character = startC
                                    }
                                ``end`` = {
                                            line = (int position.StartLine - 1)
                                            character = endC
                                    }
                        }
                        severity = Some (sevToDiagSev sev)
                        code = Some code
                        source = Some code
                        message = error
                    }
        (file, result)

    let sendDiagnostics s =
        let diagnosticFilter ((f : string), d) =
            match (f, d) with
            | _, {code = Some code} when List.contains code ignoreCodes -> false
            | f, _ when List.contains (Path.GetFileName f) ignoreFiles -> false
            | _, _ -> true
        s |>  List.groupBy fst
            |> List.map ((fun (f, rs) -> f, rs |> List.filter (diagnosticFilter)) >>
                (fun (f, rs) ->
                    try {uri = (match Uri.TryCreate(f, UriKind.Absolute) with |TrySuccess value -> value |TryFailure -> eprintfn "%s" f; Uri "/") ; diagnostics = List.map snd rs} with |e -> failwith (sprintf "%A %A" e rs)))
            |> List.iter (client.PublishDiagnostics)

    let mutable delayedLocUpdate = false

    let lint (doc: Uri) (shallowAnalyze : bool) (forceDisk : bool) : Async<unit> =
        async {
            let name =
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && doc.LocalPath.StartsWith "/"
                then doc.LocalPath.Substring(1)
                else doc.LocalPath

            if name.EndsWith(".yml") then delayedLocUpdate <- true else ()
            let filetext = if forceDisk then None else docs.GetText (FileInfo(doc.LocalPath))
            let getRange (start: FParsec.Position) (endp : FParsec.Position) = mkRange start.StreamName (mkPos (int start.Line) (int start.Column)) (mkPos (int endp.Line) (int endp.Column))
            let parserErrors =
                match docs.GetText (FileInfo(doc.LocalPath)) with
                |None -> []
                |Some t ->
                    let parsed = CKParser.parseString t name
                    match name, parsed with
                    | x, _ when x.EndsWith(".yml") -> []
                    | _, Success(_,_,_) -> []
                    | _, Failure(msg,p,s) -> [("CW001", Severity.Error, name, msg, (getRange p.Position p.Position), 0)]
            let locErrors =
                locCache.TryFind (doc.LocalPath) |> Option.defaultValue [] |> List.map (fun (c, s, n, l, e, _) -> (c, s, n.FileName, e, n, l))
            let errors =
                parserErrors @
                locErrors @
                match gameObj with
                    |None -> []
                    |Some game ->
                        let results = game.UpdateFile shallowAnalyze name filetext
                        results |> List.map (fun (c, s, n, l, e, _) -> (c, s, n.FileName, e, n, l) )
            match errors with
            | [] -> client.PublishDiagnostics {uri = doc; diagnostics = []}
            | x -> x
                    |> List.map parserErrorToDiagnostics
                    |> sendDiagnostics
            //let compilerOptions = projects.FindProjectOptions doc |> Option.defaultValue emptyCompilerOptions
            // let! parseResults, checkAnswer = checker.ParseAndCheckFileInProject(name, version, source, projectOptions)
            // for error in parseResults.Errors do
            //     eprintfn "%s %d:%d %s" error.FileName error.StartLineAlternate error.StartColumn error.Message
            // match checkAnswer with
            // | FSharpCheckFileAnswer.Aborted -> eprintfn "Aborted checking %s" name
            // | FSharpCheckFileAnswer.Succeeded checkResults ->
            //     for error in checkResults.Errors do
            //         eprintfn "%s %d:%d %s" error.FileName error.StartLineAlternate error.StartColumn error.Message
        }

    let mutable delayTime = TimeSpan(0,0,30)

    let delayedAnalyze() =
        match gameObj with
        |Some game ->
            let stopwatch = Stopwatch()
            stopwatch.Start()
            game.RefreshCaches();
            if delayedLocUpdate
            then
                game.RefreshLocalisationCaches();
                delayedLocUpdate <- false
                locCache <- game.LocalisationErrors(true, true) |> List.groupBy (fun (_, _, r, _, _, _) -> r.FileName) |> Map.ofList
            else
                locCache <- game.LocalisationErrors(false, true) |> List.groupBy (fun (_, _, r, _, _, _) -> r.FileName) |> Map.ofList
            stopwatch.Stop()
            let time = stopwatch.Elapsed
            delayTime <- TimeSpan(Math.Min(TimeSpan(0,0,60).Ticks, Math.Max(TimeSpan(0,0,10).Ticks, 5L * time.Ticks)))
        |None -> ()

    let lintAgent =
        MailboxProcessor.Start(
            (fun agent ->
            let mutable nextAnalyseTime = DateTime.Now
            let analyzeTask uri force =
                new Task(
                    fun () ->
                    let mutable nextTime = nextAnalyseTime
                    try
                        try
                            let shallowAnalyse = DateTime.Now < nextTime
                            lint uri (shallowAnalyse && (not(force))) false |> Async.RunSynchronously
                            if not(shallowAnalyse)
                            then delayedAnalyze(); nextTime <- DateTime.Now.Add(delayTime);
                            else ()
                        with
                        | e -> eprintfn "uri %A \n exception %A" uri.LocalPath e
                    finally
                        agent.Post (WorkComplete (nextTime)))
            let analyze (file : VersionedTextDocumentIdentifier) force =
                //eprintfn "Analyze %s" (file.uri.ToString())
                let task = analyzeTask file.uri force
                //let task = new Task((fun () -> lint (file.uri) false false |> Async.RunSynchronously; agent.Post (WorkComplete ())))
                task.Start()
            let rec loop (inprogress : bool) (state : Map<string, VersionedTextDocumentIdentifier * bool>) =
                async{
                    let! msg = agent.Receive()
                    match msg, inprogress with
                    | UpdateRequest (ur, force), false ->
                        analyze ur force
                        return! loop true state
                    | UpdateRequest (ur, force), true ->
                        if Map.containsKey ur.uri.LocalPath state
                        then
                            if (Map.find ur.uri.LocalPath state) |> (fun ({VersionedTextDocumentIdentifier.version = v}, _) -> v < ur.version)
                            then
                                return! loop inprogress (state |> Map.add ur.uri.LocalPath (ur, force))
                            else
                                return! loop inprogress state
                        else
                            return! loop inprogress (state |> Map.add ur.uri.LocalPath (ur, force))
                    | WorkComplete time, _ ->
                        nextAnalyseTime <- time
                        if Map.isEmpty state
                        then
                            return! loop false state
                        else
                            let key, (next, force) = state |> Map.pick (fun k v -> (k, v) |> function | (k, v) -> Some (k, v))
                            let newstate = state |> Map.remove key
                            analyze next force
                            return! loop true newstate
                }
            loop false Map.empty
            )
        )


    let rec replaceFirst predicate value = function
        | [] -> []
        | h :: t when predicate h -> value :: t
        | h :: t -> h :: replaceFirst predicate value t

    let fixEmbeddedFileName (s : string) =
        let count = (Seq.filter ((=) '.') >> Seq.length) s
        let mutable out = "//" + s
        [1 .. count - 1] |> List.iter (fun _ -> out <- (replaceFirst ((=) '.') '\\' (out |> List.ofSeq)) |> Array.ofList |> String )
        out

    let rec getAllFolders dirs =
        if Seq.isEmpty dirs then Seq.empty else
            seq { yield! dirs |> Seq.collect Directory.EnumerateDirectories
                  yield! dirs |> Seq.collect Directory.EnumerateDirectories |> getAllFolders }
    let getAllFoldersUnion dirs =
        seq {
            yield! dirs
            yield! getAllFolders dirs
        }

    let getConfigFiles() =
        let embeddedConfigFiles =
            match cachePath, useEmbeddedRules, useManualRules with
            | Some path, false, false ->
                let configFiles = (getAllFoldersUnion ([path] |> Seq.ofList)) |> Seq.collect (Directory.EnumerateFiles)
                let configFiles = configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt")
                configFiles |> List.map (fun f -> f, File.ReadAllText(f))
            | _ ->
                let embeddedConfigFileNames = Assembly.GetEntryAssembly().GetManifestResourceNames() |> Array.filter (fun f -> f.Contains("config.config") && f.EndsWith(".cwt"))
                embeddedConfigFileNames |> List.ofArray |> List.map (fun f -> fixEmbeddedFileName f, (new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(f))).ReadToEnd())
        let configpath = "Main.files.config.cwt"
        let configFiles =
            match useManualRules, manualRulesFolder with
            |true, Some rf ->
                let configFiles =
                    if Directory.Exists rf
                    then getAllFoldersUnion ([rf] |> Seq.ofList)
                    else if Directory.Exists "./.cwtools" then getAllFoldersUnion (["./.cwtools"] |> Seq.ofList) else Seq.empty
                let configFiles = configFiles |> Seq.collect (Directory.EnumerateFiles)
                configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt")
            |_ ->
                let configFiles = (if Directory.Exists "./.cwtools" then getAllFoldersUnion (["./.cwtools"] |> Seq.ofList) else Seq.empty) |> Seq.collect (Directory.EnumerateFiles)
                configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt")
        let configs =
            match configFiles.Length > 0 with
            |true ->
                configFiles |> List.map (fun f -> f, File.ReadAllText(f))
                //["./config.cwt", File.ReadAllText("./config.cwt")]
            |false ->
                embeddedConfigFiles
        configs

    let setupRulesCaches()  =
        match cachePath, remoteRepoPath, useEmbeddedRules, useManualRules with
        | Some cp, Some rp, false, false ->
            let stable = rulesChannel <> "latest"
            match initOrUpdateRules rp cp stable true with
            | true, Some date ->
                let text = sprintf "Validation rules for %O have been updated to %O." activeGame date
                client.CustomNotification ("forceReload", JsonValue.String(text))
            | _ -> ()
        | _ -> ()

    let checkOrSetGameCache(forceCreate : bool) =
        match (cachePath, isVanillaFolder) with
        | Some cp, false ->
            let gameCachePath = cp+"/../"
            let stlCacheLocation cp = if File.Exists (gameCachePath + "stl.cwb") then (gameCachePath + "stl.cwb") else (assemblyLocation + "/../../../embedded/pickled.xml")
            let doesCacheExist =
                match activeGame with
                | STL -> File.Exists (stlCacheLocation gameCachePath)
                | HOI4 -> File.Exists (gameCachePath + "hoi4.cwb")
                | EU4 -> File.Exists (gameCachePath + "eu4.cwb")
                | CK2 -> File.Exists (gameCachePath + "ck2.cwb")
            if doesCacheExist && not forceCreate
            then eprintfn "Cache exists at %s" (gameCachePath + ".cwb")
            else
                match (activeGame, stlVanillaPath, eu4VanillaPath, hoi4VanillaPath, ck2VanillaPath) with
                | STL, Some vp, _, _ ,_ ->
                    client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Generating vanilla cache...");  "enable", JsonValue.Boolean(true) |])
                    serializeSTL (vp) (gameCachePath)
                    let text = sprintf "Vanilla cache for %O has been updated." activeGame
                    client.CustomNotification ("forceReload", JsonValue.String(text))
                | STL, None, _, _, _ ->
                    client.CustomNotification ("promptVanillaPath", JsonValue.String("stellaris"))
                | EU4, _, Some vp, _, _ ->
                    client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Generating vanilla cache...");  "enable", JsonValue.Boolean(true) |])
                    serializeEU4 (vp) (gameCachePath)
                    let text = sprintf "Vanilla cache for %O has been updated." activeGame
                    client.CustomNotification ("forceReload", JsonValue.String(text))
                | EU4, _, None, _, _ ->
                    client.CustomNotification ("promptVanillaPath", JsonValue.String("eu4"))
                | HOI4, _, _, Some vp, _ ->
                    client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Generating vanilla cache...");  "enable", JsonValue.Boolean(true) |])
                    serializeHOI4 (vp) (gameCachePath)
                    let text = sprintf "Vanilla cache for %O has been updated." activeGame
                    client.CustomNotification ("forceReload", JsonValue.String(text))
                | HOI4, _, _, None, _ ->
                    client.CustomNotification ("promptVanillaPath", JsonValue.String("hoi4"))
                | CK2, _, _, _, Some vp ->
                    client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Generating vanilla cache...");  "enable", JsonValue.Boolean(true) |])
                    serializeCK2 (vp) (gameCachePath)
                    let text = sprintf "Vanilla cache for %O has been updated." activeGame
                    client.CustomNotification ("forceReload", JsonValue.String(text))
                | CK2, _, _, _, None ->
                    client.CustomNotification ("promptVanillaPath", JsonValue.String("ck2"))
        | _ -> eprintfn "No cache path"
                // client.CustomNotification ("promptReload", JsonValue.String("Cached generated, reload to use"))


    let getFolderList (filename : string, filetext : string) =
        if Path.GetFileName filename = "folders.cwt"
        then Some (filetext.Split(([|"\r\n"; "\r"; "\n"|]), StringSplitOptions.None) |> List.ofArray)
        else None


    let processWorkspace (uri : option<Uri>) =
        client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Loading project...");  "enable", JsonValue.Boolean(true) |])
        match uri with
        | Some u ->
            let path =
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                then u.LocalPath.Substring(1)
                else u.LocalPath
            try
                let timer = new System.Diagnostics.Stopwatch()
                timer.Start()

                eprintfn "%s" path
                let docspath = "Main.files.trigger_docs_2.2.4.txt"
                let docs = DocsParser.parseDocsStream (Assembly.GetEntryAssembly().GetManifestResourceStream(docspath))
                let embeddedFileNames = Assembly.GetEntryAssembly().GetManifestResourceNames() |> Array.filter (fun f -> f.Contains("common") || f.Contains("localisation") || f.Contains("interface") || f.Contains("events") || f.Contains("gfx") || f.Contains("sound") || f.Contains("music") || f.Contains("fonts") || f.Contains("flags") || f.Contains("prescripted_countries"))
                let embeddedFiles = embeddedFileNames |> List.ofArray |> List.map (fun f -> fixEmbeddedFileName f, (new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(f))).ReadToEnd())
                let assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)
                eprintfn "Parse docs time: %i" timer.ElapsedMilliseconds; timer.Restart()

                let stlCacheLocation cp = if File.Exists (cp + "/../stl.cwb") then (cp + "/../stl.cwb") else (assemblyLocation + "/../../../embedded/pickled.xml")
                let cached, cachedFiles =
                    match (activeGame, cachePath, isVanillaFolder) with
                    | _, _, true ->
                        eprintfn "Vanilla folder, so not loading cache"
                        ([], [])
                    | STL, Some cp, _ -> deserialize (stlCacheLocation cp)
                    | EU4, Some cp, _ -> deserialize (cp + "/../eu4.cwb")
                    | HOI4, Some cp, _ -> deserialize (cp + "/../hoi4.cwb")
                    | CK2, Some cp, _ -> deserialize (cp + "/../ck2.cwb")
                    | _ -> ([], [])
                eprintfn "Parse cache time: %i" timer.ElapsedMilliseconds; timer.Restart()

               // let docs = DocsParser.parseDocsFile @"G:\Projects\CK2 Events\CWTools\files\game_effects_triggers_1.9.1.txt"
                let triggers, effects = (docs |> (function |Success(p, _, _) -> DocsParser.processDocs STLConstants.parseScopes p |Failure(e, _, _) -> eprintfn "%A" e; [], []))
                let logspath = "Main.files.setup.log"


                let modfile = SetupLogParser.parseLogsStream (Assembly.GetEntryAssembly().GetManifestResourceStream(logspath))
                let modifiers = (modfile |> (function |Success(p, _, _) -> SetupLogParser.processLogs p))
                eprintfn "Parse setup.log time: %i" timer.ElapsedMilliseconds; timer.Restart()

                let configs = getConfigFiles()
                let folders = configs |> List.tryPick getFolderList
                //let configs = [
                let stlLocCommands =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
                            |> Option.bind (fun (fn, ft) -> if activeGame = STL then Some (fn, ft) else None)
                            |> Option.map (fun (fn, ft) -> STLParser.loadLocCommands fn ft)
                            |> Option.defaultValue []


                let stlsettings = {
                    CWTools.Games.Stellaris.StellarisSettings.rootDirectory = path
                    scope = FilesScope.All
                    modFilter = None
                    scriptFolders = folders
                    excludeGlobPatterns = Some dontLoadPatterns
                    validation = {
                        validateVanilla = validateVanilla
                        experimental = experimental
                        langs = languages
                    }
                    rules = Some {
                        ruleFiles = configs
                        validateRules = true
                        debugRulesOnly = false
                        debugMode = false
                    }
                    embedded = {
                        triggers = triggers
                        effects = effects
                        modifiers = modifiers
                        embeddedFiles = cachedFiles
                        cachedResourceData = cached
                        localisationCommands = stlLocCommands
                    }
                }
                let hoi4modpath = "Main.files.hoi4.modifiers"
                let hoi4Mods =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
                            |> Option.map (fun (fn, ft) -> HOI4Parser.loadModifiers fn ft)
                            |> Option.defaultValue []
                let hoi4LocCommands =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
                            |> Option.bind (fun (fn, ft) -> if activeGame = HOI4 then Some (fn, ft) else None)
                            |> Option.map (fun (fn, ft) -> HOI4Parser.loadLocCommands fn ft)
                            |> Option.defaultValue []
                // let hoi4Mods = HOI4Parser.loadModifiers "hoi4mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(hoi4modpath))).ReadToEnd())

                let hoi4settings = {
                    rootDirectory = path
                    scriptFolders = folders
                    excludeGlobPatterns = Some dontLoadPatterns
                    embedded = {
                        embeddedFiles = cachedFiles
                        modifiers = hoi4Mods
                        cachedResourceData = cached
                        triggers = []
                        effects = []
                        localisationCommands = hoi4LocCommands
                    }
                    validation = {
                        validateVanilla = validateVanilla;
                        langs = languages
                        experimental = experimental
                    }
                    rules = Some {
                        ruleFiles = configs
                        validateRules = true
                        debugRulesOnly = false
                        debugMode = false
                    }
                    scope = FilesScope.All
                    modFilter = None
                }
                let eu4modpath = "Main.files.eu4.modifiers"
                let eu4Mods =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
                            |> Option.map (fun (fn, ft) -> EU4Parser.loadModifiers fn ft)
                            |> Option.defaultValue []

                let eu4LocCommands =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
                            |> Option.bind (fun (fn, ft) -> if activeGame = EU4 then Some (fn, ft) else None)
                            |> Option.map (fun (fn, ft) -> EU4Parser.loadLocCommands fn ft)
                            |> Option.defaultValue []

                // let eu4Mods = EU4Parser.loadModifiers "eu4mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(eu4modpath))).ReadToEnd())
                let eu4settings = {
                    rootDirectory = path
                    scriptFolders = folders
                    excludeGlobPatterns = Some dontLoadPatterns
                    embedded = {
                        embeddedFiles = cachedFiles
                        modifiers = eu4Mods
                        cachedResourceData = cached
                        triggers = []
                        effects = []
                        localisationCommands = eu4LocCommands
                    }
                    validation = {
                        validateVanilla = validateVanilla;
                        langs = languages
                        experimental = experimental
                    }
                    rules = Some {
                        ruleFiles = configs
                        validateRules = true
                        debugRulesOnly = false
                        debugMode = false
                    }
                    scope = FilesScope.All
                    modFilter = None
                }
                let ck2Mods =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
                            |> Option.map (fun (fn, ft) -> CK2Parser.loadModifiers fn ft)
                            |> Option.defaultValue []

                let ck2LocCommands =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
                            |> Option.bind (fun (fn, ft) -> if activeGame = CK2 then Some (fn, ft) else None)
                            |> Option.map (fun (fn, ft) -> CK2Parser.loadLocCommands fn ft)
                            |> Option.defaultValue []

                // let ck2Mods = CK2Parser.loadModifiers "ck2mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(ck2modpath))).ReadToEnd())
                let ck2settings = {
                    rootDirectory = path
                    scriptFolders = folders
                    excludeGlobPatterns = Some dontLoadPatterns
                    embedded = {
                        embeddedFiles = cachedFiles
                        modifiers = ck2Mods
                        cachedResourceData = cached
                        triggers = []
                        effects = []
                        localisationCommands = ck2LocCommands
                    }
                    validation = {
                        validateVanilla = validateVanilla;
                        langs = languages
                        experimental = experimental
                    }
                    rules = Some {
                        ruleFiles = configs
                        validateRules = true
                        debugRulesOnly = false
                        debugMode = false
                    }
                    scope = FilesScope.All
                    modFilter = None
                }

                let game =
                    match activeGame with
                    |STL ->
                        let game = STLGame(stlsettings)
                        stlGameObj <- Some (game :> IGame<STLComputedData, STLConstants.Scope, STLConstants.Modifier>)
                        game :> IGame
                    |HOI4 ->
                        let game = CWTools.Games.HOI4.HOI4Game(hoi4settings)
                        hoi4GameObj <- Some (game :> IGame<HOI4ComputedData, HOI4Constants.Scope, HOI4Constants.Modifier>)
                        game :> IGame
                    |EU4 ->
                        let game = CWTools.Games.EU4.EU4Game(eu4settings)
                        eu4GameObj <- Some (game :> IGame<EU4ComputedData, EU4Constants.Scope, EU4Constants.Modifier>)
                        game :> IGame
                    |CK2 ->
                        let game = CWTools.Games.CK2.CK2Game(ck2settings)
                        ck2GameObj <- Some (game :> IGame<CK2ComputedData, CK2Constants.Scope, CK2Constants.Modifier>)
                        game :> IGame
                gameObj <- Some game
                let game = game
                let getRange (start: FParsec.Position) (endp : FParsec.Position) = mkRange start.StreamName (mkPos (int start.Line) (int start.Column)) (mkPos (int endp.Line) (int endp.Column))
                let parserErrors = game.ParserErrors() |> List.map (fun ( n, e, p) -> "CW001", Severity.Error, n, e, (getRange p p), 0)
                parserErrors
                    |> List.map parserErrorToDiagnostics
                    |> sendDiagnostics

                client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Validating files...");  "enable", JsonValue.Boolean(true) |])
                //eprintfn "%A" game.AllFiles
                let valErrors = game.ValidationErrors() |> List.map (fun (c, s, n, l, e, _) -> (c, s, n.FileName, e, n, l) )
                let locRaw = game.LocalisationErrors(true, true)
                locCache <- locRaw |> List.groupBy (fun (_, _, r, _, _, _) -> r.FileName) |> Map.ofList
                let locErrors = locRaw |> List.map (fun (c, s, n, l, e, _) -> (c, s, n.FileName, e, n, l) )

                valErrors @ locErrors
                    |> List.map parserErrorToDiagnostics
                    |> sendDiagnostics
                GC.Collect()
                //eprintfn "%A" game.ValidationErrors
                    // |> List.groupBy fst
                    // |> List.map ((fun (f, rs) -> f, rs |> List.filter (fun (_, d) -> match d.code with |Some s -> not (List.contains s ignoreCodes) |None -> true)) >>
                    //     (fun (f, rs) -> PublishDiagnostics {uri = (match Uri.TryCreate(f, UriKind.Absolute) with |TrySuccess value -> value |TryFailure -> eprintfn "%s" f; Uri "/") ; diagnostics = List.map snd rs}))
                    // |> List.iter (fun f -> LanguageServer.sendNotification send f)
            with
                | :? System.Exception as e -> eprintfn "%A" e

        |None -> ()
        client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("");  "enable", JsonValue.Boolean(false) |])


    let completionResolveItem (item :CompletionItem) =
        async {
            eprintfn "Completion resolve"
            return match stlGameObj, eu4GameObj, hoi4GameObj, ck2GameObj with
                    |Some game, _, _, _ ->
                        let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                        let hovered = allEffects |> List.tryFind (fun e -> e.Name = item.label)
                        match hovered with
                        |Some effect ->
                            match effect with
                            | :? DocEffect as de ->
                                let desc = "_" + de.Desc.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                                let usage = de.Usage
                                let content = String.Join("\n***\n",[desc; scopes; usage]) // TODO: usageeffect.Usage])
                                //{item with documentation = (MarkupContent ("markdown", content))}
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | :? ScriptedEffect as se ->
                                let desc = se.Name.Replace("_", "\\_")
                                let comments = se.Comments.Replace("_", "\\_")
                                let scopes = "Supports scopes: " + String.Join(", ", se.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; comments; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | e ->
                                let desc = "_" + e.Name.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                        |None -> item
                    |_, Some game, _, _ ->
                        let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                        let hovered = allEffects |> List.tryFind (fun e -> e.Name = item.label)
                        match hovered with
                        |Some effect ->
                            match effect with
                            | :? DocEffect<EU4Constants.Scope> as de ->
                                let desc = "_" + de.Desc.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                                let usage = de.Usage
                                let content = String.Join("\n***\n",[desc; scopes; usage]) // TODO: usageeffect.Usage])
                                //{item with documentation = (MarkupContent ("markdown", content))}
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | :? ScriptedEffect<EU4Constants.Scope> as se ->
                                let desc = se.Name.Replace("_", "\\_")
                                let comments = se.Comments.Replace("_", "\\_")
                                let scopes = "Supports scopes: " + String.Join(", ", se.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; comments; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | e ->
                                let desc = "_" + e.Name.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                        |None -> item
                    |_, _, Some game, _ ->
                        let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                        let hovered = allEffects |> List.tryFind (fun e -> e.Name = item.label)
                        match hovered with
                        |Some effect ->
                            match effect with
                            | :? DocEffect<HOI4Constants.Scope> as de ->
                                let desc = "_" + de.Desc.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                                let usage = de.Usage
                                let content = String.Join("\n***\n",[desc; scopes; usage]) // TODO: usageeffect.Usage])
                                //{item with documentation = (MarkupContent ("markdown", content))}
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | :? ScriptedEffect<HOI4Constants.Scope> as se ->
                                let desc = se.Name.Replace("_", "\\_")
                                let comments = se.Comments.Replace("_", "\\_")
                                let scopes = "Supports scopes: " + String.Join(", ", se.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; comments; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | e ->
                                let desc = "_" + e.Name.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                    |_, _, _, Some game ->
                        let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                        let hovered = allEffects |> List.tryFind (fun e -> e.Name = item.label)
                        match hovered with
                        |Some effect ->
                            match effect with
                            | :? DocEffect<CK2Constants.Scope> as de ->
                                let desc = "_" + de.Desc.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                                let usage = de.Usage
                                let content = String.Join("\n***\n",[desc; scopes; usage]) // TODO: usageeffect.Usage])
                                //{item with documentation = (MarkupContent ("markdown", content))}
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | :? ScriptedEffect<CK2Constants.Scope> as se ->
                                let desc = se.Name.Replace("_", "\\_")
                                let comments = se.Comments.Replace("_", "\\_")
                                let scopes = "Supports scopes: " + String.Join(", ", se.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; comments; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                            | e ->
                                let desc = "_" + e.Name.Replace("_", "\\_") + "_"
                                let scopes = "Supports scopes: " + String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                                let content = String.Join("\n***\n",[desc; scopes]) // TODO: usageeffect.Usage])
                                {item with documentation = Some ({kind = MarkupKind.Markdown ; value = content})}
                        |None -> item
                    |None, None, None, None -> item
        }
    let isRangeInError (range : LSP.Types.Range) (start : range) (length : int) =
        range.start.line = (int start.StartLine - 1) && range.``end``.line = (int start.StartLine - 1)
        && range.start.character >= int start.StartColumn && range.``end``.character <= (int start.StartColumn + length)

    let isRangeInRange (range : LSP.Types.Range) (inner : LSP.Types.Range) =
        (range.start.line < inner.start.line || (range.start.line = inner.start.line && range.start.character <= inner.start.character))
        && (range.``end``.line > inner.``end``.line || (range.``end``.line = inner.``end``.line && range.``end``.character >= inner.``end``.character))
    let catchError (defaultValue) (a : Async<_>) =
        async {
            try
                return! a
            with
                | ex ->
                    client.LogMessage { ``type`` = MessageType.Error; message = (sprintf "%A" ex)}
                    return defaultValue
        }



    interface ILanguageServer with
        member this.Initialize(p: InitializeParams) =
            async {
                rootUri <- p.rootUri
                match p.initializationOptions with
                | Some opt ->
                    match opt.Item("language") with
                    | JsonValue.String "stellaris" ->
                        activeGame <- STL
                    | JsonValue.String "hoi4" ->
                        activeGame <- HOI4
                    | JsonValue.String "eu4" ->
                        activeGame <- EU4
                    | JsonValue.String "ck2" ->
                        activeGame <- CK2
                    | _ -> ()
                    match opt.Item("rulesCache") with
                    | JsonValue.String x ->
                        match activeGame with
                        | STL -> cachePath <- Some (x + "/stellaris")
                        | HOI4 -> cachePath <- Some (x + "/hoi4")
                        | EU4 -> cachePath <- Some (x + "/eu4")
                        | CK2 -> cachePath <- Some (x + "/ck2")
                        | _ -> ()
                    | _ -> ()
                    match opt.Item("repoPath") with
                    | JsonValue.String x ->
                        eprintfn "rps %A" x
                        remoteRepoPath <- Some x
                    | x -> eprintfn "t %A" x
                    match opt.Item("isVanillaFolder") with
                    | JsonValue.Boolean b ->
                        if b then eprintfn "Client thinks this is a vanilla directory" else ()
                        isVanillaFolder <- b
                    | _ -> ()
                    // match opt.Item("rulesVersion") with
                    // | JsonValue.Array x ->
                    //     match x with
                    //     |[|JsonValue.String s; JsonValue.String e|] ->
                    //         stellarisCacheVersion <- Some s
                    //         eu4CacheVersion <- Some e
                    //     | _ -> ()
                    // | _ -> ()
                    match opt.Item("rules_version") with
                    | JsonValue.String x ->
                        match x with
                        |"none" ->
                            useEmbeddedRules <- true
                            rulesChannel <- "none"
                        |"manual" ->
                            useManualRules <- true
                            rulesChannel <- "manual"
                        |x -> rulesChannel <- x
                        | _ -> ()
                    | _ -> ()

                |None -> ()
                eprintfn "New init %s" (p.ToString())
                return { capabilities =
                    { defaultServerCapabilities with
                        hoverProvider = true
                        definitionProvider = true
                        referencesProvider = true
                        textDocumentSync =
                            { defaultTextDocumentSyncOptions with
                                openClose = true
                                willSave = true
                                save = Some { includeText = true }
                                change = TextDocumentSyncKind.Full }
                        completionProvider = Some {resolveProvider = true; triggerCharacters = []}
                        codeActionProvider = true
                        documentSymbolProvider = true
                        executeCommandProvider = Some {commands = ["genlocfile"; "genlocall"; "outputerrors"; "reloadrulesconfig"; "cacheVanilla"; "listAllFiles";"listAllLocFiles"]} } }
            }
        member this.Initialized() =
            async { () }
        member this.Shutdown() =
            async { return None }
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams) =
            async {
                let newLanguages =
                    match p.settings.Item("cwtools").Item("localisation").Item("languages"), activeGame with
                    | JsonValue.Array o, STL ->
                        o |> Array.choose (function |JsonValue.String s -> (match STLLang.TryParse<STLLang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                          |> List.ofArray
                          |> (fun l ->  if List.isEmpty l then [STLLang.English] else l)
                          |> List.map Lang.STL
                    | _, STL -> [Lang.STL STLLang.English]
                    | JsonValue.Array o, EU4 ->
                        o |> Array.choose (function |JsonValue.String s -> (match EU4Lang.TryParse<EU4Lang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                          |> List.ofArray
                          |> (fun l ->  if List.isEmpty l then [EU4Lang.English] else l)
                          |> List.map Lang.EU4
                    | _, EU4 -> [Lang.EU4 EU4Lang.English]
                    | JsonValue.Array o, HOI4 ->
                        o |> Array.choose (function |JsonValue.String s -> (match HOI4Lang.TryParse<HOI4Lang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                          |> List.ofArray
                          |> (fun l ->  if List.isEmpty l then [HOI4Lang.English] else l)
                          |> List.map Lang.HOI4
                    | _, HOI4 -> [Lang.HOI4 HOI4Lang.English]
                    | JsonValue.Array o, CK2 ->
                        o |> Array.choose (function |JsonValue.String s -> (match CK2Lang.TryParse<CK2Lang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                          |> List.ofArray
                          |> (fun l ->  if List.isEmpty l then [CK2Lang.English] else l)
                          |> List.map Lang.CK2
                    | _, CK2 -> [Lang.CK2 CK2Lang.English]
                languages <- newLanguages
                let newVanillaOnly =
                    match p.settings.Item("cwtools").Item("errors").Item("vanilla") with
                    | JsonValue.Boolean b -> b
                    | _ -> false
                validateVanilla <- newVanillaOnly
                let newExperimental =
                    match p.settings.Item("cwtools").Item("experimental") with
                    | JsonValue.Boolean b -> b
                    | _ -> false
                experimental <- newExperimental
                let newIgnoreCodes =
                    match p.settings.Item("cwtools").Item("errors").Item("ignore") with
                    | JsonValue.Array o ->
                        o |> Array.choose (function |JsonValue.String s -> Some s |_ -> None)
                          |> List.ofArray
                    | _ -> []
                ignoreCodes <- newIgnoreCodes
                let newIgnoreFiles =
                    match p.settings.Item("cwtools").Item("errors").Item("ignorefiles") with
                    | JsonValue.Array o ->
                        o |> Array.choose (function |JsonValue.String s -> Some s |_ -> None)
                          |> List.ofArray
                    | _ -> []
                ignoreFiles <- newIgnoreFiles
                let excludePatterns =
                    match p.settings.Item("cwtools").Item("ignore_patterns") with
                    | JsonValue.Array o ->
                        o |> Array.choose (function |JsonValue.String s -> Some s |_ -> None)
                          |> List.ofArray
                    | _ -> []
                dontLoadPatterns <- excludePatterns
                match p.settings.Item("cwtools").Item("trace").Item("server") with
                | JsonValue.String "messages"
                | JsonValue.String "verbose" -> CWTools.Utilities.Utils.loglevel <- CWTools.Utilities.Utils.LogLevel.Verbose
                |_ -> ()
                match p.settings.Item("cwtools").Item("cache").Item("eu4") with
                | JsonValue.String "" -> ()
                | JsonValue.String s ->
                    eu4VanillaPath <- Some s
                |_ -> ()
                match p.settings.Item("cwtools").Item("cache").Item("stellaris") with
                | JsonValue.String "" -> ()
                | JsonValue.String s ->
                    stlVanillaPath <- Some s
                |_ -> ()
                match p.settings.Item("cwtools").Item("cache").Item("hoi4") with
                | JsonValue.String "" -> ()
                | JsonValue.String s ->
                    hoi4VanillaPath <- Some s
                match p.settings.Item("cwtools").Item("cache").Item("ck2") with
                | JsonValue.String "" -> ()
                | JsonValue.String s ->
                    ck2VanillaPath <- Some s
                |_ -> ()
                match p.settings.Item("cwtools").Item("rules_folder") with
                | JsonValue.String x ->
                    manualRulesFolder <- Some x
                |_ -> ()

                eprintfn "New configuration %s" (p.ToString())
                match cachePath with
                |Some dir ->
                    if Directory.Exists dir then () else Directory.CreateDirectory dir |> ignore
                |_ -> ()
                let task = new Task((fun () -> checkOrSetGameCache(false); processWorkspace(rootUri)))
                task.Start()
                let task = new Task((fun () -> setupRulesCaches()))
                task.Start()
            }

        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams) =
            async {
                docs.Open p
                lintAgent.Post (UpdateRequest ({uri = p.textDocument.uri; version = p.textDocument.version}, true))
                // let task =
                //     new Task((fun () ->
                //                         match gameObj with
                //                         |Some game ->
                //                             locCache <- game.LocalisationErrors(true, true) |> List.groupBy (fun (_, _, r, _, _, _) -> r.FileName) |> Map.ofList
                //                             eprintfn "lc %A" locCache
                //                         |None -> ()
                //     ))
                // task.Start()
            }

        member this.DidFocusFile(p : DidFocusFileParams) =
            async {
                lintAgent.Post (UpdateRequest ({uri = p.uri; version = 0}, true))
            }
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams) =
            async {
                docs.Change p
                lintAgent.Post (UpdateRequest ({uri = p.textDocument.uri; version = p.textDocument.version}, false))
            }
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams) =
            async {
                lintAgent.Post (UpdateRequest ({uri = p.textDocument.uri; version = 0}, true))
            }
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams) = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams) =
            async {
                lintAgent.Post (UpdateRequest ({uri = p.textDocument.uri; version = 0}, false))
            }
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams) =
            async {
                docs.Close p
            }
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams) =
            async {
                for change in p.changes do
                    match change.``type`` with
                    |FileChangeType.Created ->

                        lintAgent.Post (UpdateRequest ({uri = change.uri; version = 0}, true))
                        //eprintfn "Watched file %s %s" (change.uri.ToString()) (change._type.ToString())
                    |FileChangeType.Deleted ->
                        client.PublishDiagnostics {uri = change.uri; diagnostics = []}
                    |_ ->                 ()
                    // if change.uri.AbsolutePath.EndsWith ".fsproj" then
                    //     projects.UpdateProjectFile change.uri
                        //lintAgent.Post (UpdateRequest {uri = p.textDocument.uri; version = p.textDocument.version})
            }
        member this.Completion(p: TextDocumentPositionParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        // match experimental_completion with
                        // |true ->
                            let position = Pos.fromZ p.position.line p.position.character// |> (fun p -> Pos.fromZ)
                            let path =
                                let u = p.textDocument.uri
                                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                                then u.LocalPath.Substring(1)
                                else u.LocalPath
                            let comp = game.Complete position (path) (docs.GetText (FileInfo(p.textDocument.uri.LocalPath)) |> Option.defaultValue "")
                            // let extraKeywords = ["yes"; "no";]
                            // let eventIDs = game.References.EventIDs
                            // let names = eventIDs @ game.References.TriggerNames @ game.References.EffectNames @ game.References.ModifierNames @ game.References.ScopeNames @ extraKeywords
                            let items =
                                comp |> List.map (
                                    function
                                    |Simple (e, Some score) -> {defaultCompletionItem with label = e; sortText = Some ((maxCompletionScore - score).ToString())}
                                    |Simple (e, None) -> {defaultCompletionItem with label = e}
                                    |Detailed (l, d, Some score) -> {defaultCompletionItem with label = l; documentation = d |> Option.map (fun d -> {kind = MarkupKind.Markdown; value = d}); sortText = Some ((maxCompletionScore - score).ToString())}
                                    |Detailed (l, d, None) -> {defaultCompletionItem with label = l; documentation = d |> Option.map (fun d -> {kind = MarkupKind.Markdown; value = d})}
                                    |Snippet (l, e, d, Some score) -> {defaultCompletionItem with label = l; insertText = Some e; insertTextFormat = Some InsertTextFormat.Snippet; documentation = d |> Option.map (fun d ->{kind = MarkupKind.Markdown; value = d}); sortText = Some ((maxCompletionScore - score).ToString())}
                                    |Snippet (l, e, d, None) -> {defaultCompletionItem with label = l; insertText = Some e; insertTextFormat = Some InsertTextFormat.Snippet; documentation = d |> Option.map (fun d ->{kind = MarkupKind.Markdown; value = d})})
                            // let variables = game.References.ScriptVariableNames |> List.map (fun v -> {defaultCompletionItem with label = v; kind = Some CompletionItemKind.Variable })
                            let deduped = items |> List.distinctBy(fun i -> i.label) |> List.filter (fun i -> not (i.label.StartsWith("$", StringComparison.OrdinalIgnoreCase)))
                            Some {isIncomplete = false; items = deduped}
                        // |false ->
                        //     let extraKeywords = ["yes"; "no";]
                        //     let eventIDs = game.References.EventIDs
                        //     let names = eventIDs @ game.References.TriggerNames @ game.References.EffectNames @ game.References.ModifierNames @ game.References.ScopeNames @ extraKeywords
                        //     let variables = game.References.ScriptVariableNames |> List.map (fun v -> {defaultCompletionItem with label = v; kind = Some CompletionItemKind.Variable })
                        //     let items = names |> List.map (fun n -> {defaultCompletionItem with label = n})
                        //     Some {isIncomplete = false; items = items @ variables}
                    |None -> None
            } |> catchError None
        member this.Hover(p: TextDocumentPositionParams) =
            async {
                return (LanguageServerFeatures.hoverDocument eu4GameObj hoi4GameObj stlGameObj client docs p.textDocument.uri p.position) |> Async.RunSynchronously |> Some
            } |> catchError None

        member this.ResolveCompletionItem(p: CompletionItem) =
            async {
                return completionResolveItem(p) |> Async.RunSynchronously
            } |> catchError p
        member this.SignatureHelp(p: TextDocumentPositionParams) = TODO()
        member this.GotoDefinition(p: TextDocumentPositionParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        let position = Pos.fromZ p.position.line p.position.character// |> (fun p -> Pos.fromZ)
                        eprintfn "goto fn %A" p.textDocument.uri
                        let path =
                            let u = p.textDocument.uri
                            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                            then u.LocalPath.Substring(1)
                            else u.LocalPath
                        let gototype = game.GoToType position (path) (docs.GetText (FileInfo(p.textDocument.uri.LocalPath)) |> Option.defaultValue "")
                        match gototype with
                        |Some goto ->
                            eprintfn "goto %s" goto.FileName
                            [{ uri = Uri(goto.FileName); range = (convRangeToLSPRange goto)}]
                        |None -> []
                    |None -> []
                }
        member this.FindReferences(p: ReferenceParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        let position = Pos.fromZ p.position.line p.position.character// |> (fun p -> Pos.fromZ)
                        let path =
                            let u = p.textDocument.uri
                            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                            then u.LocalPath.Substring(1)
                            else u.LocalPath
                        let gototype = game.FindAllRefs position (path) (docs.GetText (FileInfo(p.textDocument.uri.LocalPath)) |> Option.defaultValue "")
                        match gototype with
                        |Some gotos ->
                            gotos |> List.map (fun goto -> { uri = Uri(goto.FileName); range = (convRangeToLSPRange goto)})
                        |None -> []
                    |None -> []
                }
        member this.DocumentHighlight(p: TextDocumentPositionParams) = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams) =
            let createDocumentSymbol name detail range =
                let range = convRangeToLSPRange range
                {
                    name = name
                    detail = detail
                    kind = SymbolKind.Class
                    deprecated = false
                    range = range
                    selectionRange = range
                    children = []
                }
            async {
                return
                    match gameObj with
                    |Some game ->
                        let types = game.Types()
                        let (all : DocumentSymbol list) =
                            types |> Map.toList
                              |> List.collect (fun (k, vs) -> vs |> List.filter (fun (v, r) -> r.FileName = p.textDocument.uri.LocalPath)  |> List.map (fun (v, r) -> createDocumentSymbol v k r))
                              |> List.rev
                              |> List.filter (fun ds -> not (ds.name.Contains(".")))
                        all |> List.fold (fun (acc : DocumentSymbol list) (next : DocumentSymbol) ->
                                                    if acc |> List.exists (fun a -> isRangeInRange a.range next.range && a.name <> next.name)
                                                    then
                                                    acc |> List.map (fun (a : DocumentSymbol) -> if isRangeInRange a.range next.range && a.name <> next.name then { a with children = (next::(a.children))} else a )
                                                    else next::acc) []
                        // all |> List.fold (fun acc next -> acc |> List.tryFind (fun a -> isRangeInRange a.range next.range) |> function |None -> next::acc |Some ) []
                            //   |> List.map (fun (k, vs) -> createDocumentSymbol k )
                    |None -> []
            }
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams) = TODO()

        member this.CodeActions(p: CodeActionParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        let es = locCache
                        let path =
                            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && p.textDocument.uri.LocalPath.StartsWith "/"
                            then p.textDocument.uri.LocalPath.Substring(1)
                            else p.textDocument.uri.LocalPath

                        let les = es.TryFind (path) |> Option.defaultValue []
                        let les = les |> List.filter (fun (_, e, r, l, _, _) -> (r) |> (fun a -> (isRangeInError p.range a l)) )
                        match les with
                        |[] -> []
                        |_ ->
                            [
                                {title = "Generate localisation .yml for this file"; command = "genlocfile"; arguments = [p.textDocument.uri.LocalPath |> JsonValue.String]}
                                {title = "Generate localisation .yml for all"; command = "genlocall"; arguments = []}
                            ]
                    |None -> []
            }
        member this.CodeLens(p: CodeLensParams) = TODO()
        member this.ResolveCodeLens(p: CodeLens) = TODO()
        member this.DocumentLink(p: DocumentLinkParams) = TODO()
        member this.ResolveDocumentLink(p: DocumentLink) = TODO()
        member this.DocumentFormatting(p: DocumentFormattingParams) = TODO()
        member this.DocumentRangeFormatting(p: DocumentRangeFormattingParams) = TODO()
        member this.DocumentOnTypeFormatting(p: DocumentOnTypeFormattingParams) = TODO()
        member this.DidChangeWorkspaceFolders(p: DidChangeWorkspaceFoldersParams) = TODO()
        member this.Rename(p: RenameParams) = TODO()
        member this.ExecuteCommand(p: ExecuteCommandParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        match p with
                        | {command = "genlocfile"; arguments = x::_} ->
                            let les = game.LocalisationErrors(true, true) |> List.filter (fun (_, e, pos,_, _, _) -> (pos) |> (fun a -> a.FileName = x.AsString()))
                            let keys = les |> List.sortBy (fun (_, _, p, _, _, _) -> (p.FileName, p.StartLine))
                                           |> List.choose (fun (_, _, _, _, _, k) -> k)
                                           |> List.map (sprintf " %s:0 \"REPLACE_ME\"")
                                           |> List.distinct
                            let text = String.Join(Environment.NewLine,keys)
                            //let notif = CreateVirtualFile { uri = Uri "cwtools://1"; fileContent = text }
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://1");  "fileContent", JsonValue.String(text) |])
                        | {command = "genlocall"; arguments = _} ->
                            let les = game.LocalisationErrors(true, true)
                            let keys = les |> List.sortBy (fun (_, _, p, _, _, _) -> (p.FileName, p.StartLine))
                                           |> List.choose (fun (_, _, _, _, _, k) -> k)
                                           |> List.map (sprintf " %s:0 \"REPLACE_ME\"")
                                           |> List.distinct
                            let text = String.Join(Environment.NewLine,keys)
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://1");  "fileContent", JsonValue.String(text) |])
                            //LanguageServer.sendNotification send notif
                        | {command = "outputerrors"; arguments = _} ->
                            let errors = game.LocalisationErrors(true, true) @ game.ValidationErrors()
                            let texts = errors |> List.map (fun (code, sev, pos, _, error, _) -> sprintf "%s, %O, %O, %s, %O, \"%s\"" pos.FileName pos.StartLine pos.StartColumn code sev error)
                            let text = String.Join(Environment.NewLine, (texts))
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://errors.csv");  "fileContent", JsonValue.String(text) |])
                        | {command = "reloadrulesconfig"; arguments = _} ->
                            let configs = getConfigFiles()
                            game.ReplaceConfigRules configs
                        | {command = "cacheVanilla"; arguments = _} ->
                            checkOrSetGameCache(true)
                        | {command ="listAllFiles"; arguments =_} ->
                            let resources = game.AllFiles()
                            let text =
                                resources |> List.map (fun r ->
                                    match r with
                                    | EntityResource (f, _) -> f
                                    | FileResource (f, _) -> f
                                    | FileWithContentResource (f, _) -> f
                                )
                            let text = String.Join(Environment.NewLine, (text))
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://allfiles");  "fileContent", JsonValue.String(text) |])
                        | {command = "listAllLocFiles"; arguments = _} ->
                            let locs = game.AllLoadedLocalisation()
                            let text = String.Join(Environment.NewLine, (locs))
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://alllocfiles");  "fileContent", JsonValue.String(text) |])
                            // eprintfn "%A %A %A" (vanillaDirectory.AsString()) (cacheDirectory.AsString()) (cacheGame.AsString())
                            // match cacheGame.AsString() with
                            // |"stellaris" ->
                            //     serializeSTL (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            // |"hoi4" ->
                            //     serializeHOI4 (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            // |"eu4" ->
                            //     serializeEU4 (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            // client.CustomNotification ("promptReload", JsonValue.String("Cached generated, reload to use"))
                        |_ -> ()
                    |None -> ()
            }


[<EntryPoint>]
let main (argv: array<string>): int =
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    let read = new BinaryReader(Console.OpenStandardInput())
    let write = new BinaryWriter(Console.OpenStandardOutput())
    let serverFactory(client) = Server(client) :> ILanguageServer
    eprintfn "Listening on stdin"
    LanguageServer.connect(serverFactory, read, write)
    0 // return an integer exit code
    //eprintfn "%A" (JsonValue.Parse "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"processId\":12660,\"rootUri\": \"file:///c%3A/Users/Thomas/Documents/Paradox%20Interactive/Stellaris\"},\"capabilities\":{\"workspace\":{}}}")
    //0
