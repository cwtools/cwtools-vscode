module Main.Serialize

open System.IO
open CWTools.Games.Files
open CWTools.Serializer



let serializeSTL folder cacheDirectory =
    let filename =
        serializeSTL
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "stl.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "stl.cwb"))

let serializeEU4 folder cacheDirectory =
    let filename =
        serializeEU4
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "eu4.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "eu4.cwb"))

let serializeHOI4 folder cacheDirectory =
    let filename =
        serializeHOI4
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "hoi4.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "hoi4.cwb"))

let serializeCK2 folder cacheDirectory =
    let filename =
        serializeCK2
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "ck2.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "ck2.cwb"))

let serializeIR folder cacheDirectory =
    let filename =
        serializeIR
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "ir.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "ir.cwb"))

let serializeVIC2 folder cacheDirectory =
    let filename =
        serializeVIC2
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "vic2.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "vic2.cwb"))

let serializeCK3 folder cacheDirectory =
    let filename =
        serializeCK3
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "ck3.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "ck3.cwb"))


let serializeVIC3 folder cacheDirectory =
    let filename =
        serializeVIC3
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "vic3.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "vic3.cwb"))


let serializeEU5 folder cacheDirectory =
    let filename =
        serializeEU5
            { WorkspaceDirectory.name = "vanilla"
              path = folder }
            (Some "eu5.cwb")
            CWTools.CompressionOptions.NoCompression

    File.Move(filename, Path.Combine(cacheDirectory, "eu5.cwb"))

let deserialize path =
    try
        deserialize path
    with _ ->
        [], []
