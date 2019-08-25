import { workspace, Event, ExtensionContext, EventEmitter, Uri, Memento, Disposable } from "vscode";
import * as path from 'path';
import * as fs from 'fs'
import { EIO } from "constants";

'use strict';

export class GraphProvider {
    private disposables: Disposable[] = [];
    private _graphFile : Uri;
    private _cssFile : Uri;
    public _data : any;

    constructor(_ : ExtensionContext) {
        workspace.registerTextDocumentContentProvider("cwgraph", this)

    }
    set graphFile(value: Uri) {
        this._graphFile = value;
    }

    set cssFile(value: Uri) {
        this._cssFile = value;
    }

    private getNonce() {
        let text = '';
        const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        for (let i = 0; i < 32; i++) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        return text;
    };
    provideTextDocumentContent(uri: Uri): string {
        if(uri.path === 'graph.js'){
            return fs.readFileSync(uri.toString()).toString();
        }
        return this.createGraphFromData();
    }
    createGraphFromData() : string {
        console.log(this._data);
        const nonce = this.getNonce();

        return `
        <!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta id="__________cytoscape_stylesheet">
   <!-- <meta http-equiv="Content-Security-Policy" content="default-src 'nonce-${nonce}'; img-src vscode-resource: https:; script-src 'nonce-${nonce}' 'strict-dynamic'; font-src https://ajax.aspnetcdn.com/ajax/bootstrap/3.3.7; base-uri 'self'; object-src 'none'; style-src vscode-resource: https:"> -->
          <link href="${this._cssFile}" rel="stylesheet" type="text/css" nonce="${nonce}" />
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

         <script src="${this._graphFile}" nonce="${nonce}"></script>
<script nonce="${nonce}">
cwtoolsgraph.go(${JSON.stringify(this._data)})
</script>
</div>
</body>
</html>
`;
    }

    dispose(): void {
        this.disposables.forEach(d => d.dispose());
    }
}
