import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once, EventEmitter } from 'node:events';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const server = process.env.CWTOOLS_SERVER
    ? path.resolve(process.env.CWTOOLS_SERVER)
    : fileURLToPath(new URL('../../artifacts/bin/Main/release/CWTools Server.dll', import.meta.url));

export async function startServer(t, { noRoot = false, initialize = true } = {}) {
    const root = await mkdtemp(path.join(os.tmpdir(), 'cwtools-startup-'));
    const events = path.join(root, 'events');
    const rules = path.join(root, 'rules');
    await mkdir(events);
    await mkdir(rules);
    // Keep loading busy while the client opens a document, without game data or downloads.
    for (let i = 0; i < 128; i++) {
        const contents = Array.from({ length: 64 }, (_, n) =>
            `country_event = { id = startup_${i}.${n} hidden = yes }`).join('\n');
        await writeFile(path.join(events, `fixture_${i}.txt`), contents);
    }
    const file = path.join(events, 'opened.txt');
    await writeFile(file, 'country_event = { id = startup.1 hidden = yes }');
    const child = spawn('dotnet', [server], { cwd: root, stdio: 'pipe' });
    const closed = once(child, 'close');
    let stopped = false;
    child.once('close', () => { stopped = true; });
    t.after(async () => {
        if (!stopped) child.kill();
        await closed;
        await rm(root, { recursive: true, force: true });
    });
    const eventsReceived = new EventEmitter();
    const received = [];
    let buffer = Buffer.alloc(0);
    let failure;
    let stderr = '';
    child.stderr.on('data', chunk => { stderr += chunk.toString(); });
    function fail(error) {
        failure = error;
        eventsReceived.emit('message');
    }
    child.on('error', fail);
    child.stdin.on('error', fail);
    child.on('close', () => fail(new Error(`Server closed before expected message: ${stderr}`)));
    child.stdout.on('data', chunk => {
        try {
            buffer = Buffer.concat([buffer, chunk]);
            while (true) {
                const separator = buffer.indexOf('\r\n\r\n');
                if (separator === -1) break;
                const header = buffer.subarray(0, separator).toString('ascii');
                const match = /^Content-Length:\s*(\d+)$/im.exec(header);
                assert.ok(match, 'Missing Content-Length');
                const length = Number(match[1]);
                assert.ok(length <= 32 * 1024 * 1024, 'Oversized server message');
                const end = separator + 4 + length;
                if (buffer.length < end) break;
                received.push(JSON.parse(buffer.subarray(separator + 4, end).toString('utf8')));
                buffer = buffer.subarray(end);
                eventsReceived.emit('message');
            }
        } catch (error) {
            fail(error);
        }
    });
    function sendMessage(message) {
        const body = Buffer.from(JSON.stringify({ jsonrpc: '2.0', ...message }));
        child.stdin.write(Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`), body]));
    }
    function send(method, params, id) {
        sendMessage({ method, params, ...(id === undefined ? {} : { id }) });
    }
    function reply(id, result) {
        sendMessage({ id, result });
    }
    function waitFor(predicate, from = 0) {
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => finish(new Error(`LSP message timed out: ${stderr}`)), 20000);
            function finish(error, value) {
                clearTimeout(timer);
                eventsReceived.off('message', check);
                if (error) reject(error);
                else resolve(value);
            }
            function check() {
                const index = received.findIndex((message, i) => i >= from && predicate(message));
                if (index !== -1) finish(null, { message: received[index], index });
                else if (failure) finish(failure);
            }
            eventsReceived.on('message', check);
            check();
        });
    }
    const initialization = {
        processId: process.pid,
        rootUri: noRoot ? null : pathToFileURL(root).href,
        capabilities: {},
        initializationOptions: {
            language: 'eu4', isVanillaFolder: true, rulesCache: null,
            repoPath: null, rules_version: 'manual', diagnosticLogging: false,
        },
    };
    const settings = {
        localisation: { languages: ['english'], generated_strings: '' },
        errors: { vanilla: false, ignore: [], ignorefiles: [] },
        experimental: false, debug_mode: false, ignore_patterns: [],
        trace: { server: 'off' }, maxFileSize: 2, rules_folder: rules,
        cache: Object.fromEntries(['eu4', 'stellaris', 'hoi4', 'ck2', 'imperator', 'vic2', 'ck3', 'vic3', 'eu5'].map(game => [game, ''])),
    };
    if (initialize) {
        send('initialize', initialization, 1);
        await waitFor(message => message.id === 1);
        send('initialized', {});
    }
    return { send, reply, waitFor, received, settings, initialization, root, child, closed, uri: pathToFileURL(file).href };
}
