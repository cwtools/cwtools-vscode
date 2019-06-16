/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';
import * as vs from 'vscode';
import { workspace, ExtensionContext, window, Disposable, Position, Uri, WorkspaceEdit, TextEdit, Range, commands, ViewColumn, env } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType, RequestType } from 'vscode-languageclient';
import { create } from 'domain';

import * as simplegit from 'simple-git/promise';
import { FileExplorer, FileListItem } from './fileExplorer';

const stellarisRemote = `https://github.com/tboby/cwtools-stellaris-config`;
const eu4Remote = `https://github.com/tboby/cwtools-eu4-config`;
const hoi4Remote = `https://github.com/tboby/cwtools-hoi4-config`;
const ck2Remote = `https://github.com/tboby/cwtools-ck2-config`;
const irRemote = `https://github.com/tboby/cwtools-ir-config`;
const vic2Remote = `https://github.com/tboby/cwtools-vic2-config`;

let defaultClient: LanguageClient;
let fileList : FileListItem[];
let fileExplorer : FileExplorer;
export function activate(context: ExtensionContext) {


	class CwtoolsProvider
	{
		private disposables: Disposable[] = [];

		constructor(){
			workspace.registerTextDocumentContentProvider("cwtools", this)
		}
		async provideTextDocumentContent(_: Uri): Promise<string> {
			return '';
		}

		dispose(): void {
			this.disposables.forEach(d => d.dispose());
		}
	}

	const isDevDir = env.machineId === "someValue.machineId"
	const cacheDir = isDevDir ? context.globalStoragePath + '/.cwtools' : context.extensionPath + '/.cwtools'

	var init = function(language : string, isVanillaFolder : boolean) {
		vs.languages.setLanguageConfiguration(language, { wordPattern : /"?([^\s]+)"?/})
		// The server is implemented using dotnet core
		let serverDll = context.asAbsolutePath(path.join('out', 'server', 'local', 'CWTools Server.dll'));
		var serverExe: string;
		if (os.platform() == "win32") {
			serverExe = context.asAbsolutePath(path.join('out', 'server', 'win-x64', 'CWTools Server.exe'))
		}
		else if (os.platform() == "darwin") {
			serverExe = context.asAbsolutePath(path.join('out', 'server', 'osx.10.11-x64', 'CWTools Server'))
			fs.chmodSync(serverExe, '755');
		}
		else {
			serverExe = context.asAbsolutePath(path.join('out', 'server', 'linux-x64', 'CWTools Server'))
			fs.chmodSync(serverExe, '755');
		}
		var repoPath = undefined;
		switch (language) {
			case "stellaris": repoPath = stellarisRemote; break;
			case "eu4": repoPath = eu4Remote; break;
			case "hoi4": repoPath = hoi4Remote; break;
			case "ck2": repoPath = ck2Remote; break;
			case "imperator": repoPath = irRemote; break;
			case "vic2": repoPath = vic2Remote; break;
			default: repoPath = stellarisRemote; break;
		}
		console.log(language + " " + repoPath);

		// If the extension is launched in debug mode then the debug server options are used
		// Otherwise the run options are used
		let serverOptions: ServerOptions = {
			run: { command: serverExe, transport: TransportKind.stdio },
			// debug : { command: serverExe, transport: TransportKind.stdio }
			debug: { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio}//, options: { env: { TieredCompilation_Test_OptimizeTier0: 1}} }
			// debug : { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
		}

		// Options to control the language client
		let clientOptions: LanguageClientOptions = {
			// Register the server for F# documents
			documentSelector: [{ scheme: 'file', language: 'paradox' }, { scheme: 'file', language: 'yaml' }, { scheme: 'file', language: 'stellaris' },
				{ scheme: 'file', language: 'hoi4' }, { scheme: 'file', language: 'eu4' }, { scheme: 'file', language: 'ck2' }, { scheme: 'file', language: 'imperator' }
				, { scheme: 'file', language: 'vic2' }],
			synchronize: {
				// Synchronize the setting section 'languageServerExample' to the server
				configurationSection: 'cwtools',
				// Notify the server about file changes to F# project files contain in the workspace

				fileEvents: [
					workspace.createFileSystemWatcher("**/{events,common,map,map_data,prescripted_countries,flags,decisions,missions}/**/*.txt"),
					workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gui"),
					workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gfx"),
					workspace.createFileSystemWatcher("**/{interface}/**/*.sfx"),
					workspace.createFileSystemWatcher("**/{interface,gfx,fonts,music,sound}/**/*.asset"),
					workspace.createFileSystemWatcher("**/{localisation,localisation_synced}/**/*.yml")
				]
			},
			initializationOptions: {
				language: language,
				isVanillaFolder: isVanillaFolder,
				rulesCache: cacheDir,
				rules_version: workspace.getConfiguration('cwtools').get('rules_version'),
				repoPath: repoPath }
		}

		let client = new LanguageClient('cwtools', 'Paradox Language Server', serverOptions, clientOptions);
		let log = client.outputChannel
		defaultClient = client;
		log.appendLine("client init")
		log.appendLine(env.machineId)
		client.registerProposedFeatures();
		interface loadingBarParams { enable: boolean; value: string }
		let loadingBarNotification = new NotificationType<loadingBarParams, void>('loadingBar');
		interface CreateVirtualFile { uri: string; fileContent: string }
		let createVirtualFile = new NotificationType<CreateVirtualFile, void>('createVirtualFile');
		let promptReload = new NotificationType<string, void>('promptReload')
		let forceReload = new NotificationType<string, void>('forceReload')
		let promptVanillaPath = new NotificationType<string, void>('promptVanillaPath')
		interface DidFocusFile { uri : string }
		let didFocusFile = new NotificationType<DidFocusFile, void>('didFocusFile')
		let request = new RequestType<Position, string, void, void>('getWordRangeAtPosition');
		let status: Disposable;
		interface UpdateFileList { fileList: FileListItem[] }
		let updateFileList = new NotificationType<UpdateFileList, void>('updateFileList');
		client.onReady().then(() => {
			client.onNotification(loadingBarNotification, (param: loadingBarParams) => {
				if (param.enable) {
					if (status !== undefined) {
						status.dispose();
					}
					status = window.setStatusBarMessage(param.value);
					context.subscriptions.push(status);
				}
				else if (!param.enable) {
					status.dispose();
				}
				else if (status !== undefined) {
					status.dispose();
				}
			})
			client.onNotification(createVirtualFile, (param: CreateVirtualFile) => {
				let uri = Uri.parse(param.uri);
				let doc = workspace.openTextDocument(uri).then(doc => {
					let edit = new WorkspaceEdit();
					let range = new Range(0, 0, doc.lineCount, doc.getText().length);
					edit.set(uri, [new TextEdit(range, param.fileContent)]);
					workspace.applyEdit(edit);
					window.showTextDocument(uri);
					//commands.executeCommand('vscode.previewHtml', uri, ViewColumn.One, "localisation");
				});
			})
			client.onNotification(promptReload, (param: string) => {
				reloadExtension(param, "Reload")
				// reloadExtension("Validation rules for " + window.activeTextEditor.document.languageId + " have been updated to " + param + ".\n\r Reload to use.", "Reload")
			})
			client.onNotification(forceReload, (param: string) => {
				reloadExtension(param, undefined, true);
				// reloadExtension("Validation rules for " + window.activeTextEditor.document.languageId + " have been updated to " + param + ".\n\r Reload to use.", "Reload")
			})
			client.onNotification(promptVanillaPath, (param : string) => {
				var gameDisplay = ""
				switch (param) {
					case "stellaris": gameDisplay = "Stellaris"; break;
					case "hoi4": gameDisplay = "Hearts of Iron IV"; break;
					case "eu4": gameDisplay = "Europa Universalis IV"; break;
					case "ck2": gameDisplay = "Crusader Kings II"; break;
					case "imperator": gameDisplay = "Imperator"; break;
					case "vic2": gameDisplay = "Victoria II"; break;
				}
				window.showInformationMessage("Please select the vanilla installation folder for " + gameDisplay, "Select folder")
				.then((_) =>
					window.showOpenDialog({
						canSelectFiles: false,
						canSelectFolders: true,
						canSelectMany: false,
						openLabel: "Select vanilla installation folder for " + gameDisplay
					}).then(
						(uri) => {
							let directory = uri[0];
							let gameFolder = path.basename(directory.fsPath)
							let dir = directory.fsPath
							var game = ""
							switch (gameFolder) {
								case "Stellaris": game = "stellaris"; break;
								case "Hearts of Iron IV": game = "hoi4"; break;
								case "Europa Universalis IV": game = "eu4"; break;
								case "Crusader Kings II": game = "ck2"; break;
								case "Victoria II": game = "vic2"; break;
								case "Victoria 2": game = "vic2"; break;
								case "ImperatorRome":
									game = "imperator";
									dir = path.join(dir, "game");
									break;
								case "Imperator":
									game = "imperator";
									dir = path.join(dir, "game");
									 break;
							}
							console.log(path.join(dir, "common"));
							if (game === "" || !(fs.existsSync(path.join(dir, "common")))) {
								window.showErrorMessage("The selected folder does not appear to be a supported game folder")
							}
							else {
								log.appendLine("path" + dir)
								log.appendLine("log" + game)
								workspace.getConfiguration("cwtools").update("cache." + game, dir, true)
								reloadExtension("Reloading to generate vanilla cache", undefined, true);
							}
						})
				);

			})
			client.onRequest(request, (param: any, _) => {
				console.log("recieved request " + request.method + " " + param)
				let uri = Uri.parse(param.uri);
				let document = window.visibleTextEditors.find((v) => v.document.uri.path == uri.path).document
				//let document = window.activeTextEditor.document;
				let position = new Position(param.position.line, param.position.character)
				let wordRange = document.getWordRangeAtPosition(position, /"?([^\s]+)"?/g);
				if (wordRange === undefined) {
					return "none";
				}
				else {
					let text = document.getText(wordRange);
					console.log("wordAtPos " + text);
					if (text.trim().length == 0) {
						return "none";
					}
					else {
						return text;
					}
				}
			});
			client.onNotification(updateFileList, (params: UpdateFileList) =>
			{
				fileList = params.fileList;
			})
		})

		function didChangeActiveTextEditor(editor : vs.TextEditor): void {
			let path = editor.document.uri.toString();
			if(editor.document.languageId == language)
			{
				client.sendNotification(didFocusFile, {uri: path});
			}
		}

		window.onDidChangeActiveTextEditor(didChangeActiveTextEditor);


		let disposable = client.start();

		if (workspace.name === undefined) {
			window.showWarningMessage("You have opened a file directly.\n\rFor CWTools to work correctly, the mod folder should be opened using \"File, Open Folder\"")
		}
		// Create the language client and start the client.

		// Push the disposable to the context's subscriptions so that the
		// client can be deactivated on extension deactivation
		context.subscriptions.push(disposable);
		context.subscriptions.push(new CwtoolsProvider());
		context.subscriptions.push(vs.commands.registerCommand("cwtools.reloadExtension", (_) => {
			for (const sub of context.subscriptions) {
				try {
					sub.dispose();
				} catch (e) {
					console.error(e);
				}
			}
			activate(context);
		}));
		context.subscriptions.push(vs.commands.registerCommand("showFileView", (_) =>{
			if (fileExplorer) {
				fileExplorer.refresh(fileList);
			}
			else{
				fileExplorer = new FileExplorer(context, fileList);
			}
		}))
	}

	var languageId : string = null;
	switch (window.activeTextEditor.document.languageId) {
		case "stellaris": languageId = "stellaris"; break;
		case "eu4": languageId = "eu4"; break;
		case "hoi4": languageId = "hoi4"; break;
		case "ck2": languageId = "ck2"; break;
		case "imperator": languageId = "imperator"; break;
		case "vic2": languageId = "vic2"; break;
		default:
	}
	let findExeInFiles = function(gameExeName : string) {
		if (os.platform() == "win32") {
				let a = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName + "*.exe"));
				let b = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName.toUpperCase() + "*.exe"));
				let c =workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName.toLowerCase() + "*.exe"));
				return Promise.all([a, b, c]).then(results => results[0].concat(results[1], results[2]));
		}
		else {
			let a = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName + "*"))
			let b = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName.toUpperCase() + "*"))
			let c = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], gameExeName.toLowerCase() + "*"));
			return Promise.all([a, b, c]).then(results => results[0].concat(results[1], results[2]));
		}
	}
	let findExeInFilesImperator = function(gameExeName : string) {
		if (os.platform() == "win32") {
				let a = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0],"binaries/" + gameExeName + "*.exe"));
			let b = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], "binaries/" + gameExeName.toUpperCase() + "*.exe"));
			let c = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], "binaries/" + gameExeName.toLowerCase() + "*.exe"));
				return Promise.all([a, b, c]).then(results => results[0].concat(results[1], results[2]));
		}
		else {
			let a = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], "binaries/" +  gameExeName + "*"))
			let b = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], "binaries/" + gameExeName.toUpperCase() + "*"))
			let c = workspace.findFiles(new vs.RelativePattern(workspace.workspaceFolders[0], "binaries/" + gameExeName.toLowerCase() + "*"));
			return Promise.all([a, b, c]).then(results => results[0].concat(results[1], results[2]));
		}
	}
	var eu4 = findExeInFiles("eu4")
	var hoi4 = findExeInFiles("hoi4")
	var stellaris = findExeInFiles("stellaris")
	var ck2 = findExeInFiles("CK2")
	var vic2 = findExeInFiles("VIC2")
	var ir = findExeInFilesImperator("imperator")
	Promise.all([eu4, hoi4, stellaris, ck2, ir, vic2]).then(results =>
		{
			var isVanillaFolder = false;
			if (results[0].length > 0 && (languageId === null || languageId === "eu4")) {
				isVanillaFolder = true;
				languageId = "eu4";
			}
			if (results[1].length > 0 && (languageId === null || languageId === "hoi4")) {
				isVanillaFolder = true;
				languageId = "hoi4";
			}
			if (results[2].length > 0 && (languageId === null || languageId === "stellaris")) {
				isVanillaFolder = true;
				languageId = "stellaris";
			}
			if (results[3].length > 0 && (languageId === null || languageId === "ck2")) {
				isVanillaFolder = true;
				languageId = "ck2";
			}
			if (results[4].length > 0 && (languageId === null || languageId === "imperator")) {
				isVanillaFolder = true;
				languageId = "imperator";
			}
			if (results[5].length > 0 && (languageId === null || languageId === "vic2")) {
				isVanillaFolder = true;
				languageId = "vic2";
			}
			if (path.basename(workspace.workspaceFolders[0].uri.fsPath) === "game"){
				isVanillaFolder = true;
			}
			init(languageId, isVanillaFolder)
		}
	)
}

export default defaultClient;

export async function reloadExtension(prompt?: string, buttonText?: string, force? : boolean) {
	const restartAction = buttonText || "Restart";
	const actions = [restartAction];
	if (force) {
		window.showInformationMessage(prompt);
		commands.executeCommand("cwtools.reloadExtension");
	}
	else {
		const chosenAction = prompt && await window.showInformationMessage(prompt, ...actions);
		if (!prompt || chosenAction === restartAction) {
			commands.executeCommand("cwtools.reloadExtension");
		}
	}
}
