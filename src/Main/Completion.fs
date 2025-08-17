module Main.Completion

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open CWTools.Common
open CWTools.Games
open CWTools.Utilities.Position
open FSharp.Data
open LSP
open LSP.Types
open CWTools.Utilities.Utils


let completionCache = Dictionary<int, CompletionItem>()
let mutable private completionCacheKey = 0

let addToCache completionItem =
    let key = completionCacheKey
    completionCacheKey <- completionCacheKey + 1
    completionCache.Add(key, completionItem)
    key

let mutable completionCacheCount = 0

let mutable private completionPartialCache: (CompletionParams * CompletionItem list) option =
    None

let completionResolveItem (gameObj: IGame option) (item: CompletionItem) =
    async {
        logInfo "Completion resolve"

        let item =
            match item.data with
            | JsonValue.Number key -> completionCache.GetValueOrDefault(key |> int, item)
            | _ -> item

        return
            match gameObj with
            | Some game ->
                let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()

                let hovered = allEffects |> List.tryFind (fun e -> e.Name.GetString() = item.label)

                match hovered with
                | Some effect ->
                    match effect with
                    | :? DocEffect as de ->
                        let desc = "_" + de.Desc.Replace("_", "\\_") + "_"

                        let scopes =
                            "Supports scopes: "
                            + String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))

                        let usage = de.Usage

                        let content = String.Join("\n***\n", [ desc; scopes; usage ]) // TODO: usageeffect.Usage])
                        //{item with documentation = (MarkupContent ("markdown", content))}
                        { item with
                            documentation =
                                Some(
                                    { kind = MarkupKind.Markdown
                                      value = content }
                                ) }
                    | :? ScriptedEffect as se ->
                        let desc = se.Name.GetString().Replace("_", "\\_")
                        let comments = se.Comments.Replace("_", "\\_")

                        let scopes =
                            "Supports scopes: "
                            + String.Join(", ", se.Scopes |> List.map (fun f -> f.ToString()))

                        let content = String.Join("\n***\n", [ desc; comments; scopes ]) // TODO: usageeffect.Usage])

                        { item with
                            documentation =
                                Some(
                                    { kind = MarkupKind.Markdown
                                      value = content }
                                ) }
                    | e ->
                        let desc = "_" + e.Name.GetString().Replace("_", "\\_") + "_"

                        let scopes =
                            "Supports scopes: "
                            + String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))

                        let content = String.Join("\n***\n", [ desc; scopes ]) // TODO: usageeffect.Usage])

                        { item with
                            documentation =
                                Some(
                                    { kind = MarkupKind.Markdown
                                      value = content }
                                ) }
                | None -> item
            | None -> item
    }



/// Compute ranges for InsertReplaceEdit based on word boundaries using simple position data
let computeCompletionRanges (filetext: string) (line: int) (character: int) =
    let lines = filetext.Split('\n')

    let targetLine =
        if line > 0 && line <= lines.Length then
            lines.[line - 1]
        else
            ""

    // Find word boundaries using F# syntax rules (letters, digits, underscore)
    let isWordChar c =
        System.Char.IsLetterOrDigit(c) || c = '_'

    // Walk backward to find start of word/identifier
    let mutable wordStart = character

    while wordStart > 0
          && wordStart <= targetLine.Length
          && isWordChar targetLine.[wordStart - 1] do
        wordStart <- wordStart - 1

    // Walk forward to find end of word/identifier
    let mutable wordEnd = character

    while wordEnd < targetLine.Length && isWordChar targetLine.[wordEnd] do
        wordEnd <- wordEnd + 1

    // Return the ranges as a tuple to avoid anonymous record issues
    let insertRange =
        { start =
            { line = line - 1
              character = wordStart }
          ``end`` =
            { line = line - 1
              character = character } }

    let replaceRange =
        { start =
            { line = line - 1
              character = wordStart }
          ``end`` = { line = line - 1; character = wordEnd } }

    (insertRange, replaceRange)

let optimiseCompletion (completionList: CompletionItem list) =
    if completionCacheCount > 2 then
        completionCache.Clear()
        completionCacheCount <- 0
    else
        completionCacheCount <- completionCacheCount + 1

    match completionList.Length with
    | x when x > 1000 ->
        let sorted = completionList |> List.sortBy (fun c -> c.sortText)

        let first = sorted |> List.take 1000

        let rest =
            sorted
            |> List.skip 1000
            |> List.take (min 1000 (x - 1000))
            |> List.map (fun item ->
                let key = addToCache item

                { item with
                    documentation = None
                    detail = None
                    data = JsonValue.Number(decimal key) })

        first @ rest
    | _ -> completionList

