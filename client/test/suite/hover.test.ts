import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { activate, wait } from '../utils';
import { setupLSPErrorMonitoring, checkForLSPErrors, teardownLSPErrorMonitoring } from '../lspErrorMonitor';
import { expect } from 'chai';

const sampleRoot = path.resolve(__dirname, '../sample');
const testEventFile = path.join(sampleRoot, 'events', 'irm.txt');
// const testDefinesFile = path.join(sampleRoot, 'common', 'defines', 'irm_defines.txt');
const testEffectsFile = path.join(sampleRoot, 'common', 'scripted_effects', 'irm_scripted_effects.txt');
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
/**
 * Wait for the language server to be ready by checking if it can provide hover information
 */
async function waitForLanguageServer(uri: vscode.Uri, maxRetries = 30, delayMs = 500): Promise<boolean> {
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            // Try to get hover information at a simple position
            const position = new vscode.Position(0, 0);
            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                uri,
                position
            );

            // If we get a response (even empty), the LSP is responding
            if (hovers !== undefined) {
                console.log(`Language server ready after ${attempt} attempts (${attempt * delayMs}ms)`);
                return true;
            }
        } catch (error) {
            // LSP might not be ready yet, continue retrying
            console.log(`LSP check attempt ${attempt} failed:`, error instanceof Error ? error.message : error);
        }

        if (attempt < maxRetries) {
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }

    console.log(`Language server not ready after ${maxRetries} attempts (${maxRetries * delayMs}ms total)`);
    return false;
}

suite('LSP Hover Tests', function () {
    this.timeout(60000); // 1 minute timeout for LSP operations

    let testDocument: vscode.TextDocument;
    let extension: vscode.Extension<unknown>;

    setup(async function () {
        // Setup universal LSP error monitoring
        setupLSPErrorMonitoring();

        // Activate the extension first
        await activate();
        extension = vscode.extensions.getExtension('tboby.cwtools-vscode')!;
        assert.ok(extension?.isActive, 'Extension should be active');

        // Open a test document to check LSP readiness
        const uri = vscode.Uri.file(testEventFile);
        const document = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(document);

        // Wait for the language server to be ready
        const isReady = await waitForLanguageServer(uri);
        if (!isReady) {
            console.warn('Language server not ready, tests may not work as expected');
        }

        // Close the test document
        await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
    });

    teardown(async function () {
        // Clean up any open documents
        await vscode.commands.executeCommand('workbench.action.closeAllEditors');

        // Check for LSP errors
        checkForLSPErrors(this.currentTest?.title || 'unknown test');
    });

    // Final cleanup after all tests in this suite
    suiteTeardown(async function () {
        teardownLSPErrorMonitoring();
    });

    suite('Basic Hover Functionality', function () {
        setup(async function () {
            // Open the test event file
            const uri = vscode.Uri.file(testEventFile);
            testDocument = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(testDocument);

            // Wait for the language server to process this document
            await waitForLanguageServer(uri, 10, 100); // Shorter wait since LSP should already be ready
        });

        teardown(async function () {
            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        });
        test('should provide hover information with scope change - effect', async function () {
            await waitForLSP(vscode.Uri.file(testEventFile));
            const position = new vscode.Position(37, 45); // 0-indexed, so line 38 becomes 37

            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                testDocument.uri,
                position
            );

            const hover = hovers[0];
            assert.ok(hover.contents.length > 0, 'Hover should contain content');

            // Check if the hover content is meaningful
            const content = hover.contents[0];
            if (content instanceof vscode.MarkdownString) {
                assert.ok(content.value.length > 0, 'Hover content should not be empty');
                expect(content.value).to.contain("Checks if the country is a specific type")
                    .and.to.contain("Any")
                    .and.to.contain("Country")
                    .and.to.contain("ROOT")
                    .and.to.contain("THIS");
                console.log('Hover content:', content.value);
            }
        });

        test('should provide hover information with scope change - trigger', async function () {
            await waitForLSP(vscode.Uri.file(testEventFile));
            const position = new vscode.Position(15, 20);

            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                testDocument.uri,
                position
            );

            const hover = hovers[0];
            assert.ok(hover.contents.length > 0, 'Hover should contain content');

            // Check if the hover content is meaningful
            const content = hover.contents[0];
            if (content instanceof vscode.MarkdownString) {
                assert.ok(content.value.length > 0, 'Hover content should not be empty');
                expect(content.value).to.contain("Checks if the planet is its owner's homeworld")
                    .and.to.contain("System")
                    .and.to.contain("Country")
                    .and.to.contain("ROOT")
                    .and.to.contain("THIS")
                    .and.to.contain("PREV");
                console.log('Hover content:', content.value);
            }
        });
    });

    suite('Localization Hover', function () {
        test('should provide localization information in hover', async function () {
            const uri = vscode.Uri.file(testEffectsFile);
            testDocument = await vscode.workspace.openTextDocument(uri);
            const doc = await vscode.window.showTextDocument(testDocument);
            await waitForLSP(vscode.Uri.file(testEffectsFile));
            const position = new vscode.Position(36, 70);

            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                testDocument.uri,
                position
            );

            console.log(doc.document.getText(new vscode.Range(new vscode.Position(35,0) ,new vscode.Position(36,0))));



            const hover = hovers[0];
            assert.ok(hover.contents.length > 0, 'Hover should contain content');

            // Check if the hover content is meaningful
            const content = hover.contents[0];
            if (content instanceof vscode.MarkdownString) {
                assert.ok(content.value.length > 0, 'Hover content should not be empty');
                expect(content.value).to.contain("Faction Governance");
                console.log('Hover content:', content.value);
            }
        });
    });

    suite('Error Handling', function () {
        // test('should handle hover requests gracefully when LSP is not ready', async function () {
        //     // Create a minimal document
        //     const uri = vscode.Uri.parse('untitled:test.txt');
        //     const document = await vscode.workspace.openTextDocument(uri);
        //     await vscode.window.showTextDocument(document);
        //
        //     const position = new vscode.Position(0, 0);
        //
        //     try {
        //         const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
        //             'vscode.executeHoverProvider',
        //             document.uri,
        //             position
        //         );
        //
        //         // Should not throw an error, even if no hover information is available
        //         assert.ok(true, 'Hover request completed without throwing an error');
        //
        //         if (hovers) {
        //             console.log('Received hovers for untitled document:', hovers.length);
        //         }
        //     } catch (error) {
        //         console.log('Error in hover request (this might be expected):', error);
        //         assert.ok(true, 'Error handling test completed');
        //     }
        //
        //     await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        // });

        test('should handle invalid positions gracefully', async function () {
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            // Test with an invalid position (beyond document bounds)
            const invalidPosition = new vscode.Position(1000, 1000);

            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                document.uri,
                invalidPosition
            );

            console.log('Hovers for invalid position:', hovers?.length || 0);

            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        });
    });

    suite('Performance Tests', function () {
        test('should respond to hover requests within reasonable time', async function () {
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            // Wait for document processing
            await waitForLanguageServer(document.uri, 10, 100);

            const position = new vscode.Position(8, 7);
            const startTime = Date.now();

            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                'vscode.executeHoverProvider',
                document.uri,
                position
            );

            const endTime = Date.now();
            const duration = endTime - startTime;

            console.log(`Hover request took ${duration}ms`);

            assert.ok(duration < 100, `Hover request should complete within 100 ms, took ${duration}ms`);

            if (hovers) {
                console.log('Performance test - hovers found:', hovers.length);
            }

            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        });
    });
});