# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CWTools is a Visual Studio Code extension that provides language services for Paradox Interactive game modding, supporting games like Stellaris, Hearts of Iron IV, Europa Universalis IV, Crusader Kings II/III, Victoria 2/3, and Imperator: Rome. The extension offers syntax validation, autocomplete, tooltips, localization checking, and visual graph analysis for game scripts.

## Architecture

This is a hybrid .NET/TypeScript VS Code extension with three main components:

### Backend (.NET/F#)
- **Main** (`src/Main/`): Core F# language server providing validation, completion, and analysis
- **LSP** (`src/LSP/`): Language Server Protocol implementation in F#  
- **CSharpExtensions** (`src/CSharpExtensions/`): C# helper utilities
- Dependencies: Uses CWTools library via Paket (git submodule in `paket-files/`)

### Frontend (TypeScript)
- **Client Extension** (`client/extension/`): VS Code extension host and commands
- **Webview** (`client/webview/`): Graph visualization using Cytoscape.js
- **Test Suite** (`client/test/`): Extension tests with sample Stellaris mod files

### Build System
- **FAKE build script** (`build/Program.fs`): Cross-platform F# build automation
- **TypeScript compilation**: Uses `tsc` and `rollup` for client bundling
- **Release packaging**: Creates `.vsix` files for VS Code marketplace

## Development Commands

### Building
```bash
# Windows
./build.cmd QuickBuild

# Unix/Linux  
./build.sh QuickBuild

# Debug build
./build.cmd QuickBuildDebug
```

### TypeScript Client
```bash
npm install
npm run compile  # Compile TypeScript + bundle webview
npm test        # Run VS Code extension tests
```

### Available Build Targets
- `QuickBuild`: Build for local development (Release)
- `QuickBuildDebug`: Build for local development (Debug)  
- `DryRelease`: Full package build without publishing
- `Release`: Full build + publish to marketplace

### Testing
VS Code extension tests are located in `client/test/suite/` and use the sample Stellaris mod in `client/test/sample/` for validation scenarios.

## Key Files

- `package.json`: Node.js dependencies and scripts for TypeScript client
- `release/package.json`: VS Code extension manifest and configuration
- `fsharp-language-server.sln`: .NET solution with F# projects
- `paket.dependencies`: .NET package management
- Build scripts: `build.cmd` (Windows) / `build.sh` (Unix)

## Development Workflow

1. Use `./build.cmd QuickBuild` for initial setup and F# server compilation
2. Use `npm run compile` for TypeScript changes during development  
3. Debug by launching "Launch Extension" configuration in VS Code
4. Test with sample Paradox game mod files in `client/test/sample/`
5. Run tests with `npm test` before committing changes

## CWTools Integration

The extension integrates with the CWTools library (F# game script parser/validator) via git submodule. The build system automatically pulls the latest CWTools when building the language server.