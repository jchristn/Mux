import assert from 'node:assert/strict';
import * as path from 'node:path';
import { test } from 'node:test';

// The renderer ships as a plain webview script that also exports for CommonJS. Load it from source (the test
// compiles to CommonJS, so require/__dirname are available) so the test exercises the exact file the webview
// runs. From out/test/unit/, the source dashboard is three levels up.
// eslint-disable-next-line @typescript-eslint/no-var-requires
const { renderMarkdown } = require(path.join(__dirname, '..', '..', '..', 'dashboard', 'chat', 'markdown.js')) as {
    renderMarkdown: (md: string) => string;
};

test('escapes HTML so a reply cannot inject markup', () => {
    const html = renderMarkdown('<script>alert(1)</script> & <b>x</b>');
    assert.ok(!html.includes('<script>'), 'no raw script tag');
    assert.ok(html.includes('&lt;script&gt;'), 'script is escaped');
    assert.ok(html.includes('&amp;'), 'ampersand escaped');
});

test('renders bold, italic, and inline code', () => {
    const html = renderMarkdown('This is **bold**, *italic*, and `code`.');
    assert.ok(html.includes('<strong>bold</strong>'));
    assert.ok(html.includes('<em>italic</em>'));
    assert.ok(html.includes('<code>code</code>'));
});

test('does not format inside inline code', () => {
    const html = renderMarkdown('`a **b** c`');
    assert.ok(html.includes('<code>a **b** c</code>'), 'markdown inside code stays literal');
    assert.ok(!html.includes('<strong>'));
});

test('renders a fenced code block with the content escaped', () => {
    const html = renderMarkdown('```ts\nconst x = 1 < 2;\n```');
    assert.ok(html.includes('<pre><code class="language-ts">'));
    assert.ok(html.includes('const x = 1 &lt; 2;'));
});

test('renders headings and horizontal rules', () => {
    const html = renderMarkdown('# Title\n\n## Sub\n\n---');
    assert.ok(html.includes('<h1>Title</h1>'));
    assert.ok(html.includes('<h2>Sub</h2>'));
    assert.ok(html.includes('<hr />'));
});

test('renders unordered and ordered lists', () => {
    const ul = renderMarkdown('- one\n- two');
    assert.ok(ul.includes('<ul><li>one</li><li>two</li></ul>'));
    const ol = renderMarkdown('1. first\n2. second');
    assert.ok(ol.includes('<ol><li>first</li><li>second</li></ol>'));
});

test('links only safe schemes', () => {
    const ok = renderMarkdown('[docs](https://example.com)');
    assert.ok(ok.includes('<a href="https://example.com">docs</a>'));
    const bad = renderMarkdown('[x](javascript:alert(1))');
    assert.ok(!bad.includes('<a'), 'unsafe scheme is not linked');
    assert.ok(bad.includes('x'), 'label kept as text');
});

test('renders a blockquote and paragraphs', () => {
    const html = renderMarkdown('> quoted\n\nplain paragraph');
    assert.ok(html.includes('<blockquote>quoted</blockquote>'));
    assert.ok(html.includes('<p>plain paragraph</p>'));
});

test('preserves single newlines within a paragraph as line breaks', () => {
    const html = renderMarkdown('line one\nline two\nline three');
    assert.ok(html.includes('line one<br>line two<br>line three'), 'newlines become <br>');
    assert.ok(html.startsWith('<p>') && html.trim().endsWith('</p>'), 'still one paragraph');
});

test('separates paragraphs on a blank line', () => {
    const html = renderMarkdown('para one\n\npara two');
    assert.ok(html.includes('<p>para one</p>'));
    assert.ok(html.includes('<p>para two</p>'));
});

test('empty input yields empty output', () => {
    assert.equal(renderMarkdown(''), '');
});
