import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PromptCatalogEntry } from '../../src/api/types';
import { CATALOG_CLICK_COMMANDS, groupCatalogByKind } from '../../src/manage/catalog';

function entry(partial: Partial<PromptCatalogEntry>): PromptCatalogEntry {
    return {
        Key: 'k',
        Kind: 'tool-description',
        Scope: 'Global',
        DisplayName: 'Name',
        Description: '',
        Placeholders: [],
        Default: 'd',
        Effective: 'd',
        Overridden: false,
        Editable: true,
        ...partial,
    };
}

test('groupCatalogByKind sorts kinds alphabetically and entries by display name', () => {
    const groups = groupCatalogByKind([
        entry({ Kind: 'tool-description', DisplayName: 'grep description' }),
        entry({ Kind: 'compaction', DisplayName: 'Compaction user framing' }),
        entry({ Kind: 'tool-description', DisplayName: 'edit_file description' }),
    ]);

    assert.deepEqual(groups.map((g) => g.kind), ['compaction', 'tool-description']);
    assert.deepEqual(groups[1].entries.map((e) => e.DisplayName), ['edit_file description', 'grep description']);
});

test('groupCatalogByKind returns an empty list for no entries', () => {
    assert.deepEqual(groupCatalogByKind([]), []);
});

test('catalog node kinds bind their matching management commands', () => {
    assert.equal(CATALOG_CLICK_COMMANDS['catalog-entry'], 'mux.manage.editCatalog');
    assert.equal(CATALOG_CLICK_COMMANDS['catalog-subagent'], 'mux.manage.editSubagent');
});
