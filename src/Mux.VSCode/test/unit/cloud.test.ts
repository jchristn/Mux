import assert from 'node:assert/strict';
import { test } from 'node:test';

// Loads the Mux Cloud text helpers against a minimal fake of the VS Code API.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const Module = require('node:module');
// eslint-disable-next-line @typescript-eslint/no-require-imports
const fs = require('node:fs');
// eslint-disable-next-line @typescript-eslint/no-require-imports
const path = require('node:path');

const root = path.join(__dirname, '..', '..', '..');

function loadCloudText(): any {
    const fake = { l10n: { t: (message: string, ...args: unknown[]) => message.replace(/\{(\d+)\}/g, (_m, i) => String(args[Number(i)])) } };
    const original = Module._load;
    Module._load = function (request: string, ...rest: unknown[]) {
        if (request === 'vscode') {
            return fake;
        }
        return original.call(this, request, ...rest);
    };
    try {
        const resolved = require.resolve('../../src/cloud/cloudText');
        delete require.cache[resolved];
        return require(resolved);
    } finally {
        Module._load = original;
    }
}

function status(stateName: string, reason = ''): any {
    return { State: stateName, StateName: stateName, Reason: reason, ServerUrl: 'https://app.usemux.ai', MachineId: 'mch_1', MachineName: 'studio', AccountEmail: 'a@b.c', AllowRemoteToolApproval: true, HostedHere: true, ConnectionId: '' };
}

test('cloud status text uses the shared state names and wording', () => {
    const text = loadCloudText();
    assert.equal(text.describeStatus(status('Connected')), 'Connected to https://app.usemux.ai as studio');
    assert.equal(text.describeStatus(status('Reconnecting', 'dropped')), 'Reconnecting to https://app.usemux.ai: dropped');
    assert.match(text.describeStatus(status('Revoked')), /removed from Mux Cloud/);
    assert.equal(text.describeStatus(status('Disabled')), 'Not connected to Mux Cloud');
    assert.equal(text.stateIcon('Connected'), '$(cloud)');
    assert.equal(text.stateIcon('Reconnecting'), '$(sync~spin)');
    assert.equal(text.stateIcon('SignedOut'), '$(warning)');
    assert.match(text.remoteAccessNotice(), /approving tools/);
    assert.match(text.remoteAccessNotice(), /allowRemoteToolApproval/);
    assert.equal(text.statusSummary(status('Connected')), 'Mux Cloud: Connected · studio');
    assert.equal(text.statusSummary(status('Reconnecting', 'dropped')), 'Mux Cloud: Reconnecting. Reconnecting to https://app.usemux.ai: dropped');
});

test('every Mux Cloud string has a translation in every runtime l10n bundle', () => {
    const sources = ['cloudText.ts', 'CloudStatusBar.ts', 'cloudCommands.ts'].map((f) => fs.readFileSync(path.join(root, 'src', 'cloud', f), 'utf8')).join('\n');
    const keys = [...new Set([...sources.matchAll(/l10n\.t\(\s*'((?:[^'\\]|\\.)*)'/g)].map((m: RegExpMatchArray) => m[1]))];
    assert.ok(keys.length > 20, 'found the cloud strings');
    for (const file of fs.readdirSync(path.join(root, 'l10n'))) {
        const bundle = JSON.parse(fs.readFileSync(path.join(root, 'l10n', file), 'utf8'));
        const missing = keys.filter((k) => typeof bundle[k] !== 'string' || bundle[k].length === 0);
        assert.deepEqual(missing, [], file + ' is missing Mux Cloud strings');
    }
});

test('the Mux Cloud command is contributed and titled in every package.nls file', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
    assert.ok(pkg.contributes.commands.some((c: any) => c.command === 'mux.cloud' && c.title === '%cmd.cloud%'));
    for (const file of fs.readdirSync(root).filter((f: string) => /^package\.nls.*\.json$/.test(f))) {
        const nls = JSON.parse(fs.readFileSync(path.join(root, file), 'utf8'));
        assert.equal(typeof nls['cmd.cloud'], 'string', file + ' has cmd.cloud');
    }
});
