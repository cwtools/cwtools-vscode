import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { startServer } from './helpers/lsp-client.mjs';

async function fixture(t, { output, capabilities = {} } = {}) {
    const client = await startServer(t, { initialize: false });
    const types = path.join(client.root, 'common', 'fixture_items');
    await mkdir(types, { recursive: true });
    await mkdir(path.join(client.root, 'localisation'));
    await writeFile(path.join(client.root, 'localisation', 'blank_l_english.yml'), '\uFEFFl_english:\n');
    await writeFile(path.join(client.settings.rules_folder, 'fixture.cwt'), `types = {
 type[fixture_item] = {
  path = "game/common/fixture_items"
  localisation = { ## required
   name = "$"
  }
 }
 type[event] = { path = "game/events" }
}
event = { item = <fixture_item> }
`);
    const file = path.join(types, 'items.txt');
    const uri = pathToFileURL(file).href;
    const text = 'fixture_one = {}\n';
    await writeFile(file, text);
    await writeFile(path.join(client.root, 'events', 'fixture.txt'), 'event = { item = fixture_one }\n');
    client.initialization.capabilities = capabilities;
    client.settings.localisation.generated_strings = ':0 "REPLACE_ME"';
    client.initialization.initializationOptions.cwtools = client.settings;
    if (output !== undefined) client.initialization.initializationOptions.localisationOutput = output;
    client.send('initialize', client.initialization, 1);
    const initialized = await client.waitFor(message => message.id === 1);
    assert.ok(initialized.message.result?.capabilities, JSON.stringify(initialized.message));
    client.send('initialized', {});
    const from = client.received.length;
    client.send('textDocument/didOpen', { textDocument: { uri, languageId: 'plaintext', version: 1, text } });
    const { message: published } = await client.waitFor(message => message.method === 'textDocument/publishDiagnostics' && message.params.uri === uri && message.params.diagnostics.some(d => d.code === 'CW100'), from);
    const diagnostic = published.params.diagnostics.find(d => d.code === 'CW100');
    client.send('textDocument/codeAction', {
        textDocument: { uri }, range: diagnostic.range,
        context: { diagnostics: published.params.diagnostics },
    }, 2);
    const { message: actions } = await client.waitFor(message => message.id === 2);
    assert.ok(actions.result.some(action => action.command === 'genlocfile'), JSON.stringify(actions));
    return { client, file, uri, actions: actions.result };
}

const editCapabilities = {
    workspace: { applyEdit: true, workspaceEdit: { documentChanges: true, resourceOperations: ['create'] } },
};

function command(client, id, name = 'genlocall', args = []) {
    const from = client.received.length;
    client.send('workspace/executeCommand', { command: name, arguments: args }, id);
    return from;
}

test('keeps the VS Code virtual-file output by default', { timeout: 30000 }, async t => {
    const { client, actions } = await fixture(t);
    const from = command(client, 3);
    const { message } = await client.waitFor(message => message.method === 'createVirtualFile' && message.params.uri === 'cwtools://1', from);
    assert.ok(message.params.fileContent.includes('fixture_one'), JSON.stringify(message));
    assert.ok(client.received.slice(from).every(event => event.method !== 'workspace/applyEdit'));
    const action = actions.find(item => item.command === 'genlocfile');
    const actionFrom = command(client, 4, action.command, action.arguments);
    const { message: fileOutput } = await client.waitFor(message => message.method === 'createVirtualFile' && message.params.uri === 'cwtools://1', actionFrom);
    assert.ok(fileOutput.params.fileContent.includes('fixture_one'), JSON.stringify(fileOutput));
});

test('generates a new localisation file through an opted-in workspace edit', { timeout: 30000 }, async t => {
    const { client, actions } = await fixture(t, { output: 'workspaceEdit', capabilities: editCapabilities });
    const destination = path.join(client.root, 'localisation', 'cwtools_generated_l_english.yml');
    const from = command(client, 3);
    const { message: apply } = await client.waitFor(message => message.method === 'workspace/applyEdit', from);
    const changes = apply.params.edit.documentChanges;
    assert.deepEqual(changes[0], { kind: 'create', uri: pathToFileURL(destination).href, options: { overwrite: false, ignoreIfExists: false } });
    assert.equal(changes[1].textDocument.uri, pathToFileURL(destination).href);
    assert.equal(changes[1].textDocument.version, null);
    assert.match(changes[1].edits[0].newText, /^\uFEFFl_english:\r?\n/);
    assert.ok(changes[1].edits[0].newText.includes('fixture_one:0'));
    client.reply(apply.id, { applied: true });
    await client.waitFor(message => message.id === 3, from);
    assert.ok(client.received.slice(from).every(message => message.method !== 'createVirtualFile'));
    await assert.rejects(readFile(destination), { code: 'ENOENT' });
    await writeFile(destination, 'existing\n');
    const blockedFrom = command(client, 4);
    const { message: warning } = await client.waitFor(message => message.method === 'window/showMessage', blockedFrom);
    assert.equal(warning.params.type, 2);
    await client.waitFor(message => message.id === 4, blockedFrom);
    assert.ok(client.received.slice(blockedFrom).every(message => message.method !== 'workspace/applyEdit'));
    assert.equal(await readFile(destination, 'utf8'), 'existing\n');

    const action = actions.find(item => item.command === 'genlocfile');
    const actionFrom = command(client, 5, action.command, action.arguments);
    const { message: fileEdit } = await client.waitFor(message => message.method === 'workspace/applyEdit', actionFrom);
    assert.equal(fileEdit.params.edit.documentChanges[0].uri,
        pathToFileURL(path.join(client.root, 'localisation', 'cwtools_items_l_english.yml')).href);
    assert.ok(fileEdit.params.edit.documentChanges[1].edits[0].newText.includes('fixture_one:0'));
    client.reply(fileEdit.id, { applied: true });
    await client.waitFor(message => message.id === 5, actionFrom);
});

test('refuses unsupported edit capabilities without falling back to virtual output', { timeout: 30000 }, async t => {
    const { client } = await fixture(t, {
        output: 'workspaceEdit', capabilities: { workspace: { applyEdit: true, workspaceEdit: { documentChanges: true } } },
    });
    const from = command(client, 3);
    const { message: warning } = await client.waitFor(message => message.method === 'window/showMessage', from);
    assert.equal(warning.params.type, 2);
    await client.waitFor(message => message.id === 3, from);
    assert.ok(client.received.slice(from).every(message => message.method !== 'workspace/applyEdit' && message.method !== 'createVirtualFile'));
});

test('reports a rejected edit and stays responsive', { timeout: 30000 }, async t => {
    const { client } = await fixture(t, { output: 'workspaceEdit', capabilities: editCapabilities });
    const from = command(client, 3);
    const { message: apply } = await client.waitFor(message => message.method === 'workspace/applyEdit', from);
    client.reply(apply.id, { applied: false, failureReason: 'Rejected by client' });
    const { message: warning } = await client.waitFor(message => message.method === 'window/showMessage', from);
    assert.match(warning.params.message, /Rejected by client/);
    await client.waitFor(message => message.id === 3, from);
    client.send('textDocument/documentSymbol', { textDocument: { uri: client.uri } }, 4);
    await client.waitFor(message => message.id === 4, from);
});
