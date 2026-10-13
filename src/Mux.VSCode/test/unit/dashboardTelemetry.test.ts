import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import { test } from 'node:test';

const source = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'Mux.Server', 'DashboardPage.cs'), 'utf8');
function fn(name: string): string {
    const found = source.match(new RegExp('function ' + name + '\\([^]*?\\n}'));
    assert.ok(found, 'dashboard function exists: ' + name);
    return found[0];
}
function context(detail: object) {
    const messagesBox = { innerHTML: '', scrollTop: 0, scrollHeight: 10 };
    const sandbox: any = {
        messages: [], currentSessionId: 'session-1', currentModel: '', busy: false,
        api: () => Promise.resolve(detail), el: (id: string) => id === 'messages' ? messagesBox : null,
        esc: (value: unknown) => String(value), md: (value: unknown) => String(value), t: (value: unknown) => String(value),
        fmtMs: (value: unknown) => String(value), setChatTitle: () => {}, highlightConvo: () => {},
        watchConvo: () => {}, loadConvos: () => {}, toast: () => {},
        setTimeout: (callback: () => void) => { callback(); return 1; }
    };
    vm.createContext(sandbox);
    vm.runInContext(['statInfo', 'renderMessages', 'openConvo', 'reloadOpenTranscript'].map(fn).join('\n'), sandbox);
    return { sandbox, messagesBox };
}
const stats = { TtftMs: 123, StreamingMs: 456, TotalMs: 579, InputTokens: 10, OutputTokens: 20, TotalTokens: 30 };

test('opening a saved web conversation retains per-response model and telemetry icon', async () => {
    const { sandbox, messagesBox } = context({ Id: 'session-1', Model: 'latest-model', Messages: [
        { Role: 'user', Content: 'Hello' }, { Role: 'assistant', Content: 'Life continues.', Model: 'original-model', Stats: stats }
    ] });
    sandbox.openConvo('session-1'); await new Promise(resolve => setImmediate(resolve));
    assert.equal(sandbox.messages[1].model, 'original-model');
    assert.equal(sandbox.messages[1].stats.TotalTokens, 30);
    assert.ok(messagesBox.innerHTML.includes('class="statinfo"'));
    assert.ok(messagesBox.innerHTML.includes('original-model'));
});
test('passive transcript reload retains telemetry on existing and appended responses', async () => {
    const { sandbox, messagesBox } = context({ Id: 'session-1', Model: 'latest-model', Messages: [
        { Role: 'assistant', Content: 'First.', Model: 'first-model', Stats: stats },
        { Role: 'assistant', Content: 'Next.', Model: 'next-model', Stats: stats }
    ] });
    sandbox.messages = [{ role: 'assistant', content: 'First.', model: 'first-model', stats }];
    sandbox.reloadOpenTranscript('session-1'); await new Promise(resolve => setImmediate(resolve));
    assert.equal(sandbox.messages.length, 2);
    assert.equal(sandbox.messages[0].stats.TotalMs, 579);
    assert.equal((messagesBox.innerHTML.match(/class="statinfo"/g) || []).length, 2);
});
test('available stats render even without a model name; legacy responses do not invent stats', () => {
    const { sandbox, messagesBox } = context({});
    sandbox.messages = [{ role: 'assistant', content: 'Done.', stats }]; sandbox.renderMessages();
    assert.ok(messagesBox.innerHTML.includes('class="statinfo"'));
    sandbox.messages = [{ role: 'assistant', content: 'Legacy.', model: 'legacy-model' }]; sandbox.renderMessages();
    assert.ok(!messagesBox.innerHTML.includes('class="statinfo"'));
});

test('dashboard inline script parses as a complete JavaScript program', () => {
    const script = source.split('<script>')[1].split('</script>')[0];
    assert.doesNotThrow(() => new vm.Script(script));
});
test('model switch notice survives opening and reloading a conversation', async () => {
    const { sandbox, messagesBox } = context({ Id: 'session-1', Messages: [
        { Role: 'system', Content: 'Switched to endpoint homedns (gpt-oss-20b)', DisplayOnly: true }
    ] });
    sandbox.openConvo('session-1'); await new Promise(resolve => setImmediate(resolve));
    assert.equal(sandbox.messages[0].displayOnly, true);
    assert.ok(messagesBox.innerHTML.includes('class="model-switch"'));
    sandbox.messages = [];
    sandbox.reloadOpenTranscript('session-1'); await new Promise(resolve => setImmediate(resolve));
    assert.equal(sandbox.messages[0].displayOnly, true);
    assert.ok(messagesBox.innerHTML.includes('homedns (gpt-oss-20b)'));
});

test('web endpoint selection records a durable notice; repeated selection does nothing', async () => {
    const { sandbox, messagesBox } = context({});
    const saves: any[] = [];
    const selector = { value: 'B', selectedIndex: 0, options: [{ dataset: { model: 'model-B' } }] };
    const originalEl = sandbox.el;
    sandbox.el = (id: string) => id === 'endpointSelect' ? selector : originalEl(id);
    sandbox.selectedEndpointName = 'A';
    sandbox.modelSwitchSave = Promise.resolve();
    sandbox.notifySessionsChanged = () => {};
    sandbox.warmModel = () => { sandbox.selectedEndpointName = selector.value; };
    sandbox.api = (_route: string, _method: string, payload: any) => { saves.push(payload); return Promise.resolve({ Id: 'session-1' }); };
    vm.runInContext(fn('recordModelSwitch') + '\n' + fn('persistConvo'), sandbox);
    sandbox.recordModelSwitch();
    // A new prompt entered before the save starts must not leak into the switch save.
    sandbox.messages.push({ role: 'user', content: 'next turn' });
    await sandbox.modelSwitchSave;
    assert.equal(saves[0].Messages.length, 1);
    assert.equal(saves[0].Messages[0].DisplayOnly, true);
    assert.equal(saves[0].EndpointName, 'B');
    assert.ok(messagesBox.innerHTML.includes('Switched to endpoint B (model-B)'));
    sandbox.recordModelSwitch(); await sandbox.modelSwitchSave;
    assert.equal(saves.length, 1);
});

