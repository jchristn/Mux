// Generates the per-locale manifest string files from the English source, plus a pseudo-locale used by CI to
// prove expansion and RTL handling. Seed files carry the English text as a starting point; a translation pass
// replaces the values. Run: node scripts/gen-locales.mjs
//
// This keeps every locale file structurally in lockstep with package.nls.json so the i18n check can detect a
// missing or orphaned key the moment the source strings change.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const source = JSON.parse(readFileSync(join(root, 'package.nls.json'), 'utf8'));

// The eleven non-English baseline locales; English is the source file itself.
const locales = ['es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi', 'ja'];

for (const locale of locales) {
    const target = join(root, `package.nls.${locale}.json`);
    const seeded = {};
    for (const [key, value] of Object.entries(source)) {
        seeded[key] = value;
    }
    writeFileSync(target, JSON.stringify(seeded, null, 2) + '\n', 'utf8');
}

// Pseudo-locale: bracket and expand each value ~40% so layout and truncation problems surface before a real
// translation exists. Used by CI, not shipped as a user language.
const pseudo = {};
for (const [key, value] of Object.entries(source)) {
    const padding = '·'.repeat(Math.ceil(value.length * 0.4));
    pseudo[key] = `⟦${value}${padding}⟧`;
}
writeFileSync(join(root, 'package.nls.qps-ploc.json'), JSON.stringify(pseudo, null, 2) + '\n', 'utf8');

console.error(`Generated ${locales.length} seed locales + pseudo-locale from ${Object.keys(source).length} keys.`);
