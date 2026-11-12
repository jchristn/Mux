import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

// Loads the server lifecycle against fakes of the VS Code API, child_process, fetch, and the shared settings file,
// to check that connecting starts the tray agent with `mux agent start` and uses the agent's server.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const Module = require('node:module');

interface Fakes {
    execCalls: string[][];
    spawnCalls: string[][];
    execExit: number;
    execStdout: string;
    healthy: () => boolean;
}

function loadLifecycle(fakes: Fakes, settings: Record<string, unknown>): any {
    const fakeVscode = {
        EventEmitter: class {
            public event = (): void => undefined;
            public fire(): void { /* no listeners in tests */ }
            public dispose(): void { /* nothing to release */ }
        },
        workspace: { getConfiguration: () => ({ get: (key: string, fallback: unknown) => (key in settings ? settings[key] : fallback) }) },
        env: { language: 'en' },
    };
    const fakeChildProcess = {
        execFile: (file: string, args: string[], _options: unknown, callback: (error: any, stdout: string) => void) => {
            fakes.execCalls.push([file, ...args]);
            const error = fakes.execExit === 0 ? null : Object.assign(new Error('exit ' + fakes.execExit), { code: fakes.execExit });
            setImmediate(() => callback(error, fakes.execStdout));
        },
        spawn: (file: string, args: string[]) => {
            fakes.spawnCalls.push([file, ...args]);
            const handlers: Record<string, () => void> = {};
            setImmediate(() => handlers.spawn?.());
            return { on: (event: string, handler: () => void) => { handlers[event] = handler; } };
        },
    };
    const original = Module._load;
    Module._load = function (request: string, ...rest: unknown[]) {
        if (request === 'vscode') return fakeVscode;
        if (request === 'child_process') return fakeChildProcess;
        return original.call(this, request, ...rest);
    };
    try {
        // Drop every extension module from the cache so each load binds to this test's fakes.
        const sourceRoot = path.dirname(path.dirname(require.resolve('../../src/server/lifecycle')));
        for (const key of Object.keys(require.cache)) {
            if (key.startsWith(sourceRoot + path.sep)) delete require.cache[key];
        }
        return require(require.resolve('../../src/server/lifecycle'));
    } finally {
        Module._load = original;
    }
}

function withSharedSettings(apiKey: string, body: () => Promise<void>): Promise<void> {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mux-vscode-agent-'));
    fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify({ rest: { port: 8799, apiKey } }));
    const previous = process.env.MUX_CONFIG_DIR;
    process.env.MUX_CONFIG_DIR = dir;
    return body().finally(() => {
        if (previous === undefined) delete process.env.MUX_CONFIG_DIR; else process.env.MUX_CONFIG_DIR = previous;
        fs.rmSync(dir, { recursive: true, force: true });
    });
}

function fakeFetch(fakes: Fakes): typeof fetch {
    return (async (input: any) => {
        const url = String(input);
        if (!fakes.healthy()) throw new Error('connection refused');
        if (url.endsWith('/v1.0/api/health')) {
            return new Response(JSON.stringify({ Status: 'healthy', Version: '1.7.0', ContractVersion: '1.0' }), { status: 200 });
        }
        return new Response('[]', { status: 200 });
    }) as typeof fetch;
}

function fakeContext(): any {
    const secrets = new Map<string, string>();
    return { secrets: { get: async (k: string) => secrets.get(k), store: async (k: string, v: string) => { secrets.set(k, v); } } };
}

const token = { isCancellationRequested: false, onCancellationRequested: () => ({ dispose: () => undefined }) };

test('agentStartOutcome reads the mux agent start exit code and message', () => {
    const { agentStartOutcome } = loadLifecycle({ execCalls: [], spawnCalls: [], execExit: 0, execStdout: '', healthy: () => true }, {});
    assert.equal(agentStartOutcome(0, 'Started the mux agent: /Applications/Mux Agent.app/Contents/MacOS/Mux.Agent\n'), 'started');
    assert.equal(agentStartOutcome(0, 'The mux agent is already running.\n'), 'running');
    assert.equal(agentStartOutcome(3, ''), 'unavailable', 'not installed');
    assert.equal(agentStartOutcome(-1, ''), 'unavailable', 'spawn failure or timeout');
});

