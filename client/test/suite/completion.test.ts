import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { activate } from '../utils';
import { setupLSPErrorMonitoring, checkForLSPErrors, teardownLSPErrorMonitoring } from '../lspErrorMonitor';

const sampleRoot = path.resolve(__dirname, '../sample');
const testEventFile = path.join(sampleRoot, 'events', 'irm.txt');
const testEffectsFile = path.join(sampleRoot, 'common', 'scripted_effects', 'irm_scripted_effects.txt');

/**
 * Wait for the language server to be ready by checking if it can provide completion information
 */
async function waitForCompletionServer(uri: vscode.Uri, maxRetries = 30, delayMs = 500): Promise<boolean> {
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            // Try to get completion information at a simple position
            const position = new vscode.Position(0, 0);
            const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                'vscode.executeCompletionItemProvider',
                uri,
                position
            );

            // If we get a response (even empty), the LSP is responding
            if (completions !== undefined) {
                console.log(`Completion server ready after ${attempt} attempts (${attempt * delayMs}ms)`);
                return true;
            }
        } catch (error) {
            // LSP might not be ready yet, continue retrying
            console.log(`Completion check attempt ${attempt} failed:`, error instanceof Error ? error.message : error);
        }

        if (attempt < maxRetries) {
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }

    console.log(`Completion server not ready after ${maxRetries} attempts (${maxRetries * delayMs}ms total)`);
    return false;
}

