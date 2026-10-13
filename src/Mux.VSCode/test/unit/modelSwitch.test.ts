import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import * as ts from 'typescript';
import { test } from 'node:test';

const root = path.join(__dirname, '..', '..', '..');
function load(relative: string, vscode: object): any {
    const text = fs.readFileSync(path.join(root, 'src', relative), 'utf8');
    const compiled = ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
    const sandbox = { exports: {}, require: (name: string) => name === 'vscode' ? vscode : {}, console, AbortController };
    vm.runInNewContext(compiled, sandbox);
    return sandbox.exports;
}
function host() {
    return {
        commands: { executeCommand: async () => {} },
        CancellationTokenSource: class { token = {}; },
        l10n: { t: (text: string) => text }
    };
}
test('VS Code endpoint switch creates an inline notice and saves its transcript-only marker', async () => {
    const vscode = host();
    const { ChatViewProvider } = load('chat/ChatViewProvider.ts', vscode);
    const saves: any[] = [], events: any[] = [];
    const client: any = { putSession: async (body: any) => { saves.push(body); return { Id: 'saved-session' }; } };
    const provider = new ChatViewProvider({}, { getClient: async () => client });
    provider.view = { webview: { postMessage: (event: any) => events.push(event) } };
    await provider.recordModelSwitch('homedns-gpt', 'gpt-oss-20b');
    assert.equal(events[0].type, 'modelSwitch');
    assert.equal(events[0].message, 'Switched to endpoint homedns-gpt (gpt-oss-20b)');
    assert.equal(saves[0].Messages[0].DisplayOnly, true);
    assert.equal(provider.sessionId, 'saved-session');
    await provider.onMessage({ type: 'ready' });
    assert.equal(events.at(-1).messages[0].displayOnly, true);
    provider.startSessionSync = () => {};
    client.getSessionDetail = async () => ({ Id: 'saved-session', Title: 'Demo', Messages: saves[0].Messages });
    await provider.loadSession('saved-session');
    assert.equal(provider.history[0].displayOnly, true);
    assert.equal(events.at(-1).type, 'load');
    assert.equal(events.at(-1).messages[0].displayOnly, true);
});
test('VS Code picker reports actual changes and ignores choosing the same endpoint', async () => {
    let configured = 'A', picked = 'B';
    const changes: string[] = [];
    const vscode = {
        ...host(), StatusBarAlignment: { Left: 1 }, ConfigurationTarget: { Workspace: 1 },
        workspace: { getConfiguration: () => ({ get: () => configured, update: async (_key: string, value: string) => { configured = value; } }) },
        window: { createStatusBarItem: () => ({ show: () => {}, dispose: () => {} }), showQuickPick: async () => ({ label: picked }) }
    };
    const { EndpointPicker } = load('config/endpointPicker.ts', vscode);
    const picker = new EndpointPicker({ getClient: async () => ({ getEndpoints: async () => [
        { Name: 'A', Model: 'model-A', IsDefault: true }, { Name: 'B', Model: 'model-B' }
    ] }) }, async (endpoint: string, model: string) => { changes.push(endpoint + ':' + model); });
    await picker.pick();
    assert.deepEqual(changes, ['B:model-B']);
    await picker.pick();
    assert.equal(changes.length, 1);
});

test('VS Code waits for an active response before persisting a model switch', async () => {
    const { ChatViewProvider } = load('chat/ChatViewProvider.ts', host());
    const saves: any[] = [];
    const provider = new ChatViewProvider({}, { getClient: async () => ({
        putSession: async (body: any) => { saves.push(body); return { Id: 'session-1' }; }
    }) });
    let finish!: () => void;
    provider.turnFinished = new Promise<void>(resolve => { finish = resolve; });
    provider.history = [{ role: 'user', content: 'in progress' }];
    const saved = provider.recordModelSwitch('B', 'model-B');
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(saves.length, 0);
    provider.history.push({ role: 'assistant', content: 'finished' });
    finish(); await saved;
    assert.equal(saves[0].Messages.length, 3);
    assert.equal(saves[0].Messages[1].DisplayOnly, true);
    assert.equal(saves[0].Messages[2].Role, 'assistant');
});
