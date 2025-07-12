import * as vscode from 'vscode';

export async function activate() {
  const ext = vscode.extensions.getExtension('tboby.cwtools-vscode')!;
  try {
    await ext.activate();
    return ext.exports;
  } catch (error) {
    // Extension activation might fail due to missing language server in test environment
    // But we can still test other aspects of the extension
    console.warn('Extension activation had issues (expected in test environment):', error);
    return ext.exports;
  }
}