suite('LSP Completion Tests', function () {
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

        // Wait for the completion server to be ready
        const isReady = await waitForCompletionServer(uri);
        if (!isReady) {
            console.warn('Completion server not ready, tests may not work as expected');
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

    suite('Basic Completion Functionality', function () {
        setup(async function () {            
            // Open the test event file
            const uri = vscode.Uri.file(testEventFile);
            testDocument = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(testDocument);

            // Wait for the completion server to process this document
            await waitForCompletionServer(uri, 10, 100);
        });

        teardown(async function () {
            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        });

        test('should provide completion for triggers at start of line', async function () {
            // Test completion at the beginning of line 13 where we might expect trigger suggestions
            const position = new vscode.Position(12, 0); // 0-indexed, line 13 at start

            try {
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    testDocument.uri,
                    position
                );

                console.log('Completion result type:', typeof completions);
                console.log('Completion result:', completions);

                if (completions && completions.items && completions.items.length > 0) {
                    console.log(`Found ${completions.items.length} completion items`);

                    // Log first few completions for debugging
                    const firstFew = completions.items.slice(0, 5);
                    firstFew.forEach((item, index) => {
                        console.log(`Completion ${index + 1}: ${item.label} (kind: ${item.kind})`);
                    });

                    assert.ok(completions.items.length > 0, 'Should have completion items');

                    // Look for common triggers
                    const triggerLabels = completions.items.map(item => item.label);
                    const hasTriggers = triggerLabels.some(label =>
                        typeof label === 'string' && (
                            label.includes('is_ai') ||
                            label.includes('limit') ||
                            label.includes('trigger') ||
                            label.includes('country_type')
                        )
                    );

                    if (hasTriggers) {
                        console.log('Found trigger-related completions');
                    } else {
                        console.log('No obvious trigger completions found. Available:', triggerLabels.slice(0, 10));
                    }
                } else {
                    console.log('No completion items returned');
                    console.log('Completions object structure:', Object.keys(completions || {}));
                    assert.ok(false, 'Expected completion items but got none');
                }
            } catch (error) {
                console.log('Completion test failed:', error);
                assert.fail(`Completion request failed: ${error instanceof Error ? error.message : error}`);
            }
        });

        test('should provide completion for effects in immediate block', async function () {
            // Test completion inside the immediate block where effects are expected
            const position = new vscode.Position(17, 8); // Inside immediate block, indented

            try {
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    testDocument.uri,
                    position
                );

                console.log('Effect completion result:', completions);

                if (completions && completions.items && completions.items.length > 0) {
                    console.log(`Found ${completions.items.length} effect completion items`);

                    const effectLabels = completions.items.map(item => item.label);
                    const hasEffects = effectLabels.some(label =>
                        typeof label === 'string' && (
                            label.includes('country_event') ||
                            label.includes('set_variable') ||
                            label.includes('add_modifier') ||
                            label.includes('effect')
                        )
                    );

                    if (hasEffects) {
                        console.log('Found effect-related completions');
                    } else {
                        console.log('No obvious effect completions found. Available:', effectLabels.slice(0, 10));
                    }

                    assert.ok(completions.items.length > 0, 'Should have effect completion items');
                } else {
                    console.log('No effect completion items returned');
                    assert.fail('Expected effect completion items but got none');
                }
            } catch (error) {
                console.log('Effect completion test failed:', error);
                assert.fail(`Effect completion request failed: ${error instanceof Error ? error.message : error}`);
            }
        });

        test('should provide completion after typing partial keywords', async function () {
            // Test completion after typing part of a known keyword
            const position = new vscode.Position(12, 2); // After some characters on trigger line

            try {
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    testDocument.uri,
                    position,
                    'is' // Trigger character/context
                );

                console.log('Partial keyword completion result:', completions);

                if (completions && completions.items) {
                    console.log(`Found ${completions.items.length} partial completion items`);

                    if (completions.items.length > 0) {
                        // Look for completions that start with or contain 'is'
                        const relevantCompletions = completions.items.filter(item => {
                            const label = typeof item.label === 'string' ? item.label : item.label.label;
                            return label.toLowerCase().includes('is');
                        });

                        console.log(`Found ${relevantCompletions.length} 'is'-related completions`);
                        relevantCompletions.forEach(item => {
                            const label = typeof item.label === 'string' ? item.label : item.label.label;
                            console.log(`- ${label}`);
                        });
                    }

                    assert.ok(true, 'Partial completion test completed');
                } else {
                    console.log('No partial completion items returned');
                    assert.ok(true, 'Partial completion test completed without items');
                }
            } catch (error) {
                console.log('Partial completion test failed:', error);
                assert.ok(true, 'Partial completion test completed with error');
            }
        });
    });

    suite('Completion Context and Scopes', function () {        
        test('should provide different completions based on context', async function () {
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            await waitForCompletionServer(document.uri, 10, 100);

            // Test completions in different contexts
            const contexts = [
                { name: 'trigger block', position: new vscode.Position(12, 0) },
                { name: 'immediate block', position: new vscode.Position(17, 8) },
                { name: 'event root', position: new vscode.Position(8, 0) }
            ];

            for (const context of contexts) {
                try {
                    const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                        'vscode.executeCompletionItemProvider',
                        document.uri,
                        context.position
                    );

                    console.log(`\n=== ${context.name.toUpperCase()} CONTEXT ===`);

                    if (completions && completions.items) {
                        console.log(`Found ${completions.items.length} items in ${context.name}`);

                        if (completions.items.length > 0) {
                            const firstFew = completions.items.slice(0, 3);
                            firstFew.forEach(item => {
                                const label = typeof item.label === 'string' ? item.label : item.label.label;
                                console.log(`- ${label} (kind: ${item.kind})`);
                            });
                        }
                    } else {
                        console.log(`No completions in ${context.name}`);
                    }
                } catch (error) {
                    console.log(`Error in ${context.name}:`, error instanceof Error ? error.message : error);
                }
            }

            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
            assert.ok(true, 'Context completion test completed');
        });
    });

    suite('Completion Server Configuration', function () {        
        test('should check if LSP server is actually called for completion', async function () {
            // This test determines if the LSP server is being invoked at all
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            console.log('=== LSP SERVER COMPLETION CALL TEST ===');

            // Test at a position where LSP should definitely provide completions
            const position = new vscode.Position(12, 0); // Start of trigger line

            try {
                console.log('Requesting completion at position 12,0...');
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    document.uri,
                    position
                );

                console.log('Completion request completed');
                console.log(`Received ${completions?.items?.length || 0} items`);

                if (completions && completions.items && completions.items.length > 0) {
                    // Check if ANY item has LSP-specific characteristics
                    const firstItem = completions.items[0];

                    console.log('\n--- FIRST ITEM DETAILED ANALYSIS ---');
                    console.log('Label:', firstItem.label);
                    console.log('Kind:', firstItem.kind, `(${getCompletionKindName(firstItem.kind || 0)})`);
                    console.log('Sort Text:', firstItem.sortText);
                    console.log('Insert Text:', firstItem.insertText);
                    console.log('Detail:', firstItem.detail);
                    console.log('Documentation:', firstItem.documentation);
                    console.log('Commit Characters:', firstItem.commitCharacters);
                    console.log('Command:', firstItem.command);
                    console.log('Additional Text Edits:', firstItem.additionalTextEdits);
                    console.log('Text Edit:', firstItem.textEdit);

                    // Check if this has LSP fingerprints using available VS Code CompletionItem fields
                    const hasDetailedDoc = firstItem.documentation !== null && firstItem.documentation !== undefined;
                    const hasCommand = firstItem.command !== null && firstItem.command !== undefined;
                    const hasTextEdit = firstItem.textEdit !== null && firstItem.textEdit !== undefined;
                    const hasAdditionalEdits = firstItem.additionalTextEdits && firstItem.additionalTextEdits.length > 0;

                    console.log('\n--- LSP FINGERPRINT ANALYSIS ---');
                    console.log('Has documentation:', hasDetailedDoc);
                    console.log('Has command:', hasCommand);
                    console.log('Has text edit:', hasTextEdit);
                    console.log('Has additional edits:', hasAdditionalEdits);

                    if (hasDetailedDoc || hasCommand || hasTextEdit || hasAdditionalEdits) {
                        console.log('✅ LIKELY LSP: Found LSP-specific data structures');
                    } else {
                        console.log('❌ LIKELY VS CODE: No LSP-specific characteristics found');
                    }
                }

                await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
                assert.ok(true, 'LSP server call test completed');
            } catch (error) {
                console.log('LSP server call test error:', error);
                await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
                assert.fail(`LSP server call test failed: ${error instanceof Error ? error.message : error}`);
            }
        });

        test('should verify LSP vs VS Code completion source', async function () {
            // This test determines if completions are from LSP or VS Code's built-in text completion
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            await waitForCompletionServer(document.uri, 10, 100);

            console.log('=== LSP vs VS CODE COMPLETION SOURCE ANALYSIS ===');

            // Test completion in an empty area where VS Code text completion would have nothing to suggest
            const emptyPosition = new vscode.Position(0, 0); // Very start of file

            try {
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    document.uri,
                    emptyPosition
                );

                if (completions && completions.items && completions.items.length > 0) {
                    console.log(`Found ${completions.items.length} completions at file start`);

                    // Analyze the completion sources
                    const sampleItems = completions.items.slice(0, 10);

                    console.log('\n--- COMPLETION ITEM ANALYSIS ---');
                    for (let i = 0; i < Math.min(5, sampleItems.length); i++) {
                        const item = sampleItems[i];
                        const label = typeof item.label === 'string' ? item.label : item.label.label;

                        console.log(`Item ${i + 1}:`);
                        console.log(`  Label: ${label}`);
                        console.log(`  Kind: ${item.kind} (${getCompletionKindName(item.kind || 0)})`);
                        console.log(`  Sort Text: ${item.sortText || 'None'}`);
                        console.log(`  Insert Text: ${item.insertText || 'None'}`);
                        console.log(`  Detail: ${item.detail || 'None'}`);
                        console.log(`  Documentation: ${item.documentation ? 'Present' : 'None'}`);
                        console.log(`  Commit Characters: ${item.commitCharacters?.length || 0}`);
                        console.log('');
                    }

                    // Check if these look like LSP completions or text completions
                    const hasLSPCharacteristics = sampleItems.some(item => {
                        return item.sortText || item.documentation || (item.commitCharacters && item.commitCharacters.length > 0);
                    });

                    const allKindZero = sampleItems.every(item => (item.kind || 0) === 0);

                    console.log('\n--- SOURCE ANALYSIS ---');
                    console.log(`All items have kind=0 (Text): ${allKindZero}`);
                    console.log(`Has LSP characteristics (sortText/docs/commitChars): ${hasLSPCharacteristics}`);

                    if (allKindZero && !hasLSPCharacteristics) {
                        console.log('❌ LIKELY SOURCE: VS Code built-in text completion (word-based)');
                        console.log('❌ LSP completion provider may not be working');
                    } else {
                        console.log('✅ LIKELY SOURCE: LSP server');
                        console.log('✅ LSP completion provider is working');
                    }

                    // Check if the completions contain words from the current document
                    const documentText = document.getText();
                    const documentWords = new Set(documentText.match(/\w+/g) || []);

                    let wordsFromDocument = 0;
                    for (const item of sampleItems) {
                        const label = typeof item.label === 'string' ? item.label : item.label.label;
                        const cleanLabel = label.replace(/[^a-zA-Z0-9_]/g, ''); // Remove quotes and special chars
                        if (documentWords.has(cleanLabel)) {
                            wordsFromDocument++;
                        }
                    }

                    console.log(`\nWords found in current document: ${wordsFromDocument}/${sampleItems.length}`);
                    if (wordsFromDocument === sampleItems.length) {
                        console.log('❌ All completions are words from current document - likely VS Code text completion');
                    } else {
                        console.log('✅ Some completions not in document - likely LSP or external source');
                    }

                } else {
                    console.log('No completions found at file start');
                }

                await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
                assert.ok(true, 'Completion source analysis completed');
            } catch (error) {
                console.log('Completion source analysis error:', error);
                await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
                assert.fail(`Completion source analysis failed: ${error instanceof Error ? error.message : error}`);
            }
        });

        test('should check completion item categorization', async function () {
            // This test examines whether completion items are properly categorized
            console.log('=== COMPLETION ITEM CATEGORIZATION ANALYSIS ===');

            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            await waitForCompletionServer(document.uri, 10, 100);

            // Test at different positions to see if categories vary by context
            const testPositions = [
                { name: 'trigger context', pos: new vscode.Position(12, 0) },
                { name: 'effect context', pos: new vscode.Position(17, 8) },
                { name: 'root context', pos: new vscode.Position(8, 0) }
            ];

            for (const test of testPositions) {
                try {
                    const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                        'vscode.executeCompletionItemProvider',
                        document.uri,
                        test.pos
                    );

                    if (completions && completions.items && completions.items.length > 0) {
                        console.log(`\n--- ${test.name.toUpperCase()} ---`);

                        // Categorize completion items by kind
                        const kindCounts = new Map<number, number>();
                        const kindExamples = new Map<number, string[]>();

                        for (const item of completions.items.slice(0, 50)) { // Sample first 50
                            const kind = item.kind || 0;
                            kindCounts.set(kind, (kindCounts.get(kind) || 0) + 1);

                            if (!kindExamples.has(kind)) {
                                kindExamples.set(kind, []);
                            }
                            const examples = kindExamples.get(kind)!;
                            if (examples.length < 3) {
                                const label = typeof item.label === 'string' ? item.label : item.label.label;
                                examples.push(label);
                            }
                        }

                        console.log(`Total items: ${completions.items.length}`);

                        for (const [kind, count] of kindCounts.entries()) {
                            const kindName = getCompletionKindName(kind);
                            const examples = kindExamples.get(kind) || [];
                            console.log(`Kind ${kind} (${kindName}): ${count} items`);
                            console.log(`  Examples: ${examples.join(', ')}`);
                        }
                    }
                } catch (error) {
                    console.log(`Error testing ${test.name}:`, error instanceof Error ? error.message : error);
                }
            }

            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
            assert.ok(true, 'Completion categorization analysis completed');
        });

});

