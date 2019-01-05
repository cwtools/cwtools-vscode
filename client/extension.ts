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

const stellarisRemote = `https://github.com/tboby/cwtools-stellaris-config`;
const eu4Remote = `https://github.com/tboby/cwtools-eu4-config`;
const hoi4Remote = `https://github.com/tboby/cwtools-hoi4-config`;

let defaultClient: LanguageClient;

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
	const cacheDir = isDevDir ? context.storagePath + '/.cwtools' : context.extensionPath + '/.cwtools'


	var init = function() {
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
		switch (window.activeTextEditor.document.languageId) {
			case "stellaris": repoPath = stellarisRemote; break;
			case "eu4": repoPath = eu4Remote; break;
			case "hoi4": repoPath = hoi4Remote; break;
			default: repoPath = stellarisRemote; break;
		}
		console.log(window.activeTextEditor.document.languageId + " " + repoPath);

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
			{ scheme: 'file', language: 'hoi4' }, { scheme: 'file', language: 'eu4' }],
			synchronize: {
				// Synchronize the setting section 'languageServerExample' to the server
				configurationSection: 'cwtools',
				// Notify the server about file changes to F# project files contain in the workspace

				fileEvents: [
					workspace.createFileSystemWatcher("**/{events,common,map,prescripted_countries,flags,decisions,missions}/**/*.txt"),
					workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gui"),
					workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gfx"),
					workspace.createFileSystemWatcher("**/{interface,gfx,fonts,music,sound}/**/*.asset"),
					workspace.createFileSystemWatcher("**/{localisation,localisation_synced}/**/*.yml")
				]
			},
			initializationOptions: { language: window.activeTextEditor.document.languageId,
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
		let request = new RequestType<Position, string, void, void>('getWordRangeAtPosition');
		let status: Disposable;
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
							var game = ""
							switch (gameFolder) {
								case "Stellaris": game = "stellaris"; break;
								case "Hearts of Iron IV": game = "hoi4"; break;
								case "Europa Universalis IV": game = "eu4"; break;
							}
							if (game === "") {
								window.showErrorMessage("The selected folder does not appear to be a supported game folder")
							}
							else {
								log.appendLine("path" + gameFolder)
								log.appendLine("log" + game)
								workspace.getConfiguration("cwtools").update("cache." + game, directory.fsPath, true)
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
			})
		})



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
	}
	init()
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
