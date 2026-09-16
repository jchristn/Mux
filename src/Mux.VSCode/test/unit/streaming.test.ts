import assert from 'node:assert/strict';
import { test } from 'node:test';
import { drainBuffer, parseSseBlock, toStreamEvent } from '../../src/api/streaming';

test('parseSseBlock reads event name and concatenated data', () => {
    const block = parseSseBlock('event: token\ndata: "hello"');
    assert.ok(block);
    assert.equal(block?.event, 'token');
    assert.equal(block?.data, '"hello"');
});

test('parseSseBlock returns null for a block with no data', () => {
    assert.equal(parseSseBlock('event: ping'), null);
});

test('toStreamEvent decodes a JSON-encoded text token', () => {
    const event = toStreamEvent({ event: 'token', data: '"partial answer"' });
    assert.deepEqual(event, { event: 'token', data: 'partial answer' });
});

test('toStreamEvent decodes a structured tool event', () => {
    const event = toStreamEvent({ event: 'tool', data: '{"Id":"t1","Name":"glob","Status":"running","ElapsedMs":0}' });
    assert.equal(event?.event, 'tool');
    if (event?.event === 'tool') {
        assert.equal(event.data.Name, 'glob');
    }
});

test('toStreamEvent returns null for an unknown event', () => {
    assert.equal(toStreamEvent({ event: 'mystery', data: '"x"' }), null);
});

test('toStreamEvent returns null for a malformed payload', () => {
    assert.equal(toStreamEvent({ event: 'done', data: 'not json' }), null);
});

test('drainBuffer splits complete blocks and keeps the partial remainder', () => {
    const buffer = 'event: token\ndata: "a"\n\nevent: token\ndata: "b"\n\nevent: token\ndata: "c"';
    const drained = drainBuffer(buffer);
    assert.equal(drained.events.length, 2);
    assert.deepEqual(drained.events[0], { event: 'token', data: 'a' });
    assert.deepEqual(drained.events[1], { event: 'token', data: 'b' });
    assert.equal(drained.rest, 'event: token\ndata: "c"');
});

test('drainBuffer decodes an approval event', () => {
    const buffer = 'event: approval\ndata: {"RunId":"r1","ToolCallId":"tc1","Name":"write_file","Arguments":"{}"}\n\n';
    const drained = drainBuffer(buffer);
    assert.equal(drained.events.length, 1);
    assert.equal(drained.events[0].event, 'approval');
});

test('toStreamEvent decodes the run announcement carrying the run and session ids', () => {
    const event = toStreamEvent({ event: 'run', data: '{"RunId":"r1","SessionId":"s1"}' });
    assert.equal(event?.event, 'run');
    if (event?.event === 'run') {
        assert.equal(event.data.RunId, 'r1');
        assert.equal(event.data.SessionId, 's1');
    }
});

test('toStreamEvent decodes a canceled terminal event', () => {
    const event = toStreamEvent({ event: 'canceled', data: '{"RunId":"r1","SessionId":"s1"}' });
    assert.equal(event?.event, 'canceled');
});
