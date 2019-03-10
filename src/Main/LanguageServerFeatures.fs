namespace Main.Lang
open LSP.Json.Ser
open LSP
open LSP.Types
open System
open System.Runtime.InteropServices
open CWTools.Utilities.Position
open CWTools.Games
open System.IO
open CWTools.Localisation
open LSP.Types

module LanguageServerFeatures =

    let getWordAtPos (client : ILanguageClient) pos doc =
        let pjson = pos |> (serializerFactory<LSP.Types.Position> defaultJsonWriteOptions)
        let ujson = doc |> (serializerFactory<Uri> defaultJsonWriteOptions)
        let json = serializerFactory<GetWordRangeAtPositionParams> defaultJsonWriteOptions ({ position = pos; uri = doc })
        client.CustomRequest("getWordRangeAtPosition", json)

    let getPathFromDoc (doc : Uri) =
        let u = doc
        if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && u.LocalPath.StartsWith "/"
        then u.LocalPath.Substring(1)
        else u.LocalPath

    let lochoverFromInfo (localisation : (string * Entry) list) (infoOption : CWTools.Games.SymbolInformation option) (word : string) =
        let locToText (loc : SymbolLocalisationInfo) =
            let locdesc = localisation |> List.tryPick (fun (k, v) -> if k = loc.value then Some v.desc else None)
                                       |> Option.defaultValue ""
            "|" + loc.key.Trim('\"') + "|" + locdesc.Trim('\"') + "|"
        match infoOption with
        |Some info ->
            match info.localisation with
            |[] -> localisation |> List.tryPick (fun (k, v) -> if k = word then Some v.desc else None)
            |[h] -> localisation |> List.tryPick (fun (k, v) -> if k = h.value then Some v.desc else None)
            |h::t ->
                let head = locToText h
                let tail = t |> List.map locToText
                Some ((head :: "|:---|:---|" :: tail) |> (fun s -> String.Join("\n", s)))
        |None -> localisation |> List.tryPick (fun (k, v) -> if k = word then Some v.desc else None)

    let docstringFromInfo (infoOption : CWTools.Games.SymbolInformation option) =
        match infoOption with
        | Some info ->
            let ruleDesc = info.ruleDescription
            let scopes =
                match info.ruleRequiredScopes with
                | [] -> None
                | x -> Some( "Supports scopes: " +  String.Join(", ", info.ruleRequiredScopes))
            Some (String.Join("\n***\n",[|ruleDesc; scopes|] |> Array.choose id))
        | None -> None


    let hoverDocument (eu4GameObj) (hoi4GameObj) (stlGameObj) (ck2GameObj) (client : ILanguageClient) (docs : DocumentStore) (doc : Uri)  (pos: LSP.Types.Position) =
        async {
            let! word = getWordAtPos client pos doc
            let unescapedword = word.ToString().Replace("\\\"", "\"").Trim('"')
            let position = Pos.fromZ pos.line pos.character
            let path = getPathFromDoc doc
            let hoverFunction (game : IGame<_, 'a, _>) =
                let symbolInfo = game.InfoAtPos position path (docs.GetText (FileInfo (doc.LocalPath)) |> Option.defaultValue "")
                let scopeContext = game.ScopesAtPos position (path) (docs.GetText (FileInfo (doc.LocalPath)) |> Option.defaultValue "")
                let allEffects = game.ScriptedEffects() @ game.ScriptedTriggers()
                eprintfn "Looking for effect %s in the %i effects loaded" (word.ToString()) (allEffects.Length)
                let hovered = allEffects |> List.tryFind (fun e -> e.Name = unescapedword)
                let lochover = lochoverFromInfo (game.References().Localisation) symbolInfo unescapedword
                let scopesExtra =
                    if scopeContext.IsNone then "" else
                    let scopes = scopeContext.Value
                    let header = "| Context | Scope |\n| ----- | -----|\n"
                    let root = sprintf "| ROOT | %s |\n" (scopes.Root.ToString())
                    let prevs = scopes.Scopes |> List.mapi (fun i s -> "| " + (if i = 0 then "THIS" else (String.replicate (i) "PREV")) + " | " + (s.ToString()) + " |\n") |> String.concat ""
                    let froms = scopes.From |> List.mapi (fun i s -> "| " + (String.replicate (i+1) "FROM") + " | " + (s.ToString()) + " |\n") |> String.concat ""
                    header + root + prevs + froms

                let effect =
                    hovered |> Option.map (fun e ->
                        match e with
                        | :? CWTools.Common.DocEffect<'a> as de ->
                            let scopes = String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                            let desc = de.Desc.Replace("_", "\\_").Trim() |> (fun s -> if s = "" then "" else "_"+s+"_" )
                            (String.Join("\n***\n",[desc; "Supports scopes: " + scopes]) )// TODO: usageeffect.Usage])
                        | e ->
                            let scopes = String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                            let name = e.Name.Replace("_","\\_").Trim()
                            (String.Join("\n***\n",["_"+name+"_"; "Supports scopes: " + scopes])) // TODO: usageeffect.Usage])
                    )
                let docStringOrEffect = Option.orElse (docstringFromInfo symbolInfo) effect
                let text =
                    [|docStringOrEffect; lochover; Some scopesExtra|]
                    |> Array.choose id
                    |> (fun a -> String.Join("\n\n***\n\n", a))

                match text with
                | "" -> {contents = MarkupContent ("markdown", ""); range = None}
                | text -> {contents = MarkupContent ("markdown", text); range = None}
                // match hovered, lochover, docstringFromInfo symbolInfo with
                // |Some effect, _, _ ->
                //     match effect with
                //     | :? CWTools.Common.DocEffect<'a> as de ->
                //         let scopes = String.Join(", ", de.Scopes |> List.map (fun f -> f.ToString()))
                //         let desc = de.Desc.Replace("_", "\\_").Trim() |> (fun s -> if s = "" then "" else "_"+s+"_" )
                //         let content = String.Join("\n***\n",[desc; "Supports scopes: " + scopes; scopesExtra]) // TODO: usageeffect.Usage])
                //         {contents = (MarkupContent ("markdown", content)) ; range = None}
                //     | e ->
                //         let scopes = String.Join(", ", e.Scopes |> List.map (fun f -> f.ToString()))
                //         let name = e.Name.Replace("_","\\_").Trim()
                //         let content = String.Join("\n***\n",["_"+name+"_"; "Supports scopes: " + scopes; scopesExtra]) // TODO: usageeffect.Usage])
                //         {contents = (MarkupContent ("markdown", content)) ; range = None}
                // |None, Some loc, _->
                //     {contents = MarkupContent ("markdown", loc + "\n\n***\n\n" + scopesExtra); range = None}
                // |None, None, Some ruleDesc ->
                //     {contents = MarkupContent ("markdown", ruleDesc + "\n\n***\n\n" + scopesExtra); range = None}
                // |None, None, None ->
                //     {contents = MarkupContent ("markdown", scopesExtra); range = None}
            return
                match stlGameObj, hoi4GameObj, eu4GameObj, ck2GameObj with
                |Some game, _, _, _ -> hoverFunction game
                |_, Some game, _, _ -> hoverFunction game
                // |_, Some game, _ ->
                //     let lochover = game.References().Localisation |> List.tryFind (fun (k, v) -> k = unescapedword)
                //     match lochover with
                //     |Some (_, loc) ->
                //         { contents = MarkupContent ("markdown", loc.desc); range = None }
                //     |None ->
                //         { contents = MarkupContent ("markdown", ""); range = None }
                |_, _, Some game, _ -> hoverFunction game
                |_, _, _, Some game -> hoverFunction game
                |_ -> {contents = MarkupContent ("markdown", ""); range = None}

        }
