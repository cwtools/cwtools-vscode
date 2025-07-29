import * as vscode from 'vscode';

//#region Utilities

    export interface TreeNode {
        isDirectory: boolean
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

    // Intermediate node type used during tree construction
    interface TreeNodeInternal {
        fileName?: string;
        isDirectory?: boolean;
        uri?: string;
        children: Record<string, TreeNodeInternal>;
    }

    export const filesToTreeNodes: fileToTreeNodeType = (arr: FileListItem[]): TreeNode[] => {
        const tree: Record<string, TreeNodeInternal> = {};

        function addnode(obj: FileListItem): void {
            const path = obj.scope + "/" + obj.logicalpath;
            const splitpath = path.replace(/^\/|\/$/g, "").split('/');
            let ptr = tree;

            for (let i = 0; i < splitpath.length; i++) {
                const segment = splitpath[i];
                const isLastSegment = i === splitpath.length - 1;

                // Initialize node if it doesn't exist
                if (!ptr[segment]) {
                    ptr[segment] = {
                        fileName: segment,
                        isDirectory: !isLastSegment,
                        children: {},
                    };

                    if (isLastSegment) {
                        ptr[segment].uri = obj.uri;
                    }
                }

                ptr = ptr[segment].children;
            }
        }

        function convertToTreeNode(node: TreeNodeInternal): TreeNode {
            // Convert children from Record to Array
            const childrenArray = Object.values(node.children).map(convertToTreeNode);

            return {
                isDirectory: node.isDirectory ?? true,
                fileName: node.fileName ?? "",
                uri: node.uri ?? "",
                children: childrenArray
            };
        }

        // Process all input files
        arr.forEach(addnode);

        // Convert the tree to the expected format
        return Object.values(tree).map(convertToTreeNode);
      }

    // interface Entry {
    //     uri: vscode.Uri;
    //     type: vscode.FileType;
    // }

    export class FilesProvider implements vscode.TreeDataProvider<TreeNode> {
        private readonly _tree : TreeNode = {
            fileName: "root",
            isDirectory: true,
            children: [] ,
            uri: ""
        }
        constructor(private files: FileListItem[]) {
            this.parseTree(files);
        }
        private _onDidChangeTreeData: vscode.EventEmitter<TreeNode | null> = new vscode.EventEmitter<TreeNode | null>();
        readonly onDidChangeTreeData: vscode.Event<TreeNode | null> = this._onDidChangeTreeData.event;


        private parseTree(files: FileListItem[]): void {
            this._tree.children = filesToTreeNodes(files);
        }

        getTreeItem(element: TreeNode): vscode.TreeItem {
            const treeItem = new vscode.TreeItem(element.fileName, element.isDirectory ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
            if (!element.isDirectory) {
                treeItem.command = { command: 'cwtools-files.openFile', title: "Open File", arguments: [vscode.Uri.parse(element.uri)], };
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
                return this._tree.children;
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

        }
        refresh(files : FileListItem[]) {
            this.parseTree(files);
            this._onDidChangeTreeData.fire(null);
        }

    }

    export class FileExplorer {

	private fileExplorer: vscode.TreeView<TreeNode>;
    private treeDataProvider: FilesProvider;

	constructor(context: vscode.ExtensionContext, files : FileListItem[]) {
		this.treeDataProvider = new FilesProvider(files);
		this.fileExplorer = vscode.window.createTreeView('cwtools-files', { treeDataProvider: this.treeDataProvider });
		context.subscriptions.push(vscode.commands.registerCommand('cwtools-files.openFile', (resource) => this.openResource(resource)));
	}

	private openResource(resource: vscode.Uri): void {
		vscode.window.showTextDocument(resource);
    }

    refresh(files : FileListItem[]): void {
        this.treeDataProvider.refresh(files);
        // this.fileExplorer.dispose();
        // const treeDataProvider = new FilesProvider(files);
        // this.fileExplorer = vscode.window.createTreeView('cwtools-files', { treeDataProvider });
    }
}
