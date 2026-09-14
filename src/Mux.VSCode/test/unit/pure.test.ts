import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ApiError } from '../../src/api/ApiError';
import { composePrompt, estimateTokens } from '../../src/context/composePrompt';
import { checkContract } from '../../src/server/contract';
import { formatList, formatRelativeTime, formatTokens } from '../../src/i18n/format';
import { resolveLocale } from '../../src/i18n/locales';

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
