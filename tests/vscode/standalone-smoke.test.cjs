const assert = require('node:assert/strict');
const vscode = require('vscode');
const path = require('node:path');

suite('VS Code standalone compatibility', () => {
    test('opens the bundled server and publishes a syntax error', async function () {
        this.timeout(60000);
        const workspace = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspace, 'The test workspace must be open');
        const uri = vscode.Uri.file(path.join(workspace.uri.fsPath, 'events', 'broken.txt'));
        const document = await vscode.workspace.openTextDocument(uri);
        assert.equal(document.languageId, 'eu4');
        await vscode.window.showTextDocument(document);
        const extension = vscode.extensions.getExtension('tboby.cwtools-vscode');
        assert.ok(extension, 'The development extension must be installed');
        await extension.activate();
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => finish(new Error('Timed out waiting for CW001')), 45000);
            const subscription = vscode.languages.onDidChangeDiagnostics(check);
            function finish(error) {
                clearTimeout(timer);
                subscription.dispose();
                if (error) reject(error);
                else resolve();
            }
            function check() {
                if (vscode.languages.getDiagnostics(uri).some(diagnostic => diagnostic.code === 'CW001')) finish();
            }
            check();
        });
    });
});
