/**
 * Pure helpers for presenting the operational prompt catalog in the management tree. Kept free of any
 * `vscode` dependency so the grouping and command-routing rules are unit-testable in isolation.
 */

import { PromptCatalogEntry } from '../api/types';

/** A catalog kind and the entries that belong to it, for a group node in the tree. */
export interface CatalogGroup {
    /** The kind wire string (for example `tool-description`). */
    kind: string;

    /** The entries in this kind, sorted by display name. */
    entries: PromptCatalogEntry[];
}

/**
 * The click command each catalog-related node kind runs. A global-scoped catalog entry edits its override;
 * a subagent-persona row deep-links to the existing subagent editor (personas are not catalog overrides).
 * Profile-scoped entries route through the same catalog-edit command, which redirects to the profile editor.
 */
export const CATALOG_CLICK_COMMANDS: Readonly<Record<string, string>> = {
    'catalog-entry': 'mux.manage.editCatalog',
    'catalog-subagent': 'mux.manage.editSubagent',
};

/**
 * Groups catalog entries by kind, kinds sorted alphabetically and entries within each kind sorted by display
 * name, so the tree renders a stable, grouped inventory.
 *
 * @param entries The flat catalog entries from the server.
 * @returns The entries grouped and sorted.
 */
export function groupCatalogByKind(entries: PromptCatalogEntry[]): CatalogGroup[] {
    const byKind = new Map<string, PromptCatalogEntry[]>();
    for (const entry of entries) {
        const list = byKind.get(entry.Kind) ?? [];
        list.push(entry);
        byKind.set(entry.Kind, list);
    }

    const groups: CatalogGroup[] = [];
    for (const kind of [...byKind.keys()].sort((a, b) => a.localeCompare(b))) {
        const list = (byKind.get(kind) ?? []).slice().sort((a, b) => a.DisplayName.localeCompare(b.DisplayName));
        groups.push({ kind, entries: list });
    }

    return groups;
}
