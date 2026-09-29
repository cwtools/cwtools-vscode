import assert from 'node:assert/strict';
import test from 'node:test';
import { startServer } from './helpers/lsp-client.mjs';

async function shutdown(client) {
    client.send('shutdown', null, 2);
    const { message } = await client.waitFor(message => message.id === 2);
    assert.equal(message.result, null);
}

test('shutdown followed by exit returns success', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    await shutdown(client);
    client.send('exit');
    assert.deepEqual(await client.closed, [0, null]);
});

test('exit without shutdown returns failure', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    client.send('exit');
    assert.deepEqual(await client.closed, [1, null]);
});

test('EOF without shutdown terminates with failure', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    client.child.stdin.end();
    assert.deepEqual(await client.closed, [1, null]);
});

test('EOF after shutdown terminates with success', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    await shutdown(client);
    client.child.stdin.end();
    assert.deepEqual(await client.closed, [0, null]);
});

test('truncated input terminates rather than waiting on an empty queue', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    client.child.stdin.end('Content-Length: 100\r\n\r\n{');
    assert.deepEqual(await client.closed, [1, null]);
});

test('truncated input after shutdown still returns failure', { timeout: 10000 }, async t => {
    const client = await startServer(t);
    await shutdown(client);
    client.child.stdin.end('Content-Length: 100\r\n\r\n{');
    assert.deepEqual(await client.closed, [1, null]);
});
