# Use CWTools without VS Code

CWTools Server speaks LSP over standard input and output. The standalone build needs the .NET 10 runtime, but does not need the VS Code extension or a VSIX. The existing extension continues to use the same server.

## Build and run

From a checkout with submodules initialized and the .NET 10 SDK installed:

```sh
git submodule update --init --recursive
dotnet publish src/Main/Main.fsproj -c Release --no-self-contained -p:PublishTrimmed=false -p:PublishReadyToRunComposite=false -o "$HOME/cwtools-server"
```

Copy the entire `$HOME/cwtools-server` directory to the target machine. Install the .NET 10 runtime there, then configure an LSP client to start `dotnet "/path/to/standalone/CWTools Server.dll"`. On Windows, the published `CWTools Server.exe` can also be started directly. The server uses stdio; it has no command-line setup wizard. Keep logs off stdout, which carries LSP messages.

The project enables trimming and ReadyToRun composite output by default. The flags above disable both to reproduce the tested framework-dependent build. With Node.js 20 or newer, the binary can be tested from a separate working directory with `CWTOOLS_SERVER` pointing to the published DLL (the example uses a POSIX shell):

```sh
CWTOOLS_SERVER="/path/to/standalone/CWTools Server.dll" node --test tests/*.test.mjs
```

From PowerShell, set `$env:CWTOOLS_SERVER = "$HOME/cwtools-server/CWTools Server.dll"` and run `node --test (Get-ChildItem tests/*.test.mjs).FullName` from the checkout.

## Configure a client

Set `initialize.rootUri` to a mod folder. With `initializationOptions.cwtools` present, a null `rootUri` makes `initialize` fail even if `workspaceFolders` is nonempty. When nonempty, those folders are loaded instead of the `rootUri` folder and share the same game selection. Send complete settings in `initialize.initializationOptions`, not just the game name: this lets the server finish loading before it answers `initialize`. The game, rules cache and output mode are selected only during `initialize`. A client can later send `workspace/didChangeConfiguration` with `settings.cwtools` to reload the `cwtools` settings, but it cannot select a game with that notification alone. Allow sufficient time for initialization when loading game data.

Set `initializationOptions.language` to one of `stellaris`, `hoi4`, `eu4`, `ck2`, `imperator`, `vic2`, `ck3`, `vic3`, `eu5` or `paradox` (the custom-game loader). These are server loader identifiers, not a guarantee that maintained rules exist for every game. The value is lowercase and selects one game for the lifetime of the server process; start another process when changing games. File extensions and LSP document `languageId` values do not select a game: different games share `.txt` scripts and `.yml` localisation files. A client managing multiple games should keep their server processes and settings separate.

Without `initializationOptions`, the server initially uses Stellaris and needs a later `workspace/didChangeConfiguration` to load settings. If an options object is present but lacks `language`, `initialize` returns an error. An unknown or non-string `language` silently leaves Stellaris selected. Specify and validate the identifier in the client rather than relying on that fallback. The server has a native `eu5` loader, while the existing VS Code extension maps EU5 to `paradox` for its custom loader. A standalone client should choose the loader that matches its rules rather than copying that mapping unconditionally.

Example `initializationOptions` for an EU4 mod, with placeholder paths:

```json
{
  "language": "eu4",
  "isVanillaFolder": false,
  "rulesCache": "/work/cwtools-cache",
  "repoPath": null,
  "rules_version": "manual",
  "diagnosticLogging": false,
  "cwtools": {
    "cache": {
      "eu4": "/games/Europa Universalis IV",
      "stellaris": "", "hoi4": "", "ck2": "", "imperator": "",
      "vic2": "", "ck3": "", "vic3": "", "eu5": ""
    },
    "rules_folder": "/work/cwtools-eu4-config",
    "localisation": { "languages": ["English"], "generated_strings": ":0 \"REPLACE_ME\"" },
    "errors": { "vanilla": false, "ignore": [], "ignorefiles": [] },
    "experimental": false,
    "debug_mode": false,
    "ignore_patterns": [],
    "trace": { "server": "off" },
    "maxFileSize": 2
  }
}
```

Replace the placeholders with your own paths. `rules_folder` must contain the game's `.cwt` rules; for EU4, use [cwtools-eu4-config](https://github.com/cwtools/cwtools-eu4-config). `rulesCache` is a writable directory for generated cache files, while `cache.eu4` names the vanilla game installation. They are different paths. With `rules_version: "manual"`, the server reads rules from `rules_folder` instead of fetching them, and the `repoPath: null` example needs no rules repository URL. `diagnosticLogging: false` keeps detailed diagnostic logging off. Set `language` and the matching `cache.<game>` for another game. Use `isVanillaFolder: true` when the workspace itself is the game installation.

A client may use its own language identifiers: `eu4` and `plaintext` both work for EU4 scripts; `yaml` and `plaintext` work for `.yml` localisation. The server sends diagnostics as `textDocument/publishDiagnostics`. It also advertises hover, completion, definition, references, document symbols and code actions; their useful results depend on the installed game rules and document content. A missing cache or rules folder may leave semantic checks unavailable even if the process starts. Without a vanilla path the server can send `promptVanillaPath`, which a generic client may ignore; configure `cwtools.cache.<game>` to supply the game path instead.

## Localisation actions

The default `genlocfile` and `genlocall` commands send `createVirtualFile` to the VS Code extension. A generic client can opt into a standard `workspace/applyEdit` request by adding `"localisationOutput": "workspaceEdit"` next to `language` in `initializationOptions`. The client must advertise `workspace.applyEdit`, `workspace.workspaceEdit.documentChanges` and `workspace.workspaceEdit.resourceOperations` containing `"create"`.

With this opt-in, a missing-localisation code action can create a new English `.yml` in the workspace's existing `localisation/` folder. The edit contains a create-file operation with overwrite disabled and a text edit with a BOM and `l_english:` header. The client applies the edit; the server does not write the file. It reports rejection or an existing destination instead of silently replacing it. This path currently requires English localisation and does not support CK2's CSV localisation. Do not enable it in clients that cannot apply resource operations. The VS Code extension does not request this mode, so its virtual-file flow stays unchanged.

For a clean exit, send `shutdown`, wait for its response, then send `exit`. Ending input without that sequence is treated as an unsuccessful session.
