// Merge per-language translation files (scratchpad/tr-<lang>.json) into the authored catalog
// (desktop-i18n.js), then rewrite desktop-i18n.js self-contained. Run:
//   node tools/i18n/merge.js <scratchpadDir>
// After merging, run tools/i18n/gen.js to regenerate DesktopStrings.cs.
const fs = require('fs');
const path = require('path');

const LANGS = ['es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi'];
const ORDER = ['en', ...LANGS];
const scratch = process.argv[2];
if (!scratch) { throw new Error('usage: node merge.js <scratchpadDir>'); }

const catPath = path.join(__dirname, 'desktop-i18n.js');
const cat = require('./desktop-i18n.js');

let applied = {};
for (const lang of LANGS) {
  const p = path.join(scratch, 'tr-' + lang + '.json');
  if (!fs.existsSync(p)) { console.error('WARNING: missing', p); continue; }
  const tr = JSON.parse(fs.readFileSync(p, 'utf8'));
  let n = 0;
  for (const key of Object.keys(tr)) {
    if (cat[key]) {
      const v = tr[key];
      if (typeof v === 'string' && v.length > 0) { cat[key][lang] = v; n++; }
    }
  }
  applied[lang] = n;
}
console.error('Applied per language:', JSON.stringify(applied));

// Rewrite desktop-i18n.js self-contained (one key per line, en first).
let out = '';
out += '// Authored desktop translation table. Edit here, then run: node tools/i18n/gen.js\n';
out += '// Shape: { "<key>": { en, es, pt, fr, it, de, zh, ar, ru, ms, hi } }\n';
out += '// - en is REQUIRED for every key. Omit a locale to fall back to English at runtime.\n';
out += '// - A key named after a dashboard key (e.g. "nav.endpoints") OVERRIDES the shared dashboard pack.\n';
out += '// Translations for non-en locales are produced by tools/i18n translate + merge passes.\n';
out += '// Languages: en es pt fr it de zh ar ru ms hi (same set as the mux serve dashboard).\n\n';
out += 'module.exports = {\n';
for (const key of Object.keys(cat)) {
  const entry = cat[key];
  const parts = [];
  for (const lang of ORDER) {
    if (typeof entry[lang] === 'string' && entry[lang].length > 0) {
      parts.push(lang + ':' + JSON.stringify(entry[lang]));
    }
  }
  out += '  ' + JSON.stringify(key) + ': { ' + parts.join(', ') + ' },\n';
}
out += '};\n';

fs.writeFileSync(catPath, out, 'utf8');
console.error('Rewrote', catPath, out.length, 'bytes');
