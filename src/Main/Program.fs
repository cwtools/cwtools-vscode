module Main.Program

open LSP
open LSP.Types
open System
open System.IO
open CWTools.Parser
open CWTools.Parser.EU4Parser
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

let private TODO() = raise (Exception "TODO")

[<assembly: AssemblyDescription("CWTools language server for PDXScript")>]
do()

type LintRequestMsg =
    | UpdateRequest of VersionedTextDocumentIdentifier * bool
    | WorkComplete of DateTime

type GameLanguage = |STL |HOI4 |EU4
type Server(client: ILanguageClient) =
    let docs = DocumentStore()
    let projects = ProjectManager()
    let notFound (doc: Uri) (): 'Any =
        raise (Exception (sprintf "%s does not exist" (doc.ToString())))
    let mutable docErrors : DocumentHighlight list = []

    let mutable activeGame = STL
    let mutable gameObj : option<IGame> = None
    let mutable stlGameObj : option<IGame<STLComputedData, STLConstants.Scope, STLConstants.Modifier>> = None
    let mutable hoi4GameObj : option<IGame<HOI4ComputedData, HOI4Constants.Scope, HOI4Constants.Modifier>> = None
    let mutable eu4GameObj : option<IGame<EU4ComputedData, EU4Constants.Scope, EU4Constants.Modifier>> = None

    let mutable languages : Lang list = []
    let mutable rootUri : Uri option = None
    let mutable cachePath : string option = None
    let mutable stellarisCacheVersion : string option = None
    let mutable eu4CacheVersion : string option = None
    let mutable hoi4CacheVersion : string option = None
    let mutable remoteRepoPath : string option = None

    let mutable rulesChannel : string = "stable"
    let mutable useEmbeddedRules : bool = false
    let mutable validateVanilla : bool = false
    let mutable experimental : bool = false

    let mutable ignoreCodes : string list = []
    let mutable ignoreFiles : string list = []
    let mutable locCache = []

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
                    try {uri = (match Uri.TryCreate(f, UriKind.Absolute) with |TrySuccess value -> value |TryFailure -> eprintfn "%s" f; Uri "/") ; diagnostics = List.map snd rs} with |e -> failwith (sprintf "%A" rs)))
            |> List.iter (client.PublishDiagnostics)

    let lint (doc: Uri) (shallowAnalyze : bool) (forceDisk : bool) : Async<unit> =
        async {
            let name =
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && doc.LocalPath.StartsWith "/"
                then doc.LocalPath.Substring(1)
                else doc.LocalPath
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
            let errors =
                parserErrors @
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
    let delayedAnalyze() =
        match gameObj with
        |Some game -> game.RefreshCaches()
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
                            then delayedAnalyze(); nextTime <- DateTime.Now.AddSeconds(30.0);
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
            match cachePath, useEmbeddedRules with
            | Some path, false ->
                let configFiles = (getAllFoldersUnion ([path] |> Seq.ofList)) |> Seq.collect (Directory.EnumerateFiles)
                let configFiles = configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt")
                configFiles |> List.map (fun f -> f, File.ReadAllText(f))
            | _ ->
                let embeddedConfigFileNames = Assembly.GetEntryAssembly().GetManifestResourceNames() |> Array.filter (fun f -> f.Contains("config.config") && f.EndsWith(".cwt"))
                embeddedConfigFileNames |> List.ofArray |> List.map (fun f -> fixEmbeddedFileName f, (new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(f))).ReadToEnd())
        let configpath = "Main.files.config.cwt"
        let configFiles = (if Directory.Exists "./.cwtools" then getAllFoldersUnion (["./.cwtools"] |> Seq.ofList) else Seq.empty) |> Seq.collect (Directory.EnumerateFiles)
        let configFiles = configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt")
        let configs =
            match configFiles.Length > 0 with
            |true ->
                configFiles |> List.map (fun f -> f, File.ReadAllText(f))
                //["./config.cwt", File.ReadAllText("./config.cwt")]
            |false ->
                embeddedConfigFiles
        configs

    let setupRulesCaches()  =
        match cachePath, remoteRepoPath, useEmbeddedRules with
        |Some cp, Some rp, false ->
            let stable = rulesChannel <> "latest"
            match initOrUpdateRules rp cp stable true with
            |true, Some date ->
                let text = sprintf "Validation rules for %O have been updated to %O." activeGame date
                client.CustomNotification ("forceReload", JsonValue.String(text))
            |_ -> ()
        |_ -> ()

    let getFolderList (filename : string, filetext : string) =
        if Path.GetFileName filename = "folders.cwt"
        then Some (filetext.Split(([|"\r\n"; "\r"; "\n"|]), StringSplitOptions.None) |> List.ofArray)
        else None


    let processWorkspace (uri : option<Uri>) =
        client.CustomNotification  ("loadingBar", JsonValue.Record [| "value", JsonValue.String("Loading project...");  "enable", JsonValue.Boolean(true) |])
        match uri with
        |Some u ->
            let path =
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                then u.LocalPath.Substring(1)
                else u.LocalPath
            try
                let timer = new System.Diagnostics.Stopwatch()
                timer.Start()

                eprintfn "%s" path
                let filelist = Assembly.GetEntryAssembly().GetManifestResourceStream("Main.files.vanilla_files_2.1.3.csv")
                                |> (fun f -> (new StreamReader(f)).ReadToEnd().Split(Environment.NewLine))
                                |> Array.toList |> List.map (fun f -> f, "")
                let docspath = "Main.files.trigger_docs_2.2.txt"
                let docs = DocsParser.parseDocsStream (Assembly.GetEntryAssembly().GetManifestResourceStream(docspath))
                let embeddedFileNames = Assembly.GetEntryAssembly().GetManifestResourceNames() |> Array.filter (fun f -> f.Contains("common") || f.Contains("localisation") || f.Contains("interface") || f.Contains("events") || f.Contains("gfx") || f.Contains("sound") || f.Contains("music") || f.Contains("fonts") || f.Contains("flags") || f.Contains("prescripted_countries"))
                let embeddedFiles = embeddedFileNames |> List.ofArray |> List.map (fun f -> fixEmbeddedFileName f, (new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(f))).ReadToEnd())
                let assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)
                eprintfn "Parse docs time: %i" timer.ElapsedMilliseconds; timer.Restart()

                let stlCacheLocation cp = if File.Exists (cp + "/../stl.cwb") then (cp + "/../stl.cwb") else (assemblyLocation + "/../../../embedded/pickled.xml")
                let cached, cachedFiles =
                    match activeGame, cachePath with
                    |STL, Some cp -> deserialize (stlCacheLocation cp)
                    |EU4, Some cp -> deserialize (cp + "/../eu4.cwb")
                    |HOI4, Some cp -> deserialize (cp + "/../hoi4.cwb")
                    |_ -> [], []
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

                let stlsettings = {
                    CWTools.Games.Stellaris.StellarisSettings.rootDirectory = path
                    scope = FilesScope.All
                    modFilter = None
                    scriptFolders = folders
                    validation = {
                        validateVanilla = validateVanilla
                        experimental = experimental
                        langs = languages
                    }
                    rules = Some {
                        ruleFiles = configs
                        validateRules = true
                        debugRulesOnly = false
                    }
                    embedded = {
                        triggers = triggers
                        effects = effects
                        modifiers = modifiers
                        embeddedFiles = cachedFiles
                        cachedResourceData = cached
                    }
                }
                let hoi4modpath = "Main.files.hoi4.modifiers"
                let hoi4Mods =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
                            |> Option.map (fun (fn, ft) -> HOI4Parser.loadModifiers fn ft)
                            |> Option.defaultValue []
                // let hoi4Mods = HOI4Parser.loadModifiers "hoi4mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(hoi4modpath))).ReadToEnd())

                let hoi4settings = {
                    HOI4.rootDirectory = path
                    HOI4.scriptFolders = folders
                    HOI4.embedded = {
                        CWTools.Games.HOI4.embeddedFiles = []
                        HOI4.modifiers = hoi4Mods
                        cachedResourceData = cached
                    }
                    HOI4.validation = {
                        HOI4.validateVanilla = validateVanilla;
                        HOI4.langs = [(Lang.HOI4 (HOI4Lang.English))]
                        HOI4.experimental = experimental
                    }
                    HOI4.rules = Some {
                        ruleFiles = configs
                        validateRules = true
                    }
                    HOI4.scope = FilesScope.All
                    HOI4.modFilter = None
                }
                let eu4modpath = "Main.files.eu4.modifiers"
                let eu4Mods =
                    configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
                            |> Option.map (fun (fn, ft) -> EU4Parser.loadModifiers fn ft)
                            |> Option.defaultValue []

                // let eu4Mods = EU4Parser.loadModifiers "eu4mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(eu4modpath))).ReadToEnd())
                let eu4settings = {
                    EU4.rootDirectory = path
                    EU4.scriptFolders = folders
                    EU4.embedded = {
                        CWTools.Games.EU4.embeddedFiles = []
                        EU4.modifiers = eu4Mods
                        cachedResourceData = cached
                    }
                    EU4.validation = {
                        EU4.validateVanilla = validateVanilla;
                        EU4.langs = [(Lang.EU4 (EU4Lang.English))]
                    }
                    EU4.rules = Some {
                        ruleFiles = configs
                        validateRules = true
                    }
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
                let locRaw = game.LocalisationErrors(true)
                locCache <- locRaw
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

    let hoverDocument (doc :Uri, pos: LSP.Types.Position) =
        async {
            //eprintfn "Hover before word"
            let pjson = pos |> (serializerFactory<LSP.Types.Position> defaultJsonWriteOptions)
            let ujson = doc |> (serializerFactory<Uri> defaultJsonWriteOptions)
            let json = serializerFactory<GetWordRangeAtPositionParams> defaultJsonWriteOptions ({ position = pos; uri = doc })
            let! word = client.CustomRequest("getWordRangeAtPosition", json)
            // let! word = client.CustomRequest("getWordRangeAtPosition", JsonValue.Record [| "position", JsonValue.Parse(pjson); "uri", doc |])
            //eprintfn "Hover after word"
            let position = Pos.fromZ pos.line pos.character// |> (fun p -> Pos.fromZ)
            let path =
                let u = doc
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
                then u.LocalPath.Substring(1)
                else u.LocalPath
            let unescapedword = word.ToString().Replace("\\\"", "\"").Trim('"')
            let hoverFunction (game : IGame<_, 'a, _>) =
                let scopeContext = game.ScopesAtPos position (path) (docs.GetText (FileInfo (doc.LocalPath)) |> Option.defaultValue "")
                let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                eprintfn "Looking for effect %s in the %i effects loaded" (word.ToString()) (allEffects.Length)
                let hovered = allEffects |> List.tryFind (fun e -> e.Name = unescapedword)
                let lochover = game.References().Localisation |> List.tryFind (fun (k, v) -> k = unescapedword)
                let scopesExtra = if scopeContext.IsNone then "" else
                    let scopes = scopeContext.Value
                    let header = "| Context | Scope |\n| ----- | -----|\n"
                    let root = sprintf "| ROOT | %s |\n" (scopes.Root.ToString())
                    let prevs = scopes.Scopes |> List.mapi (fun i s -> "| " + (if i = 0 then "THIS" else (String.replicate (i) "PREV")) + " | " + (s.ToString()) + " |\n") |> String.concat ""
                    let froms = scopes.From |> List.mapi (fun i s -> "| " + (String.replicate (i+1) "FROM") + " | " + (s.ToString()) + " |\n") |> String.concat ""
                    header + root + prevs + froms

                match hovered, lochover with
                |Some effect, _ ->
                    match effect with
                    | :? DocEffect<'a> as de ->
                        let scopes = String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                        let desc = de.Desc.Replace("_", "\\_").Trim() |> (fun s -> if s = "" then "" else "_"+s+"_" )
                        let content = String.Join("\n***\n",[desc; "Supports scopes: " + scopes; scopesExtra]) // TODO: usageeffect.Usage])
                        {contents = (MarkupContent ("markdown", content)) ; range = None}
                    | e ->
                        let scopes = String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                        let name = e.Name.Replace("_","\\_").Trim()
                        let content = String.Join("\n***\n",["_"+name+"_"; "Supports scopes: " + scopes; scopesExtra]) // TODO: usageeffect.Usage])
                        {contents = (MarkupContent ("markdown", content)) ; range = None}
                |None, Some (_, loc) ->
                    {contents = MarkupContent ("markdown", loc.desc + "\n***\n" + scopesExtra); range = None}
                |None, None ->
                    {contents = MarkupContent ("markdown", scopesExtra); range = None}
            return
                match stlGameObj, hoi4GameObj, eu4GameObj with
                |Some game, _, _ -> hoverFunction game
                |_, Some game, _ -> hoverFunction game
                // |_, Some game, _ ->
                //     let lochover = game.References().Localisation |> List.tryFind (fun (k, v) -> k = unescapedword)
                //     match lochover with
                //     |Some (_, loc) ->
                //         { contents = MarkupContent ("markdown", loc.desc); range = None }
                //     |None ->
                //         { contents = MarkupContent ("markdown", ""); range = None }
                |_, _, Some game -> hoverFunction game
                |_ -> {contents = MarkupContent ("markdown", ""); range = None}

        }

    let completionResolveItem (item :CompletionItem) =
        async {
            eprintfn "Completion resolve"
            return match stlGameObj, eu4GameObj, hoi4GameObj with
                    |Some game, _, _ ->
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
                    |_, Some game, _ ->
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
                    |_, _, Some game ->
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
                        |None -> item
                    |None, None, None -> item
        }
    let isRangeInError (range : LSP.Types.Range) (start : range) (length : int) =
        range.start.line = (int start.StartLine - 1) && range.``end``.line = (int start.StartLine - 1)
        && range.start.character >= int start.StartColumn && range.``end``.character <= (int start.StartColumn + length)
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
                |Some opt ->
                    match opt.Item("language") with
                    | JsonValue.String "stellaris" ->
                        activeGame <- STL
                    | JsonValue.String "hoi4" ->
                        activeGame <- HOI4
                    | JsonValue.String "eu4" ->
                        activeGame <- EU4
                    | _ -> ()
                    match opt.Item("rulesCache") with
                    | JsonValue.String x ->
                        match activeGame with
                        |STL -> cachePath <- Some (x + "/stellaris")
                        |HOI4 -> cachePath <- Some (x + "/hoi4")
                        |EU4 -> cachePath <- Some (x + "/eu4")
                        | _ -> ()
                    | _ -> ()
                    match opt.Item("repoPath") with
                    | JsonValue.String x ->
                        eprintfn "rps %A" x
                        remoteRepoPath <- Some x
                    | x -> eprintfn "t %A" x
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
                        |x -> rulesChannel <- x
                        | _ -> ()
                    | _ -> ()

                |None -> ()
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
                        executeCommandProvider = Some {commands = ["genlocfile"; "genlocall"; "outputerrors"; "reloadrulesconfig"; "cacheVanilla"]} } }
            }
        member this.Initialized() =
            async { () }
        member this.Shutdown() =
            async { return None }
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams) =
            async {
                let newLanguages =
                    match p.settings.Item("cwtools").Item("localisation").Item("languages") with
                    | JsonValue.Array o ->
                        o |> Array.choose (function |JsonValue.String s -> (match STLLang.TryParse<STLLang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                          |> List.ofArray
                          |> (fun l ->  if List.isEmpty l then [STLLang.English] else l)
                    | _ -> [STLLang.English]
                languages <- newLanguages |> List.map Lang.STL
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
                eprintfn "New configuration %s" (p.ToString())
                match cachePath with
                |Some dir ->
                    if Directory.Exists dir then () else Directory.CreateDirectory dir |> ignore
                |_ -> ()
                let task = new Task((fun () -> processWorkspace(rootUri)))
                task.Start()
                let task = new Task((fun () -> setupRulesCaches()))
                task.Start()
            }

        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams) =
            async {
                docs.Open p
                lintAgent.Post (UpdateRequest ({uri = p.textDocument.uri; version = p.textDocument.version}, true))
                match gameObj with
                |Some game -> locCache <- game.LocalisationErrors(true)
                |None -> ()
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
                                    |Simple e -> {defaultCompletionItem with label = e}
                                    |Detailed (l, d) -> {defaultCompletionItem with label = l; documentation = d |> Option.map (fun d -> {kind = MarkupKind.Markdown; value = d})}
                                    |Snippet (l, e, d) -> {defaultCompletionItem with label = l; insertText = Some e; insertTextFormat = Some InsertTextFormat.Snippet; documentation = d |> Option.map (fun d ->{kind = MarkupKind.Markdown; value = d})})
                            // let variables = game.References.ScriptVariableNames |> List.map (fun v -> {defaultCompletionItem with label = v; kind = Some CompletionItemKind.Variable })
                            let deduped = items |> List.distinctBy(fun i -> i.label)
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
                return hoverDocument (p.textDocument.uri, p.position) |> Async.RunSynchronously |> Some
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
        member this.DocumentSymbols(p: DocumentSymbolParams) = TODO()
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams) = TODO()

        member this.CodeActions(p: CodeActionParams) =
            async {
                return
                    match gameObj with
                    |Some game ->
                        let es = locCache
                        let les = es |> List.filter (fun (_, e, r, l, _, _) -> (r) |> (fun a -> (isRangeInError p.range a l) && a.FileName = p.textDocument.uri.LocalPath) )
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
                            let les = game.LocalisationErrors(true) |> List.filter (fun (_, e, pos,_, _, _) -> (pos) |> (fun a -> a.FileName = x.AsString()))
                            let keys = les |> List.sortBy (fun (_, _, p, _, _, _) -> (p.FileName, p.StartLine))
                                           |> List.choose (fun (_, _, _, _, _, k) -> k)
                                           |> List.map (sprintf " %s:0 \"REPLACE_ME\"")
                                           |> List.distinct
                            let text = String.Join(Environment.NewLine,keys)
                            //let notif = CreateVirtualFile { uri = Uri "cwtools://1"; fileContent = text }
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://1");  "fileContent", JsonValue.String(text) |])
                        | {command = "genlocall"; arguments = _} ->
                            let les = game.LocalisationErrors(true)
                            let keys = les |> List.sortBy (fun (_, _, p, _, _, _) -> (p.FileName, p.StartLine))
                                           |> List.choose (fun (_, _, _, _, _, k) -> k)
                                           |> List.map (sprintf " %s:0 \"REPLACE_ME\"")
                                           |> List.distinct
                            let text = String.Join(Environment.NewLine,keys)
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://1");  "fileContent", JsonValue.String(text) |])
                            //LanguageServer.sendNotification send notif
                        | {command = "outputerrors"; arguments = _} ->
                            let errors = game.LocalisationErrors(true) @ game.ValidationErrors()
                            let texts = errors |> List.map (fun (code, sev, pos, _, error, _) -> sprintf "%s, %O, %O, %s, %O, \"%s\"" pos.FileName pos.StartLine pos.StartColumn code sev error)
                            let text = String.Join(Environment.NewLine, (texts))
                            client.CustomNotification  ("createVirtualFile", JsonValue.Record [| "uri", JsonValue.String("cwtools://errors.csv");  "fileContent", JsonValue.String(text) |])
                        | {command = "reloadrulesconfig"; arguments = _} ->
                            let configs = getConfigFiles()
                            game.ReplaceConfigRules configs
                        | {command = "cacheVanilla"; arguments = vanillaDirectory::cacheDirectory::cacheGame::_} ->
                            eprintfn "%A %A %A" (vanillaDirectory.AsString()) (cacheDirectory.AsString()) (cacheGame.AsString())
                            match cacheGame.AsString() with
                            |"stellaris" ->
                                serializeSTL (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            |"hoi4" ->
                                serializeHOI4 (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            |"eu4" ->
                                serializeEU4 (vanillaDirectory.AsString()) (cacheDirectory.AsString())
                            client.CustomNotification ("promptReload", JsonValue.String("Cached generated, reload to use"))
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
