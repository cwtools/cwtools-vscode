import vscode from "vscode";
import * as path from 'path';
import * as fs from 'fs'
import { GraphData } from "../common/graphTypes";

export enum State {
    New,
    DataReady,
    ClientReady,
    Done
}
export class GraphPanel {

    /**
    * Track the currently panel. Only allow a single panel to exist at a time.
    */
    public static currentPanel: GraphPanel | undefined;
    private static readonly viewType = 'cwtools-graph';
    private readonly _panel: vscode.WebviewPanel;
    private _state: State;
    private readonly _onLoad = new vscode.EventEmitter<undefined>();
    private readonly onLoad: vscode.Event<undefined> = this._onLoad.event;

    // Methods for testing
    public getState(): State {
        return this._state;
    }

    // Method to check if cytoscape has rendered elements
    public async checkCytoscapeRendered(): Promise<boolean> {
        return new Promise<boolean>((resolve) => {
            // Send a message to the webview to check if cytoscape has rendered elements
            this._panel.webview.postMessage({ "command": "checkCytoscapeRendered" });

            // Set up a one-time message handler to receive the result
            const disposable = this._panel.webview.onDidReceiveMessage(message => {
                if (message.command === 'cytoscapeRenderedResult') {
                    disposable.dispose();
                    resolve(message.rendered);
                }
            });
            this._disposables.push(disposable);
        });
    }

    private _disposables: vscode.Disposable[] = [];
    private readonly _webviewRootPath: string;


    public static create(extensionPath: string) {
        const column = vscode.window.activeTextEditor ? vscode.window.activeTextEditor.viewColumn : undefined;

        // If we already have a panel, dispose of it.
        // Create a new panel.
        if (GraphPanel.currentPanel && GraphPanel.currentPanel._state != State.New && GraphPanel.currentPanel._state != State.ClientReady) {
            GraphPanel.currentPanel.dispose();
        }
        if (!GraphPanel.currentPanel) {
            GraphPanel.currentPanel = new GraphPanel(extensionPath, column || vscode.ViewColumn.One);
        }
    }

    private constructor(extensionPath: string, column: vscode.ViewColumn) {
        this._webviewRootPath = path.join(extensionPath, 'bin/client/webview');

        this._state = State.New;

        // Create and show a new webview panel
        this._panel = vscode.window.createWebviewPanel(GraphPanel.viewType, "Graph", column, {
            // Enable javascript in the webview
            enableScripts: true,
            retainContextWhenHidden: true,

            // And restric the webview to only loading content from our extension's `media` directory.
            localResourceRoots: [
                vscode.Uri.file(this._webviewRootPath)
            ]
        });

        // Set the webview's initial html content
        this._panel.webview.html = this._getHtmlForWebview();

        // Listen for when the panel is disposed
        // This happens when the user closes the panel or when the panel is closed programatically
        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);

        // Handle messages from the webview
        this._disposables.push((this._panel.webview.onDidReceiveMessage(message => {
            switch (message.command) {
                case 'goToFile':
                    {
                        const uri = vscode.Uri.file(message.uri);
                        const range = new vscode.Range(message.line, message.column, message.line, message.column);
                        vscode.window.showTextDocument(uri).then((texteditor) => texteditor.revealRange(range, vscode.TextEditorRevealType.AtTop))
                        return;
                    }
                case 'saveImage':
                    {
                        const image = message.image;
                        vscode.window.showSaveDialog({ filters: { 'Image': ['png'] } })
                            .then((dest) => fs.writeFile(dest.fsPath, image, "base64", (error) => console.error(error)))
                        return;
                    }
                case 'saveJson':
                    {
                        const json = message.json;
                        vscode.window.showSaveDialog({ filters: { 'Json': ['json'] } })
                            .then((dest) => fs.writeFile(dest.fsPath, json, "utf-8", (error) => console.error(error)))
                        return;
                    }
                case 'ready':
                    if (this._state == State.DataReady) {
                        this._onLoad.fire(undefined);
                    } else {
                        this._state = State.ClientReady;
                    }
            }
        }, null, this._disposables)));

        // Handle state change
        this._disposables.push((this._panel.onDidChangeViewState((e) => {
            vscode.commands.executeCommand('setContext', "cwtoolsWebview", e.webviewPanel.active);
        }, null, this._disposables)))