let checkPartialCompletionCache (p: CompletionParams) genItems =
    match p.context, completionPartialCache, p.textDocument, p.position with
    | Some { triggerKind = CompletionTriggerKind.TriggerForIncompleteCompletions }, Some(c, res), td, pos when
        c.position.line = pos.line && c.textDocument.uri = td.uri
        ->
        res
    | _ ->
        let items = genItems ()
        completionPartialCache <- Some(p, items)
        items

let completionCallLSP (game: IGame) (p: CompletionParams) _ debugMode supportsInsertReplaceEdit filetext position =

    let path =
        let u = p.textDocument.uri

        if
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && u.LocalPath.StartsWith "/"
        then
            u.LocalPath.Substring(1)
        else
            u.LocalPath

    let comp = game.Complete position path filetext


    // logInfo $"completion {prefixSoFar}"
    // let extraKeywords = ["yes"; "no";]
    // let eventIDs = game.References.EventIDs
    // let names = eventIDs @ game.References.TriggerNames @ game.References.EffectNames @ game.References.ModifierNames @ game.References.ScopeNames @ extraKeywords
    let convertKind (x: CompletionCategory) =
        match x with
        | CompletionCategory.Link -> (true, CompletionItemKind.Method)
        | CompletionCategory.Value -> (false, CompletionItemKind.Value)
        | CompletionCategory.Global -> (false, CompletionItemKind.Constant)
        | CompletionCategory.Variable -> (false, CompletionItemKind.Variable)
        | _ -> (false, CompletionItemKind.Function)

    /// Wrap in quotes if it contains spaces
    let createInsertText (s: string) =
        if s.Contains " " && not (s.StartsWith("\"")) && not (s.EndsWith("\"")) then
            $"\"{s}\""
        else
            s

    /// Create the appropriate textEdit based on client capabilities
    let createTextEdit text =
        if supportsInsertReplaceEdit then
            let (insertRange, replaceRange) =
                computeCompletionRanges filetext position.Line position.Column

            Some(
                { newText = text
                  insert = insertRange
                  replace = replaceRange }
            )
        else
            // Fallback to regular TextEdit for older clients
            None // Let the client use insertText instead

    let items =
        comp
        |> List.map (function
            | CompletionResponse.Simple(e, Some score, kind) ->
                let insertText = createInsertText e

                { defaultCompletionItemKind (convertKind kind) with
                    label = e
                    labelDetails =
                        if debugMode then
                            Some
                                { detail = Some $"({score})"
                                  description = None }
                        else
                            None
                    insertText = if supportsInsertReplaceEdit then None else Some insertText
                    textEdit = createTextEdit insertText
                    sortText = Some((maxCompletionScore - score).ToString()) }
            | CompletionResponse.Simple(e, None, kind) ->
                let insertText = createInsertText e

                { defaultCompletionItemKind (convertKind kind) with
                    label = e
                    insertText = if supportsInsertReplaceEdit then None else Some insertText
                    textEdit = createTextEdit insertText
                    sortText = Some(maxCompletionScore.ToString()) }
            | CompletionResponse.Detailed(l, d, Some score, kind) ->
                let insertText = createInsertText l

                { defaultCompletionItemKind (convertKind kind) with
                    label = l
                    labelDetails =
                        if debugMode then
                            Some
                                { detail = Some $"({score})"
                                  description = None }
                        else
                            None
                    insertText = if supportsInsertReplaceEdit then None else Some insertText
                    textEdit = createTextEdit insertText
                    documentation =
                        d
                        |> Option.map (fun d ->
                            { kind = MarkupKind.Markdown
                              value = d })
                    sortText = Some((maxCompletionScore - score).ToString()) }
            | CompletionResponse.Detailed(l, d, None, kind) ->
                let insertText = createInsertText l

                { defaultCompletionItemKind (convertKind kind) with
                    label = l
                    insertText = if supportsInsertReplaceEdit then None else Some insertText
                    textEdit = createTextEdit insertText
                    documentation =
                        d
                        |> Option.map (fun d ->
                            { kind = MarkupKind.Markdown
                              value = d }) }
            | CompletionResponse.Snippet(l, e, d, Some score, kind) ->
                { defaultCompletionItemKind (convertKind kind) with
                    label = l
                    labelDetails =
                        if debugMode then
                            Some
                                { detail = Some $"({score})"
                                  description = None }
                        else
                            None
                    insertText = if supportsInsertReplaceEdit then None else Some e
                    insertTextFormat = Some InsertTextFormat.Snippet
                    textEdit = createTextEdit e
                    documentation =
                        d
                        |> Option.map (fun d ->
                            { kind = MarkupKind.Markdown
                              value = d })
                    sortText = Some((maxCompletionScore - score).ToString()) }
            | CompletionResponse.Snippet(l, e, d, None, kind) ->
                { defaultCompletionItemKind (convertKind kind) with
                    label = l
                    insertText = if supportsInsertReplaceEdit then None else Some e
                    insertTextFormat = Some InsertTextFormat.Snippet
                    textEdit = createTextEdit e
                    documentation =
                        d
                        |> Option.map (fun d ->
                            { kind = MarkupKind.Markdown
                              value = d }) })

    items