test('web endpoint commands select directly and bare aliases open the editor', () => {
    const selector = { value: 'other', options: [{ value: 'homedns-gpt' }, { value: 'other' }] };
    const switches: string[] = [], views: string[] = [];
    const sandbox: any = { el: () => selector, recordModelSwitch: () => switches.push(selector.value), switchView: (name: string) => views.push(name), CHAT_COMMAND_VIEWS: { endpoint: 'endpoints', endpoints: 'endpoints' } };
    vm.createContext(sandbox); vm.runInContext(fn('handleChatCommand'), sandbox);
    sandbox.handleChatCommand('/endpoint HOMEDNS-GPT');
    assert.deepEqual(switches, ['homedns-gpt']); assert.equal(views.length, 0);
    sandbox.handleChatCommand('/endpoint'); sandbox.handleChatCommand('/endpoints');
    assert.deepEqual(views, ['endpoints', 'endpoints']);
});

test('telemetry tooltip stays within the transcript at every edge and on a narrow screen', () => {
    const sandbox: any = { window: { innerWidth: 1000, innerHeight: 800 } };
    vm.createContext(sandbox); vm.runInContext(fn('statTooltipBounds'), sandbox);
    const box = { left: 300, top: 120, right: 960, bottom: 700 };
    const anchors = [
        { left: 305, top: 125, bottom: 140 }, { left: 940, top: 125, bottom: 140 },
        { left: 305, top: 680, bottom: 695 }, { left: 940, top: 680, bottom: 695 },
    ];
    for (const anchor of anchors) {
        const rect = sandbox.statTooltipBounds(box, anchor, 240, 180);
        assert.ok(rect.left >= box.left + 8);
        assert.ok(rect.top >= box.top + 8);
        assert.ok(rect.left + rect.width <= box.right - 8);
        assert.ok(rect.top + rect.height <= box.bottom - 8);
    }
    const top = sandbox.statTooltipBounds(box, anchors[0], 240, 180);
    assert.equal(top.top, anchors[0].bottom + 8, 'top-edge tooltip flips below its icon');
    sandbox.window.innerWidth = 390; sandbox.window.innerHeight = 320;
    const narrow = sandbox.statTooltipBounds({ left: 0, top: 60, right: 420, bottom: 500 }, { left: 365, top: 80, bottom: 95 }, 500, 400);
    assert.ok(narrow.left >= 8 && narrow.top >= 68);
    assert.ok(narrow.left + narrow.width <= 382);
    assert.ok(narrow.top + narrow.height <= 312);
});

test('telemetry hover and keyboard focus measure the tooltip inside the transcript; scrolling closes it', () => {
    const styles: any = {}, classes = new Set<string>();
    const tooltip = { style: styles, offsetWidth: 240, offsetHeight: 170 };
    const icon: any = {
        querySelector: () => tooltip, getBoundingClientRect: () => ({ left: 690, top: 110, bottom: 125 }),
        classList: { add: (name: string) => classes.add(name), remove: (name: string) => classes.delete(name) },
        contains: (target: unknown) => target === tooltip, closest: () => icon,
    };
    const events: Record<string, (event: any) => void> = {}, windowEvents: Record<string, (event: any) => void> = {};
    const box = { getBoundingClientRect: () => ({ left: 250, top: 100 }), clientLeft: 0, clientTop: 0,
        clientWidth: 470, clientHeight: 400, contains: () => true,
        addEventListener: (name: string, listener: (event: any) => void) => { events[name] = listener; } };
    const sandbox: any = { el: () => box, statTooltipIcon: null,
        window: { innerWidth: 900, innerHeight: 700, addEventListener: (name: string, listener: (event: any) => void) => { windowEvents[name] = listener; } } };
    vm.createContext(sandbox);
    vm.runInContext(['statTooltipBounds', 'hideStatTooltip', 'showStatTooltip', 'wireStatTooltips'].map(fn).join('\n'), sandbox);
    sandbox.wireStatTooltips(); events.mouseover({ target: icon });
    assert.ok(classes.has('tip-open'));
    assert.equal(styles.maxWidth, '454px'); assert.equal(styles.maxHeight, '384px');
    assert.equal(styles.left, '472px'); assert.equal(styles.top, '133px');
    windowEvents.scroll({ target: tooltip }); assert.ok(classes.has('tip-open'), 'tooltip itself can scroll');
    events.scroll({}); assert.ok(!classes.has('tip-open'));
    events.focusin({ target: icon }); assert.ok(classes.has('tip-open'));
    windowEvents.resize({}); assert.ok(!classes.has('tip-open'));
    events.mouseover({ target: icon }); events.mouseout({ relatedTarget: tooltip });
    assert.ok(classes.has('tip-open'), 'moving onto the tooltip keeps it open');
    events.mouseout({ relatedTarget: null }); assert.ok(!classes.has('tip-open'));
});