        // Set up commands
        this._disposables.push(vscode.commands.registerCommand('saveGraphImage', () => {
            this._panel.webview.postMessage({ "command": "exportImage" })
        }))
        this._disposables.push(vscode.commands.registerCommand('saveGraphJson', () => {
            this._panel.webview.postMessage({ "command": "exportJson" })
        }))

        vscode.commands.executeCommand('setContext', "cwtoolsWebview", true);

    }

    public initialiseGraph(data: string | GraphData, wheelSensitivity: number): Promise<void> {
        return new Promise<void>((resolve) => {
            const settings = {
                wheelSensitivity: wheelSensitivity
            }

            // Add a one-time listener to resolve the promise when the state becomes Done
            const disposable = this.onLoad(() => {
                if (this._state === State.Done) {
                    disposable.dispose();
                    resolve();
                }
            });
            this._disposables.push(disposable);

            if (typeof(data) === 'string') {
                this._disposables.push(this.onLoad(() => this._panel.webview.postMessage({ "command": "importJson", "json": data, "settings": settings })));
            } else {
                this._disposables.push(this.onLoad(() => this._panel.webview.postMessage({ "command": "go", "data": data, "settings": settings })));
            }

            if (this._state == State.Done) {
                resolve();
                return;
            }
            else if (this._state == State.ClientReady) {
                this._state = State.Done;
                this._onLoad.fire(undefined);
            } else {
                this._state = State.DataReady;
            }
        });
    }

    public dispose() {
        vscode.commands.executeCommand('setContext', "cwtoolsWebview", false);

        // Clean up our resources
        this._panel.dispose();
        this._onLoad.dispose();

        while (this._disposables.length) {
            const x = this._disposables.pop();
            if (x) {
                x.dispose();
            }
        }
        GraphPanel.currentPanel = undefined;
    }


    private _getHtmlForWebview() {
        const scriptUri = this._panel.webview.asWebviewUri(vscode.Uri.file(path.join(this._webviewRootPath, 'graph.js')));
        // const scriptPathOnDisk = vscode.Uri.file(path.join(this._webviewRootPath, 'graph.js'));
        // const scriptUri = scriptPathOnDisk.with({ scheme: 'vscode-resource' });
        // const stylePathOnDisk = vscode.Uri.file(path.join(this._webviewRootPath, 'site.css'));
        // const styleUri = stylePathOnDisk.with({ scheme: 'vscode-resource' });
        const styleUri = this._panel.webview.asWebviewUri(vscode.Uri.file(path.join(this._webviewRootPath, 'site.css')));

        const nonce = this.getNonce();
        return `
        <!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta id="__________cytoscape_stylesheet">
   <meta http-equiv="Content-Security-Policy" content="default-src 'nonce-${nonce}'; img-src vscode-resource: https: data:; script-src 'nonce-${nonce}' 'strict-dynamic'; font-src https://ajax.aspnetcdn.com/ajax/bootstrap/3.3.7; base-uri 'self'; object-src 'none'; style-src vscode-resource: https:">
          <link href="${styleUri}" rel="stylesheet" type="text/css" nonce="${nonce}" />
          <link href="https://unpkg.com/tippy.js@4.3.5/index.css" rel="stylesheet" type="text/css" nonce="${nonce}" />
    </head>
<body>
    <div class="vbox viewport body-content">

        <div class="hbox cy-container">
    <!-- <div class="cy-row"><div class="test" id="cy"%></div></div> -->
    <div class="cy-row" id="cy"></div>
</div>

 <script src="https://cdnjs.cloudflare.com/ajax/libs/systemjs/5.0.0/system.js" nonce="${nonce}"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/systemjs/5.0.0/extras/amd.js" nonce="${nonce}"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/systemjs/5.0.0/extras/named-register.js" nonce="${nonce}"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/systemjs/5.0.0/extras/named-exports.js" nonce="${nonce}"></script>
         <script src="${scriptUri}" nonce="${nonce}"></script>
</div>
</body>
</html>
`;


    }
    private getNonce() {
        let text = '';
        const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        for (let i = 0; i < 32; i++) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        return text;
    };
}
