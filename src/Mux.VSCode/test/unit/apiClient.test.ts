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
    const result = await client.buildFileContext({ path: 'src/big.cs', content: 'x'.repeat(100), mode: 'summarize', endpointName: 'claude' });

    assert.equal(capturedUrl, 'http://127.0.0.1:8710/v1.0/api/context/file');
    assert.equal(capturedMethod, 'POST');
    assert.equal(result.Mode, 'map');
    assert.equal(result.OutlineEntryCount, 3);
    assert.ok(capturedBody.includes('src/big.cs'), 'the request carries the file path');
    assert.ok(capturedBody.includes('summarize'), 'the request carries the mode');
    assert.ok(capturedBody.includes('claude'), 'the request carries the selected endpoint');
});

test('expandSkill posts the slash text and working directory and returns the expansion', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';
    globalThis.fetch = (async (input: unknown, init?: { method?: string; body?: string }) => {
        capturedUrl = String(input);
        capturedMethod = init?.method ?? 'GET';
        capturedBody = init?.body ?? '';
        return new Response(JSON.stringify({ Matched: true, Skill: 'code-review', Arguments: 'file src/a.ts', Prompt: 'Run the "code-review" skill...', IsPlaybook: false }), { status: 200 });
    }) as typeof fetch;

    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: 'secret' });
    const result = await client.expandSkill('/code-review file src/a.ts', '/work/repo');
    assert.equal(capturedUrl, 'http://127.0.0.1:8710/v1.0/api/skills/expand');
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), { Input: '/code-review file src/a.ts', WorkingDirectory: '/work/repo' });
    assert.equal(result.Matched, true);
    assert.equal(result.Skill, 'code-review');
});

test('expandSkill sends a null working directory when none is given and reports no match', async () => {
    let capturedBody = '';
    globalThis.fetch = (async (_input: unknown, init?: { body?: string }) => {
        capturedBody = init?.body ?? '';
        return new Response(JSON.stringify({ Matched: false, Skill: '', Arguments: '', Prompt: '', IsPlaybook: false }), { status: 200 });
    }) as typeof fetch;

    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    const result = await client.expandSkill('/no-such-skill');
    assert.equal(JSON.parse(capturedBody).WorkingDirectory, null);
    assert.equal(result.Matched, false);
});

test('expandSkill rejects when the server fails', async () => {
    globalThis.fetch = (async () => new Response('{"error":"BadRequest"}', { status: 400 })) as typeof fetch;
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    await assert.rejects(() => client.expandSkill(''));
});

test('validateMcpServer posts the server name and returns the failure details', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';
    globalThis.fetch = (async (input: unknown, init?: { method?: string; body?: string }) => {
        capturedUrl = String(input);
        capturedMethod = init?.method ?? 'GET';
        capturedBody = init?.body ?? '';
        return new Response(JSON.stringify({
            Name: 'docs', Connected: false, Method: 'http', ToolCount: 0, Tools: [],
            Error: "Failed to connect to HTTP MCP server 'docs' at http://localhost:9: HTTP 401 Unauthorized",
            Details: 'Response: HTTP 401 Unauthorized in 3 ms\nBody: {"error":"invalid token"}', ElapsedMs: 12,
        }), { status: 200 });
    }) as typeof fetch;

    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    const result = await client.validateMcpServer('docs');
    assert.equal(capturedUrl, 'http://127.0.0.1:8710/v1.0/api/mcp-servers/validate');
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), { Name: 'docs' });
    assert.equal(result.Connected, false);
    assert.ok(result.Error?.includes('HTTP 401'));
    assert.ok(result.Details?.includes('invalid token'));
});

test('validateMcpServer returns the tools of a connected server', async () => {
    globalThis.fetch = (async () => new Response(JSON.stringify({ Name: 'fs', Connected: true, Method: 'stdio', ToolCount: 2, Tools: ['fs.read', 'fs.write'], Error: null, Details: null, ElapsedMs: 80 }), { status: 200 })) as typeof fetch;
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    const result = await client.validateMcpServer('fs');
    assert.equal(result.Connected, true);
    assert.deepEqual(result.Tools, ['fs.read', 'fs.write']);
});

test('validateMcpServer rejects when the server is unknown', async () => {
    globalThis.fetch = (async () => new Response('{"Error":"NotFound","Message":"No MCP server named \'nope\'."}', { status: 404 })) as typeof fetch;
    const client = new ApiClient({ baseUrl: 'http://127.0.0.1:8710', apiKey: null });
    await assert.rejects(() => client.validateMcpServer('nope'));
});
