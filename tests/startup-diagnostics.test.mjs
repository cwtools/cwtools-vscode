import assert from 'node:assert/strict';
import { writeFile, rm } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { startServer } from './helpers/lsp-client.mjs';

test('a missing workspace reports a load failure without publishing diagnostics', { timeout: 30000 }, async t => {
    const client = await startServer(t, { noRoot: true });
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {' },
    });
    await client.waitFor(message => message.method === 'window/logMessage'
        && message.params.message.includes('Workspace loading failed'), from);
    assert.equal(client.received.slice(from).some(message =>
        message.method === 'textDocument/publishDiagnostics' && message.params.uri === client.uri), false);
});

test('recovers from invalid rules without another document change', { timeout: 30000 }, async t => {
    const client = await startServer(t);
    const invalidRule = path.join(client.settings.rules_folder, 'invalid.cwt');
    await writeFile(invalidRule, '## severity = impossible\ntest = scalar\n');
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {' },
    });
    await client.waitFor(message => message.method === 'window/logMessage'
        && message.params.message.includes('Workspace loading failed'), from);
    assert.equal(client.received.slice(from).some(message =>
        message.method === 'textDocument/publishDiagnostics' && message.params.uri === client.uri), false);
    await rm(invalidRule);
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    const diagnostic = await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
        && message.params.uri === client.uri, from);
    assert.ok(diagnostic.message.params.diagnostics.some(item => item.code === 'CW001'));
});

test('reconfiguration rechecks the open buffer without another document change', { timeout: 30000 }, async t => {
    const client = await startServer(t);
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {' },
    });
    await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
        && message.params.uri === client.uri && message.params.diagnostics.some(item => item.code === 'CW001'));
    const reloadAt = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
        && message.params.uri === client.uri && message.params.diagnostics.some(item => item.code === 'CW001'), reloadAt);
});

test('waits for consecutive reloads and uses the latest buffer', { timeout: 30000 }, async t => {
    const client = await startServer(t);
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {}' },
    });
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didChange', {
        textDocument: { uri: client.uri, version: 2 }, contentChanges: [{ text: 'country_event = {' }],
    });
    const diagnostic = await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
        && message.params.uri === client.uri, from);
    const completed = client.received.slice(from, diagnostic.index).filter(message =>
        message.method === 'loadingBar' && message.params.enable === false);
    assert.equal(completed.length, 2, 'Diagnostics did not wait for both reloads');
    assert.ok(diagnostic.message.params.diagnostics.some(item => item.code === 'CW001'));
});

test('does not diagnose a discarded buffer after it closes during loading', { timeout: 30000 }, async t => {
    const client = await startServer(t);
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {' },
    });
    client.send('textDocument/didClose', { textDocument: { uri: client.uri } });
    const diagnostic = await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
        && message.params.uri === client.uri, from);
    assert.deepEqual(diagnostic.message.params.diagnostics, []);
});

for (const languageId of ['eu4', 'plaintext']) {
    test(`waits for workspace loading before diagnosing ${languageId}`, { timeout: 30000 }, async t => {
        const client = await startServer(t);
        const from = client.received.length;
        client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
        client.send('textDocument/didOpen', {
            textDocument: { uri: client.uri, languageId, version: 1, text: 'country_event = {' },
        });
        const diagnostic = await client.waitFor(message =>
            message.method === 'textDocument/publishDiagnostics' && message.params.uri === client.uri, from);
        const loaded = client.received.findIndex((message, i) =>
            i >= from && message.method === 'loadingBar' && message.params.enable === false);
        assert.ok(loaded !== -1 && loaded < diagnostic.index, 'Diagnostics arrived before workspace loading completed');
        assert.ok(diagnostic.message.params.diagnostics.some(item => item.code === 'CW001'));
        const changedAt = client.received.length;
        client.send('textDocument/didChange', {
            textDocument: { uri: client.uri, version: 2 },
            contentChanges: [{ text: 'country_event = { id = startup.1 hidden = yes }' }],
        });
        await client.waitFor(message => message.method === 'textDocument/publishDiagnostics'
            && message.params.uri === client.uri && message.params.diagnostics.length === 0, changedAt);
    });
}
