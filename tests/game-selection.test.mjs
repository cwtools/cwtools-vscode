import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { startServer } from './helpers/lsp-client.mjs';

function diagnosticFor(uri) {
    return message => message.method === 'textDocument/publishDiagnostics' && message.params.uri === uri;
}

async function initialize(client) {
    client.initialization.initializationOptions.cwtools = client.settings;
    client.send('initialize', client.initialization, 1);
    return (await client.waitFor(message => message.id === 1)).message;
}

for (const game of ['eu4', 'hoi4']) {
    test(`${game} selects its own folders for the same script and localisation files`, { timeout: 40000 }, async t => {
        const client = await startServer(t, { initialize: false });
        const missions = path.join(client.root, 'missions');
        const localisation = path.join(client.root, 'localisation');
        await mkdir(missions);
        await mkdir(localisation);
        const missionUri = pathToFileURL(path.join(missions, 'probe.txt')).href;
        const localisationUri = pathToFileURL(path.join(localisation, 'probe_l_english.yml')).href;
        const brokenScript = 'country_event = { id = probe.1';
        const validScript = 'country_event = { id = probe.1 hidden = yes }';
        const validLocalisation = 'l_english:\n fixture_key:0 "Fixture text"\n';
        await writeFile(path.join(missions, 'probe.txt'), brokenScript);
        await writeFile(path.join(client.root, 'events', 'opened.txt'), brokenScript);
        await writeFile(path.join(localisation, 'probe_l_english.yml'), validLocalisation);
        client.initialization.initializationOptions.language = game;
        client.initialization.initializationOptions.isVanillaFolder = false;
        client.initialization.initializationOptions.rulesCache = path.join(client.root, 'rules-cache');
        const from = client.received.length;
        assert.ok((await initialize(client)).result?.capabilities);
        client.send('initialized', {});
        client.send('textDocument/documentSymbol', { textDocument: { uri: client.uri } }, 2);
        await client.waitFor(message => message.id === 2);
        await client.waitFor(message => message.method === 'loadingBar' && message.params.enable === false, from);
        const prompt = await client.waitFor(message => message.method === 'promptVanillaPath', from);
        assert.equal(prompt.message.params, game);
        await client.waitFor(message => diagnosticFor(client.uri)(message)
            && message.params.diagnostics.some(item => item.code === 'CW001'), from);
        if (game === 'eu4') {
            await client.waitFor(message => diagnosticFor(missionUri)(message)
                && message.params.diagnostics.some(item => item.code === 'CW001'), from);
        } else {
            // The load-complete fence makes the absent mission diagnostic observable.
            assert.equal(client.received.slice(from).filter(diagnosticFor(missionUri)).length, 0,
                'HOI4 should not load EU4 missions');
        }

        const scriptUri = game === 'eu4' ? missionUri : client.uri;
        const changedAt = client.received.length;
        client.send('textDocument/didOpen', {
            textDocument: { uri: scriptUri, languageId: 'plaintext', version: 1, text: brokenScript },
        });
        await client.waitFor(message => diagnosticFor(scriptUri)(message)
            && message.params.diagnostics.some(item => item.code === 'CW001'), changedAt);
        const correctedAt = client.received.length;
        client.send('textDocument/didChange', {
            textDocument: { uri: scriptUri, version: 2 }, contentChanges: [{ text: validScript }],
        });
        await client.waitFor(message => diagnosticFor(scriptUri)(message)
            && message.params.diagnostics.length === 0, correctedAt);

        const brokenLocalisation = 'l_english:\n fixture_key:0 "Unclosed text\n';
        const openedAt = client.received.length;
        client.send('textDocument/didOpen', {
            textDocument: { uri: localisationUri, languageId: 'yaml', version: 1, text: brokenLocalisation },
        });
        await client.waitFor(message => diagnosticFor(localisationUri)(message)
            && message.params.diagnostics.some(item => item.code === 'CW268'), openedAt);
        const fixedAt = client.received.length;
        client.send('textDocument/didChange', {
            textDocument: { uri: localisationUri, version: 2 }, contentChanges: [{ text: validLocalisation }],
        });
        await client.waitFor(message => diagnosticFor(localisationUri)(message)
            && message.params.diagnostics.length === 0, fixedAt);
    });
}

for (const language of ['unknown-game', 'EU4', null, 42]) {
    test(`an unrecognized game ${JSON.stringify(language)} retains the Stellaris fallback`, { timeout: 30000 }, async t => {
        const client = await startServer(t, { initialize: false });
        client.initialization.initializationOptions.language = language;
        client.initialization.initializationOptions.isVanillaFolder = false;
        client.initialization.initializationOptions.rulesCache = path.join(client.root, 'rules-cache');
        const from = client.received.length;
        assert.ok((await initialize(client)).result?.capabilities);
        client.send('initialized', {});
        const prompt = await client.waitFor(message => message.method === 'promptVanillaPath', from);
        assert.equal(prompt.message.params, 'stellaris');
    });
}

test('a present initializationOptions object requires the language property', { timeout: 30000 }, async t => {
    const client = await startServer(t, { initialize: false });
    delete client.initialization.initializationOptions.language;
    const response = await initialize(client);
    assert.equal(response.error?.code, -32603);
    assert.match(response.error?.message ?? '', /language/);
    assert.equal(response.result, undefined);
});

test('no initializationOptions waits for later configuration before loading', { timeout: 30000 }, async t => {
    const client = await startServer(t, { initialize: false });
    delete client.initialization.initializationOptions;
    client.send('initialize', client.initialization, 1);
    assert.ok((await client.waitFor(message => message.id === 1)).message.result?.capabilities);
    assert.equal(client.received.some(message => message.method === 'loadingBar'), false);
    client.send('initialized', {});
    const from = client.received.length;
    client.send('workspace/didChangeConfiguration', { settings: { cwtools: client.settings } });
    await client.waitFor(message => message.method === 'loadingBar'
        && message.params.enable === false, from);
});

test('the native eu5 identifier selects the EU5 loader', { timeout: 30000 }, async t => {
    const client = await startServer(t, { initialize: false });
    client.initialization.initializationOptions.language = 'eu5';
    client.initialization.initializationOptions.isVanillaFolder = false;
    client.initialization.initializationOptions.rulesCache = path.join(client.root, 'rules-cache');
    const from = client.received.length;
    assert.ok((await initialize(client)).result?.capabilities);
    client.send('initialized', {});
    const prompt = await client.waitFor(message => message.method === 'promptVanillaPath', from);
    assert.equal(prompt.message.params, 'eu5');
});

test('incomplete initial settings fail instead of silently loading a different game', { timeout: 30000 }, async t => {
    const client = await startServer(t, { initialize: false });
    delete client.settings.localisation.generated_strings;
    const response = await initialize(client);
    assert.equal(response.error?.code, -32603);
    assert.equal(response.result, undefined);
});