test('connecting starts the tray agent and uses its server instead of mux serve', async () => {
    let calls = 0;
    const fakes: Fakes = { execCalls: [], spawnCalls: [], execExit: 0, execStdout: 'Started the mux agent: x\n', healthy: () => ++calls > 2 };
    const { MuxServerLifecycle } = loadLifecycle(fakes, {});
    const previousFetch = globalThis.fetch;
    globalThis.fetch = fakeFetch(fakes);
    try {
        await withSharedSettings('shared-key', async () => {
            const lifecycle = new MuxServerLifecycle(fakeContext());
            const client = await lifecycle.getClient(token);
            assert.ok(client, 'connected');
            assert.deepEqual(fakes.execCalls, [['mux', 'agent', 'start']], 'the agent is started first');
            assert.deepEqual(fakes.spawnCalls, [], 'no mux serve when the agent answers');
            assert.equal(lifecycle.state.ownedByExtension, false, 'the agent owns the server');
            assert.equal(lifecycle.state.detail, 'Connected to the mux agent');
        });
    } finally {
        globalThis.fetch = previousFetch;
    }
});

test('a running server is reused, and the agent is still started for the tray', async () => {
    const fakes: Fakes = { execCalls: [], spawnCalls: [], execExit: 0, execStdout: 'The mux agent is already running.\n', healthy: () => true };
    const { MuxServerLifecycle } = loadLifecycle(fakes, {});
    const previousFetch = globalThis.fetch;
    globalThis.fetch = fakeFetch(fakes);
    try {
        await withSharedSettings('shared-key', async () => {
            const lifecycle = new MuxServerLifecycle(fakeContext());
            await lifecycle.getClient(token);
            assert.deepEqual(fakes.execCalls, [['mux', 'agent', 'start']]);
            assert.deepEqual(fakes.spawnCalls, []);
            assert.equal(lifecycle.state.detail, 'Reusing a running server');
        });
    } finally {
        globalThis.fetch = previousFetch;
    }
});

test('without an installed agent the extension falls back to mux serve; with auto-start off it starts nothing', async () => {
    let started = false;
    const fakes: Fakes = { execCalls: [], spawnCalls: [], execExit: 3, execStdout: '', healthy: () => started };
    const { MuxServerLifecycle } = loadLifecycle(fakes, {});
    const previousFetch = globalThis.fetch;
    globalThis.fetch = fakeFetch(fakes);
    try {
        await withSharedSettings('shared-key', async () => {
            const lifecycle = new MuxServerLifecycle(fakeContext());
            const origSpawnCount = fakes.spawnCalls.length;
            const connecting = lifecycle.getClient(token);
            // mux serve comes up once spawned.
            const timer = setInterval(() => { if (fakes.spawnCalls.length > origSpawnCount) started = true; }, 5);
            await connecting.finally(() => clearInterval(timer));
            assert.deepEqual(fakes.execCalls, [['mux', 'agent', 'start']]);
            assert.equal(fakes.spawnCalls.length, 1, 'fell back to mux serve');
            assert.equal(fakes.spawnCalls[0][1], 'serve');
        });

        const off: Fakes = { execCalls: [], spawnCalls: [], execExit: 0, execStdout: '', healthy: () => false };
        const disabled = loadLifecycle(off, { 'server.autoStart': false });
        globalThis.fetch = fakeFetch(off);
        await withSharedSettings('shared-key', async () => {
            const lifecycle = new disabled.MuxServerLifecycle(fakeContext());
            await assert.rejects(lifecycle.getClient(token), /auto-start is disabled/);
            assert.deepEqual(off.execCalls, [], 'auto-start off starts nothing');
        });
    } finally {
        globalThis.fetch = previousFetch;
    }
});
