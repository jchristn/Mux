import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mirrorEventFromEnvelope } from '../../src/api/mirror';

test('mirrorEventFromEnvelope maps assistant_text to a text event', () => {
    const e = mirrorEventFromEnvelope({ eventType: 'assistant_text', text: 'hello' });
    assert.deepEqual(e, { kind: 'text', text: 'hello' });
});

test('mirrorEventFromEnvelope maps assistant_thinking to a thinking event', () => {
    const e = mirrorEventFromEnvelope({ eventType: 'assistant_thinking', text: 'pondering' });
    assert.deepEqual(e, { kind: 'thinking', text: 'pondering' });
});

test('mirrorEventFromEnvelope maps a proposed tool call to a running tool event', () => {
    const e = mirrorEventFromEnvelope({ eventType: 'tool_call_proposed', toolCall: { id: 'c1', name: 'glob' } });
    assert.deepEqual(e, { kind: 'tool', id: 'c1', name: 'glob', status: 'running', ms: 0 });
});

test('mirrorEventFromEnvelope maps a completed tool call to ok/fail with elapsed', () => {
    const ok = mirrorEventFromEnvelope({ eventType: 'tool_call_completed', toolCallId: 'c1', toolName: 'read_file', elapsedMs: 12, result: { success: true } });
    assert.deepEqual(ok, { kind: 'tool', id: 'c1', name: 'read_file', status: 'ok', ms: 12 });
    const fail = mirrorEventFromEnvelope({ eventType: 'tool_call_completed', toolCallId: 'c2', toolName: 'run_process', elapsedMs: 3, result: { success: false } });
    assert.equal(fail?.kind === 'tool' && fail.status, 'fail');
});

test('mirrorEventFromEnvelope maps run_completed to a done event', () => {
    const e = mirrorEventFromEnvelope({ eventType: 'run_completed', status: 'canceled' });
    assert.deepEqual(e, { kind: 'done', status: 'canceled' });
});

test('mirrorEventFromEnvelope ignores non-render frames and bad input', () => {
    assert.equal(mirrorEventFromEnvelope({ eventType: 'run_started' }), null);
    assert.equal(mirrorEventFromEnvelope({ eventType: 'heartbeat', stepNumber: 2 }), null);
    assert.equal(mirrorEventFromEnvelope(null), null);
    assert.equal(mirrorEventFromEnvelope('not an object'), null);
});
