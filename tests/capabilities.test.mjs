import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { startServer } from './helpers/lsp-client.mjs';

test('resolves a typed reference across files and offers the declared type', { timeout: 30000 }, async t => {
    const client = await startServer(t, { initialize: false });
    const types = path.join(client.root, 'common', 'fixture_items');
    const localisation = path.join(client.root, 'localisation');
    await mkdir(types, { recursive: true });
    await mkdir(localisation);
    const definition = path.join(types, 'items.txt');
    const usage = path.join(client.root, 'events', 'fixture.txt');
    const definitionUri = pathToFileURL(definition).href;
    const usageUri = pathToFileURL(usage).href;
    await writeFile(path.join(client.settings.rules_folder, 'fixture.cwt'), `types = {
 type[fixture_item] = {
  path = "common/fixture_items"
  localisation = { name = "$" }
 }
 type[event] = { path = "events" }
}
event = { item = <fixture_item> }
`);
    await writeFile(definition, 'fixture_one = {}\n');
    await writeFile(usage, 'event = {\n item = fixture_one\n}\n');
    await writeFile(path.join(localisation, 'fixture_l_english.yml'), '\uFEFFl_english:\n fixture_one:0 "Fixture one"\n');
    client.initialization.initializationOptions.cwtools = client.settings;
    client.send('initialize', client.initialization, 1);
    const initialized = await client.waitFor(message => message.id === 1);
    assert.ok(initialized.message.result?.capabilities, JSON.stringify(initialized.message));
    client.send('initialized', {});
    const from = client.received.length;
    client.send('textDocument/didOpen', {
        textDocument: { uri: usageUri, languageId: 'plaintext', version: 1, text: 'event = {\n item = fixture_one\n}\n' },
    });
    await client.waitFor(message => message.method === 'textDocument/publishDiagnostics' && message.params.uri === usageUri, from);
    const request = async (method, params, id) => {
        client.send(method, params, id);
        const { message } = await client.waitFor(message => message.id === id);
        assert.equal(message.error, undefined, JSON.stringify(message));
        return message.result;
    };
    const symbols = await request('textDocument/documentSymbol', { textDocument: { uri: definitionUri } }, 2);
    assert.ok(symbols.some(symbol => symbol.name === 'fixture_one'), JSON.stringify(symbols));
    const point = { textDocument: { uri: usageUri }, position: { line: 1, character: 12 } };
    const definitions = await request('textDocument/definition', point, 3);
    assert.ok(definitions.some(location => location.uri === definitionUri && location.range.start.line === 0), JSON.stringify(definitions));
    const references = await request('textDocument/references', { ...point, context: { includeDeclaration: true } }, 4);
    assert.equal(references.length, 2, JSON.stringify(references));
    assert.ok(references.some(location => location.uri === usageUri && location.range.start.line === 1), JSON.stringify(references));
    assert.ok(references.some(location => location.uri === definitionUri && location.range.start.line === 0), JSON.stringify(references));
    const usages = await request('textDocument/references', { ...point, context: { includeDeclaration: false } }, 8);
    assert.equal(usages.length, 1, JSON.stringify(usages));
    assert.equal(usages[0].uri, usageUri);
    const completions = await request('textDocument/completion', {
        textDocument: { uri: usageUri }, position: { line: 1, character: 8 }, context: { triggerKind: 1 },
    }, 5);
    assert.ok(completions?.items?.some(item => item.label === 'fixture_one'), JSON.stringify(completions));
    const hover = await request('textDocument/hover', point, 6);
    assert.ok(JSON.stringify(hover).includes('Fixture one'), JSON.stringify(hover));
});
