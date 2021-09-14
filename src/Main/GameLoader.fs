module Main.Lang.GameLoader
open LSP.Json.Ser
open LSP
open LSP.Types
open System
open System.Runtime.InteropServices
open CWTools.Utilities.Position
open CWTools.Games
open System.IO
open System.IO.Compression
open CWTools.Localisation
open LSP.Types
open CWTools.Games.Files
open CWTools.Parser
open Main.Serialize
open CWTools.Common
open System.Text
open System.Reflection
open FParsec
open CWTools.Utilities.Utils

// let loadSTL() =
//     let stlLocCommands =
//         configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
//                 |> Option.bind (fun (fn, ft) -> if activeGame = STL then Some (fn, ft) else None)
//                 |> Option.map (fun (fn, ft) -> STLParser.loadLocCommands fn ft)
//                 |> Option.defaultValue []
//     let stlEventTargetLinks =
//         configs |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "links.cwt")
//                 |> Option.map (fun (fn, ft) -> UtilityParser.loadEventTargetLinks STLConstants.Scope.Any STLConstants.parseScope STLConstants.allScopes fn ft)
//                 |> Option.defaultValue (Scopes.STL.scopedEffects |> List.map SimpleLink)


//     let stlsettings = {
//         CWTools.Games.Stellaris.StellarisSettings.rootDirectory = path
//         scope = FilesScope.All
//         modFilter = None
//         scriptFolders = folders
//         excludeGlobPatterns = Some dontLoadPatterns
//         validation = {
//             validateVanilla = validateVanilla
//             experimental = experimental
//             langs = languages
//         }
//         rules = Some {
//             ruleFiles = configs
//             validateRules = true
//             debugRulesOnly = false
//             debugMode = false
//         }
//         embedded = {
//             triggers = triggers
//             effects = effects
//             modifiers = modifiers
//             embeddedFiles = cachedFiles
//             cachedResourceData = cached
//             localisationCommands = stlLocCommands
//             eventTargetLinks = stlEventTargetLinks
//         }
//         initialLookup = STLLookup()
//     }

let rec replaceFirst predicate value = function
    | [] -> []
    | h :: t when predicate h -> value :: t
    | h :: t -> h :: replaceFirst predicate value t

let fixEmbeddedFileName (s : string) =
    let count = (Seq.filter ((=) '.') >> Seq.length) s
    let mutable out = "//" + s
    [1 .. count - 1] |> List.iter (fun _ -> out <- (replaceFirst ((=) '.') '\\' (out |> List.ofSeq)) |> Array.ofList |> System.String )
    out

let rec getAllFolders dirs =
    if Seq.isEmpty dirs then Seq.empty else
        seq { yield! dirs |> Seq.collect (fun s -> try Directory.EnumerateDirectories s with | _ -> Seq.empty)
              yield! dirs |> Seq.collect (fun s -> try Directory.EnumerateDirectories s with | _ -> Seq.empty) |> getAllFolders }
let getAllFoldersUnion dirs =
    seq {
        yield! dirs
        yield! getAllFolders dirs
    }

let getConfigFiles cachePath useManualRules manualRulesFolder =
    let embeddedConfigFiles =
        match cachePath, useManualRules with
        | Some path, false ->
            let configFiles = (getAllFoldersUnion ([path] |> Seq.ofList))
                                |> Seq.collect (fun s -> try Directory.EnumerateFiles s with | _ -> Seq.empty)
            let configFiles = configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt" || Path.GetExtension f = ".log")
            configFiles |> List.map (fun f -> f, File.ReadAllText(f))
        | _ -> []
    let configpath = "Main.files.config.cwt"
    let configFiles =
        match useManualRules, manualRulesFolder with
        |true, Some rf ->
            let configFiles =
                if Directory.Exists rf
                then getAllFoldersUnion ([rf] |> Seq.ofList)
                else if Directory.Exists "./.cwtools" then getAllFoldersUnion (["./.cwtools"] |> Seq.ofList) else Seq.empty
            let configFiles = configFiles |> Seq.collect (fun s -> try Directory.EnumerateFiles s with | _ -> Seq.empty)
            configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt" || Path.GetExtension f = ".log")
        |_ ->
            let configFiles = (if Directory.Exists "./.cwtools" then getAllFoldersUnion (["./.cwtools"] |> Seq.ofList) else Seq.empty) |> Seq.collect (fun s -> try Directory.EnumerateFiles s with | _ -> Seq.empty)
            configFiles |> List.ofSeq |> List.filter (fun f -> Path.GetExtension f = ".cwt" || Path.GetExtension f = ".log")
    let configs =
        match configFiles.Length > 0 with
        |true ->
            configFiles |> List.map (fun f -> f, File.ReadAllText(f))
            //["./config.cwt", File.ReadAllText("./config.cwt")]
        |false ->
            embeddedConfigFiles
    configs

