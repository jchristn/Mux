import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import { test } from 'node:test';

const source = fs.readFileSync(path.join(__dirname, '../../../../Mux.Server/DashboardTemplate.cs'), 'utf8');
const handler = source.slice(source.indexOf('function handleApproval(req){'), source.indexOf('\nfunction loadEndpoints()'));

function harness(seconds: number) {
    let now = 0;
    const elements: any[] = [], decisions: any[] = [], timers = new Set<() => void>();
    const document: any = {
        hidden: false,
        createElement(tag: string) {
            const element: any = {
                tag, children: [], style: {}, listeners: {}, textContent: '', removed: false,
                appendChild(child: any) { this.children.push(child); },
                addEventListener(name: string, callback: any) { this.listeners[name] = callback; },
                showModal() {}, remove() { this.removed = true; },
            };
            elements.push(element);
            return element;
        },
        body: { appendChild() {} },
    };
    const context: any = {
        document, busy: true, pendingApproval: null,
        performance: { now: () => now },
        setInterval(fn: () => void) { timers.add(fn); return fn; },
        clearInterval(fn: () => void) { timers.delete(fn); },
        api(_route: string, _method: string, payload: any) { decisions.push(payload); return Promise.resolve(); },
    };
    vm.createContext(context);
    vm.runInContext(handler, context);
    context.handleApproval({ RunId: 'r', ToolCallId: 't', Name: 'write_file', Arguments: '{}', ToolAutoApproveSeconds: seconds });
    return {
        context, document, decisions, elements,
        advance(ms: number) { now += ms; [...timers].forEach(fn => fn()); },
        label: () => elements.find(e => e.tag === 'p').textContent,
        click(text: string) { elements.find(e => e.tag === 'button' && e.textContent === text).onclick(); },
    };
}

test('web approval countdown labels remaining time and approves once at the deadline', () => {
    const h = harness(10);
    assert.equal(h.label(), 'Auto-approve in 10s');
    h.advance(9000);
    assert.equal(h.label(), 'Auto-approve in 1s');
    assert.equal(h.decisions.length, 0);
    h.advance(1000); h.advance(10000);
    assert.equal(h.decisions.length, 1);
    assert.equal(h.decisions[0].Decision, 'y');
});

test('web approval zero is disabled; deny and Escape cancel an enabled countdown', () => {
    const disabled = harness(0); disabled.advance(3600000);
    assert.equal(disabled.decisions.length, 0);
    assert.equal(disabled.label(), 'Automatic approval disabled');
    const denied = harness(10); denied.click('Deny'); denied.advance(20000);
    assert.equal(denied.decisions.length, 1);
    assert.equal(denied.decisions[0].Decision, 'n');
    const escaped = harness(10);
    escaped.elements.find(e => e.tag === 'dialog').listeners.cancel({ preventDefault() {} });
    escaped.advance(20000);
    assert.equal(escaped.decisions[0].Decision, 'n');
    assert.equal(escaped.decisions.length, 1);
});

test('web approval pauses while hidden and denies when the run stops', () => {
    const h = harness(10);
    h.document.hidden = true; h.advance(30000);
    assert.equal(h.label(), 'Auto-approve in 10s');
    assert.equal(h.decisions.length, 0);
    h.document.hidden = false; h.advance(9000);
    assert.equal(h.label(), 'Auto-approve in 1s');
    h.context.busy = false; h.advance(1000);
    assert.equal(h.decisions[0].Decision, 'n');
    h.advance(30000); assert.equal(h.decisions.length, 1);
});
