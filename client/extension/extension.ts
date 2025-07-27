/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';
import * as vs from 'vscode';
import { workspace, ExtensionContext, window, Disposable, Position, Uri, WorkspaceEdit, TextEdit, Range, commands, env } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType, RequestType, ExecuteCommandRequest, ExecuteCommandParams, RevealOutputChannelOn } from 'vscode-languageclient/node';

import { FileExplorer, FileListItem } from './fileExplorer';
import * as gp from './graphPanel';
import * as exe from './executable';
import { getGraphData } from './graphTypes';

const stellarisRemote = `https://github.com/cwtools/cwtools-stellaris-config`;
const eu4Remote = `https://github.com/cwtools/cwtools-eu4-config`;
const hoi4Remote = `https://github.com/cwtools/cwtools-hoi4-config`;
const ck2Remote = `https://github.com/cwtools/cwtools-ck2-config`;
const irRemote = `https://github.com/cwtools/cwtools-ir-config`;
const vic2Remote = `https://github.com/cwtools/cwtools-vic2-config`;
const vic3Remote = `https://github.com/cwtools/cwtools-vic3-config`;
const ck3Remote = `https://github.com/cwtools/cwtools-ck3-config`;

let defaultClient: LanguageClient;
let fileList : FileListItem[];
let fileExplorer : FileExplorer;
export async function activate(context: ExtensionContext) {


	class CwtoolsProvider implements vs.TextDocumentContentProvider
	{
		private disposables: Disposable[] = [];

		constructor(){
			workspace.registerTextDocumentContentProvider("cwtools", this)
		}
		async provideTextDocumentContent() {
			return '';
		}

		dispose(): void {
			this.disposables.forEach(d => d.dispose());
		}
	}

	const isDevDir = env.machineId === "someValue.machineId"
	const cacheDir = isDevDir ? context.globalStorageUri + '/.cwtools' : context.extensionPath + '/.cwtools'

	const init = async function(language : string, isVanillaFolder : boolean) {
		vs.languages.setLanguageConfiguration(language, { wordPattern : /"?([^\s.]+)"?/ })
		// The server is implemented using dotnet core
		let serverExe: string;
		if (os.platform() == "win32") {
			serverExe = context.asAbsolutePath(path.join('bin', 'server', 'win-x64', 'CWTools Server.exe'))
		}
		else if (os.platform() == "darwin") {
			serverExe = context.asAbsolutePath(path.join('bin', 'server', 'osx-x64', 'CWTools Server'))
			fs.chmodSync(serverExe, '755');
		}
		else {
			serverExe = context.asAbsolutePath(path.join('bin', 'server', 'linux-x64', 'CWTools Server'))
			fs.chmodSync(serverExe, '755');
		}
		let repoPath = undefined;
		switch (language) {
			case "stellaris": repoPath = stellarisRemote; break;
			case "eu4": repoPath = eu4Remote; break;
			case "hoi4": repoPath = hoi4Remote; break;
			case "ck2": repoPath = ck2Remote; break;
			case "imperator": repoPath = irRemote; break;
			case "vic2": repoPath = vic2Remote; break;
			case "vic3": repoPath = vic3Remote; break;
			case "ck3": repoPath = ck3Remote; break;
			default: repoPath = stellarisRemote; break;
		}
		console.log(language + " " + repoPath);

		// If the extension is launched in debug mode then the debug server options are used
		// Otherwise the run options are used
		const serverOptions: ServerOptions = {
			run: { command: serverExe, transport: TransportKind.stdio },
			debug : { command: serverExe, transport: TransportKind.stdio }
		}

		const fileEvents = [
			workspace.createFileSystemWatcher("**/{events,common,map,map_data,prescripted_countries,flags,decisions,missions}/**/*.txt"),
			workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gui"),
			workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gfx"),
			workspace.createFileSystemWatcher("**/{interface}/**/*.sfx"),
			workspace.createFileSystemWatcher("**/{interface,gfx,fonts,music,sound}/**/*.asset"),
			workspace.createFileSystemWatcher("**/{localisation,localisation_synced,localization}/**/*.yml")
		]

		// Options to control the language client
		const clientOptions: LanguageClientOptions = {
			// Register the server for F# documents
			documentSelector: [{ scheme: 'file', language: 'paradox' }, { scheme: 'file', language: 'yaml' }, { scheme: 'file', language: 'stellaris' },
				{ scheme: 'file', language: 'hoi4' }, { scheme: 'file', language: 'eu4' }, { scheme: 'file', language: 'ck2' }, { scheme: 'file', language: 'imperator' }
				, { scheme: 'file', language: 'vic2' }, { scheme: 'file', language: 'vic3' }, { scheme: 'file', language: 'ck3' }, { scheme: 'file', language: 'paradox'}],
			synchronize: {
				// Synchronize the setting section 'languageServerExample' to the server
				configurationSection: 'cwtools',
				// Notify the server about file changes to F# project files contain in the workspace

				fileEvents: fileEvents
			},
			initializationOptions: {
				language: language,
				isVanillaFolder: isVanillaFolder,
				rulesCache: cacheDir,
				rules_version: workspace.getConfiguration('cwtools').get('rules_version'),
				repoPath: repoPath,
				diagnosticLogging: workspace.getConfiguration('cwtools').get('logging.diagnostic') },
				revealOutputChannelOn: RevealOutputChannelOn.Error
		}

		const client = new LanguageClient('cwtools', 'Paradox Language Server', serverOptions, clientOptions);
		const log = client.outputChannel
		defaultClient = client;
		client.registerProposedFeatures();
		interface loadingBarParams { enable: boolean; value: string }
		const loadingBarNotification = new NotificationType<loadingBarParams>('loadingBar');
		interface debugStatusBarParams { enable: boolean; value: string }
		const debugStatusBarParamsNotification = new NotificationType<debugStatusBarParams>('debugBar');
		interface CreateVirtualFile { uri: string; fileContent: string }
		const createVirtualFile = new NotificationType<CreateVirtualFile>('createVirtualFile');
		const promptReload = new NotificationType<string>('promptReload')
		const forceReload = new NotificationType<string>('forceReload')
		const promptVanillaPath = new NotificationType<string>('promptVanillaPath')
		interface DidFocusFile { uri : string }
		const didFocusFile = new NotificationType<DidFocusFile>('didFocusFile')
		interface GetWordRangeAtPositionParams { position : Position, uri: string }
		const request = new RequestType<GetWordRangeAtPositionParams, string, void>('getWordRangeAtPosition');
		let status: Disposable;
		interface UpdateFileList { fileList: FileListItem[] }
		const updateFileList = new NotificationType<UpdateFileList>('updateFileList');

		let latestType : string = undefined;

		function didChangeActiveTextEditor(editor : vs.TextEditor): void {
			if (editor){
				const path = editor.document.uri.toString();
				if (languageId == "paradox" && editor.document.languageId == "plaintext") {
					vs.languages.setTextDocumentLanguage(editor.document, "paradox")
			}
			if(editor.document.languageId == language)
			{
				client.sendNotification(didFocusFile, {uri: path});
			}
			const params: ExecuteCommandParams = {
				command: "getFileTypes",
				arguments: [path]
			};
			client.sendRequest(ExecuteCommandRequest.type, params).then(
				(data : string[]) =>
				{
					if (data !== undefined && data && data[0]) {
						latestType = data[0];
						commands.executeCommand('setContext', 'cwtoolsGraphFile', true);
					}
					else {
						commands.executeCommand('setContext', 'cwtoolsGraphFile', false);
					}
				}
				);

			}
		}

		context.subscriptions.push(window.onDidChangeActiveTextEditor(didChangeActiveTextEditor));

		if (languageId == "paradox") {
			for (const textDocument of workspace.textDocuments){
				if (textDocument.languageId == "plaintext"){
					vs.languages.setTextDocumentLanguage(textDocument, "paradox")
				}
			}
		}

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
		const debugStatusBar = window.createStatusBarItem(vs.StatusBarAlignment.Left);
		context.subscriptions.push(debugStatusBar);
		client.onNotification(debugStatusBarParamsNotification, (param: debugStatusBarParams) => {
			if (param.enable) {
				debugStatusBar.text = param.value;
				debugStatusBar.show();
			}
			else if (!param.enable) {
				debugStatusBar.hide();
			}
		})
		client.onNotification(createVirtualFile, (param: CreateVirtualFile) => {
			const uri = Uri.parse(param.uri);
			workspace.openTextDocument(uri).then(doc => {
				const edit = new WorkspaceEdit();
				const range = new Range(0, 0, doc.lineCount, doc.getText().length);
				edit.set(uri, [new TextEdit(range, param.fileContent)]);
				workspace.applyEdit(edit);
				window.showTextDocument(uri);
			});
		})
		client.onNotification(promptReload, (param: string) => {
			reloadExtension(param, "Reload")
		})
		client.onNotification(forceReload, (param: string) => {
			reloadExtension(param, undefined, true);
		})
		client.onNotification(promptVanillaPath, (param: string) => {
			let gameDisplay = ""
			switch (param) {
				case "stellaris": gameDisplay = "Stellaris"; break;
				case "hoi4": gameDisplay = "Hearts of Iron IV"; break;
				case "eu4": gameDisplay = "Europa Universalis IV"; break;
				case "ck2": gameDisplay = "Crusader Kings II"; break;
				case "imperator": gameDisplay = "Imperator"; break;
				case "vic2": gameDisplay = "Victoria II"; break;
				case "vic3": gameDisplay = "Victoria 3"; break;
				case "ck3": gameDisplay = "Crusader Kings III"; break;
			}
			window.showInformationMessage("Please select the vanilla installation folder for " + gameDisplay, "Select folder")
				.then(() =>
					window.showOpenDialog({
						canSelectFiles: false,
						canSelectFolders: true,
						canSelectMany: false,
						openLabel: "Select vanilla installation folder for " + gameDisplay
					}).then(
						(uri) => {
							const directory = uri[0];
							const gameFolder = path.basename(directory.fsPath)
							let dir = directory.fsPath
							let game = ""
							switch (gameFolder) {
								case "Stellaris": game = "stellaris"; break;
								case "Hearts of Iron IV": game = "hoi4"; break;
								case "Europa Universalis IV": game = "eu4"; break;
								case "Crusader Kings II": game = "ck2"; break;
								case "Crusader Kings III":
									game = "ck3";
									dir = path.join(dir, "game");
									break;
								case "Victoria II": game = "vic2"; break;
								case "Victoria 2": game = "vic2"; break;
								case "Victoria 3":
									game = "vic3";
									dir = path.join(dir, "game");
									break;
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
		client.onRequest(request, (param) => {
			console.log("received request " + request.method + " " + param)
			const uri = Uri.parse(param.uri);
			const document = window.visibleTextEditors.find((v) => v.document.uri.path == uri.path).document
			const position = new Position(param.position.line, param.position.character)
			const wordRange = document.getWordRangeAtPosition(position, /"?([^\s]+)"?/g);
			if (wordRange === undefined) {
				return "none";
			}
			else {
				const text = document.getText(wordRange);
				console.log("wordAtPos " + text);
				if (text.trim().length == 0) {
					return "none";
				}
				else {
					return text;
				}
			}
		});
		client.onNotification(updateFileList, (params: UpdateFileList) => {
			fileList = params.fileList;
			if (fileExplorer) {
				fileExplorer.refresh(fileList);
			}
			else {
				fileExplorer = new FileExplorer(context, fileList);
			}
		})

		if (workspace.name === undefined) {
			window.showWarningMessage("You have opened a file directly.\n\rFor CWTools to work correctly, the mod folder should be opened using \"File, Open Folder\"")
		}

/// TODO graph
		// let disposable2 = commands.registerCommand('techGraph', () => {
		// 	commands.executeCommand("gettech").then((t: any) => {
		// 		//console.log(t);
		// 		let uri = Uri.parse("cwgraph://test.html")

		// 		workspace.openTextDocument(uri).then(_ => {
		// 			// let exponentPage = vscode.window.createWebviewPanel("Expo QR Code", "Expo QR Code", vscode.ViewColumn.Two, {});
		// 			// exponentPage.webview.html = this.qrCodeContentProvider.provideTextDocumentContent(vscode.Uri.parse(exponentUrl));

		// 			// vscode.commands.executeCommand("vscode.previewHtml", vscode.Uri.parse(exponentUrl), 1, "Expo QR code");
		// 			// commands.executeCommand('vscode.previewHtml', uri, ViewColumn.Active, "test")
		// 			let graphPage = window.createWebviewPanel("CWTools graph", "Technology graph", ViewColumn.Active, { enableScripts: true, localResourceRoots: [Uri.file(context.extensionPath)]});
		// 			graphPage.webview.html = graphProvider.provideTextDocumentContent(uri);
		// 		})
		// 	});
		// });

		let currentGraphDepth = 3;
		const showGraph = async function() {
			const graphData = await getGraphData(latestType, currentGraphDepth);
			const wheelSensitivity : number = workspace.getConfiguration('cwtools.graph').get('zoomSensitivity')
			gp.GraphPanel.create(context.extensionPath);
			gp.GraphPanel.currentPanel.initialiseGraph(graphData, wheelSensitivity);
		}
		context.subscriptions.push(commands.registerCommand('showGraph', () => {
			showGraph();
		}));
		context.subscriptions.push(commands.registerCommand('setGraphDepth', () => {
			window.showInputBox(
				{
					placeHolder: "default: 3",
					prompt: "Set graph depth (how many connections to go back from this file)",
					value: currentGraphDepth.toString(),
					validateInput: (v : string) => Number.isInteger(Number(v)) ? undefined : "Please enter a number"
			 }).then((res) => {
				 if (Number.isInteger(Number(res)))
				{
					currentGraphDepth = Number(res)
					showGraph()
				}
			 })
		}));
		context.subscriptions.push(commands.registerCommand('graphFromJson', () => {
			window.showOpenDialog({filters: {'Json': ['json']}})
					.then((uri) =>
					{
						fs.readFile(uri[0].fsPath, {encoding: "utf-8"}, (_, data) => {
							const wheelSensitivity: number = workspace.getConfiguration('cwtools.graph').get('zoomSensitivity')
							gp.GraphPanel.create(context.extensionPath);
							gp.GraphPanel.currentPanel.initialiseGraph(data, wheelSensitivity);
						})
					})
		}));
		// Create the language client and start the client.

		// Push the disposable to the context's subscriptions so that the
		// client can be deactivated on extension deactivation
		context.subscriptions.push(new CwtoolsProvider());
		context.subscriptions.push(vs.commands.registerCommand("cwtools.reloadExtension", () => {
			for (const sub of context.subscriptions) {
				try {
					sub.dispose();
				} catch (e) {
					console.error(e);
				}
			}
			activate(context);
		}));
		await client.start();
	}

	let languageId : string = null;
	const knownLanguageIds = ["stellaris", "eu4", "hoi4", "ck2", "imperator", "vic2", "vic3", "ck3"];
	const getLanguageIdFallback = async function() {
		const markerFiles = await workspace.findFiles("**/*.txt", null, 1);
		if (markerFiles.length == 1) {
			return (await workspace.openTextDocument(markerFiles[0])).languageId;
		}
		return null;
	}

	let guessedLanguageId = window.activeTextEditor?.document?.languageId;
	if(guessedLanguageId === undefined || !knownLanguageIds.includes(guessedLanguageId)){
		guessedLanguageId = await getLanguageIdFallback();
	}

	switch (guessedLanguageId) {
		case "stellaris": languageId = "stellaris"; break;
		case "eu4": languageId = "eu4"; break;
		case "hoi4": languageId = "hoi4"; break;
		case "ck2": languageId = "ck2"; break;
		case "imperator": languageId = "imperator"; break;
		case "vic2": languageId = "vic2"; break;
		case "vic3": languageId = "vic3"; break;
		case "ck3": languageId = "ck3"; break;
		default: languageId = "paradox"; break;
	}
	async function findExeInFiles(gameExeName: string, binariesPrefix = false) {
		if (!workspace.workspaceFolders || workspace.workspaceFolders.length === 0) {
			return [];
		}

		const root = workspace.workspaceFolders[0];
		const isWin = os.platform() === "win32";
		const ext = isWin ? "*.exe" : "*";
		const prefix = binariesPrefix ? "binaries/" : "";
		const names = [gameExeName, gameExeName.toUpperCase(), gameExeName.toLowerCase()];
		const patterns = names.map(name => new vs.RelativePattern(root, `${prefix}${name}${ext}`));

		const results = await Promise.all(patterns.map(p => workspace.findFiles(p)));
		const allFiles = results.flat();

		// Proper async filter
		const validFiles = await Promise.all(
			allFiles.map(async (v) => (await exe.existAndIsExe(v.fsPath)) ? v : null)
		).then(arr => arr.filter(Boolean));

		return validFiles;
	}
	const games = [
		{ id: "eu4", exeName: "eu4", binariesPrefix: false },
		{ id: "hoi4", exeName: "hoi4", binariesPrefix: false },
		{ id: "stellaris", exeName: "stellaris", binariesPrefix: false },
		{ id: "ck2", exeName: "CK2", binariesPrefix: false },
		{ id: "imperator", exeName: "imperator", binariesPrefix: true },
		{ id: "vic2", exeName: "v2game", binariesPrefix: false },
		{ id: "ck3", exeName: "ck3", binariesPrefix: true },
		{ id: "vic3", exeName: "victoria3", binariesPrefix: true },
	];

	const promises = games.map(({ exeName, binariesPrefix }) =>
		findExeInFiles(exeName, binariesPrefix)
	);

	const results = await Promise.all(promises);

	let isVanillaFolder = false;

	for (let i = 0; i < results.length; i++) {
		const { id } = games[i];
		if (results[i].length > 0 && (languageId === null || languageId === id)) {
			isVanillaFolder = true;
			languageId = id;
		}
	}

	if (
		workspace.workspaceFolders &&
		workspace.workspaceFolders.length > 0 &&
		path.basename(workspace.workspaceFolders[0].uri.fsPath) === "game"
	) {
		isVanillaFolder = true;
	}

	await init(languageId, isVanillaFolder);
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
