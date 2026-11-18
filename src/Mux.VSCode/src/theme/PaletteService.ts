import * as vscode from 'vscode';
import { MuxServerLifecycle } from '../server/lifecycle';
import { FormField, FormPanel } from '../manage/FormPanel';
import { logError } from '../util/logger';
import { PALETTE_ROLES, PaletteDto, PaletteListDto, normalizePaletteMode, paletteCss, resolvePalette } from './palette';

/**
 * Owns the palette the extension's webviews render with. `mux.colorPalette` = `editor` (the default) follows the
 * VS Code theme; `mux` uses the shared palette chosen on any mux surface. Webviews read {@link PaletteService.css}
 * when they render and subscribe to {@link PaletteService.onDidChange} to restyle live.
 */
export class PaletteService implements vscode.Disposable {
    private static instance: PaletteService | undefined;
    private readonly changed = new vscode.EventEmitter<string>();
    private readonly disposables: vscode.Disposable[] = [];
    private list: PaletteListDto | undefined;
    private currentCss = '';

    /** Fires with the new CSS (empty to follow the editor theme) whenever the palette changes. */
    public readonly onDidChange = this.changed.event;

    /**
     * Creates the service and starts tracking the setting, the editor theme, and the server connection.
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     */
    public constructor(private readonly lifecycle: MuxServerLifecycle) {
        PaletteService.instance = this;
        this.disposables.push(
            this.changed,
            vscode.workspace.onDidChangeConfiguration((event) => {
                if (event.affectsConfiguration('mux.colorPalette')) {
                    void this.refresh();
                }
            }),
            vscode.window.onDidChangeActiveColorTheme(() => this.recompute()),
            lifecycle.onDidChangeState(() => void this.refresh()),
        );
        void this.refresh();
    }

    /** The CSS for the active palette, or an empty string when following the editor theme or before setup. */
    public static get css(): string {
        return PaletteService.instance?.currentCss ?? '';
    }

    /** Subscribes to palette changes from any webview; safe to call before the service exists (no-op). */
    public static subscribe(listener: (css: string) => void): vscode.Disposable {
        return PaletteService.instance ? PaletteService.instance.onDidChange(listener) : { dispose: () => undefined };
    }

    /** Re-reads the palette listing from the server when the setting asks for the mux palette. */
    public async refresh(): Promise<void> {
        if (this.mode() === 'editor') {
            this.list = undefined;
            this.recompute();
            return;
        }

        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            this.list = await client.getPalettes();
        } catch (error) {
            logError('Failed to read the mux color palettes.', error);
        }

