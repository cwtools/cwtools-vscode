module LSP.LanguageServer 

open FSharp.Data
open System
open System.IO
open System.Text
open Types 
open Json
open System.Collections.Concurrent
open System.Threading.Tasks
open System.Globalization
open FSharp.Collections.ParallelSeq
open System.Threading

let jsonWriteOptions = 
    { defaultJsonWriteOptions with 
        customWriters = 
            [ writeTextDocumentSyncKind;
              writeDiagnosticSeverity;
              writeInsertTextFormat;
              writeCompletionItemKind;
              writeMarkedString;
              writeHoverContent;
              writeDocumentHighlightKind;
              writeSymbolKind ] }

let private serializeInitializeResult = serializerFactory<InitializeResult> jsonWriteOptions
let private serializeTextEditList = serializerFactory<list<TextEdit>> jsonWriteOptions
let private serializeCompletionList = serializerFactory<CompletionList> jsonWriteOptions
let private serializeHover = serializerFactory<Hover> jsonWriteOptions
let private serializeCompletionItem = serializerFactory<CompletionItem> jsonWriteOptions
let private serializeSignatureHelp = serializerFactory<SignatureHelp> jsonWriteOptions
let private serializeLocationList = serializerFactory<list<Location>> jsonWriteOptions
let private serializeDocumentHighlightList = serializerFactory<list<DocumentHighlight>> jsonWriteOptions
let private serializeSymbolInformationList = serializerFactory<list<SymbolInformation>> jsonWriteOptions
let private serializeCommandList = serializerFactory<list<Command>> jsonWriteOptions
let private serializeCodeLensList = serializerFactory<list<CodeLens>> jsonWriteOptions
let private serializeCodeLens = serializerFactory<CodeLens> jsonWriteOptions
let private serializeDocumentLinkList = serializerFactory<list<DocumentLink>> jsonWriteOptions
let private serializeDocumentLink = serializerFactory<DocumentLink> jsonWriteOptions
let private serializeWorkspaceEdit = serializerFactory<WorkspaceEdit> jsonWriteOptions
let private serializePublishDiagnostics = serializerFactory<PublishDiagnosticsParams> jsonWriteOptions
let private serializeLoadingBarParams = serializerFactory<LoadingBarParams> jsonWriteOptions
let private serializeGetWordRangeAtPosition = serializerFactory<GetWordRangeAtPositionParams> jsonWriteOptions

type msg =
    | Request of int * AsyncReplyChannel<string>
    | Response of int * string
let responseAgent = MailboxProcessor.Start(fun agent ->
    let rec loop state =
        async{
            let! msg = agent.Receive()
            match msg with
            | Request (id, reply) ->
                eprintfn "request state: %A" state
                return! loop ((id, reply)::state)
            | Response (id, value) ->
                eprintf "response state: %A" state
                let result = state |> List.tryFind (fun (i, _) -> i = id)
                match result with
                |Some(_, reply) ->
                    reply.Reply(value)
                |None -> eprintfn "Unexpected response %i" id
                return! loop (state |> List.filter (fun (i, _) -> i <> id))
        }
    loop [])

type sendMsg =
    | ServerResponse of BinaryWriter * int * string
    | ServerNotification of BinaryWriter * string * string
let messageQueue = MailboxProcessor.Start(fun agent ->
    let rec loop = 
        async {
            let! msg = agent.Receive()
            match msg with
            | ServerResponse(client, requestId, jsonText) ->
                let messageText = sprintf """{"id":%d,"result":%s}""" requestId jsonText
                let messageBytes = Encoding.UTF8.GetBytes messageText
                let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
                let headerBytes = Encoding.UTF8.GetBytes headerText
                client.Write headerBytes
                client.Write messageBytes
            | ServerNotification(client, notificationMethod, jsonText) ->
                let messageText = sprintf """{"method":"%s","params":%s}""" notificationMethod jsonText
                let messageBytes = Encoding.UTF8.GetBytes messageText
                let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
                let headerBytes = Encoding.UTF8.GetBytes headerText
                client.Write headerBytes
                client.Write messageBytes
            do! loop
        }
    loop)

let respond (client: BinaryWriter) (requestId: int) (jsonText: string) = 
    messageQueue.Post(ServerResponse(client, requestId, jsonText))
    // let messageText = sprintf """{"id":%d,"result":%s}""" requestId jsonText
    // let messageBytes = Encoding.UTF8.GetBytes messageText
    // let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
    // let headerBytes = Encoding.UTF8.GetBytes headerText
    // client.Write headerBytes
    // client.Write messageBytes

let notify (client: BinaryWriter) (notificationMethod: string) (jsonText: string) =
    messageQueue.Post(ServerNotification(client, notificationMethod, jsonText))
    // let messageText = sprintf """{"method":"%s","params":%s}""" notificationMethod jsonText
    // let messageBytes = Encoding.UTF8.GetBytes messageText
    // let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
    // let headerBytes = Encoding.UTF8.GetBytes headerText
    // client.Write headerBytes
    // client.Write messageBytes

let request (client: BinaryWriter) (requestId: int) (requestMethod: string) (jsonText: string) =
    async{
        let reply = responseAgent.PostAndAsyncReply((fun replyChannel -> Request(requestId, replyChannel)))
        let messageText = sprintf """{"id":%d,"method":"%s", "params":%s}""" requestId requestMethod jsonText
        let messageBytes = Encoding.UTF8.GetBytes messageText
        let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
        let headerBytes = Encoding.UTF8.GetBytes headerText
        client.Write headerBytes
        client.Write messageBytes
        return! reply
    }


