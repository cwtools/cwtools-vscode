module Main.GraphTypes

open CWTools.Utilities.Position

/// Represents a reference between graph nodes
type GraphReference =
    {
        /// The key/id of the referenced node
        key: string
        /// Whether this is an outgoing reference
        isOutgoing: bool
        /// Optional label for the reference
        label: string option
    }

/// Represents a node in the graph
type GraphNode =
    {
        /// Unique identifier for the node
        id: string
        /// Display name for the node
        displayName: string option
        /// List of references to other nodes
        references: GraphReference list
        /// Location of the node in a file
        location: range option
        /// Documentation for the node
        documentation: string option
        /// Additional details as key-value pairs
        details: Map<string, string list> option
        /// Whether this is a primary node
        isPrimary: bool
        /// Type of the entity
        entityType: string
        /// Display name for the entity type
        entityTypeDisplayName: string option
        /// Abbreviation for the node
        abbreviation: string option
    }

/// Complete graph data returned by getGraphData
type GraphData = GraphNode list

/// Converts a GraphNode to a JSON representation
let graphNodeToJson (node: GraphNode) =
    let convRangeToJson (loc: range) =
        [| "filename", FSharp.Data.JsonValue.String(loc.FileName.Replace("\\", "/"))
           "line", FSharp.Data.JsonValue.Number(loc.StartLine |> decimal)
           "column", FSharp.Data.JsonValue.Number(loc.StartColumn |> decimal) |]
        |> FSharp.Data.JsonValue.Record

    let referenceToJson (ref: GraphReference) =
        [| Some("key", FSharp.Data.JsonValue.String ref.key)
           Some("isOutgoing", FSharp.Data.JsonValue.Boolean ref.isOutgoing)
           ref.label |> Option.map (fun l -> "label", FSharp.Data.JsonValue.String l) |]
        |> Array.choose id
        |> FSharp.Data.JsonValue.Record

    let detailsToJson (details: Map<string, string list>) =
        details
        |> Map.toArray
        |> Array.map (fun (k, vs) ->
            FSharp.Data.JsonValue.Record
                [| "key", FSharp.Data.JsonValue.String k
                   "values",
                   (vs
                    |> Array.ofList
                    |> Array.map FSharp.Data.JsonValue.String
                    |> FSharp.Data.JsonValue.Array) |])
        |> FSharp.Data.JsonValue.Array

    [| Some("id", FSharp.Data.JsonValue.String node.id)
       node.displayName
       |> Option.map (fun s -> ("name", FSharp.Data.JsonValue.String s))
       Some("references", FSharp.Data.JsonValue.Array(node.references |> Array.ofList |> Array.map referenceToJson))
       node.location |> Option.map (fun loc -> "location", convRangeToJson loc)
       node.documentation
       |> Option.map (fun s -> "documentation", FSharp.Data.JsonValue.String s)
       node.details |> Option.map (fun m -> "details", detailsToJson m)
       Some("isPrimary", FSharp.Data.JsonValue.Boolean node.isPrimary)
       Some("entityType", FSharp.Data.JsonValue.String node.entityType)
       node.entityTypeDisplayName
       |> Option.map (fun s -> ("entityTypeDisplayName", FSharp.Data.JsonValue.String s))
       node.abbreviation
       |> Option.map (fun s -> ("abbreviation", FSharp.Data.JsonValue.String s)) |]
    |> Array.choose id
    |> FSharp.Data.JsonValue.Record

/// Converts GraphData to a JSON representation
let graphDataToJson (data: GraphData) =
    data |> List.map graphNodeToJson |> Array.ofList |> FSharp.Data.JsonValue.Array