        this.recompute();
    }

    /** Lets the user choose a palette: follow the VS Code theme, or any shared mux palette (saved for every surface). */
    public async pick(): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const list = await client.getPalettes();
            const mode = this.mode();
            const active = mode === 'mux' ? (list.Active ?? '') : '';
            type Item = vscode.QuickPickItem & { id: string };
            const items: Item[] = [
                { id: 'editor', label: vscode.l10n.t('Follow the VS Code theme'), description: mode === 'editor' ? vscode.l10n.t('active') : undefined },
                { id: 'system', label: vscode.l10n.t('mux: System (light or dark like the editor)'), description: active === 'system' ? vscode.l10n.t('active') : undefined },
                { id: '', label: '', kind: vscode.QuickPickItemKind.Separator },
                ...list.Palettes.map((p) => ({
                    id: p.Id,
                    label: p.Name,
                    description: [p.Dark ? vscode.l10n.t('dark') : vscode.l10n.t('light'), p.BuiltIn ? '' : vscode.l10n.t('custom'), p.Id === active ? vscode.l10n.t('active') : '']
                        .filter((s) => s).join(' · '),
                })),
                { id: '', label: '', kind: vscode.QuickPickItemKind.Separator },
                { id: '+edit', label: vscode.l10n.t('$(edit) Create or edit a custom palette…') },
            ];

            const picked = await vscode.window.showQuickPick(items, { placeHolder: vscode.l10n.t('Color palette for mux (shared with the terminal, desktop app, and web dashboard)') });
            if (!picked || picked.id === '') {
                return;
            }

            if (picked.id === '+edit') {
                await this.edit();
                return;
            }

            const config = vscode.workspace.getConfiguration('mux');
            if (picked.id === 'editor') {
                await config.update('colorPalette', 'editor', vscode.ConfigurationTarget.Global);
                return;
            }

            this.list = await client.setActivePalette(picked.id);
            if (this.mode() !== 'mux') {
                await config.update('colorPalette', 'mux', vscode.ConfigurationTarget.Global);
            }

            this.recompute();
        } catch (error) {
            logError('Failed to choose a color palette.', error);
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
        }
    }

    /** Creates a custom palette (starting from a chosen one) or edits an existing custom palette in a form. */
    public async edit(): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const list = await client.getPalettes();
            const seedPick = await vscode.window.showQuickPick(
                list.Palettes.map((p) => ({
                    label: p.Name,
                    description: p.BuiltIn ? vscode.l10n.t('start a new palette from this one') : vscode.l10n.t('edit'),
                    palette: p,
                })),
                { placeHolder: vscode.l10n.t('Start from (built-in) or edit (custom) a palette') },
            );
            if (!seedPick) {
                return;
            }

            const seed: PaletteDto = seedPick.palette;
            const editing = !seed.BuiltIn;
            const fields: FormField[] = [
                ...(editing ? [] : [{ key: 'Id', label: vscode.l10n.t('Id'), type: 'text' as const, value: '', required: true, hint: vscode.l10n.t('Lowercase kebab-case, for example my-palette.') }]),
                { key: 'Name', label: vscode.l10n.t('Name'), type: 'text', value: editing ? seed.Name : `${seed.Name} copy`, required: true },
                { key: 'Dark', label: vscode.l10n.t('Dark palette'), type: 'checkbox', value: seed.Dark },
                ...PALETTE_ROLES.map((role) => ({ key: `c_${role}`, label: role, type: 'text' as const, value: seed.Colors[role] ?? '', hint: vscode.l10n.t('Hex color such as #1e293b; blank inherits from the base palette.') })),
            ];
            if (editing) {
                fields.push({ key: 'Delete', label: vscode.l10n.t('Delete this palette instead of saving'), type: 'checkbox', value: false });
            }

            const result = await FormPanel.show(editing ? vscode.l10n.t('Edit color palette') : vscode.l10n.t('New color palette'), fields);
            if (!result) {
                return;
            }

            if (editing && result.Delete === true) {
                this.list = await client.deletePalette(seed.Id);
                this.recompute();
                return;
            }

            const colors: Record<string, string> = {};
            for (const role of PALETTE_ROLES) {
                const value = String(result[`c_${role}`] ?? '').trim();
                if (value) {
                    colors[role] = value;
                }
            }

            const id = editing ? seed.Id : String(result.Id ?? '').trim().toLowerCase();
            this.list = await client.putPalette({
                Id: id,
                Name: String(result.Name ?? '').trim(),
                Dark: result.Dark === true,
                Base: editing ? seed.Base ?? null : seed.Id,
                Colors: colors,
            }, true);
            if (this.mode() !== 'mux') {
                await vscode.workspace.getConfiguration('mux').update('colorPalette', 'mux', vscode.ConfigurationTarget.Global);
            }

            this.recompute();
        } catch (error) {
            logError('Failed to save the color palette.', error);
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
        }
    }

    public dispose(): void {
        if (PaletteService.instance === this) {
            PaletteService.instance = undefined;
        }

        this.disposables.forEach((d) => d.dispose());
    }

    private mode(): 'editor' | 'mux' {
        return normalizePaletteMode(vscode.workspace.getConfiguration('mux').get<string>('colorPalette', 'editor'));
    }

    private recompute(): void {
        const kind = vscode.window.activeColorTheme.kind;
        const editorIsDark = kind === vscode.ColorThemeKind.Dark || kind === vscode.ColorThemeKind.HighContrast;
        const css = this.mode() === 'mux' ? paletteCss(resolvePalette(this.list, editorIsDark)) : '';
        if (css !== this.currentCss) {
            this.currentCss = css;
            this.changed.fire(css);
        }
    }
}
