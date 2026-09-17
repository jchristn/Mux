import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';
import { ApiClient } from '../../src/api/ApiClient';

const realFetch = globalThis.fetch;

afterEach(() => {
    globalThis.fetch = realFetch;
});

interface CapturedCall {
    url: string;
    method: string;
    authorization: string | undefined;
}

function stubFetch(status: number): CapturedCall {
    const captured: CapturedCall = { url: '', method: '', authorization: undefined };
    globalThis.fetch = (async (input: unknown, init?: { method?: string; headers?: Record<string, string> }) => {
        captured.url = String(input);
        captured.method = init?.method ?? 'GET';
        captured.authorization = init?.headers?.Authorization;
        return new Response(status >= 400 ? '{"error":"x"}' : '', { status });
    }) as typeof fetch;
    return captured;
}

test('cancelRun posts to the run cancel route with the bearer key', async () => {
    const captured = stubFetch(200);
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710/', apiKey: 'secret' });
    await client.cancelRun('run-42');
    assert.equal(captured.url, 'http://127.0.0.1:8710/v1.0/api/runs/run-42/cancel');
    assert.equal(captured.method, 'POST');
    assert.equal(captured.authorization, 'Bearer secret');
});

test('cancelRun swallows a 404 so a redundant cancel is harmless', async () => {
    stubFetch(404);
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    await client.cancelRun('gone'); // must not throw
});

test('cancelRun rethrows a non-404 error', async () => {
    stubFetch(500);
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    await assert.rejects(() => client.cancelRun('boom'));
});

test('buildFileContext posts to the context route and returns the built block', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';
    globalThis.fetch = (async (input: unknown, init?: { method?: string; body?: string }) => {
        capturedUrl = String(input);
        capturedMethod = init?.method ?? 'GET';
        capturedBody = init?.body ?? '';
        return new Response(JSON.stringify({ Text: 'Structural map (3 entries): …', Mode: 'map', Inlined: false, OutlineEntryCount: 3, FromCache: false }), { status: 200 });
    }) as typeof fetch;

    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: 'secret' });
    const result = await client.buildFileContext({ path: 'src/big.cs', content: 'x'.repeat(100), mode: 'map' });

    assert.equal(capturedUrl, 'http://127.0.0.1:8710/v1.0/api/context/file');
    assert.equal(capturedMethod, 'POST');
    assert.equal(result.Mode, 'map');
    assert.equal(result.OutlineEntryCount, 3);
    assert.ok(capturedBody.includes('src/big.cs'), 'the request carries the file path');
});
