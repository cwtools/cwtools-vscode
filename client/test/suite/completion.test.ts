import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { activate } from '../utils';
import { setupLSPErrorMonitoring, checkForLSPErrors, teardownLSPErrorMonitoring } from '../lspErrorMonitor';
import { expect } from 'chai';

const sampleRoot = path.resolve(__dirname, '../sample');
const testEventFile = path.join(sampleRoot, 'events', 'irm.txt');
const testNicheFile = path.join(sampleRoot, 'common', 'pop_faction_types', 'irm_regionalist.txt');

async function waitForLSP(uri: vscode.Uri, maxRetries = 60, delayMs = 500): Promise<void> {
    let diagnosticsReady = false;

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            // Check if diagnostics are available (indicates LSP is processing files)
            const diagnostics = vscode.languages.getDiagnostics(uri);
            if (diagnostics && diagnostics.length >= 0) {
                diagnosticsReady = true;
            }

            // Try to get meaningful completions (not just any response)
            const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                'vscode.executeCompletionItemProvider',
                uri,
                new vscode.Position(12, 0) // Position where we expect trigger completions
            );

            // Check if we have actual LSP completions (not just fallback)
            if (completions?.items?.length) {
                const hasNonTextCompletions = completions.items.some(item => (item.kind || 0) !== 0);

                if (hasNonTextCompletions) {
                    console.log(`LSP ready after ${attempt} attempts (${attempt * delayMs}ms) - found ${completions.items.length} completions`);
                    return;
                }
            }

            // If we have diagnostics but no good completions yet, LSP is still starting up
            if (diagnosticsReady) {
                console.log(`LSP starting (attempt ${attempt}) - diagnostics available but completions not ready`);
            }

        } catch (error) {
            // LSP might not be ready yet, continue retrying
            console.log(`LSP check attempt ${attempt} failed:`, error instanceof Error ? error.message : String(error));
        }

        if (attempt < maxRetries) {
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }

    throw new Error(`LSP not ready after ${maxRetries} attempts (${maxRetries * delayMs}ms total)`);
}

async function getCompletions(uri: vscode.Uri, position: vscode.Position): Promise<vscode.CompletionList> {
    const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
        'vscode.executeCompletionItemProvider',
        uri,
        position
    );

    assert.ok(completions?.items?.length, 'No completions received');

    // Check that LSP is being used (not VS Code text completion fallback)
    const textTypeCount = completions.items.filter(item => (item.kind || 0) === 0).length;
    assert.ok(textTypeCount == 0,
        `Too many Text type completions (${textTypeCount}/${completions.items.length}) - LSP may not be working`);

    return completions;
}

suite('LSP Completion Tests', function () {
    this.timeout(30000);

    async function openAndGetTestDocument() {
        const uri = vscode.Uri.file(testEventFile);
        const document = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(document);
        return document;
    }
    async function openAndGetNicheDocument() {
        const uriNiche = vscode.Uri.file(testNicheFile);
        const document = await vscode.workspace.openTextDocument(uriNiche);
        await vscode.window.showTextDocument(document);
        return document;
    }
    setup(async function () {
        setupLSPErrorMonitoring();
        await activate();

        const extension = vscode.extensions.getExtension('tboby.cwtools-vscode')!;
        assert.ok(extension?.isActive, 'Extension should be active');

        const document = await openAndGetTestDocument();
        await waitForLSP(document.uri);
    });

    teardown(async function () {
        await vscode.commands.executeCommand('workbench.action.closeAllEditors');
        checkForLSPErrors(this.currentTest?.title || 'unknown test');
    });

    suiteTeardown(async function () {
        teardownLSPErrorMonitoring();
    });
    test('should provide completions in niche context', async function () {
        const document = await openAndGetNicheDocument();
        const completions = await getCompletions(document.uri, new vscode.Position(26,41));

        const labels = completions.items.map(item =>
            typeof item.label === 'string' ? item.label : item.label.label
        );
        expect(labels).deep.equal(["regionalist_dublicated", "sector_policy_leadership"])
    });

    test('should provide completions in trigger context', async function () {
        const document = await openAndGetTestDocument();
        const completions = await getCompletions(document.uri, new vscode.Position(12, 0));

        const labels = completions.items.map(item =>
            typeof item.label === 'string' ? item.label : item.label.label
        );

        // Check for common trigger keywords
        const hasRelevantTriggers = labels.some(label =>
            label.includes('is_ai') || label.includes('limit') || label.includes('country_type')
        );

        assert.ok(hasRelevantTriggers);
        assert.ok(completions.items.length > 0, 'Should have completion items');
        // Note: Don't assert specific content as it depends on LSP implementation
    });

    test('should provide completions in effect context', async function () {
        const document = await openAndGetTestDocument();
        const completions = await getCompletions(document.uri, new vscode.Position(17, 8));

        const labels = completions.items.map(item =>
            typeof item.label === 'string' ? item.label : item.label.label
        );

        assert.ok(labels.length > 0);
        assert.ok(completions.items.length > 0, 'Should have completion items in effect context');
    });

    test('should respond to completion requests quickly', async function () {
        const document = await openAndGetTestDocument();
        const start = Date.now();
        const completions = await getCompletions(document.uri, new vscode.Position(12, 0));
        const duration = Date.now() - start;

        assert.ok(duration < 5000, `Completion should be fast, took ${duration}ms`);
        assert.ok(completions.items.length > 0, 'Should have completion items');
    });

    test('should provide LSP-based completions not just text fallback', async function () {
        const document = await openAndGetTestDocument();
        const completions = await getCompletions(document.uri, new vscode.Position(12, 0));

        // The getCompletions helper already validates no Text type completions
        // This test confirms completions have LSP-specific characteristics
        const hasLSPFeatures = completions.items.some(item =>
            item.detail || item.documentation || item.sortText ||
            (item.commitCharacters && item.commitCharacters.length > 0)
        );

        assert.ok(hasLSPFeatures, 'Completions should have LSP-specific features like detail, documentation, or sortText');
    });

});

