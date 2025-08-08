module LSP.Json.Ser

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open System.Text.RegularExpressions
open FSharp.Data

let private escapeChars = Regex("[\t\n\r\"\\\\]", RegexOptions.Compiled)

let private replaceChars =
    MatchEvaluator(fun m ->
        match m.Value with
        | "\\" -> "\\\\"
        | "\t" -> "\\t"
        | "\n" -> "\\n"
        | "\r" -> "\\r"
        | "\"" -> "\\\""
        | v -> v)

let private escapeStr (text: string) =
    let escaped = escapeChars.Replace(text, replaceChars)
    $"\"%s{escaped}\""

let private isOption (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ option>

let private isSeq (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<seq<_>>

let private implementsSeq (t: Type) =
    let is = t.GetInterfaces()
    Seq.exists isSeq is

let private isList (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

let private isMap (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Map<_, _>>

type JsonWriteOptions = { customWriters: obj list }

let defaultJsonWriteOptions: JsonWriteOptions = { customWriters = [] }

let private matchWriter (t: Type, w: obj) : bool =
    let domain, _ = w.GetType() |> FSharpType.GetFunctionElements
    domain.IsAssignableFrom(t)

let private findWriter (t: Type, customWriters: obj list) : obj option =
    let matchT (w: obj) = matchWriter (t, w)
    Seq.tryFind matchT customWriters

let asFun (w: obj) : obj -> obj =
    let invoke = w.GetType().GetMethod("Invoke")
    fun x -> invoke.Invoke(w, [| x |])

type MakeHelpers =
    static member MakeList<'T>(items: obj seq) : 'T list =
        [ for i in items do
              yield i :?> 'T ]

    static member MakeMap<'T>(items: (string * obj) seq) : Map<string, 'T> =
        let castV (k: string, v: obj) = (k, v :?> 'T)
        let castItems = Seq.map castV items
        Map.ofSeq (castItems)

    static member MakeOption<'T>(item: obj option) : 'T option =
        match item with
        | None -> None
        | Some(i) -> Some(i :?> 'T)

let private makeList (t: Type, items: obj seq) =
    typeof<MakeHelpers>.GetMethod("MakeList").MakeGenericMethod([| t |]).Invoke(null, [| items |])

let private makeMap (t: Type, kvs: (string * obj) seq) =
    typeof<MakeHelpers>.GetMethod("MakeMap").MakeGenericMethod([| t |]).Invoke(null, [| kvs |])

let private makeOption (t: Type, item: obj option) =
    typeof<MakeHelpers>.GetMethod("MakeOption").MakeGenericMethod([| t |]).Invoke(null, [| item |])

let rec private serializer (depth: int, options: JsonWriteOptions, t: Type) : obj -> string =
    let custom = findWriter (t, options.customWriters)

    if depth >= 20 then
        fun _ -> ""
    elif custom.IsSome then
        let fObj = custom.Value
        let fType = fObj.GetType()
        let _, range = FSharpType.GetFunctionElements(fType)
        let serialize = serializer (depth, options, range)
        let transform = asFun (fObj)
        fun o -> serialize (transform (o))
    elif t = typeof<bool> then
        fun o -> $"%b{unbox<bool> o}"
    elif t = typeof<int> then
        fun o -> $"%d{unbox<int> o}"
    elif t = typeof<char> then
        fun o -> $"%c{unbox<char> o}" |> escapeStr
    elif t = typeof<string> then
        fun o -> escapeStr (o :?> string)
    elif t = typeof<Uri> then
        fun o ->
            let uri = o :?> Uri
            escapeStr (uri.ToString())
    elif t = typeof<JsonValue> then
        fun o ->
            let asJson = o :?> JsonValue
            asJson.ToString(JsonSaveOptions.DisableFormatting)
    elif FSharpType.IsRecord t then
        let fields = FSharpType.GetRecordFields(t)

        let serializers =
            [| for f in fields do
                   yield fieldSerializer (depth, options, f) |]

        fun outer ->
            let fieldStrings =
                [| for f in serializers do
                       yield f (outer) |]

            let innerString = String.concat "," fieldStrings
            $"{{%s{innerString}}}"
    elif implementsSeq t then
        let innerType = t.GetGenericArguments()
        let serializeInner = serializer (depth, options, innerType[0])

        fun outer ->
            let asEnum = outer :?> System.Collections.IEnumerable
            let asSeq = Seq.cast<obj> (asEnum)
            let inners = Seq.map serializeInner asSeq
            let join = String.Join(",", inners)
            $"[%s{join}]"
    elif isOption t then
        let innerType = t.GetGenericArguments()
        let isSomeProp = t.GetProperty("IsSome")

        let isSome outer =
            isSomeProp.GetValue(None, [| outer |]) :?> bool

        let valueProp = t.GetProperty("Value")
        let serializeInner = serializer (depth, options, innerType[0])

        fun outer ->
            if isSome outer then
                let value = valueProp.GetValue outer
                serializeInner (value)
            else
                "null"
    else
        raise (Exception $"Don't know how to serialize %s{t.ToString()} to JSON")

and fieldSerializer (depth: int, options: JsonWriteOptions, field: PropertyInfo) : obj -> string =
    let name = escapeStr (field.Name)
    let innerSerializer = serializer (depth + 1, options, field.PropertyType)

    fun outer ->
        let value = field.GetValue(outer)
        let json = innerSerializer (value)
        $"%s{name}:%s{json}"

let serializerFactory<'T> (options: JsonWriteOptions) : 'T -> string = serializer (1, options, typeof<'T>)

type JsonReadOptions = { customReaders: obj list }

let defaultJsonReadOptions: JsonReadOptions = { customReaders = [] }

let private matchReader (t: Type, w: obj) : bool =
    let _, range = w.GetType() |> FSharpType.GetFunctionElements
    t.IsAssignableFrom(range)

let private findReader (t: Type, customReaders: obj list) : obj option =
    let matchT (reader: obj) = matchReader (t, reader)
    Seq.tryFind matchT customReaders

let rec private deserializer<'T> (options: JsonReadOptions, t: Type) : JsonValue -> obj =
    let custom = findReader (t, options.customReaders)

    if custom.IsSome then
        let domain, _ = FSharpType.GetFunctionElements(custom.Value.GetType())
        let deserializeDomain = deserializer (options, domain)
        let deserializeInner = asFun (custom.Value)
        fun j -> deserializeInner (deserializeDomain (j))
    elif t = typeof<bool> then
        fun j -> box (j.AsBoolean())
    elif t = typeof<int> then
        fun j -> box (j.AsInteger())
    elif t = typeof<char> then
        fun j ->
            let s = j.AsString()

            if s.Length = 1 then
                box (s.[0])
            else
                raise (Exception $"Expected char but found '%s{s}'")
    elif t = typeof<string> then
        fun j -> box (j.AsString())
    elif t = typeof<Uri> then
        fun j ->
            // It seems that the Uri(...) constructor assumes the string has already been unescaped
            let escaped = j.AsString()
            let unescaped = Uri.UnescapeDataString(escaped)
            box (Uri(unescaped))
    elif t = typeof<JsonValue> then
        fun j -> box (j)
    elif isList t then
        let innerType = t.GetGenericArguments()
        let deserializeInner = deserializer (options, innerType[0])

        fun j ->
            let array = j.AsArray()
            let parse = Seq.map deserializeInner array
            let list = makeList (innerType[0], parse)
            box (list)
    elif isMap t then
        let arguments = t.GetGenericArguments()
        let stringType = arguments[0]
        let valueType = arguments[1]

        if stringType <> typeof<string> then
            raise (Exception $"Keys of %A{t} are not strings")

        let deserializeInner = deserializer (options, valueType)

        fun j ->
            let props = j.Properties()
            let parse = Seq.map (fun (k, v) -> k, deserializeInner v) props
            makeMap (valueType, parse)
    elif isOption t then
        let innerType = t.GetGenericArguments()
        let deserializeInner = deserializer (options, innerType[0])

        fun j ->
            if j = JsonValue.Null then
                box (makeOption (innerType[0], None))
            else
                let parse = deserializeInner j
                box (makeOption (innerType[0], Some parse))
    elif FSharpType.IsRecord t then
        let fields = FSharpType.GetRecordFields(t)

        let readers =
            [| for f in fields do
                   yield fieldDeserializer (options, f) |]

        fun j ->
            let array =
                [| for field, reader in readers do
                       yield reader j |]

            FSharpValue.MakeRecord(t, array)
    else
        raise (Exception $"Don't know how to deserialize %A{t} from JSON")

and fieldDeserializer (options: JsonReadOptions, field: PropertyInfo) : string * (JsonValue -> obj) =
    let deserializeInner = deserializer (options, field.PropertyType)

    let deserializeField (j: JsonValue) =
        let value =
            match j.TryGetProperty(field.Name) with
            | Some v -> v
            | None -> JsonValue.Null

        box (deserializeInner (value))

    field.Name, deserializeField

let deserializerFactory<'T> (options: JsonReadOptions) : JsonValue -> 'T =
    let d = deserializer (options, typeof<'T>)
    fun s -> d (s) :?> 'T
