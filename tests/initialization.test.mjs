import assert from 'node:assert/strict';
import { rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { startServer } from './helpers/lsp-client.mjs';

async function createClient(t) {
    const client = await startServer(t, { initialize: false });
    client.initialization.initializationOptions.cwtools = client.settings;
    return client;
}

async function initialize(client, id = 1) {
    client.send('initialize', client.initialization, id);
    return (await client.waitFor(message => message.id === id)).message;
}

function completedLoads(client) {
    return client.received.filter(message => message.method === 'loadingBar' && message.params.enable === false).length;
}

async function finishInitialization(client) {
    client.send('initialized', {});
    client.send('textDocument/documentSymbol', { textDocument: { uri: client.uri } }, 99);
    await client.waitFor(message => message.id === 99);
    assert.equal(completedLoads(client), 1);
}

function openBrokenDocument(client) {
    client.send('textDocument/didOpen', {
        textDocument: { uri: client.uri, languageId: 'plaintext', version: 1, text: 'country_event = {' },
    });
}

const isDiagnostic = client => message => message.method === 'textDocument/publishDiagnostics'
    && message.params.uri === client.uri;

test('initialization loads the workspace and defers its results until initialized', { timeout: 30000 }, async t => {
    const client = await createClient(t);
    await writeFile(fileURLToPath(client.uri), 'country_event = {');
    const response = await initialize(client);
    assert.ok(response.result?.capabilities);
    assert.ok(client.received.every(message => message.id !== undefined
        || ['window/logMessage', 'window/showMessage', 'telemetry/event'].includes(message.method)));
    client.send('initialized', {});
    // The response fences processing of initialized; it is not a symbol capability assertion.
    client.send('textDocument/documentSymbol', { textDocument: { uri: client.uri } }, 2);
    await client.waitFor(message => message.id === 2);
    assert.equal(completedLoads(client), 1);
    const diagnostic = client.received.find(isDiagnostic(client));
    assert.ok(diagnostic?.params.diagnostics.some(item => item.code === 'CW001'));
});

test('skips one identical initial configuration but permits later reloads', { timeout: 30000 }, async t => {
    const client = await createClient(t);
    assert.ok((await initialize(client)).result?.capabilities);
    await finishInitialization(client);
    const reordered = Object.fromEntries(Object.entries(client.settings).reverse());
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: reordered } });
    openBrokenDocument(client);
    const first = await client.waitFor(isDiagnostic(client));
    assert.ok(first.message.params.diagnostics.some(item => item.code === 'CW001'));
    assert.equal(completedLoads(client), 1);
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: reordered } });
    const reloaded = await client.waitFor(isDiagnostic(client), from);
    assert.ok(reloaded.message.params.diagnostics.some(item => item.code === 'CW001'));
    assert.equal(completedLoads(client), 2);
});

test('a different first configuration still reloads the workspace', { timeout: 30000 }, async t => {
    const client = await createClient(t);
    assert.ok((await initialize(client)).result?.capabilities);
    await finishInitialization(client);
    client.send('workspace/didChangeConfiguration', {
        settings: { cwtools: { ...client.settings, errors: { ...client.settings.errors, ignore: ['CW001'] } } },
    });
    openBrokenDocument(client);
    const diagnostic = await client.waitFor(isDiagnostic(client));
    assert.deepEqual(diagnostic.message.params.diagnostics, []);
    assert.equal(completedLoads(client), 2);
});

test('failed initialization returns an error and can be retried after repairing rules', { timeout: 30000 }, async t => {
    const client = await createClient(t);
    const invalidRule = path.join(client.settings.rules_folder, 'invalid.cwt');
    await writeFile(invalidRule, '## severity = impossible\ntest = scalar\n');
    const failed = await initialize(client);
    assert.equal(failed.error?.code, -32603);
    assert.equal(failed.result, undefined);
    assert.equal(client.received.some(isDiagnostic(client)), false);
    await rm(invalidRule);
    assert.ok((await initialize(client, 2)).result?.capabilities);
    client.send('initialized', {});
    openBrokenDocument(client);
    const diagnostic = await client.waitFor(isDiagnostic(client));
    assert.ok(diagnostic.message.params.diagnostics.some(item => item.code === 'CW001'));
});

test('server responses and notifications include the JSON-RPC version', { timeout: 30000 }, async t => {
    const client = await startServer(t);
    assert.ok(client.received.length > 0);
    assert.ok(client.received.every(message => message.jsonrpc === '2.0'));
});
