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

/**
 * Shared small test utilities to reduce duplication across suites
 */
export async function wait(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

export async function retryAsync(fn: () => Promise<boolean>, maxRetries = 3, delayMs = 500): Promise<boolean> {
  for (let attempt = 1; attempt <= maxRetries; attempt++) {
    try {
      const result = await fn();
      if (result === true) {
        return true;
      }
    } catch (err) {
      if (attempt === maxRetries) {
        throw err;
      }
    }
    if (attempt < maxRetries) {
      await wait(delayMs);
    }
  }
  return false;
}

export async function openDocumentAndShow(uri: vscode.Uri): Promise<vscode.TextDocument> {
  const doc = await vscode.workspace.openTextDocument(uri);
  await vscode.window.showTextDocument(doc);
  return doc;
}