let getFolderList (filename : string, filetext : string) =
    if Path.GetFileName filename = "folders.cwt"
    then Some (filetext.Split(([|"\r\n"; "\r"; "\n"|]), StringSplitOptions.None) |> List.ofArray |> List.filter (fun s -> s <> ""))
    else None

type ServerSettings =
    {
        cachePath : string option
        useManualRules : bool
        manualRulesFolder : string option
        isVanillaFolder : bool
        path : string
        workspaceFolders : WorkspaceFolder list
        dontLoadPatterns : string list
        validateVanilla : bool
        languages : CWTools.Common.Lang list
        experimental : bool
        debug_mode : bool
        maxFileSize : int
    }

type GameLanguage = |STL |HOI4 |EU4 |CK2 |IR |VIC2 |CK3 |Custom

let getCachedFiles (game : GameLanguage) cachePath isVanillaFolder =
    let timer = new System.Diagnostics.Stopwatch()
    timer.Start()
    let cached, cachedFiles =
        match (game, cachePath, isVanillaFolder) with
        | _, _, true ->
            logInfo "Vanilla folder, so not loading cache"
            ([], [])
        | STL, Some cp, _ -> deserialize (cp + "/../stl.cwb")
        | EU4, Some cp, _ -> deserialize (cp + "/../eu4.cwb")
        | HOI4, Some cp, _ -> deserialize (cp + "/../hoi4.cwb")
        | CK2, Some cp, _ -> deserialize (cp + "/../ck2.cwb")
        | IR, Some cp, _ -> deserialize (cp + "/../ir.cwb")
        | VIC2, Some cp, _ -> deserialize (cp + "/../vic2.cwb")
        | CK3, Some cp, _ -> deserialize (cp + "/../ck3.cwb")
        | _ -> ([], [])
    logInfo (sprintf "Parse cache time: %i" timer.ElapsedMilliseconds);
    timer.Restart()
    cached, cachedFiles


let getRootDirectories (serverSettings : ServerSettings) =
    let rawdirs =
        match serverSettings.workspaceFolders with
        | [] ->
            [{ WorkspaceDirectory.name = Path.GetFileName serverSettings.path; path = serverSettings.path}]
        | ws ->
            ws |> List.map (fun wd -> { WorkspaceDirectory.name = wd.name; path = wd.uri.LocalPath })
    (rawdirs |> List.map WD) @ (rawdirs |> List.collect addDLCs )


