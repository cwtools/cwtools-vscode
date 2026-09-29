import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { startServer } from './helpers/lsp-client.mjs';

for (const [languageId, bom] of [['yaml', false], ['plaintext', false], ['yaml', true], ['plaintext', true]]) {
    test(`diagnoses unsaved localisation changes with ${languageId}${bom ? ' and BOM' : ''}`, { timeout: 30000 }, async t => {
        const client = await startServer(t, { initialize: false });
        const folder = path.join(client.root, 'localisation');
        await mkdir(folder);
        const file = path.join(folder, 'fixture_l_english.yml');
        const uri = pathToFileURL(file).href;
        const valid = `${bom ? '\uFEFF' : ''}l_english:\n fixture_key:0 "Fixture text"\n`;
        const invalid = `${bom ? '\uFEFF' : ''}l_english:\n fixture_key:0 "Unclosed text\n`;
        const diskText = `\uFEFF${valid.replace(/^\uFEFF/, '')}`;
        await writeFile(file, diskText);
        client.initialization.initializationOptions.cwtools = client.settings;
        client.send('initialize', client.initialization, 1);
        await client.waitFor(message => message.id === 1);
        client.send('initialized', {});
        const from = client.received.length;
        client.send('textDocument/didOpen', {
            textDocument: { uri, languageId, version: 1, text: invalid },
        });
        const isDiagnostic = message => message.method === 'textDocument/publishDiagnostics' && message.params.uri === uri;
        const first = await client.waitFor(isDiagnostic, from);
        assert.ok(first.message.params.diagnostics.some(item => item.code === 'CW268'), JSON.stringify(first.message.params.diagnostics));
        for (const [version, text, broken] of [[2, valid, false], [3, invalid, true], [4, valid, false]]) {
            const from = client.received.length;
            client.send('textDocument/didChange', {
                textDocument: { uri, version }, contentChanges: [{ text }],
            });
            await client.waitFor(message => isDiagnostic(message)
                && (broken ? message.params.diagnostics.some(item => item.code === 'CW268') : message.params.diagnostics.length === 0), from);
        }
        assert.equal(await readFile(file, 'utf8'), diskText, 'The test must not save the changed buffer');
    });
}
