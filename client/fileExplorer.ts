import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

//#region Utilities

    export interface TreeNode {
        isDirectory: Boolean
        children: TreeNode[]
        fileName: string
        uri: string
    }
    export interface File {
        fileName: string
    }
    export interface FileListItem {
        scope: string;
        uri: string;
        logicalpath: string
    }

    export type fileToTreeNodeType = (files: FileListItem[]) => TreeNode[]

    function filesToTreeNodes(arr : FileListItem[]) : TreeNode[] {
        var tree : any = {}
        function addnode(obj : FileListItem) {
            var path = obj.scope + "/" + obj.logicalpath
            var splitpath = path.replace(/^\/|\/$/g, "").split('/');
            var ptr = tree;
            for (let i = 0; i < splitpath.length; i++) {
                let node: any = {
                fileName: splitpath[i],
                isDirectory: true,
                };
                if (i == splitpath.length - 1) {
                    node.uri = obj.uri;
                    node.isDirectory = false;
                    // console.log(splitpath[i] + "," + obj.fileName)
                }
                ptr[splitpath[i]] = ptr[splitpath[i]] || node;
                ptr[splitpath[i]].children = ptr[splitpath[i]].children || {};
                ptr = ptr[splitpath[i]].children;
            }
        }
        function objectToArr(node : any) {
          Object.keys(node || {}).map((k) => {
            if (node[k].children) {
              objectToArr(node[k])
            }
          })
          if (node.children) {
            node.children = (<any>Object).values(node.children)
            node.children.forEach(objectToArr)
          }
        }
        arr.map(addnode);
        objectToArr(tree)
        return (<any>Object).values(tree)
      }

    // interface Entry {
    //     uri: vscode.Uri;
    //     type: vscode.FileType;
    // }

    export class FilesProvider implements vscode.TreeDataProvider<TreeNode> {
        private tree : TreeNode;
        constructor(private files: FileListItem[]) {
            // const t : File[] = files.map(f => ({fileName: f}));
            this.tree = { fileName: "root", isDirectory: true, children: filesToTreeNodes(files), uri: "" };
        }

        onDidChangeTreeData?: vscode.Event<TreeNode>;
        getTreeItem(element: TreeNode): vscode.TreeItem {
            const treeItem = new vscode.TreeItem(element.fileName, element.isDirectory ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
            if (!element.isDirectory) {
                treeItem.command = { command: 'fileExplorer.openFile', title: "Open File", arguments: [vscode.Uri.parse(element.uri)], };
                treeItem.contextValue = 'file';
                treeItem.resourceUri = vscode.Uri.parse(element.uri)
            }
            return treeItem;
        }
        async getChildren(element?: TreeNode): Promise<TreeNode[]> {
            if (element) {
                return element.children;
                // const children = await this.readDirectory(element.uri);
                // return children.map(([name, type]) => ({ uri: vscode.Uri.file(path.join(element.uri.fsPath, name)), type }));
            }
            else {
                return [this.tree];
            }

            // const workspaceFolder = vscode.workspace.workspaceFolders.filter(folder => folder.uri.scheme === 'file')[0];
            // if (workspaceFolder) {
            //     const children = await this.readDirectory(workspaceFolder.uri);
            //     children.sort((a, b) => {
            //         if (a[1] === b[1]) {
            //             return a[0].localeCompare(b[0]);
            //         }
            //         return a[1] === vscode.FileType.Directory ? -1 : 1;
            //     });
            //     return children.map(([name, type]) => ({ uri: vscode.Uri.file(path.join(workspaceFolder.uri.fsPath, name)), type }));
            // }

            return [];
        }

    }

    export class FileExplorer {

	private fileExplorer: vscode.TreeView<TreeNode>;

	constructor(context: vscode.ExtensionContext, files : FileListItem[]) {
		const treeDataProvider = new FilesProvider(files);
		this.fileExplorer = vscode.window.createTreeView('fileExplorer', { treeDataProvider });
		vscode.commands.registerCommand('fileExplorer.openFile', (resource) => this.openResource(resource));
	}

	private openResource(resource: vscode.Uri): void {
		vscode.window.showTextDocument(resource);
    }

    refresh(files : FileListItem[]): void {
        this.fileExplorer.dispose();
        const treeDataProvider = new FilesProvider(files);
        this.fileExplorer = vscode.window.createTreeView('fileExplorer', { treeDataProvider });
    }
}