let loadEU4 (serverSettings : ServerSettings) =
    let cached, cachedFiles = getCachedFiles EU4 serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList

    // let eu4Mods = EU4Parser.loadModifiers "eu4mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(eu4modpath))).ReadToEnd())
    let eu4settings = {
        rootDirectories = getRootDirectories serverSettings
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        embedded = FromConfig (cachedFiles, cached)
        validation = {
            validateVanilla = serverSettings.validateVanilla;
            langs = serverSettings.languages
            experimental = serverSettings.experimental
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        modFilter = None
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.EU4.EU4Game(eu4settings)
    game


let loadHOI4 serverSettings =
    let cached, cachedFiles = getCachedFiles HOI4 serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList

    let hoi4modpath = "Main.files.hoi4.modifiers"

    let hoi4settings = {
        rootDirectories = getRootDirectories serverSettings
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        embedded = FromConfig (cachedFiles, cached)
        validation = {
            validateVanilla = serverSettings.validateVanilla;
            langs = serverSettings.languages
            experimental = serverSettings.experimental
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        modFilter = None
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.HOI4.HOI4Game(hoi4settings)
    game

let loadCK2 serverSettings =
    let cached, cachedFiles = getCachedFiles CK2 serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList


    // let ck2Mods = CK2Parser.loadModifiers "ck2mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(ck2modpath))).ReadToEnd())
    let ck2settings = {
        rootDirectories = getRootDirectories serverSettings
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        embedded = FromConfig (cachedFiles, cached)
        validation = {
            validateVanilla = serverSettings.validateVanilla;
            langs = serverSettings.languages
            experimental = serverSettings.experimental
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        modFilter = None
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.CK2.CK2Game(ck2settings)
    game

let loadIR serverSettings =
    let cached, cachedFiles = getCachedFiles IR serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList



    // let ck2Mods = CK2Parser.loadModifiers "ck2mods" ((new StreamReader(Assembly.GetEntryAssembly().GetManifestResourceStream(ck2modpath))).ReadToEnd())
    let irsettings = {
        rootDirectories = getRootDirectories serverSettings
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        embedded = FromConfig (cachedFiles, cached)
        validation = {
            validateVanilla = serverSettings.validateVanilla;
            langs = serverSettings.languages
            experimental = serverSettings.experimental
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        modFilter = None
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.IR.IRGame(irsettings)
    game

let loadVIC2 serverSettings =
    let cached, cachedFiles = getCachedFiles VIC2 serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList


    let vic2settings = {
        rootDirectories = getRootDirectories serverSettings
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        embedded = FromConfig (cachedFiles, cached)
        validation = {
            validateVanilla = serverSettings.validateVanilla;
            langs = serverSettings.languages
            experimental = serverSettings.experimental
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        modFilter = None
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.VIC2.VIC2Game(vic2settings)
    game

let loadSTL serverSettings =
    let cached, cachedFiles = getCachedFiles STL serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList

    let timer = new System.Diagnostics.Stopwatch()
    timer.Start()



    let stlsettings = {
        CWTools.Games.Stellaris.StellarisSettings.rootDirectories = getRootDirectories serverSettings
        modFilter = None
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        validation = {
            validateVanilla = serverSettings.validateVanilla
            experimental = serverSettings.experimental
            langs = serverSettings.languages
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        embedded = FromConfig (cachedFiles, cached)
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.Stellaris.STLGame(stlsettings)
    game

let loadCK3 serverSettings =
    let cached, cachedFiles = getCachedFiles CK3 serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList



    let stlsettings = {
        CWTools.Games.CK3.CK3Settings.rootDirectories = getRootDirectories serverSettings
        modFilter = None
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        validation = {
            validateVanilla = serverSettings.validateVanilla
            experimental = serverSettings.experimental
            langs = serverSettings.languages
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        embedded = FromConfig (cachedFiles, cached)
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.CK3.CK3Game(stlsettings)
    game
let loadCustom serverSettings =
    // let cached, cachedFiles = getCachedFiles STL serverSettings.cachePath serverSettings.isVanillaFolder
    let configs = getConfigFiles serverSettings.cachePath serverSettings.useManualRules serverSettings.manualRulesFolder
    let folders = configs |> List.tryPick getFolderList



    let stlsettings = {
        CWTools.Games.Custom.CustomSettings.rootDirectories = getRootDirectories serverSettings
        modFilter = None
        scriptFolders = folders
        excludeGlobPatterns = Some serverSettings.dontLoadPatterns
        validation = {
            validateVanilla = serverSettings.validateVanilla
            experimental = serverSettings.experimental
            langs = serverSettings.languages
        }
        rules = Some {
            ruleFiles = configs
            validateRules = true
            debugRulesOnly = false
            debugMode = serverSettings.debug_mode
        }
        embedded = FromConfig ([], [])
        maxFileSize = Some serverSettings.maxFileSize
    }
    let game = CWTools.Games.Custom.CustomGame(stlsettings, "custom")
    game
