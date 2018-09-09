/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';

import { workspace, ExtensionContext, window, Disposable, Position, Uri, WorkspaceEdit, TextEdit, Range, commands, ViewColumn } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType, RequestType } from 'vscode-languageclient';
import { create } from 'domain';

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

	// The server is implemented using dotnet core
	let serverDll = context.asAbsolutePath(path.join('out', 'server', 'local', 'CWTools Server.dll'));
	var serverExe : string;
	if(os.platform() == "win32"){
		serverExe = context.asAbsolutePath(path.join('out', 'server','win-x64', 'CWTools Server.exe'))
	}
	else if (os.platform() == "darwin"){
		serverExe = context.asAbsolutePath(path.join('out', 'server', 'osx.10.11-x64', 'CWTools Server'))
		fs.chmodSync(serverExe, '755');
	}
	else{
		serverExe = context.asAbsolutePath(path.join('out', 'server', 'linux-x64', 'CWTools Server'))
		fs.chmodSync(serverExe, '755');
	}

	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	let serverOptions: ServerOptions = {
		run : { command: serverExe, transport: TransportKind.stdio },
		// debug : { command: serverExe, transport: TransportKind.stdio }
		debug : { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
		// debug : { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
	}

	// Options to control the language client
	let clientOptions: LanguageClientOptions = {
		// Register the server for F# documents
		documentSelector: [{scheme: 'file', language: 'paradox'}, {scheme: 'file', language: 'yaml'}, {scheme: 'file', language: 'stellaris'},
							{scheme: 'file', language: 'hoi4'}],
		synchronize: {
			// Synchronize the setting section 'languageServerExample' to the server
			configurationSection: 'cwtools',
			// Notify the server about file changes to F# project files contain in the workspace

			fileEvents: [
				workspace.createFileSystemWatcher("**/{events,common,map,prescripted_countries,flags}/**/*.txt"),
				workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gui"),
				workspace.createFileSystemWatcher("**/{interface,gfx}/**/*.gfx"),
				workspace.createFileSystemWatcher("**/{interface,gfx,fonts,music,sound}/**/*.asset"),
				workspace.createFileSystemWatcher("**/{localisation,localisation_synced}/**/*.yml")
				]
		},
		initializationOptions : {language : window.activeTextEditor.document.languageId}
	}

	let client = new LanguageClient('cwtools', 'Paradox Language Server', serverOptions, clientOptions);
	defaultClient = client;
	console.log("client init")
	client.registerProposedFeatures();
	interface loadingBarParams { enable : boolean; value : string }
	let loadingBarNotification = new NotificationType<loadingBarParams, void>('loadingBar');
	interface CreateVirtualFile { uri : string; fileContent : string }
	let createVirtualFile = new NotificationType<CreateVirtualFile, void>('createVirtualFile');
	let request = new RequestType<Position, string, void, void>('getWordRangeAtPosition');
	let status : Disposable;
	client.onReady().then(() => {
		client.onNotification(loadingBarNotification, (param: loadingBarParams) =>{
			if(param.enable){
				if (status !== undefined) {
					status.dispose();
				}
				status = window.setStatusBarMessage(param.value);
			}
			else if(!param.enable){
				status.dispose();
			}
			else if(status !== undefined){
				status.dispose();
			}
		})
		client.onNotification(createVirtualFile, (param : CreateVirtualFile) => {
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
		client.onRequest(request, (param : any, _) => {
			console.log("recieved request " + request.method + " "+ param)
			let document = window.activeTextEditor.document;
			let position = new Position(param.position.line, param.position.character)
			let wordRange = document.getWordRangeAtPosition(position, /"?([^\s]+)"?/g);
			if(wordRange === undefined){
				return "none";
			}
			else{
				let text = document.getText(wordRange);
				console.log("wordAtPos "+ text);
				if (text.trim().length == 0){
					return "none";
				}
				else{
					return text;
				}
			}
		})
	})
	let disposable = client.start();

	// Create the language client and start the client.

	// Push the disposable to the context's subscriptions so that the
	// client can be deactivated on extension deactivation
	context.subscriptions.push(disposable);
	context.subscriptions.push(new CwtoolsProvider());
}

export default defaultClient;