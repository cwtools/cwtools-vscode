import { workspace, Event, ExtensionContext, EventEmitter, Uri, Memento, Disposable } from "vscode";
import * as path from 'path';
import * as fs from 'fs'
import { EIO } from "constants";
import LocalWebService from "./localWebService";

'use strict';

export class GraphProvider {
    private disposables: Disposable[] = [];
    private _service: LocalWebService;
    private _graphFile : Uri;
    private _cssFile : Uri;
    public _data : any;

    constructor(context : ExtensionContext) {
        workspace.registerTextDocumentContentProvider("cwgraph", this)
        this._service = new LocalWebService(context);
        this._service.start();

    }
    get serviceUrl(): string {
        return this._service.serviceUrl;
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
        console.log(this._data);
        const nonce = this.getNonce();

        return `
        <!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <link rel="stylesheet" href="https://ajax.aspnetcdn.com/ajax/bootstrap/3.3.7/css/bootstrap.min.css" type="text/css" nonce="${nonce}"/>
    <!-- <meta http-equiv="Content-Security-Policy" content="default-src 'nonce-${nonce}'; img-src vscode-resource: https:; script-src 'nonce-${nonce}';"> -->
</head>
<body>
    <div class="vbox viewport body-content">

        <div class="hbox cy-container">
    <!-- <div class="cy-row"><div class="test" id="cy"%></div></div> -->
    <div class="cy-row" id="cy"></div>

    <div class="cy-details" id="detailsTarget"></div>

</div>

                <script nonce="${nonce}" src="${this._graphFile}"></script>

          <link href="https://cdn.jsdelivr.net/npm/cytoscape-navigator@1.3.1/cytoscape.js-navigator.css" rel="stylesheet" type="text/css nonce="${nonce}"" />
          <link href="${this._cssFile}" rel="stylesheet" type="text/css" />
<script src="https://code.jquery.com/jquery-2.2.4.min.js" nonce="${nonce}"></script>
<script src="http://cdnjs.cloudflare.com/ajax/libs/qtip2/3.0.3/jquery.qtip.min.js" nonce="${nonce}"></script>
<link rel="stylesheet" href ="http://cdnjs.cloudflare.com/ajax/libs/qtip2/3.0.3/jquery.qtip.min.css" nonce="${nonce}"/>
<script src="https://cdnjs.cloudflare.com/ajax/libs/systemjs/0.19.47/system.js" nonce="${nonce}></script>
<script>
    SystemJS.config({
        map:{
            'cytoscape' : 'https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.2.6/cytoscape.js',
            'cytoscape-qtip':'https://cdn.jsdelivr.net/npm/cytoscape-qtip@2.7.1/cytoscape-qtip.min.js',
            'dagre':'https://cdn.rawgit.com/cpettitt/dagre/v0.7.4/dist/dagre.min.js',
            'cytoscape-dagre':'https://cdn.rawgit.com/cytoscape/cytoscape.js-dagre/1.5.0/cytoscape-dagre.js',
            'cytoscape-navigator':'https://cdn.jsdelivr.net/npm/cytoscape-navigator@1.3.1/cytoscape-navigator.min.js',
            'cytoscape-canvas':'https://cdn.jsdelivr.net/npm/cytoscape-canvas@3.0.1/dist/cytoscape-canvas.min.js',
            'handlebars' :'https://cdnjs.cloudflare.com/ajax/libs/handlebars.js/4.0.11/handlebars.js',
            'graph':'${this._graphFile}'
        },
        bundles:{
            'graph':'graph',
            'cytoscape':'cytoscape',
            'cytoscape-qtip':'cytoscape-qtip',
            'dagre':'dagre',
            'cytoscape-dagre':'cytoscape-dagre',
            'cytoscape-navigator':'cytoscape-navigator',
            'cytoscape-canvas':'cytoscape-canvas',
            'handlebars' : 'handlebars'
        }
    })
        SystemJS.import('dagre').then(function(dagre){
        SystemJS.import('cytoscape').then(function(cytoscape){
        SystemJS.import('cytoscape-dagre').then(function(cytoscapedagre){
        SystemJS.import('cytoscape-qtip').then(function(cyqtip) {
        SystemJS.import('cytoscape-navigator').then(function(nav){
        SystemJS.import('graph').then(function(graph){
            graph.go('${JSON.stringify(this._data)}');
        });
        });
        });
        });
        });
        });
    $(function () {
});
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
