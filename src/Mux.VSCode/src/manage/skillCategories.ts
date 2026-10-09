/**
 * Pure helpers for skill categories in the management tree: grouping skills by their effective category in the
 * canonical order, and normalizing typed categories the way the server does. Kept free of the vscode module so
 * they are unit-testable.
 */

/** The canonical categories, in the order surfaces offer them (mirrors Mux.Core SkillCategories.Known). */
export const KNOWN_SKILL_CATEGORIES: readonly string[] = [
    'git', 'review', 'testing', 'debugging', 'languages', 'frontend', 'devops', 'containers', 'kubernetes', 'cloud',
    'infrastructure', 'security', 'data', 'docs', 'scaffolding', 'hygiene', 'workflow', 'loops', 'engineering',
    'product', 'productivity', 'research', 'marketing', 'compliance', 'business', 'general',
];

/** A category and the skills in it. */
export interface SkillCategoryGroup<T> {
    category: string;
    skills: T[];
}

/**
 * Normalizes typed input the way the server does: trims, lowercases, and turns spaces and underscores into single
 * hyphens. Returns undefined for blank input (which means "clear the override").
 *
 * @param value The typed value.
 * @returns The normalized category, or undefined when blank.
 */
export function normalizeSkillCategory(value: string | undefined | null): string | undefined {
    if (!value || value.trim().length === 0) {
        return undefined;
    }

    const normalized = value.trim().toLowerCase().replace(/[\s_-]+/g, '-').replace(/^-+|-+$/g, '');
    return normalized.length === 0 ? undefined : normalized;
}

/**
 * Whether a normalized category is well-formed: letters and digits separated by single hyphens, at most 40
 * characters.
 *
 * @param value The category.
 * @returns True when well-formed.
 */
export function isValidSkillCategory(value: string): boolean {
    return value.length <= 40 && /^[a-z0-9]+(-[a-z0-9]+)*$/.test(value);
}

/**
 * Groups skills by category: canonical categories first in canonical order, then any others alphabetically.
 * Skills without a category fall into `general`.
 *
 * @param skills The skills.
 * @returns The groups.
 */
export function groupSkillsByCategory<T extends { Category?: string }>(skills: readonly T[]): SkillCategoryGroup<T>[] {
    const map = new Map<string, T[]>();
    for (const skill of skills) {
        const category = skill.Category && skill.Category.length > 0 ? skill.Category : 'general';
        const list = map.get(category) ?? [];
        list.push(skill);
        map.set(category, list);
    }

    const order = (category: string): number => {
        const index = KNOWN_SKILL_CATEGORIES.indexOf(category);
        return index < 0 ? KNOWN_SKILL_CATEGORIES.length : index;
    };

    return [...map.entries()]
        .sort((a, b) => (order(a[0]) !== order(b[0]) ? order(a[0]) - order(b[0]) : a[0].localeCompare(b[0])))
        .map(([category, list]) => ({ category, skills: list }));
}
