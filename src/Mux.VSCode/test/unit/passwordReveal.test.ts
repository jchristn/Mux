import assert from 'node:assert/strict';
import { test } from 'node:test';
import { passwordControlHtml, REVEAL_CSS, REVEAL_ICON_HIDE, REVEAL_ICON_SHOW, REVEAL_SCRIPT } from '../../src/manage/passwordReveal';

test('passwordControlHtml renders a masked input with a labeled eye button', () => {
    const html = passwordControlHtml('f_ApiKey', '••••••• (unchanged)', { show: 'Show key', hide: 'Hide key' });
    assert.match(html, /<input type="password" id="f_ApiKey"/);
    assert.match(html, /placeholder="••••••• \(unchanged\)"/);
    assert.match(html, /class="pwreveal"/);
    assert.match(html, /type="button"/, 'the toggle never submits the form');
    assert.match(html, /aria-label="Show key"/);
    assert.match(html, /data-hide="Hide key"/);
    assert.ok(html.includes(REVEAL_ICON_SHOW), 'starts with the eye icon');
    assert.ok(!html.includes(REVEAL_ICON_HIDE));
});

test('passwordControlHtml escapes labels and ids', () => {
    const html = passwordControlHtml('a"b', '', { show: 'Show <b>"key"</b>', hide: 'Hide & mask' });
    assert.match(html, /id="a&quot;b"/);
    assert.match(html, /aria-label="Show &lt;b&gt;&quot;key&quot;&lt;\/b&gt;"/);
    assert.match(html, /data-hide="Hide &amp; mask"/);
});

test('the toggle keeps text clear of the button and flips type, icon, and label', () => {
    assert.match(REVEAL_CSS, /padding-inline-end: 34px/);
    assert.match(REVEAL_CSS, /inset-inline-end/);
    assert.match(REVEAL_SCRIPT, /input\.type = reveal \? 'text' : 'password'/);
    assert.match(REVEAL_SCRIPT, /data-hide/);
    assert.match(REVEAL_SCRIPT, /preventDefault/, 'clicking does not steal focus from the field');
});
