/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';
import * as child_process from 'child_process';
import * as vs from 'vscode';
import { workspace, ExtensionContext, window, Disposable, Uri, WorkspaceEdit, TextEdit, Range, commands, env } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType, ExecuteCommandRequest, ExecuteCommandParams, RevealOutputChannelOn } from 'vscode-languageclient/node';

import { FileExplorer, FileListItem } from './fileExplorer';
import * as gp from './graphPanel';
import * as exe from './executable';
import { getGraphData } from '../common/graphTypes';

const stellarisRemote = `https://github.com/cwtools/cwtools-stellaris-config`;
const eu4Remote = `https://github.com/cwtools/cwtools-eu4-config`;
const hoi4Remote = `https://github.com/cwtools/cwtools-hoi4-config`;
const ck2Remote = `https://github.com/cwtools/cwtools-ck2-config`;
const irRemote = `https://github.com/cwtools/cwtools-ir-config`;
const vic2Remote = `https://github.com/cwtools/cwtools-vic2-config`;
const vic3Remote = `https://github.com/cwtools/cwtools-vic3-config`;
const ck3Remote = `https://github.com/cwtools/cwtools-ck3-config`;
const eu5Remote = `https://github.com/kaiser-chris/cwtools-eu5-config`;

export let defaultClient: LanguageClient;
let fileList: FileListItem[];
let fileExplorer: FileExplorer;
export async function activate(context: ExtensionContext) {


	class CwtoolsProvider implements vs.TextDocumentContentProvider {
		private disposables: Disposable[] = [];

		constructor() {
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

	const init = async function (language: string, isVanillaFolder: boolean) {
		vs.languages.setLanguageConfiguration(language, { wordPattern: /"?([^\s.]+)"?/ })
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
			case "eu5": repoPath = eu5Remote; break;
			default: repoPath = stellarisRemote; break;
		}
		console.log(language + " " + repoPath);

		// If the extension is launched in debug mode then the debug server options are used
		// Otherwise the run options are used
		const serverOptions = {
			run: { command: serverExe, transport: TransportKind.stdio },
			debug: { command: serverExe, transport: TransportKind.stdio }
		};

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
				, { scheme: 'file', language: 'vic2' }, { scheme: 'file', language: 'vic3' }, { scheme: 'file', language: 'ck3' }, { scheme: 'file', language: 'eu5' }, { scheme: 'file', language: 'paradox' }],
			synchronize: {
				// Synchronize the setting section 'languageServerExample' to the server
				configurationSection: 'cwtools',
				// Notify the server about file changes to F# project files contain in the workspace

				fileEvents: fileEvents
			},
			initializationOptions: {
				language: language === 'eu5' ? 'paradox' : language,
				isVanillaFolder: isVanillaFolder,
				rulesCache: cacheDir,
				rules_version: workspace.getConfiguration('cwtools').get('rules_version'),
				repoPath: repoPath,
				diagnosticLogging: workspace.getConfiguration('cwtools').get('logging.diagnostic')
			},
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
		interface DidFocusFile { uri: string }
		const didFocusFile = new NotificationType<DidFocusFile>('didFocusFile')
		let status: Disposable;
		interface UpdateFileList { fileList: FileListItem[] }
		const updateFileList = new NotificationType<UpdateFileList>('updateFileList');

		let latestType: string;

		async function didChangeActiveTextEditor(editor: vs.TextEditor | undefined): Promise<void> {
			if (editor) {
				const path = editor.document.uri.toString();
				if (languageId == "paradox" && editor.document.languageId == "plaintext") {
					await vs.languages.setTextDocumentLanguage(editor.document, "paradox")
				}
				if (editor.document.languageId == language) {
					await client.sendNotification(didFocusFile, { uri: path });
				}
				const params: ExecuteCommandParams = {
					command: "getFileTypes",
					arguments: [path]
				};
				const data = await client.sendRequest(ExecuteCommandRequest.type, params);
				if (data !== undefined && data && data[0]) {
					latestType = data[0];
					await commands.executeCommand('setContext', 'cwtoolsGraphFile', true);
				}
				else {
					await commands.executeCommand('setContext', 'cwtoolsGraphFile', false);
				}
			}
		}

		context.subscriptions.push(window.onDidChangeActiveTextEditor(didChangeActiveTextEditor));

		if (languageId == "paradox") {
			for (const textDocument of workspace.textDocuments) {
				if (textDocument.languageId == "plaintext") {
					await vs.languages.setTextDocumentLanguage(textDocument, "paradox")
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
		client.onNotification(createVirtualFile, async (param: CreateVirtualFile) => {
			const uri = Uri.parse(param.uri);
			const doc = await workspace.openTextDocument(uri);
			const edit = new WorkspaceEdit();
			const range = new Range(0, 0, doc.lineCount, doc.getText().length);
			edit.set(uri, [new TextEdit(range, param.fileContent)]);
			await workspace.applyEdit(edit);
			await window.showTextDocument(uri);
		})
		client.onNotification(promptReload, async (param: string) => {
			await reloadExtension(param, "Reload")
		})
		client.onNotification(forceReload, async (param: string) => {
			await reloadExtension(param, undefined, true);
		})
		client.onNotification(promptVanillaPath, async (param: string) => {
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
				case "eu5": gameDisplay = "Europa Universalis V"; break;
			}
			const result = await window.showInformationMessage("Please select the vanilla installation folder for " + gameDisplay, "Select folder");
			if (!result) {
				return;
			}
			const uri = await window.showOpenDialog({
				canSelectFiles: false,
				canSelectFolders: true,
				canSelectMany: false,
				openLabel: "Select vanilla installation folder for " + gameDisplay
			});
			if (!uri) {
				return;
			}
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
				case "Europa Universalis V":
					game = "eu5";
					dir = path.join(dir, "game");
					break;
			}
			console.log(path.join(dir, "common"));
			if (game === "" || !(fs.existsSync(path.join(dir, "common")))) {
				await window.showErrorMessage("The selected folder does not appear to be a supported game folder")
			}
			else {
				log.appendLine("path" + dir)
				log.appendLine("log" + game)
				await workspace.getConfiguration("cwtools").update("cache." + game, dir, true)
				await reloadExtension("Reloading to generate vanilla cache", undefined, true);
			}
		})
		client.onNotification(updateFileList, (params: UpdateFileList) => {
			fileList = params.fileList;
			if (fileExplorer) {
				fileExplorer.refresh(fileList);
			}
			else {
				fileExplorer = new FileExplorer(context, fileList);
			}
		})

		// Memory usage status bar — polls the server process every 60 seconds
		const PROCD_NAME = process.platform === 'win32' ? 'CWTools Server' : 'CWTools Server';
		const memoryBar = window.createStatusBarItem(vs.StatusBarAlignment.Right, 100);
		memoryBar.command = 'cwtools.showMemoryDetails';
		memoryBar.name = 'CWTools Memory';
		memoryBar.text = '$(database) ?MB';
		memoryBar.tooltip = 'Starting up…';
		memoryBar.show();
		context.subscriptions.push(memoryBar);

		let latestPid = 0;
		let latestWs = 0;
		let cachedPid: number | undefined;
		let cachedDetails: any = undefined;  // cached result from getMemoryDetails

		const queryServerMemory = (): number | undefined => {
			try {
				const cmd = process.platform === 'win32'
					? `powershell -NoProfile -NonInteractive -Command "&{$p=Get-Process -Name '${PROCD_NAME}' -ErrorAction SilentlyContinue|Select-Object -First 1;if($p){Write-Output ($p.Id.ToString()+'|'+$p.WorkingSet64.ToString())}}"`
					: `bash -c 'p=\$(pgrep -f "${PROCD_NAME}" | head -1); [ -n "$p" ] && ps -o rss= -p $p | xargs -I{} echo "$p|$(({} * 1024))"'`;
				const output = child_process.execSync(cmd, { encoding: 'utf8', timeout: 5000 }).trim();
				const parts = output.split('|');
				if (parts.length === 2) {
					const pid = parseInt(parts[0], 10);
					const ws = parseInt(parts[1], 10);
					if (!isNaN(pid) && pid > 0 && !isNaN(ws) && ws > 0) {
						cachedPid = pid;
						return ws;
					}
				}
			} catch { /* process not yet available */ }
			return undefined;
		};

		// Fetch detailed stats from server (5-min interval, less frequent than total memory poll)
		const fetchMemoryDetails = async () => {
			try {
				const params: ExecuteCommandParams = { command: "getMemoryDetails", arguments: [] };
				const result: any = await client.sendRequest(ExecuteCommandRequest.type, params);
				if (result) { cachedDetails = result; }
			} catch { /* server command not ready */ }
		};

		// Helper: get localized label from server response, fallback to English key
		const loc = (key: string, fallback: string): string => {
			return cachedDetails?.locLabels?.[key] ?? fallback;
		};

		const updateMemoryBar = () => {
			const ws = queryServerMemory();
			if (ws !== undefined) {
				latestWs = ws;
				const mb = (ws / 1048576).toFixed(0);
				// Build tooltip with fragmentation info if available
				let tip = `${loc('clickForDetails', 'Click for details')}  (PID ${cachedPid ?? '?'})`;
				if (cachedDetails) {
					const mh = (cachedDetails.managedHeap / 1048576).toFixed(0);
					const nm = (cachedDetails.nonManagedMB ?? 0);
					const pct = ws > 0 ? ((nm * 1048576 / ws) * 100).toFixed(0) : '?';
					tip += `\n${loc('managedHeap', 'Managed Heap')}: ${mh} MB | ${loc('unmanaged', 'Unmanaged')}: ${nm} MB (${pct}%)`;
				}
				tip += `\n${loc('totalWorkingSet', 'Total Working Set')}: ${mb} MB`;
				memoryBar.text = `$(database) ${mb}MB`;
				memoryBar.tooltip = tip;
			}
		};

		// First reading after 3s, then every 60s
		setTimeout(updateMemoryBar, 3000);
		const memTimer = setInterval(updateMemoryBar, 60000);
		context.subscriptions.push({ dispose: () => clearInterval(memTimer) });
		// Fetch detailed stats every 5 minutes
		setTimeout(fetchMemoryDetails, 10000);
		const detailTimer = setInterval(fetchMemoryDetails, 300000);
		context.subscriptions.push({ dispose: () => clearInterval(detailTimer) });

		context.subscriptions.push(commands.registerCommand('cwtools.showMemoryDetails', async () => {
			const mb = (latestWs / 1048576).toFixed(0);
			const pid = cachedPid ?? '?';
			// Always fetch fresh data on click
			try {
				const params: ExecuteCommandParams = { command: "getMemoryDetails", arguments: [] };
				const result: any = await client.sendRequest(ExecuteCommandRequest.type, params);
				if (result) { cachedDetails = result; }
			} catch (_e) { /* server command not available */ }
			const r = cachedDetails;
			if (r) {
				const L = (key: string, fallback: string): string => r?.locLabels?.[key] ?? fallback;
				const wsMB = (r.processWorkingSet / 1048576).toFixed(0);
				const mhMB = (r.managedHeap / 1048576).toFixed(0);
				const nmMB = r.nonManagedMB ?? 0;
				const nmPct = r.processWorkingSet > 0 ? ((nmMB * 1048576 / r.processWorkingSet) * 100).toFixed(0) : '?';
				const entityF = r.entityFiles ?? '?';
				const fileR = r.fileResources ?? '?';
				const fwcF = r.fileWithContentFiles ?? '?';
				const openDocs = r.openDocuments ?? '?';
				const entityCache = r.entityCacheEntries >= 0 ? r.entityCacheEntries.toLocaleString() : '?';
				const valErr = r.totalValidationErrors ?? '?';
				const locErr = r.totalLocalisationErrors ?? '?';
				const locCache = r.locCacheEntries ?? '?';
				const tds = r.typeDefinitions ?? '?';
				const tes = r.typeEntries ?? '?';
				const efs = r.scriptedEffects ?? '?';
				const trs = r.scriptedTriggers ?? '?';
				const sms = r.staticModifiers ?? '?';
				const strKeys = r.internedUniqueKeys >= 0 ? r.internedUniqueKeys.toLocaleString() : '?';
				const strTotal = r.internedTotalEntries >= 0 ? r.internedTotalEntries.toLocaleString() : '?';
				const gc0 = r.gcGen0 ?? '?';
				const gc1 = r.gcGen1 ?? '?';
				const gc2 = r.gcGen2 ?? '?';
				const allocMB = r.gcTotalAllocatedMB ?? '?';
				window.showInformationMessage(
					`${L('title', 'CWTools Memory Details')} (PID ${pid})` +
					`\n━━━ ${L('sectionProcessMemory', 'Process Memory')} ━━━` +
					`\n${L('workingSet', 'Working Set')}:  ${wsMB} MB` +
					`\n${L('managedHeap', 'Managed Heap')}:      ${mhMB} MB` +
					`\n${L('unmanaged', 'Unmanaged')}:      ${nmMB} MB (${nmPct}%)` +
					`\n━━━ ${L('sectionGcStatus', 'GC Status')} ━━━` +
					`\n  ${L('gcCollections', 'Gen0/1/2 Collections')}: ${gc0} / ${gc1} / ${gc2}` +
					`\n  ${L('totalAllocated', 'Total Allocated')}:   ${allocMB} MB` +
					`\n━━━ ${L('sectionFileCache', 'File Cache')} (${r.totalFiles ?? '?'}) ━━━` +
					`\n  ${L('parsedEntities', 'Parsed Entities')}:  ${entityF}  (${L('parsedEntitiesHint', 'AST parse trees')})` +
					`\n  ${L('fileReferences', 'File References')}:  ${fileR}  (${L('fileReferencesHint', 'dds/png etc.')})` +
					`\n  ${L('filesWithContent', 'Files With Content')}:  ${fwcF}  (${L('filesWithContentHint', 'yml localisation')})` +
					`\n  ${L('entityCacheEntries', 'Entity Cache Entries')}: ${entityCache}  (${L('entityCacheHint', 'entitiesMap')})` +
					`\n  ${L('openDocuments', 'Open Documents')}: ${openDocs}` +
					`\n━━━ ${L('sectionTypeSystem', 'Type System')} ━━━` +
					`\n  ${L('typeDefinitions', 'Type Definitions')}:    ${tds}` +
					`\n  ${L('typeEntries', 'Type Entries')}:    ${tes}` +
					`\n  ${L('scriptedEffects', 'Scripted Effects')}:    ${efs}` +
					`\n  ${L('scriptedTriggers', 'Scripted Triggers')}:  ${trs}` +
					(r.scriptedTriggersGrowth > 0 && r.scriptedTriggersPrev >= 0 ? ` ⚠️ +${r.scriptedTriggersGrowth.toLocaleString()}` : '') +
					`\n  ${L('staticModifiers', 'Static Modifiers')}:  ${sms}` +
					`\n━━━ ${L('sectionValidationCache', 'Validation Cache')} ━━━` +
					`\n  ${L('validationErrors', 'Validation Errors')}:    ${valErr}` +
					`\n  ${L('localisationErrors', 'Localisation Errors')}:  ${locErr}` +
					`\n  ${L('locCacheEntries', 'Loc Cache Entries')}:    ${locCache}` +
					`\n━━━ ${L('sectionStringInterning', 'String Interning')} ━━━` +
					`\n  ${L('uniqueKeys', 'Unique Keys')}:      ${strKeys}` +
					`\n  ${L('totalEntries', 'Total Entries')}:      ${strTotal}`,
					{ modal: false }
				);
				return;
			}
			// Fallback if server command fails
			window.showInformationMessage(
				`CWTools Language Server\nPID: ${pid}\nWorking Set: ${mb} MB`,
				{ modal: false }
			);
		}));

		if (workspace.name === undefined) {
			await window.showWarningMessage("You have opened a file directly.\n\rFor CWTools to work correctly, the mod folder should be opened using \"File, Open Folder\"")
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
		const showGraph = async function () {
			const graphData = await getGraphData(latestType, currentGraphDepth);
			const wheelSensitivity: number = workspace.getConfiguration('cwtools.graph').get('zoomSensitivity') ?? 1;
			gp.GraphPanel.create(context.extensionPath);
			gp.GraphPanel.currentPanel!.initialiseGraph(graphData, wheelSensitivity);
		}
		context.subscriptions.push(commands.registerCommand('showGraph', async () => {
			await showGraph();
		}));
		context.subscriptions.push(commands.registerCommand('setGraphDepth', async () => {
			const res = await window.showInputBox(
				{
					placeHolder: "default: 3",
					prompt: "Set graph depth (how many connections to go back from this file)",
					value: currentGraphDepth.toString(),
					validateInput: (v: string) => Number.isInteger(Number(v)) ? undefined : "Please enter a number"
				});
			if (Number.isInteger(Number(res))) {
				currentGraphDepth = Number(res)
				await showGraph()
			}
		}));
		context.subscriptions.push(commands.registerCommand('graphFromJson', async () => {
			const uri = await window.showOpenDialog({ filters: { 'Json': ['json'] } })
			if (!uri) {
				return;
			}
			const bytes = await vs.workspace.fs.readFile(uri[0]);
			const data = new TextDecoder('utf-8').decode(bytes);
			const wheelSensitivity: number = workspace.getConfiguration('cwtools.graph').get('zoomSensitivity') ?? 1;
			gp.GraphPanel.create(context.extensionPath);
			gp.GraphPanel.currentPanel!.initialiseGraph(data, wheelSensitivity);
		}));
		// Create the language client and start the client.

		// Push the disposable to the context's subscriptions so that the
		// client can be deactivated on extension deactivation
		context.subscriptions.push(new CwtoolsProvider());
		context.subscriptions.push(vs.commands.registerCommand("cwtools.reloadExtension", async () => {
			for (const sub of context.subscriptions) {
				try {
					sub.dispose();
				} catch (e) {
					console.error(e);
				}
			}
			await activate(context);
		}));
		await client.start();
	}

	let languageId: string;
	const knownLanguageIds = ["stellaris", "eu4", "hoi4", "ck2", "imperator", "vic2", "vic3", "ck3", "eu5"];
	const getLanguageIdFallback = async function () {
		const markerFiles = await workspace.findFiles("**/*.txt", null, 1);
		if (markerFiles.length == 1) {
			return (await workspace.openTextDocument(markerFiles[0])).languageId;
		}
		return null;
	}

	let guessedLanguageId: string | undefined | null = window.activeTextEditor?.document?.languageId;
	if (guessedLanguageId === undefined || !knownLanguageIds.includes(guessedLanguageId)) {
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
		case "eu5": languageId = "eu5"; break;
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
		{ id: "eu5", exeName: "eu5", binariesPrefix: true },
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


export async function reloadExtension(prompt: string, buttonText?: string, force?: boolean) {
	const restartAction = buttonText || "Restart";
	const actions = [restartAction];
	if (force) {
		const result = await window.showInformationMessage(prompt);
		if (result) {
			await commands.executeCommand("cwtools.reloadExtension");
		}
	}
	else {
		const chosenAction = prompt && await window.showInformationMessage(prompt, ...actions);
		if (!prompt || chosenAction === restartAction) {
			await commands.executeCommand("cwtools.reloadExtension");
		}
	}
}
// export default defaultClient;
