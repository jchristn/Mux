import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mixHex, normalizePaletteMode, paletteCss, paletteStyleTag, PaletteListDto, resolvePalette } from '../../src/theme/palette';

const midnight = {
    Id: 'midnight', Name: 'Midnight', Dark: true, BuiltIn: true,
    Colors: {
        background: '#0b1020', surface: '#121a2e', surfaceAlt: '#1b2540', border: '#2c3a5e', text: '#e6ecff', muted: '#8f9bc2',
        accent: '#7aa2ff', accentText: '#0b1020', cursor: '#7aa2ff', selection: '#26345c', success: '#5fd7a7',
        warning: '#f2c46d', error: '#ff7a90', info: '#6fd3ff',
    },
};
const mux = { ...midnight, Id: 'mux', Name: 'Mux' };
const muxLight = { ...midnight, Id: 'mux-light', Name: 'Mux Light', Dark: false };
const list = (active: string | null): PaletteListDto => ({ Active: active, DefaultId: 'mux', CursorBlink: true, Palettes: [mux, muxLight, midnight] });

test('normalizePaletteMode only accepts mux, everything else follows the editor', () => {
    assert.equal(normalizePaletteMode('mux'), 'mux');
    assert.equal(normalizePaletteMode(' MUX '), 'mux');
    assert.equal(normalizePaletteMode('editor'), 'editor');
    assert.equal(normalizePaletteMode(undefined), 'editor');
    assert.equal(normalizePaletteMode('midnight'), 'editor');
});

test('resolvePalette honors the shared choice, system, and the default', () => {
    assert.equal(resolvePalette(list('midnight'), true)?.Id, 'midnight');
    assert.equal(resolvePalette(list('system'), true)?.Id, 'mux');
    assert.equal(resolvePalette(list('system'), false)?.Id, 'mux-light');
    assert.equal(resolvePalette(list(null), false)?.Id, 'mux-light', 'an unset choice follows the editor kind');
    assert.equal(resolvePalette(list('gone'), true)?.Id, 'mux', 'unknown ids fall back to the default');
    assert.equal(resolvePalette(undefined, true), undefined);
});

test('paletteCss overrides the VS Code variables with !important and sets the caret', () => {
    const css = paletteCss(midnight);
    assert.match(css, /--vscode-editor-background: #0b1020 !important;/);
    assert.match(css, /--vscode-foreground: #e6ecff !important;/);
    assert.match(css, /--vscode-button-background: #7aa2ff !important;/);
    assert.match(css, /--mux-accent: #7aa2ff !important;/);
    assert.match(css, /caret-color: #7aa2ff/);
    assert.equal(paletteCss(undefined), '', 'no palette follows the editor theme');
});

test('paletteCss rejects non-hex values so a palette cannot inject CSS', () => {
    const hostile = { ...midnight, Colors: { ...midnight.Colors, text: 'red; } body { display:none', accent: '#abc' } };
    const css = paletteCss(hostile);
    assert.ok(!css.includes('display:none'), 'injected text never reaches the stylesheet');
    assert.match(css, /--vscode-foreground: #808080 !important;/, 'an invalid color falls back');
    assert.match(css, /--vscode-focusBorder: #aabbcc !important;/, 'short hex expands');
});

test('mixHex blends and paletteStyleTag carries the nonce', () => {
    assert.equal(mixHex('#000000', '#ffffff', 0.5), '#808080');
    assert.equal(mixHex('#000000', '#ffffff', 0), '#000000');
    assert.equal(mixHex('nope', '#ffffff', 0.5), 'nope');
    assert.equal(paletteStyleTag('abc', 'x{}'), '<style id="mux-palette" nonce="abc">x{}</style>');
    assert.ok(!paletteStyleTag('n', '</style><script>').includes('</style><script>'), 'a closing tag cannot escape the style element');
});
