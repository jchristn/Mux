import assert from 'node:assert/strict';
import * as path from 'node:path';
import * as fs from 'node:fs';
import { test } from 'node:test';
import * as vm from 'node:vm';

const root = path.join(__dirname, '..', '..', '..');
const completion = require(path.join(root, 'dashboard', 'chat', 'slash-completion.js')) as {
    match(text: string, caret: number, aliases: string[], limit?: number): string[];
    matchArgument(text: string, caret: number, command: string, values: string[], limit?: number): string[];
    attach(input: unknown, list: unknown, aliases: string[] | (() => string[]), argumentsFor?: (text: string, caret: number) => string[]): { refresh(): void };
};

class Element {
    value = ''; selectionStart = 0; selectionEnd = 0; hidden = false;
    id = ''; type = ''; tabIndex = 0; className = ''; children: Element[] = [];
    attributes = new Map<string, string>();
    listeners = new Map<string, Array<(event: any) => void>>();
    private text = '';
    set textContent(value: string) { this.text = value; this.children = []; }
    get textContent(): string { return this.text; }
    setAttribute(name: string, value: string): void { this.attributes.set(name, value); }
    removeAttribute(name: string): void { this.attributes.delete(name); }
    appendChild(child: Element): void { this.children.push(child); }
    addEventListener(type: string, listener: (event: any) => void): void {
        this.listeners.set(type, [...(this.listeners.get(type) || []), listener]);
    }
    dispatchEvent(event: any): boolean {
        for (const listener of this.listeners.get(event.type) || []) { listener(event); if (event.stopped) break; }
        return true;
    }
    setSelectionRange(start: number, end: number): void { this.selectionStart = start; this.selectionEnd = end; }
    focus(): void { /* Composer focus stays in place. */ }
    scrollIntoView(): void { /* No layout in this event harness. */ }
}
function fixture(argumentsFor?: (text: string, caret: number) => string[]) {
    const input = new Element(), list = new Element(); list.id = 'suggestions';
    (globalThis as any).document = { createElement: () => new Element() };
    const controller = completion.attach(input, list, ['endpoints', 'endpoint', 'help'], argumentsFor);
    function type(text: string) { input.value = text; input.setSelectionRange(text.length, text.length); input.dispatchEvent({ type: 'input' }); }
    function key(name: string, extra: object = {}) {
        const event = { type: 'keydown', key: name, prevented: false, stopped: false,
            preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; }, ...extra };
        input.dispatchEvent(event); return event;
    }
    return { input, list, controller, type, key };
}

test('prefixes are case-insensitive, sorted, limited, and deduplicated', () => {
    assert.deepEqual(completion.match('/END', 4, ['endpoints', 'endpoint', 'ENDPOINT', 'help']), ['/endpoint', '/endpoints']);
    assert.equal(completion.match('/', 1, ['b', 'a'], 1).length, 1);
    assert.deepEqual(completion.match('/toast', 6, ['help', 'endpoint']), []);
    for (const text of ['hello /end', ' /end', '/endpoint value', '/end\nhello', '/endpoint\targ'])
        assert.deepEqual(completion.match(text, text.length, ['endpoint']), []);
    assert.deepEqual(completion.match('/end', 2, ['endpoint']), []);
});
test('arrows select and Tab completes without submitting; accessibility tracks selection', () => {
    const { input, list, type, key } = fixture(); let submitted = 0;
    input.addEventListener('keydown', event => { if (event.key === 'Enter') submitted++; });
    type('/end'); assert.equal(list.hidden, false);
    assert.equal(input.attributes.get('aria-expanded'), 'true');
    key('ArrowDown'); assert.equal(list.children[1].attributes.get('aria-selected'), 'true');
    const tab = key('Tab'); assert.equal(tab.prevented, true);
    assert.equal(input.value, '/endpoints '); assert.equal(list.hidden, true); assert.equal(submitted, 0);
});
test('Enter completes prefixes, exact commands fall through, Escape dismisses until text changes', () => {
    const { input, list, type, key } = fixture();
    type('/end'); assert.equal(key('Enter').prevented, true); assert.equal(input.value, '/endpoint ');
    type('/endpoint'); assert.equal(key('Enter').prevented, false);
    type('/end'); key('Escape'); assert.equal(list.hidden, true); key('ArrowDown'); assert.equal(list.hidden, true);
    type('/en'); assert.equal(list.hidden, false);
});
test('mouse selection completes and argument/caret edits hide the list', () => {
    const { input, list, controller, type } = fixture();
    type('/end'); list.children[1].dispatchEvent({ type: 'click' }); assert.equal(input.value, '/endpoints ');
    type('/endpoint argument'); assert.equal(list.hidden, true);
    type('/end'); input.setSelectionRange(2, 2); controller.refresh(); assert.equal(list.hidden, true);
    input.setSelectionRange(0, 4); controller.refresh(); assert.equal(list.hidden, true);
});
test('IME composition and modifier shortcuts are left alone', () => {
    const { input, list, type, key } = fixture();
    type('/end'); assert.equal(key('Enter', { isComposing: true }).prevented, false);
    for (const modifier of ['shiftKey', 'ctrlKey', 'metaKey', 'altKey']) assert.equal(key('Enter', { [modifier]: true }).prevented, false);
    input.dispatchEvent({ type: 'compositionstart' }); assert.equal(list.hidden, true);
    input.dispatchEvent({ type: 'compositionend' }); assert.equal(list.hidden, false);
});
test('VS Code completion aliases exactly cover its local slash dispatcher', () => {
    const source = fs.readFileSync(path.join(root, 'src', 'chat', 'ChatViewProvider.ts'), 'utf8');
    const aliases = JSON.parse(source.match(/const CHAT_SLASH_COMMANDS = (\[[^;]+\]);/)![1]) as string[];
    const handler = source.slice(source.indexOf('private async handleSlash'), source.indexOf('private postHelp'));
    const supported = [...handler.matchAll(/case '\/([^']+)':/g)].map(match => match[1]);
    assert.deepEqual(aliases.slice().sort(), supported.slice().sort());
    assert.ok(!aliases.includes('toast'));
});