let processRequest (server: ILanguageServer) (send: BinaryWriter) (id: int) (request: Request) = 
    match request with 
    | Initialize p -> 
        server.Initialize p |> serializeInitializeResult |> respond send id
    | WillSaveWaitUntilTextDocument p -> 
        server.WillSaveWaitUntilTextDocument p |> serializeTextEditList |> respond send id
    | Completion p -> 
        server.Completion p |> serializeCompletionList |> respond send id
    | Hover p -> 
        server.Hover p |> serializeHover |> respond send id
    | ResolveCompletionItem p -> 
        server.ResolveCompletionItem p |> serializeCompletionItem |> respond send id 
    | SignatureHelp p -> 
        server.SignatureHelp p |> serializeSignatureHelp |> respond send id
    | GotoDefinition p -> 
        server.GotoDefinition p |> serializeLocationList |> respond send id
    | FindReferences p -> 
        server.FindReferences p |> serializeLocationList |> respond send id
    | DocumentHighlight p -> 
        server.DocumentHighlight p |> serializeDocumentHighlightList |> respond send id
    | DocumentSymbols p -> 
        server.DocumentSymbols p |> serializeSymbolInformationList |> respond send id
    | WorkspaceSymbols p -> 
        server.WorkspaceSymbols p |> serializeSymbolInformationList |> respond send id
    | CodeActions p -> 
        server.CodeActions p |> serializeCommandList |> respond send id
    | CodeLens p -> 
        server.CodeLens p |> serializeCodeLensList |> respond send id
    | ResolveCodeLens p -> 
        server.ResolveCodeLens p |> serializeCodeLens |> respond send id
    | DocumentLink p -> 
        server.DocumentLink p |> serializeDocumentLinkList |> respond send id
    | ResolveDocumentLink p -> 
        server.ResolveDocumentLink p |> serializeDocumentLink |> respond send id
    | DocumentFormatting p -> 
        server.DocumentFormatting p |> serializeTextEditList |> respond send id
    | DocumentRangeFormatting p -> 
        server.DocumentRangeFormatting p |> serializeTextEditList |> respond send id
    | DocumentOnTypeFormatting p -> 
        server.DocumentOnTypeFormatting p |> serializeTextEditList |> respond send id
    | Rename p -> 
        server.Rename p |> serializeWorkspaceEdit |> respond send id
    | ExecuteCommand p -> 
        server.ExecuteCommand p 

let processNotification (server: ILanguageServer) (send: BinaryWriter) (n: Notification) = 
    match n with 
    | Cancel id ->
        eprintfn "Cancel request %d is not yet supported" id
    | Initialized ->
        server.Initialized()
    | Shutdown ->
        server.Shutdown()
    | DidChangeConfiguration p -> 
        server.DidChangeConfiguration p
    | DidOpenTextDocument p -> 
        server.DidOpenTextDocument p
    | DidChangeTextDocument p -> 
        server.DidChangeTextDocument p
    | WillSaveTextDocument p -> 
        server.WillSaveTextDocument p 
    | DidSaveTextDocument p -> 
        server.DidSaveTextDocument p
    | DidCloseTextDocument p -> 
        server.DidCloseTextDocument p
    | DidChangeWatchedFiles p -> 
        server.DidChangeWatchedFiles p
    | OtherNotification _ ->
        ()

let sendNotification (send: BinaryWriter) (n: ServerNotification) =
    try
        match n with
        | PublishDiagnostics p ->
            p |> serializePublishDiagnostics |> notify send "textDocument/publishDiagnostics"
        | LoadingBar p->
            p |> serializeLoadingBarParams |> notify send "loadingBar"
    with
    |e -> eprintfn "message %s failed with: %A" (n.ToString()) e

let mutable callbacks = new ConcurrentDictionary<int, Delegate>()

let sendRequest (send: BinaryWriter) (n: ServerRequest) =
    async {
        try
            let id = System.Random().Next()
            match n with
            | GetWordRangeAtPosition p ->
                eprintfn "send request %i" id
                return! p |> serializeGetWordRangeAtPosition |> request send id "getWordRangeAtPosition"
        with
        |e -> eprintfn "message %s failed with: %A" (n.ToString()) e; return ""
    }

let (|TrySuccess|TryFailure|) tryResult =  
    match tryResult with
    | true, value -> TrySuccess value
    | _ -> TryFailure
let processMessage (server: ILanguageServer) (send: BinaryWriter) (m: Parser.Message) = 
    try
        eprintfn "process message"
        match m with 
        | Parser.RequestMessage (id, method, json) -> 
            processRequest server send id (Parser.parseRequest method json) 
        | Parser.NotificationMessage (method, json) -> 
            processNotification server send (Parser.parseNotification method json)
        | Parser.ResponseMessage (id, result) ->
            responseAgent.Post(Response(id, result.ToString()))
    with
    |e -> eprintfn "message %s failed with: %A" (m.ToString()) e
   
    

let private notExit (message: Parser.Message) = 
    match message with 
    | Parser.NotificationMessage ("exit", _) -> false 
    | _ -> true

let readMessages (receive: BinaryReader): seq<Parser.Message> = 
    Tokenizer.tokenize receive |> Seq.map Parser.parseMessage |> Seq.takeWhile notExit

let connect (server: ILanguageServer) (receive: BinaryReader) (send: BinaryWriter) = 
    let task = new Task((fun () -> while true do Thread.Sleep(5000); eprintfn "%A" responseAgent.CurrentQueueLength ))
    task.Start()
    eprintfn "%s" "Connecting"
    let doProcessMessage = 
        (fun m -> 
            let task = new Task((fun () -> processMessage server send m))
            task.Start())
    readMessages receive |> Seq.iter doProcessMessage