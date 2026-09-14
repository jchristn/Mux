// Fails when the manifest references a string key with no English entry, when an English key is orphaned (no
// manifest reference), or when a locale file's key set drifts from English. Wire into CI so a new hard-coded
// %key% or a stale translation file breaks the build rather than shipping. Run: node scripts/i18n-check.mjs

import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');

const manifest = readFileSync(join(root, 'package.json'), 'utf8');
const english = JSON.parse(readFileSync(join(root, 'package.nls.json'), 'utf8'));
const englishKeys = new Set(Object.keys(english));

const referenced = new Set([...manifest.matchAll(/%([A-Za-z0-9_.]+)%/g)].map((m) => m[1]));

const problems = [];

for (const key of referenced) {
    if (!englishKeys.has(key)) {
        problems.push(`Manifest references %${key}% but package.nls.json has no such key.`);
    }
}

for (const key of englishKeys) {
    if (!referenced.has(key)) {
        problems.push(`package.nls.json defines "${key}" but the manifest never references it (orphan).`);
    }
}

const locales = ['es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi', 'ja', 'qps-ploc'];
for (const locale of locales) {
    const file = join(root, `package.nls.${locale}.json`);
    if (!existsSync(file)) {
        problems.push(`Missing locale file package.nls.${locale}.json (run scripts/gen-locales.mjs).`);
        continue;
    }

    const localeKeys = new Set(Object.keys(JSON.parse(readFileSync(file, 'utf8'))));
    for (const key of englishKeys) {
        if (!localeKeys.has(key)) {
            problems.push(`Locale ${locale} is missing key "${key}".`);
        }
    }
    for (const key of localeKeys) {
        if (!englishKeys.has(key)) {
            problems.push(`Locale ${locale} has orphan key "${key}".`);
        }
    }
}

if (problems.length > 0) {
    for (const problem of problems) {
        console.error(`i18n: ${problem}`);
    }
    process.exit(1);
}

console.error(`i18n check passed: ${englishKeys.size} keys, ${locales.length} locales in lockstep.`);
