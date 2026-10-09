import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ApiError } from '../../src/api/ApiError';
import { composePrompt, estimateTokens } from '../../src/context/composePrompt';
import { checkContract } from '../../src/server/contract';
import { formatList, formatRelativeTime, formatTokens } from '../../src/i18n/format';
import { resolveLocale } from '../../src/i18n/locales';
import { reviewFileInvocation } from '../../src/commands/skillInvocation';
import { copyButtonHtml, escapeHtml } from '../../src/manage/formCopy';
import { formatMcpValidation } from '../../src/manage/mcpValidation';

test('ApiError derives a message from a JSON body', () => {
    const error = new ApiError(404, '{"error":"NotFound","message":"Unknown endpoint: nope"}');
    assert.equal(error.status, 404);
    assert.equal(error.message, 'Unknown endpoint: nope');
    assert.ok(error instanceof ApiError);
    assert.ok(error instanceof Error);
});

test('ApiError falls back to a status message for a non-JSON body', () => {
    const error = new ApiError(500, 'boom');
    assert.equal(error.message, 'Request failed with status 500');
});

test('composePrompt fences context before the user message and reports truncation', () => {
    const composed = composePrompt('fix this', [{ kind: 'selection', label: 'Selection', content: 'x'.repeat(20) }], 8);
    assert.ok(composed.prompt.includes('--- Selection ---'));
    assert.ok(composed.prompt.trimEnd().endsWith('fix this'));
    assert.deepEqual(composed.truncated, ['Selection']);
    assert.ok(composed.estimatedTokens > 0);
});

test('composePrompt with no context returns just the message', () => {
    const composed = composePrompt('hello', []);
    assert.equal(composed.prompt, 'hello');
    assert.deepEqual(composed.truncated, []);
});

test('estimateTokens approximates four characters per token', () => {
    assert.equal(estimateTokens('abcdefgh'), 2);
});

test('checkContract accepts a matching major and rejects others', () => {
    assert.equal(checkContract('1.0').compatible, true);
    assert.equal(checkContract('1.7').compatible, true);
    assert.equal(checkContract('2.0').compatible, false);
    assert.equal(checkContract('0.9').compatible, false);
    assert.equal(checkContract('').compatible, false);
    assert.equal(checkContract(undefined).compatible, false);
});

test('resolveLocale matches exactly, by base language, then falls back to en', () => {
    assert.equal(resolveLocale('ar').code, 'ar');
    assert.equal(resolveLocale('pt-BR').code, 'pt');
    assert.equal(resolveLocale('xx').code, 'en');
    assert.equal(resolveLocale('').code, 'en');
    assert.equal(resolveLocale('ar').direction, 'rtl');
});

test('formatters produce locale-aware output without hand-rolled strings', () => {
    assert.equal(formatList('en', ['a', 'b', 'c']), 'a, b, and c');
    assert.ok(formatTokens('en', 1500).length > 0);
    const rel = formatRelativeTime('en', new Date(1_000_000).toISOString(), 1_000_000 + 3_600_000);
    assert.ok(/hour/.test(rel));
});

test('reviewFileInvocation builds a code-review file invocation', () => {
    assert.equal(reviewFileInvocation('src/app.ts'), '/code-review file src/app.ts');
});

test('reviewFileInvocation normalizes backslashes and quotes paths with spaces', () => {
    assert.equal(reviewFileInvocation('src\\My Folder\\app.ts'), '/code-review file "src/My Folder/app.ts"');
});

test('copyButtonHtml targets the control and escapes the label', () => {
    const html = copyButtonHtml('f_Body', 'SKILL.md <draft>');
    assert.ok(html.includes('data-copy="f_Body"'));
    assert.ok(html.includes('class="copy secondary"'));
    assert.ok(html.includes('aria-label="Copy SKILL.md &lt;draft&gt; to the clipboard"'));
    assert.ok(!html.includes('<draft>'));
});

test('escapeHtml escapes markup and quotes', () => {
    assert.equal(escapeHtml('a<b>"c"&d'), 'a&lt;b&gt;&quot;c&quot;&amp;d');
    assert.equal(escapeHtml(''), '');
});

test('formatMcpValidation shows the failure cause and every detail line', () => {
    const text = formatMcpValidation({
        Name: 'docs', Connected: false, Method: 'http', ToolCount: 0, Tools: [], ElapsedMs: 9,
        Error: "Failed to connect to HTTP MCP server 'docs' at http://localhost:9: connection refused by localhost:9",
        Details: 'Request: POST http://localhost:9/mcp\r\nHint: start the MCP server',
    });
    assert.ok(text.startsWith('MCP server "docs": Failed (http, 9 ms)'));
    assert.ok(text.includes('connection refused by localhost:9'));
    assert.ok(text.includes('\nRequest: POST http://localhost:9/mcp\n'));
    assert.ok(text.includes('\nHint: start the MCP server\n'));
    assert.ok(!text.includes('\r'));
});

test('formatMcpValidation lists the tools of a connected server', () => {
    const text = formatMcpValidation({ Name: 'fs', Connected: true, Method: 'stdio', ToolCount: 2, Tools: ['fs.read', 'fs.write'], ElapsedMs: 40 });
    assert.ok(text.includes('Connected (stdio, 40 ms)'));
    assert.ok(text.includes('Tools (2):\n  fs.read\n  fs.write'));
    assert.ok(!text.includes('Failed'));
});