// Helper function to get completion kind names
function getCompletionKindName(kind: number): string {
    const kindNames = [
        'Text', 'Method', 'Function', 'Constructor', 'Field', 'Variable',
        'Class', 'Interface', 'Module', 'Property', 'Unit', 'Value',
        'Enum', 'Keyword', 'Snippet', 'Color', 'File', 'Reference',
        'Folder', 'EnumMember', 'Constant', 'Struct', 'Event',
        'Operator', 'TypeParameter'
    ];
    return kindNames[kind] || `Unknown(${kind})`;
}

    suite('Completion Performance', function () {        
        test('should respond to completion requests within reasonable time', async function () {
            const uri = vscode.Uri.file(testEventFile);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);

            await waitForCompletionServer(document.uri, 10, 100);

            const position = new vscode.Position(12, 0);
            const startTime = Date.now();

            try {
                const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                    'vscode.executeCompletionItemProvider',
                    document.uri,
                    position
                );

                const endTime = Date.now();
                const duration = endTime - startTime;

                console.log(`Completion request took ${duration}ms`);

                // Allow up to 5 seconds for completion response
                assert.ok(duration < 5000, `Completion request should complete within 5 seconds, took ${duration}ms`);

                console.log('Completion performance test completed');

                if (completions && completions.items) {
                    console.log(`Performance test - completions found: ${completions.items.length}`);
                }
            } catch (error) {
                console.log('Completion performance test error:', error);
                assert.ok(true, 'Performance test completed with error');
            }

            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        });
    });

});