let completion
    (gameObj: IGame option)
    (p: CompletionParams)
    (docs: DocumentStore)
    (debugMode: bool)
    (supportsInsertReplaceEdit: bool)
    =
    match gameObj with
    | Some game ->
        // match experimental_completion with
        // |true ->

        // let variables = game.References.ScriptVariableNames |> List.map (fun v -> {defaultCompletionItem with label = v; kind = Some CompletionItemKind.Variable })
        // logInfo (sprintf "completion prefix %A %A" prefixSoFar (items |> List.map (fun x -> x.label)))

        let stopwatch = System.Diagnostics.Stopwatch.StartNew()
        stopwatch.Start()
        let position = Pos.fromZ p.position.line p.position.character // |> (fun p -> Pos.fromZ)

        let filetext =
            (docs.GetText(FileInfo(p.textDocument.uri.LocalPath)) |> Option.defaultValue "")

        let items =
            checkPartialCompletionCache p (fun () ->
                completionCallLSP game p docs debugMode supportsInsertReplaceEdit filetext position)

        logInfo $"completion items time %i{stopwatch.ElapsedMilliseconds}ms"
        let split = filetext.Split('\n')
        let targetLine = split[position.Line - 1]
        let textBeforeCursor = targetLine.Remove(position.Column)
        logInfo $"{p} {position}"

        let prefixSoFar =
            match textBeforeCursor.Split([||]) |> Array.tryLast with
            | Some lastWord when not (String.IsNullOrWhiteSpace lastWord) -> lastWord.Split('.') |> Array.last |> Some
            | _ -> None

        let partialReturn = items |> List.length > 2000

        let filtered =
            match prefixSoFar, partialReturn with
            | None, _ -> items
            | _, false -> items
            | Some prefix, true ->
                items
                |> List.filter (fun i -> i.label.Contains(prefix, StringComparison.OrdinalIgnoreCase))

        let deduped =
            filtered
            |> List.distinctBy (fun i -> (i.label, i.documentation))
            |> List.filter (fun i -> not (i.label.StartsWith("$", StringComparison.OrdinalIgnoreCase)))

        let optimised = optimiseCompletion deduped

        // Log completion info only if we have items
        if not deduped.IsEmpty then
            logInfo $"completion mid %A{prefixSoFar} %A{deduped.Head.sortText} %A{deduped.Head.label}"
        else
            logInfo $"completion mid %A{prefixSoFar} - no items after filtering"

        //        let docLength =
        //            optimised
        //            |> List.sumBy
        //                (fun i ->
        //                    if i.documentation.IsSome then
        //                        i.documentation.Value.value.Length
        //                    else
        //                        0)
        //
        //        let labelLength =
        //            optimised |> List.sumBy (fun i -> i.label.Length)
        //
        //        logInfo $"Completion items: %i{deduped |> List.length} %i{optimised |> List.length} %i{stopwatch.ElapsedMilliseconds}ms"

        //        let items =
        //            [ { defaultCompletionItem with
        //                    label = "test"
        //                    insertText = Some "test ${1|yes,no,{ test = true }|}"
        //                    insertTextFormat = Some InsertTextFormat.Snippet } ]

        Some
            { isIncomplete = partialReturn
              items = optimised }
    // |false ->
    //     let extraKeywords = ["yes"; "no";]
    //     let eventIDs = game.References.EventIDs
    //     let names = eventIDs @ game.References.TriggerNames @ game.References.EffectNames @ game.References.ModifierNames @ game.References.ScopeNames @ extraKeywords
    //     let variables = game.References.ScriptVariableNames |> List.map (fun v -> {defaultCompletionItem with label = v; kind = Some CompletionItemKind.Variable })
    //     let items = names |> List.map (fun n -> {defaultCompletionItem with label = n})
    //     Some {isIncomplete = false; items = items @ variables}
    | None -> None
