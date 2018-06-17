# [CWTools-VSCode: Paradox Language Services](https://marketplace.visualstudio.com/items/tboby.cwtools-vscode)
**Paradox Language Features for Visual Studio Code**

*Works best with a syntax highlighting extension, such as [Paradox Syntax Highlighting](https://marketplace.visualstudio.com/items?itemName=tboby.paradox-syntax)*
# Disclaimer
This extension is still in preview, it may not work, it may stop working at any time.
**Make backups of your mod files.**
# Features
* Immediate highlighting of syntax errors
* Autocomplete triggers, effects and modifiers, providing descriptions when available
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
* Tooltips providing effect documentation

# Usage
1. Install this extension
2. If on linux, possibly follow [these instructions](https://code.visualstudio.com/docs/setup/linux#_error-enospc)
3. Either open your mod folder directly
3. or open the Stellaris folder containing your mods. This can be one of:
    * "C:\Users\name\Paradox Interactive\Stellaris"
    * "C:\Program Files(x86)\Steam\steamapps\common\Stellaris"

    or on linux
    * "/home/name/.local/share/Paradox Interactive/Stellars"
    * "/home/name/.steam/steam/steamapps/common/Stellaris"
4. Edit files and watch syntax errors show up when you make mistakes
5. Wait up to a minute for the extension to scan all your mods and find all errors