test('endpoint argument completion supports selection while bare endpoint Enter still opens the editor', () => {
    const names = ['homedns-llama', 'homedns-gpt', 'other'];
    const { input, list, type, key } = fixture((text, caret) => completion.matchArgument(text, caret, 'endpoint', names));
    type('/endpoint homedn');
    assert.deepEqual(list.children.map(child => child.textContent), ['/endpoint homedns-gpt', '/endpoint homedns-llama']);
    key('ArrowDown'); key('Tab');
    assert.equal(input.value, '/endpoint homedns-llama ');
    assert.equal(list.hidden, true);
    assert.equal(key('Enter').prevented, false);
    type('/endpoint ');
    assert.equal(list.hidden, false);
    assert.equal(key('Enter').prevented, false);
    assert.equal(key('Tab').prevented, true);
    assert.equal(input.value, '/endpoint homedns-gpt ');
    type('/endpoint homedns-gpt');
    assert.equal(key('Enter').prevented, false);
    type('/endpoint homedn'); key('ArrowDown');
    let submitted = '';
    input.addEventListener('keydown', event => { if (event.key === 'Enter') submitted = input.value; });
    assert.equal(key('Enter').prevented, false);
    assert.equal(submitted, '/endpoint homedns-llama ');
    for (const text of ['/endpoints homedn', '/endpoint homedn\n', '/endpoint homedn '])
        assert.deepEqual(completion.matchArgument(text, text.length, 'endpoint', names), []);
});

test('complete dashboard bootstrap wires command and endpoint autocomplete into the chat composer', () => {
    const source = fs.readFileSync(path.join(root, '..', 'Mux.Server', 'DashboardPage.cs'), 'utf8');
    const asset = fs.readFileSync(path.join(root, 'dashboard', 'chat', 'slash-completion.js'), 'utf8');
    const script = source.split('<script>')[1].split('</script>')[0].replace('__SLASH_COMPLETION_JS__', asset);
    const elements = new Map<string, any>();
    function element(id: string): any {
        if (!elements.has(id)) elements.set(id, Object.assign(new Element(), {
            id, style: {}, dataset: {}, options: [], scrollHeight: 44,
            querySelectorAll: () => [], querySelector: () => null,
            classList: { add: () => {}, remove: () => {}, toggle: () => {} },
            getAttribute: () => null,
        }));
        return elements.get(id);
    }
    const sandbox: any = {
        document: { getElementById: element, createElement: () => new Element(), querySelectorAll: () => [],
            querySelector: () => null, documentElement: element('html'), addEventListener: () => {} },
        localStorage: { getItem: () => null, setItem: () => {} },
        location: { host: 'localhost', protocol: 'http:', hash: '' },
        fetch: () => new Promise(() => {}), setTimeout: () => 1, setInterval: () => 1,
        clearTimeout: () => {}, navigator: {}, console, Event,
    };
    sandbox.window = sandbox; sandbox.addEventListener = () => {};
    vm.createContext(sandbox);
    assert.doesNotThrow(() => vm.runInContext(script, sandbox));
    const input = element('composer'), list = element('slash-completions');
    input.value = '/end'; input.setSelectionRange(4, 4); input.dispatchEvent({ type: 'input' });
    assert.equal(list.hidden, false);
    assert.deepEqual(list.children.map((child: Element) => child.textContent), ['/endpoint', '/endpoints']);
    element('endpointSelect').options = [{ value: 'homedns-gpt' }, { value: 'homedns-llama' }];
    input.value = '/endpoint homedn'; input.setSelectionRange(input.value.length, input.value.length); input.dispatchEvent({ type: 'input' });
    assert.deepEqual(list.children.map((child: Element) => child.textContent), ['/endpoint homedns-gpt', '/endpoint homedns-llama']);
    const sendChat = sandbox.sendChat;
    let submitted = false; sandbox.sendChat = () => { submitted = true; };
    const event = { type: 'keydown', key: 'Tab', preventDefault() {}, stopImmediatePropagation() { (this as any).stopped = true; } };
    input.dispatchEvent(event);
    assert.equal(input.value, '/endpoint homedns-gpt ');
    assert.equal(submitted, false);
    // Exercise the real dashboard dispatcher, notice renderer, and endpoint selection in one Enter.
    sandbox.sendChat = sendChat;
    sandbox.warmModel = () => { sandbox.selectedEndpointName = element('endpointSelect').value; };
    sandbox.persistConvo = () => Promise.resolve();
    sandbox.selectedEndpointName = 'other';
    const select = element('endpointSelect');
    select.value = 'other'; select.selectedIndex = 0;
    select.options = [{ value: 'homedns-gpt', dataset: { model: 'gpt-oss-20b' } }, { value: 'other', dataset: { model: 'other-model' } }];
    input.value = '/endpoint homedns'; input.setSelectionRange(input.value.length, input.value.length); input.dispatchEvent({ type: 'input' });
    input.dispatchEvent({ type: 'keydown', key: 'Enter', preventDefault() {}, stopImmediatePropagation() { (this as any).stopped = true; } });
    assert.equal(select.value, 'homedns-gpt');
    assert.equal(input.value, '');
    assert.equal(sandbox.messages.length, 1);
    assert.equal(sandbox.messages[0].displayOnly, true);
    assert.ok(element('messages').innerHTML.includes('Switched to endpoint homedns-gpt (gpt-oss-20b)'));
});
