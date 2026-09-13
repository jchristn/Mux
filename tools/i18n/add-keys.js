// Fold new-key files (scratchpad/keys-*.json, each key->English) into desktop-i18n.js as en-only
// entries (skipping keys already present). Then rewrite desktop-i18n.js. Run:
//   node tools/i18n/add-keys.js <scratchpadDir>
// Afterwards: emit todo, translate, merge, gen.
const fs = require('fs');
const path = require('path');

const ORDER = ['en', 'es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi'];
const scratch = process.argv[2];
if (!scratch) { throw new Error('usage: node add-keys.js <scratchpadDir>'); }

const catPath = path.join(__dirname, 'desktop-i18n.js');
const cat = require('./desktop-i18n.js');

const files = fs.readdirSync(scratch).filter(f => /^keys-.*\.json$/.test(f));
let added = 0, skipped = 0, collisions = [];
for (const f of files) {
  const map = JSON.parse(fs.readFileSync(path.join(scratch, f), 'utf8'));
  for (const key of Object.keys(map)) {
    const en = map[key];
    if (typeof en !== 'string' || en.length === 0) { continue; }
    if (cat[key]) {
      // Already present. If English differs, flag it (possible key clash across areas).
      if (cat[key].en !== en) { collisions.push(f + ':' + key + ' ("' + cat[key].en + '" vs "' + en + '")'); }
      skipped++;
      continue;
    }
    cat[key] = { en: en };
    added++;
  }
}
console.error('Files:', files.join(', '));
console.error('Added:', added, 'Skipped(existing):', skipped);
if (collisions.length) { console.error('COLLISIONS (same key, different English):\n  ' + collisions.join('\n  ')); }

// Rewrite desktop-i18n.js self-contained.
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
console.error('Rewrote', catPath, Object.keys(cat).length, 'total keys');
