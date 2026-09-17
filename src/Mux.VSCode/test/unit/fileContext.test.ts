import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
    computeInlineThresholdBytes,
    resolveFileContext,
    DEFAULT_INLINE_THRESHOLD_BYTES,
} from '../../src/context/fileContext';
import { composePrompt } from '../../src/context/composePrompt';
import { FileContextResponse } from '../../src/api/types';

function mapResponse(text: string): FileContextResponse {
    return { Text: text, Mode: 'map', Inlined: false, OutlineEntryCount: 2, FromCache: false };
}

test('computeInlineThresholdBytes scales with the context window and falls back when unknown', () => {
    assert.equal(computeInlineThresholdBytes(undefined), DEFAULT_INLINE_THRESHOLD_BYTES, 'unknown window falls back');
    assert.equal(computeInlineThresholdBytes(0), DEFAULT_INLINE_THRESHOLD_BYTES, 'zero window falls back');
    assert.equal(computeInlineThresholdBytes(200000), Math.floor(0.25 * 200000 * 3.5), 'wide window scales up (175000)');
    assert.ok(computeInlineThresholdBytes(200000) > computeInlineThresholdBytes(8000), 'a wider window inlines more');
    assert.equal(computeInlineThresholdBytes(100), 1024, 'a tiny window is floored so a small inline is still allowed');
});

test('resolveFileContext forwards the mode and endpoint to the fetcher for a large file', async () => {
    const big = 'x'.repeat(500);
    let seen: { mode?: string; endpointName?: string } = {};
    await resolveFileContext('big.ts', big, {
        inlineThresholdBytes: 100,
        mode: 'summarize',
        endpointName: 'claude',
        fetcher: async (request) => {
            seen = { mode: request.mode, endpointName: request.endpointName };
            return mapResponse('summary …');
        },
    });
    assert.equal(seen.mode, 'summarize', 'mode is forwarded');
    assert.equal(seen.endpointName, 'claude', 'endpoint is forwarded');
});

test('resolveFileContext forwards an undefined mode when inheriting the server default', async () => {
    const big = 'x'.repeat(500);
    let sawMode: string | undefined = 'unset';
    await resolveFileContext('big.ts', big, {
        inlineThresholdBytes: 100,
        mode: undefined,
        fetcher: async (request) => {
            sawMode = request.mode;
            return mapResponse('map …');
        },
    });
    assert.equal(sawMode, undefined, 'inherit sends no mode so the server decides');
});

test('resolveFileContext inlines a small file whole without calling the server', async () => {
    let called = false;
    const resolved = await resolveFileContext('a.ts', 'small', {
        inlineThresholdBytes: 100,
        fetcher: async () => {
            called = true;
            return mapResponse('should not be used');
        },
    });
    assert.equal(called, false, 'the server is not called for a small file');
    assert.equal(resolved.content, 'small');
    assert.equal(resolved.mode, 'inline');
    assert.equal(resolved.noTruncate, true);
});

test('resolveFileContext fetches a map for a large file and marks it no-truncate', async () => {
    const big = 'x'.repeat(500);
    const resolved = await resolveFileContext('big.ts', big, {
        inlineThresholdBytes: 100,
        fetcher: async (request) => {
            assert.equal(request.path, 'big.ts');
            return mapResponse('Structural map (2 entries): …');
        },
    });
    assert.equal(resolved.content, 'Structural map (2 entries): …');
    assert.equal(resolved.mode, 'map');
    assert.equal(resolved.noTruncate, true);
    assert.equal(resolved.usedFallback, false);
});

test('resolveFileContext falls back to truncatable original content when the server is unreachable', async () => {
    const big = 'x'.repeat(500);
    const resolved = await resolveFileContext('big.ts', big, {
        inlineThresholdBytes: 100,
        fetcher: async () => {
            throw new Error('server down');
        },
    });
    assert.equal(resolved.content, big, 'the original content is kept for the caller to truncate');
    assert.equal(resolved.mode, 'truncate');
    assert.equal(resolved.noTruncate, false);
    assert.equal(resolved.usedFallback, true);
});

test('composePrompt does not truncate a no-truncate item but still caps others', () => {
    const bigMap = 'M'.repeat(50);
    const bigOther = 'O'.repeat(50);
    const composed = composePrompt(
        'do it',
        [
            { kind: 'activeFile', label: 'Active file: big.ts', content: bigMap, noTruncate: true },
            { kind: 'gitDiff', label: 'Working-tree diff', content: bigOther },
        ],
        20,
    );
    assert.ok(composed.prompt.includes(bigMap), 'the no-truncate map survives whole');
    assert.deepEqual(composed.truncated, ['Working-tree diff'], 'only the ordinary item is truncated');
});
