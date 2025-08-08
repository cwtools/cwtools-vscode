module LSP.LanguageServer

open LSP.Log
open System
open System.Threading
open System.IO
open System.Text
open FSharp.Data
open Types
open LSP.Json.Ser
open JsonExtensions

let private jsonWriteOptions =
    { defaultJsonWriteOptions with
        customWriters =
            [ writeTextDocumentSaveReason
              writeFileChangeType
              writeTextDocumentSyncKind
              writeDiagnosticSeverity
              writeTrace
              writeInsertTextFormat
              writeCompletionItemKind
              writeMarkedString
              writeDocumentHighlightKind
              writeSymbolKind
              writeRegisterCapability
              writeMessageType
              writeMarkupKind
              writeHoverContent ] }

let private serializeInitializeResult =
    serializerFactory<InitializeResult> jsonWriteOptions

let private serializeTextEditList =
    serializerFactory<TextEdit list> jsonWriteOptions

let private serializeCompletionList =
    serializerFactory<CompletionList> jsonWriteOptions

let private serializeCompletionListOption = Option.map serializeCompletionList
let private serializeHover = serializerFactory<Hover> jsonWriteOptions
let private serializeHoverOption = Option.map serializeHover

let private serializeCompletionItem =
    serializerFactory<CompletionItem> jsonWriteOptions

let private serializeSignatureHelp =
    serializerFactory<SignatureHelp> jsonWriteOptions

let private serializeSignatureHelpOption = Option.map serializeSignatureHelp

let private serializeLocationList =
    serializerFactory<Location list> jsonWriteOptions

let private serializeDocumentHighlightList =
    serializerFactory<DocumentHighlight list> jsonWriteOptions

let private serializeSymbolInformationList =
    serializerFactory<SymbolInformation list> jsonWriteOptions

let private serializeDocumentSymbolList =
    serializerFactory<DocumentSymbol list> jsonWriteOptions

let private serializeCommandList = serializerFactory<Command list> jsonWriteOptions

let private serializeCodeLensList =
    serializerFactory<CodeLens list> jsonWriteOptions

let private serializeCodeLens = serializerFactory<CodeLens> jsonWriteOptions

let private serializeDocumentLinkList =
    serializerFactory<DocumentLink list> jsonWriteOptions

let private serializeDocumentLink = serializerFactory<DocumentLink> jsonWriteOptions

let private serializeWorkspaceEdit =
    serializerFactory<WorkspaceEdit> jsonWriteOptions

let private serializePublishDiagnostics =
    serializerFactory<PublishDiagnosticsParams> jsonWriteOptions

let private serializeShowMessage =
    serializerFactory<ShowMessageParams> jsonWriteOptions

let private serializeRegistrationParams =
    serializerFactory<RegistrationParams> jsonWriteOptions

let private serializeLoadingBarParams =
    serializerFactory<LoadingBarParams> jsonWriteOptions

let private serializeGetWordRangeAtPosition =
    serializerFactory<GetWordRangeAtPositionParams> jsonWriteOptions

let private serializeApplyWorkspaceEdit =
    serializerFactory<ApplyWorkspaceEditParams> jsonWriteOptions

let private serializeCreateVirtualFileParams =
    serializerFactory<CreateVirtualFileParams> jsonWriteOptions

let private serializeLogMessageParams =
    serializerFactory<LogMessageParams> jsonWriteOptions

let private serializeExecuteCommandResponse =
    serializerFactory<ExecuteCommandResponse> jsonWriteOptions

let private serializeExecuteCommandResponseOption =
    Option.map serializeExecuteCommandResponse

let private serializeShutdownResponse =
    serializerFactory<int option> jsonWriteOptions

type msg =
    | Request of int * AsyncReplyChannel<JsonValue>
    | Response of int * JsonValue

let responseAgent =
    MailboxProcessor.Start(fun agent ->
        let rec loop state =
            async {
                let! msg = agent.Receive()

                match msg with
                | Request(id, reply) -> return! loop ((id, reply) :: state)
                | Response(id, value) ->
                    let result = state |> List.tryFind (fun (i, _) -> i = id)

                    match result with
                    | Some(_, reply) -> reply.Reply(value)
                    | None -> eprintfn $"Unexpected response %i{id}"

                    return! loop (state |> List.filter (fun (i, _) -> i <> id))
            }

        loop [])

let monitor = Lock()

let private writeClient (client: BinaryWriter, messageText: string) =
    let messageBytes = Encoding.UTF8.GetBytes(messageText)
    let headerText = $"Content-Length: %d{messageBytes.Length}\r\n\r\n"
    let headerBytes = Encoding.UTF8.GetBytes(headerText)

    monitor.Enter()

    try
        client.Write(headerBytes)
        client.Write(messageBytes)
    finally
        monitor.Exit()

