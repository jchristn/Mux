import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import { test } from 'node:test';

// The dashboard opens the conversation named by ?session=<id> or #session=<id> (Mux Cloud links to a machine's
// conversation this way) and leaves the normal view alone when the id is missing or unknown.
const source = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'Mux.Server', 'DashboardTemplate.cs'), 'utf8');
function fn(name: string): string {
    const found = source.match(new RegExp('function ' + name + '\\([^]*?\\n}'));
    const oneLine = source.match(new RegExp('function ' + name + '\\([^\\n]*'));
    const text = found && found[0].length < 2000 ? found[0] : oneLine ? oneLine[0] : null;
    assert.ok(text, 'dashboard function exists: ' + name);
    return text;
}
function context(known: string[]) {
    const calls: string[] = [];
    const sandbox: any = {
        calls,
        api: (url: string) => {
            const id = decodeURIComponent(url.split('id=')[1] || '');
            return known.includes(id) ? Promise.resolve({ Id: id, Messages: [] }) : Promise.reject(new Error('NotFound'));
        },
        switchView: (v: string) => calls.push('view:' + v),
        openConvo: (id: string) => calls.push('open:' + id),
    };
    vm.createContext(sandbox);
    vm.runInContext([fn('sessionFromLocation'), fn('openSessionFromLocation')].join('\n'), sandbox);
    return sandbox;
}

test('session id is read from the query string or the hash', () => {
    const sandbox = context([]);
    assert.equal(sandbox.sessionFromLocation('?session=abc123', ''), 'abc123');
    assert.equal(sandbox.sessionFromLocation('?x=1&session=a%20b', ''), 'a b');
    assert.equal(sandbox.sessionFromLocation('', '#session=h1'), 'h1');
    assert.equal(sandbox.sessionFromLocation('', '#settings'), null);
    assert.equal(sandbox.sessionFromLocation('?mysession=x', ''), null);
    assert.equal(sandbox.sessionFromLocation('', ''), null);
});

test('a known session opens in Chat; an unknown or absent one leaves the view alone', async () => {
    const sandbox = context(['s-1']);
    assert.equal(await sandbox.openSessionFromLocation('?session=s-1', ''), true);
    assert.deepEqual(sandbox.calls, ['view:chat', 'open:s-1']);
    sandbox.calls.length = 0;
    assert.equal(await sandbox.openSessionFromLocation('', '#session=missing'), false);
    assert.equal(await sandbox.openSessionFromLocation('', '#settings'), false);
    assert.deepEqual(sandbox.calls, []);
});

test('the view hash handler ignores parameter-style hashes', () => {
    assert.ok(source.includes('if(h&&h.indexOf("=")<0&&document'), '#session=... is not treated as a view name');
});
