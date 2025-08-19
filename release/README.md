# [CWTools: Paradox Language Services](https://marketplace.visualstudio.com/items/tboby.cwtools-vscode)

**Paradox Language Features for Visual Studio Code**

## Disclaimer

This extension is still in preview, it may not work, it may stop working at any time.
**Make backups of your mod files.**

## Supported games

* Stellaris
* Hearts of Iron IV
* Europa Universalis IV
* Imperator: Rome - outdated, help needed
* Crusader Kings II - partial
* Crusader Kings III - in progress, help needed
* Victoria 3 - in progress, help needed

## Features

* Immediate highlighting of syntax errors
* Autocomplete while you type, providing descriptions when available
* Tooltips on hover showing:
  * Related localisation
  * Documentation for that element
  * Scope context at that position
* A wide range of validators for common, interface, and events, checking
  * That required localisation keys are defined
  * Existence of effects/triggers/modifiers
  * Scope context for used effects/triggers/modifiers
  * Usage of scripted effects/triggers
  * Correct entries for weights/AI_chance/etc
  * That event\_targets are saved before they're used
  * That referenced sprites and graphics files exist
  * and a number of other specific validators
* "Code actions" to generate .yml for missing localisation

## Usage

1. Install this extension
2. Open your mod folder directly, which should be within a folder containing the game name:

* `C:\Users\name\Documents\Paradox Interactive\Stellaris\mod\your_mod`

3. Follow the prompts to select your vanilla folder
4. Edit files and watch syntax errors show up when you make mistakes
5. Wait up to a minute for the extension to scan your mod and find errors

### Multiple mods - workspace

If you have multiple mods that need to be loaded at once, use VS Code's [multi-root workspace](https://code.visualstudio.com/docs/editing/workspaces/workspaces#_untitled-multiroot-workspaces) feature.

1. Open your first mod
2. Use "File", "Add folder to workspace" to add your next mod
3. cwtools should reload including both mods and vanilla in context using correct mod load order

If you want to browse vanilla files, you can use the "CWTOOLS LOADED FILES" section in the Explorer tab.

### Completion

![Completion](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/completion.gif)

### Tooltips

![Tooltips](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/tooltips.gif)

### Scope tooltips

![Scope tooltips](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/scopetooltip.gif)

### Scope errors

![Scope ](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/scopeerror.gif)

### Localisation error

![Localisation error](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/localisationerror.gif)

### Go to definition

![Go to definition](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/gotodef.gif)

### Find all references

![Find all references](https://raw.githubusercontent.com/cwtools/cwtools-vscode/refs/heads/main/release/docs/findallrefs.png)



## Links

* [vic2-config](https://github.com/cwtools/cwtools-vic2-config)
* [vic3-config](https://github.com/cwtools/cwtools-vic3-config)
* [ck2-config](https://github.com/cwtools/cwtools-ck2-config)
* [eu4-config](https://github.com/cwtools/cwtools-eu4-config)
* [hoi4-config](https://github.com/cwtools/cwtools-hoi4-config)
* [stellaris-config](https://github.com/cwtools/cwtools-stellaris-config)
* [ir-config](https://github.com/cwtools/cwtools-ir-config)
* [ck3-config](https://github.com/cwtools/cwtools-ck3-config)
