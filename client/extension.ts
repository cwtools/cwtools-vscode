/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';

import { workspace, ExtensionContext, window, Disposable, Position } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType, RequestType } from 'vscode-languageclient';

export function activate(context: ExtensionContext) {

	// The server is implemented using dotnet core
	//let serverDll = context.asAbsolutePath(path.join('src', 'Main', 'bin', 'Debug', 'netcoreapp2.0', 'Main.dll'));
	let serverExe = context.asAbsolutePath(path.join('out', 'server', 'Main.exe'))
	
	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	let serverOptions: ServerOptions = {
		run : { command: serverExe, transport: TransportKind.stdio },
		debug : { command: serverExe, transport: TransportKind.stdio}
		//debug : { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
		// debug : { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
	}
	
	// Options to control the language client
	let clientOptions: LanguageClientOptions = {
		// Register the server for F# documents
		documentSelector: [{scheme: 'file', language: 'paradox'}],
		synchronize: {
			// Synchronize the setting section 'languageServerExample' to the server
			configurationSection: 'cwtools',
			// Notify the server about file changes to F# project files contain in the workspace
			fileEvents: workspace.createFileSystemWatcher('**/{events, common}/**/*.txt')
		}
	}
	
	let client = new LanguageClient('cwtoolsvscode', 'Paradox Language Server', serverOptions, clientOptions);
	client.registerProposedFeatures();
	let notification = new NotificationType<boolean, void>('loadingBar');
	let request = new RequestType<Position, string, void, void>('getWordRangeAtPosition');
	let status : Disposable;
	client.onReady().then(() => {
		client.onNotification(notification, (param : any) =>{
			if(param.value){
				status = window.setStatusBarMessage("Loading files...");
			}
			else{
				status.dispose();
			}
		})
		client.onRequest(request, (param : Position) => {
			let document = window.activeTextEditor.document;
			let wordRange = document.getWordRangeAtPosition(param);
			let word = document.getText(wordRange);
			return word;
		})
	})
	let disposable = client.start();

	
	// Create the language client and start the client.
	
	// Push the disposable to the context's subscriptions so that the 
	// client can be deactivated on extension deactivation
	context.subscriptions.push(disposable);
}