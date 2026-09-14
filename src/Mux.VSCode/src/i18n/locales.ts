/**
 * The locale registry for the extension. Each entry records the BCP 47 code, its English name and autonym,
 * writing direction, and fallback. `en` is the source and the ultimate fallback. The set matches the mux
 * desktop app's reach so a user sees the editor in the same language as the rest of mux.
 */

/** Writing direction for a locale. */
export type TextDirection = 'ltr' | 'rtl';

/** A registered locale. */
export interface LocaleEntry {
    /** BCP 47 code. */
    code: string;

    /** English name. */
    englishName: string;

    /** The language's own name for itself. */
    autonym: string;

    /** Writing direction. */
    direction: TextDirection;

    /** The code to fall back to for a missing key; `en` for every non-English locale. */
    fallback: string;
}

/** The twelve baseline locales. `en` is source and fallback. */
export const LOCALES: readonly LocaleEntry[] = [
    { code: 'en', englishName: 'English', autonym: 'English', direction: 'ltr', fallback: 'en' },
    { code: 'es', englishName: 'Spanish', autonym: 'Español', direction: 'ltr', fallback: 'en' },
    { code: 'pt', englishName: 'Portuguese', autonym: 'Português', direction: 'ltr', fallback: 'en' },
    { code: 'fr', englishName: 'French', autonym: 'Français', direction: 'ltr', fallback: 'en' },
    { code: 'it', englishName: 'Italian', autonym: 'Italiano', direction: 'ltr', fallback: 'en' },
    { code: 'de', englishName: 'German', autonym: 'Deutsch', direction: 'ltr', fallback: 'en' },
    { code: 'zh', englishName: 'Chinese (Simplified)', autonym: '简体中文', direction: 'ltr', fallback: 'en' },
    { code: 'ar', englishName: 'Arabic', autonym: 'العربية', direction: 'rtl', fallback: 'en' },
    { code: 'ru', englishName: 'Russian', autonym: 'Русский', direction: 'ltr', fallback: 'en' },
    { code: 'ms', englishName: 'Malay', autonym: 'Bahasa Melayu', direction: 'ltr', fallback: 'en' },
    { code: 'hi', englishName: 'Hindi', autonym: 'हिन्दी', direction: 'ltr', fallback: 'en' },
    { code: 'ja', englishName: 'Japanese', autonym: '日本語', direction: 'ltr', fallback: 'en' },
];

/**
 * Resolves a requested locale to a supported one, falling back to the base language (`pt-BR` → `pt`) and
 * finally to `en`.
 *
 * @param requested The requested BCP 47 code, or empty to use the default.
 * @returns The matching registry entry.
 */
export function resolveLocale(requested: string | undefined): LocaleEntry {
    const wanted = (requested ?? '').trim().toLowerCase();
    if (!wanted) {
        return LOCALES[0];
    }

    const exact = LOCALES.find((l) => l.code.toLowerCase() === wanted);
    if (exact) {
        return exact;
    }

    const base = wanted.split('-')[0];
    const byBase = LOCALES.find((l) => l.code.toLowerCase() === base);
    return byBase ?? LOCALES[0];
}
