module Main.Program

open LSP
open LSP.Types
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open CWTools.Parser
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

let private TODO() = raise (Exception "TODO")

type Server(send : BinaryWriter) = 
    let send = send
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()
    let emptyProjectOptions = checker.GetProjectOptionsFromCommandLineArgs("NotFound.fsproj", [||])
    let notFound (doc: Uri) (): 'Any = 
        raise (Exception (sprintf "%s does not exist" (doc.ToString())))
    let mutable docErrors : DocumentHighlight list = []

    let mutable gameObj : option<STLGame> = None
    let mutable languages : Lang list = []
    let mutable rootUri : Uri option = None
    let mutable validateVanilla : bool = false
    let mutable experimental : bool = false

    let mutable ignoreCodes : string list = []
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
    let parserErrorToDiagnostics e =
        let code, sev, file, error, (position : Position), length = e
        let startC, endC = match length with
        | 0 -> 0,( int position.Column) - 1
        | x ->(int position.Column) - 1,(int position.Column) + length - 1
        let result = {
                        range = {
                                start = { 
                                        line = (int position.Line - 1)
                                        character = startC
                                    }
                                ``end`` = {
                                            line = (int position.Line - 1)
                                            character = endC
                                    }
                        }
                        severity = Some (sevToDiagSev sev)
                        code = Some code
                        source = Some code
                        message = error
                    }
        (file, result)
    let lint (doc: Uri): Async<unit> = 
        async {
            let name = 
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                then doc.LocalPath.Substring(1)
                else doc.LocalPath            
            let version = docs.GetVersion doc |> Option.defaultWith (notFound doc)
            let source = docs.GetText doc |> Option.defaultWith (notFound doc)
            let parsed = CKParser.parseEventString source name
            let parserErrors = match parsed with
                                |Success(_,_,_) -> []
                                |Failure(msg,p,s) -> [("CW001", Severity.Error, name, msg, p.Position, 0)]
            let valErrors = match gameObj with
                                |None -> []
                                |Some game ->
                                    let results = game.UpdateFile name
                                    results |> List.map (fun (c, s, n, l, e) -> let (Position p) = n in (c, s, p.StreamName, e, p, l) )
            eprintfn "%A" parserErrors
            match parserErrors @ valErrors with
            | [] -> LanguageServer.sendNotification send (PublishDiagnostics {uri = doc; diagnostics = []})
            | x -> x
                    |> List.map parserErrorToDiagnostics
                    |> List.groupBy fst
                    |> List.map ((fun (f, rs) -> f, rs |> List.filter (fun (_, d) -> match d.code with |Some s -> not (List.contains s ignoreCodes) |None -> true)) >>
                        (fun (f, rs) -> PublishDiagnostics {uri = (match Uri.TryCreate(f, UriKind.Absolute) with |TrySuccess value -> value |TryFailure -> eprintfn "%s" f; Uri "/") ; diagnostics = List.map snd rs}))
                    |> List.iter (fun f -> LanguageServer.sendNotification send f)
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

    let rec replaceFirst predicate value = function
        | [] -> []
        | h :: t when predicate h -> value :: t
        | h :: t -> h :: replaceFirst predicate value t
 
    let fixEmbeddedFileName (s : string) =
        let count = (Seq.filter ((=) '.') >> Seq.length) s
        let mutable out = "//" + s
        [1 .. count - 1] |> List.iter (fun _ -> out <- (replaceFirst ((=) '.') '/' (out |> List.ofSeq)) |> Array.ofList |> String )
        out
    let processWorkspace (uri : option<Uri>) =
        LanguageServer.sendNotification send (LoadingBar {value = true})
        match uri with
        |Some u -> 
            let path = 
                if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                then u.LocalPath.Substring(1)
                else u.LocalPath
            try
                eprintfn "%s" path
                let filelist = Assembly.GetEntryAssembly().GetManifestResourceStream("Main.files.vanilla_files_2.0.csv") 
                                |> (fun f -> (new StreamReader(f)).ReadToEnd().Split(Environment.NewLine))
                                |> Array.toList |> List.map (fun f -> f, "")
                let docspath = "Main.files.game_effects_triggers_2.0.txt"
                let docs = DocsParser.parseDocsStream (Assembly.GetEntryAssembly().GetManifestResourceStream(docspath))
                let embeddedFileNames = Assembly.GetEntryAssembly().GetManifestResourceNames() |> Array.filter (fun f -> f.Contains("common") || f.Contains("localisation") || f.Contains("interface"))
                let embeddedFiles = embeddedFileNames |> List.ofArray |> List.map (fun f -> fixEmbeddedFileName f, (new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(f))).ReadToEnd())
                
               // let docs = DocsParser.parseDocsFile @"G:\Projects\CK2 Events\CWTools\files\game_effects_triggers_1.9.1.txt"
                let triggers, effects = (docs |> (function |Success(p, _, _) -> DocsParser.processDocs p))
                let logspath = "Main.files.setup.log"
                let modfile = SetupLogParser.parseLogsStream (Assembly.GetEntryAssembly().GetManifestResourceStream(logspath))
                let modifiers = (modfile |> (function |Success(p, _, _) -> SetupLogParser.processLogs p))
                eprintfn "%A" languages                
                let game = STLGame(path, FilesScope.All, "", triggers, effects, modifiers, embeddedFiles @ filelist, languages, validateVanilla)
                gameObj <- Some game
                //eprintfn "%A" game.AllFiles
                let valErrors = game.ValidationErrors |> List.map (fun (c, s, n, l, e) -> let (Position p) = n in (c, s, p.StreamName, e, p, l) )
                let locErrors = game.LocalisationErrors |> List.map (fun (c, s, n, l, e) -> let (Position p) = n in (c, s, p.StreamName, e, p, l) )

                //eprintfn "%A" game.ValidationErrors
                let parserErrors = game.ParserErrors |> List.map (fun ( n, e, p) -> "CW001", Severity.Error, n, e, p, 0)
                parserErrors @ valErrors @ locErrors
                    |> List.map parserErrorToDiagnostics
                    |> List.groupBy fst
                    |> List.map ((fun (f, rs) -> f, rs |> List.filter (fun (_, d) -> match d.code with |Some s -> not (List.contains s ignoreCodes) |None -> true)) >>
                        (fun (f, rs) -> PublishDiagnostics {uri = (match Uri.TryCreate(f, UriKind.Absolute) with |TrySuccess value -> value |TryFailure -> eprintfn "%s" f; Uri "/") ; diagnostics = List.map snd rs}))
                    |> List.iter (fun f -> LanguageServer.sendNotification send f)
            with
                | :? System.Exception as e -> eprintfn "%A" e
            
        |None -> ()
        LanguageServer.sendNotification send (LoadingBar {value = false})

    let hoverDocument (doc :Uri, pos: LSP.Types.Position) =
        async { 
            eprintfn "Hover before word"
            let! word = LanguageServer.sendRequest send (GetWordRangeAtPosition {position = pos})
            eprintfn "Hover after word"
            return match gameObj with
            |Some game ->
                let allEffects = game.ScriptedEffects @ game.ScripteTriggers
                eprintfn "Looking for effect %s in the %i effects loaded" word (allEffects.Length)
                let hovered = allEffects |> List.tryFind (fun e -> "\"" + e.Name + "\"" = word)
                match hovered with
                |Some effect ->
                    match effect with
                    | :? DocEffect as de ->
                        let scopes = String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                        let content = String.Join("\n***\n",["_"+de.Desc+"_"; "Supports scopes: " + scopes;]) // TODO: usageeffect.Usage])
                        {contents = (MarkupContent ("markdown", content)) ; range = None}
                    | e ->
                        let scopes = String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                        let content = String.Join("\n***\n",["_"+e.Name+"_"; "Supports scopes: " + scopes;]) // TODO: usageeffect.Usage])
                        {contents = (MarkupContent ("markdown", content)) ; range = None}
                |None ->  {contents = MarkupContent ("markdown", ""); range = None}
            |_ -> {contents = MarkupContent ("markdown", ""); range = None}
        }


    interface ILanguageServer with 
        member this.Initialize(p: InitializeParams): InitializeResult = 
            rootUri <- p.rootUri
            { capabilities = 
                { defaultServerCapabilities with 
                    hoverProvider = true
                    textDocumentSync = 
                        { defaultTextDocumentSyncOptions with 
                            openClose = true 
                            save = Some { includeText = true }
                            change = TextDocumentSyncKind.Full }
                    completionProvider = Some {resolveProvider = false; triggerCharacters = []} } }
        member this.Initialized(): unit = 
            ()
        member this.Shutdown(): unit = 
            ()
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): unit =
            let newLanguages = 
                match p.settings.Item("cwtools").Item("localisation").Item("languages") with 
                | JsonValue.Array o -> 
                    o |> Array.choose (function |JsonValue.String s -> (match STLLang.TryParse<STLLang> s with |TrySuccess s -> Some s |TryFailure -> None) |_ -> None)
                      |> List.ofArray
                      |> (fun l ->  if List.isEmpty l then [STLLang.English] else l)
                | _ -> [STLLang.English]                  
            languages <- newLanguages |> List.map STL
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
            eprintfn "New configuration %s" (p.ToString())
            let task = new Task((fun () -> processWorkspace(rootUri)))
            task.Start()

        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): unit = 
            docs.Open p
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): unit = 
            docs.Change p
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): unit = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): list<TextEdit> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): unit = ()
           // eprintfn "%s" (p.ToString())
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): unit = 
            docs.Close p
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): unit = 
            for change in p.changes do 
                eprintfn "Watched file %s %s" (change.uri.ToString()) (change._type.ToString())
                if change.uri.AbsolutePath.EndsWith ".fsproj" then
                    projects.UpdateProjectFile change.uri 
                lint change.uri |> Async.RunSynchronously
        member this.Completion(p: TextDocumentPositionParams): CompletionList = 
            let defaultCompletionItem = { label = ""; additionalTextEdits = []; kind = None; detail = None; documentation = None; sortText = None; filterText = None; insertText = None; insertTextFormat = None; textEdit = None; commitCharacters = None; command = None; data = None}
            match gameObj with
            |Some game ->
                let eventIDs = game.References.EventIDs
                let names = eventIDs @ game.References.TriggerNames @ game.References.EffectNames @ game.References.ModifierNames
                let items = names |> List.map (fun e -> {defaultCompletionItem with label = e})
                {isIncomplete = false; items = items}
            |None -> {isIncomplete = false; items = []}
        member this.Hover(p: TextDocumentPositionParams): Hover = 
            eprintfn "Hover"
            hoverDocument (p.textDocument.uri, p.position) |> Async.RunSynchronously

        member this.ResolveCompletionItem(p: CompletionItem): CompletionItem = TODO()
        member this.SignatureHelp(p: TextDocumentPositionParams): SignatureHelp = TODO()
        member this.GotoDefinition(p: TextDocumentPositionParams): list<Location> = TODO()
        member this.FindReferences(p: ReferenceParams): list<Location> = TODO()
        member this.DocumentHighlight(p: TextDocumentPositionParams): list<DocumentHighlight> = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams): list<SymbolInformation> = TODO()
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): list<SymbolInformation> = TODO()
        member this.CodeActions(p: CodeActionParams): list<Command> = TODO()
        member this.CodeLens(p: CodeLensParams): List<CodeLens> = TODO()
        member this.ResolveCodeLens(p: CodeLens): CodeLens = TODO()
        member this.DocumentLink(p: DocumentLinkParams): list<DocumentLink> = TODO()
        member this.ResolveDocumentLink(p: DocumentLink): DocumentLink = TODO()
        member this.DocumentFormatting(p: DocumentFormattingParams): list<TextEdit> = TODO()
        member this.DocumentRangeFormatting(p: DocumentRangeFormattingParams): list<TextEdit> = TODO()
        member this.DocumentOnTypeFormatting(p: DocumentOnTypeFormattingParams): list<TextEdit> = TODO()
        member this.Rename(p: RenameParams): WorkspaceEdit = TODO()
        member this.ExecuteCommand(p: ExecuteCommandParams): unit = TODO()

[<EntryPoint>]
let main (argv: array<string>): int =
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    let read = new BinaryReader(Console.OpenStandardInput())
    let write = new BinaryWriter(Console.OpenStandardOutput())
    let server = Server(write)
    eprintfn "Listening on stdin"
    LanguageServer.connect server read write
    0 // return an integer exit code