let respond (client: BinaryWriter, requestId: int, jsonText: string) =
    let messageText = $"""{{"id":%d{requestId},"result":%s{jsonText}}}"""
    writeClient (client, messageText)

let private notifyClient (client: BinaryWriter, method: string, jsonText: string) =
    let messageText = $"""{{"method":"%s{method}","params":%s{jsonText}}}"""
    writeClient (client, messageText)

let private requestClient (client: BinaryWriter, id: int, method: string, jsonText: string) =
    async {
        let reply =
            responseAgent.PostAndAsyncReply((fun replyChannel -> Request(id, replyChannel)))

        let messageText =
            $"""{{"id":%d{id},"method":"%s{method}", "params":%s{jsonText}}}"""

        writeClient (client, messageText)
        return! reply
    }

let private thenMap (f: 'A -> 'B) (result: Async<'A>) : Async<'B> =
    async {
        let! a = result
        return f a
    }

let private thenSome = thenMap Some
let private thenNone (result: Async<'A>) : Async<string option> = result |> thenMap (fun _ -> None)

let private notExit (message: Parser.Message) =
    match message with
    | Parser.NotificationMessage("exit", _) -> false
    | _ -> true

let readMessages (receive: BinaryReader) : seq<Parser.Message> =
    let tokens = Tokenizer.tokenize (receive)
    let parse = Seq.map Parser.parseMessage tokens
    Seq.takeWhile notExit parse

type RealClient(send: BinaryWriter) =
    interface ILanguageClient with
        member this.LogMessage(p: LogMessageParams) : unit =
            let json = serializeLogMessageParams (p)
            notifyClient (send, "window/logMessage", json)

        member this.PublishDiagnostics(p: PublishDiagnosticsParams) : unit =
            let json = serializePublishDiagnostics (p)
            notifyClient (send, "textDocument/publishDiagnostics", json)

        member this.ShowMessage(p: ShowMessageParams) : unit =
            let json = serializeShowMessage (p)
            notifyClient (send, "window/showMessage", json)

        member this.RegisterCapability(p: RegisterCapability) : unit =
            match p with
            | RegisterCapability.DidChangeWatchedFiles _ ->
                let register =
                    { id = Guid.NewGuid().ToString()
                      method = "workspace/didChangeWatchedFiles"
                      registerOptions = p }

                let message = { registrations = [ register ] }
                let json = serializeRegistrationParams (message)
                notifyClient (send, "client/registerCapability", json)

        member this.CustomNotification(method: string, json: JsonValue) : unit =
            let jsonString = json.ToString(JsonSaveOptions.DisableFormatting)
            notifyClient (send, method, jsonString)

        member this.ApplyWorkspaceEdit(p: ApplyWorkspaceEditParams) : Async<JsonValue> =
            async {
                let json = serializeApplyWorkspaceEdit (p)
                let id = System.Random().Next()
                return! requestClient (send, id, "workspace/applyEdit", json)
            }

        member this.CustomRequest(method: string, json: string) : Async<JsonValue> =
            async {
                // let jsonString = json.ToString(JsonSaveOptions.DisableFormatting)
                let id = System.Random().Next()
                return! requestClient (send, id, method, json)
            }


type private PendingTask =
    | ProcessNotification of method: string * task: Async<unit>
    | ProcessRequest of id: int * task: Async<string option> * cancel: CancellationTokenSource
    | Quit

let connect (serverFactory: ILanguageClient -> ILanguageServer, receive: BinaryReader, send: BinaryWriter) =
    let server = serverFactory (RealClient(send))

    let processRequest (request: Request) : Async<string option> =
        match request with
        | Initialize(p) -> server.Initialize(p) |> thenMap serializeInitializeResult |> thenSome
        | Shutdown -> server.Shutdown() |> thenMap serializeShutdownResponse |> thenSome
        | WillSaveWaitUntilTextDocument(p) ->
            server.WillSaveWaitUntilTextDocument(p)
            |> thenMap serializeTextEditList
            |> thenSome
        | Completion(p) -> server.Completion(p) |> thenMap serializeCompletionListOption
        | Hover(p) ->
            server.Hover(p)
            |> thenMap serializeHoverOption
            |> thenMap (Option.defaultValue "null")
            |> thenSome
        | ResolveCompletionItem(p) -> server.ResolveCompletionItem(p) |> thenMap serializeCompletionItem |> thenSome
        | SignatureHelp(p) ->
            server.SignatureHelp(p)
            |> thenMap serializeSignatureHelpOption
            |> thenMap (Option.defaultValue "null")
            |> thenSome
        | GotoDefinition(p) -> server.GotoDefinition(p) |> thenMap serializeLocationList |> thenSome
        | FindReferences(p) -> server.FindReferences(p) |> thenMap serializeLocationList |> thenSome
        | DocumentHighlight(p) ->
            server.DocumentHighlight(p)
            |> thenMap serializeDocumentHighlightList
            |> thenSome
        | DocumentSymbols(p) -> server.DocumentSymbols(p) |> thenMap serializeDocumentSymbolList |> thenSome
        | WorkspaceSymbols(p) -> server.WorkspaceSymbols(p) |> thenMap serializeSymbolInformationList |> thenSome
        | CodeActions(p) -> server.CodeActions(p) |> thenMap serializeCommandList |> thenSome
        | CodeLens(p) -> server.CodeLens(p) |> thenMap serializeCodeLensList |> thenSome
        | ResolveCodeLens(p) -> server.ResolveCodeLens(p) |> thenMap serializeCodeLens |> thenSome
        | DocumentLink(p) -> server.DocumentLink(p) |> thenMap serializeDocumentLinkList |> thenSome
        | ResolveDocumentLink(p) -> server.ResolveDocumentLink(p) |> thenMap serializeDocumentLink |> thenSome
        | DocumentFormatting(p) -> server.DocumentFormatting(p) |> thenMap serializeTextEditList |> thenSome
        | DocumentRangeFormatting(p) -> server.DocumentRangeFormatting(p) |> thenMap serializeTextEditList |> thenSome
        | DocumentOnTypeFormatting(p) -> server.DocumentOnTypeFormatting(p) |> thenMap serializeTextEditList |> thenSome
        | Rename(p) -> server.Rename(p) |> thenMap serializeWorkspaceEdit |> thenSome
        | ExecuteCommand(p) -> server.ExecuteCommand p |> thenMap serializeExecuteCommandResponseOption
        | DidChangeWorkspaceFolders(p) -> server.DidChangeWorkspaceFolders(p) |> thenNone

    let processNotification (n: Notification) =
        match n with
        | Initialized -> server.Initialized()
        | DidChangeConfiguration(p) -> server.DidChangeConfiguration(p)
        | DidOpenTextDocument(p) -> server.DidOpenTextDocument(p)
        | DidChangeTextDocument(p) -> server.DidChangeTextDocument(p)
        | WillSaveTextDocument(p) -> server.WillSaveTextDocument(p)
        | DidSaveTextDocument(p) -> server.DidSaveTextDocument(p)
        | DidCloseTextDocument(p) -> server.DidCloseTextDocument(p)
        | DidChangeWatchedFiles(p) -> server.DidChangeWatchedFiles(p)
        | DidFocusFile(p) -> server.DidFocusFile(p)
        | OtherNotification(_) -> async { () }
    // Read messages and process cancellations on a separate thread
    let pendingRequests =
        new System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource>()

    let processQueue =
        new System.Collections.Concurrent.BlockingCollection<PendingTask>(10)

    Thread(fun () ->
        try
            // Read all messages on the main thread
            for m in readMessages (receive) do
                // Process cancellations immediately
                match m with
                | Parser.NotificationMessage("$/cancelRequest", Some json) ->
                    let id = json?id.AsInteger()
                    let stillRunning, pendingRequest = pendingRequests.TryGetValue(id)

                    if stillRunning then
                        //dprintfn "Cancelling request %d" id
                        pendingRequest.Cancel()
                    else
                        ()
                //dprintfn "Request %d has already finished" id
                // Process other requests on worker thread
                | Parser.NotificationMessage(method, json) ->
                    let n = Parser.parseNotification (method, json)
                    let task = processNotification (n)
                    processQueue.Add(ProcessNotification(method, task))
                | Parser.RequestMessage(id, method, json) ->
                    let task = processRequest (Parser.parseRequest (method, json))
                    let cancel = new CancellationTokenSource()
                    processQueue.Add(ProcessRequest(id, task, cancel))
                    pendingRequests.[id] <- cancel
                | Parser.ResponseMessage(id, result) -> responseAgent.Post(Response(id, result))

            processQueue.Add(Quit)
        with e ->
            dprintfn $"Exception in read thread {e}"

    )
        .Start()
    // Process messages on main thread
    let mutable quit = false

    while not quit do
        match processQueue.Take() with
        | Quit -> quit <- true
        | ProcessNotification(method, task) -> Async.RunSynchronously(task)
        | ProcessRequest(id, task, cancel) ->
            if cancel.IsCancellationRequested then
                ()
            //dprintfn "Skipping cancelled request %d" id
            else
                try
                    match Async.RunSynchronously(task, 0, cancel.Token) with
                    | Some(result) -> respond (send, id, result)
                    | None -> respond (send, id, "null")
                with :? OperationCanceledException ->
                    ()
            //dprintfn "Request %d was cancelled" id
            pendingRequests.TryRemove(id) |> ignore

    System.Environment.Exit(1)
