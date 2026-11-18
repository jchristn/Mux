/**
 * Shared mux color palettes in the VS Code webviews (chat, usage, forms, about). By default the webviews follow
 * the VS Code theme; with `mux.colorPalette` set to `mux` they render in the palette chosen on any mux surface
 * (the shared `colorPalette` in settings.json, served by `GET /v1.0/api/palettes`). The mapping overrides the
 * `--vscode-*` variables the webviews already use, with `!important` so it wins over the values VS Code injects.
 * Pure functions only, so the mapping is unit-testable.
 */

/** One palette as served by `/v1.0/api/palettes`. */
export interface PaletteDto {
    Id: string;
    Name: string;
    Dark: boolean;
    BuiltIn: boolean;
    Base?: string | null;
    Colors: Record<string, string | null | undefined>;
}

/** The palette listing served by `/v1.0/api/palettes`. */
export interface PaletteListDto {
    Active?: string | null;
    DefaultId: string;
    CursorBlink: boolean;
    Palettes: PaletteDto[];
}

/** The fourteen palette roles, in display order. */
export const PALETTE_ROLES = [
    'background', 'surface', 'surfaceAlt', 'border', 'text', 'muted', 'accent', 'accentText',
    'cursor', 'selection', 'success', 'warning', 'error', 'info',
] as const;

/** Values of the `mux.colorPalette` setting. */
export type PaletteMode = 'editor' | 'mux';

/**
 * Normalizes a `mux.colorPalette` setting value; anything other than `mux` follows the editor theme.
 *
 * @param value The raw setting value.
 */
export function normalizePaletteMode(value: unknown): PaletteMode {
    return typeof value === 'string' && value.trim().toLowerCase() === 'mux' ? 'mux' : 'editor';
}

/**
 * Picks the palette to render from a listing: the shared active id (`system` follows the editor's light or dark
 * kind), falling back to the default palette.
 *
 * @param list The palette listing.
 * @param editorIsDark Whether the VS Code theme is dark (used for `system` and an unset choice).
 */
export function resolvePalette(list: PaletteListDto | undefined, editorIsDark: boolean): PaletteDto | undefined {
    if (!list || !Array.isArray(list.Palettes) || list.Palettes.length === 0) {
        return undefined;
    }

    let id = (list.Active ?? '').trim().toLowerCase();
    if (id === '' || id === 'system') {
        id = editorIsDark ? 'mux' : 'mux-light';
    }

    return list.Palettes.find((p) => p.Id === id) ?? list.Palettes.find((p) => p.Id === list.DefaultId) ?? list.Palettes[0];
}

/**
 * Blends two `#rrggbb` colors; `amount` 0 returns `from`, 1 returns `to`. Invalid input returns `from`.
 *
 * @param from The starting color.
 * @param to The target color.
 * @param amount The blend amount (0 to 1).
 */
export function mixHex(from: string, to: string, amount: number): string {
    const a = parseHex(from);
    const b = parseHex(to);
    if (!a || !b) {
        return from;
    }

    const t = Math.min(1, Math.max(0, amount));
    return '#' + a.map((v, i) => Math.round(v + (b[i] - v) * t).toString(16).padStart(2, '0')).join('');
}

/**
 * The CSS that recolors a webview in a palette, or an empty string to follow the VS Code theme.
 *
 * @param palette The palette, or undefined for the editor theme.
 */
export function paletteCss(palette: PaletteDto | undefined): string {
    if (!palette || !palette.Colors) {
        return '';
    }

    const c = (role: string, fallback = '#808080'): string => sanitize(palette.Colors[role]) ?? fallback;
    const background = c('background');
    const surface = c('surface', background);
    const surfaceAlt = c('surfaceAlt', surface);
    const text = c('text');
    const accent = c('accent');
    const hover = mixHex(surface, text, 0.07);
    const vars: Record<string, string> = {
        '--vscode-editor-background': background,
        '--vscode-sideBar-background': surface,
        '--vscode-editorWidget-background': surface,
        '--vscode-foreground': text,
        '--vscode-descriptionForeground': c('muted'),
        '--vscode-disabledForeground': c('muted'),
        '--vscode-panel-border': c('border'),
        '--vscode-widget-border': c('border'),
        '--vscode-editorWidget-border': c('border'),
        '--vscode-input-border': c('border'),
        '--vscode-dropdown-border': c('border'),
        '--vscode-focusBorder': accent,
        '--vscode-textLink-foreground': accent,
        '--vscode-charts-green': c('success'),
        '--vscode-charts-red': c('error'),
        '--vscode-errorForeground': c('error'),
        '--vscode-inputValidation-errorBorder': c('error'),
        '--vscode-inputValidation-errorBackground': mixHex(background, c('error'), 0.15),
        '--vscode-charts-yellow': c('warning'),
        '--vscode-charts-orange': c('warning'),
        '--vscode-notificationsWarningIcon-foreground': c('warning'),
        '--vscode-charts-blue': c('info'),
        '--vscode-charts-purple': mixHex(c('info'), accent, 0.5),
        '--vscode-button-background': accent,
        '--vscode-button-foreground': c('accentText'),
        '--vscode-button-hoverBackground': mixHex(accent, text, 0.15),
        '--vscode-button-secondaryBackground': surfaceAlt,
        '--vscode-button-secondaryForeground': text,
        '--vscode-button-secondaryHoverBackground': mixHex(surfaceAlt, text, 0.08),
        '--vscode-input-background': surfaceAlt,
        '--vscode-input-foreground': text,
        '--vscode-dropdown-background': surfaceAlt,
        '--vscode-dropdown-foreground': text,
        '--vscode-textCodeBlock-background': surfaceAlt,
        '--vscode-list-hoverBackground': hover,
        '--vscode-toolbar-hoverBackground': hover,
        '--vscode-list-activeSelectionBackground': c('selection'),
        '--vscode-list-activeSelectionForeground': text,
        '--vscode-editor-selectionBackground': c('selection'),
        '--vscode-badge-background': accent,
        '--vscode-badge-foreground': c('accentText'),
        '--mux-accent': accent,
        '--mux-cursor': c('cursor', accent),
    };

    const declarations = Object.entries(vars).map(([name, value]) => `  ${name}: ${value} !important;`).join('\n');
    return `html {\n${declarations}\n}\n`
        + `html, body { background: ${background} !important; color: ${text} !important; }\n`
        + `textarea, input { caret-color: ${c('cursor', accent)}; }\n`
        + `::selection { background: ${c('selection')}; color: ${text}; }\n`;
}

/**
 * The script a webview runs to swap its palette stylesheet when the extension posts `{ type: 'muxPalette', css }`.
 * The `<style id="mux-palette">` element carries the page nonce, so updating its text stays within the CSP.
 */
export const PALETTE_UPDATE_SCRIPT = `window.addEventListener('message', function (event) {
  var m = event.data;
  if (!m || m.type !== 'muxPalette') return;
  var s = document.getElementById('mux-palette');
  if (s) s.textContent = m.css || '';
});`;

/**
 * The `<style>` element a webview embeds for the current palette.
 *
 * @param nonce The page's CSP nonce.
 * @param css The palette CSS (empty to follow the editor theme).
 */
export function paletteStyleTag(nonce: string, css: string): string {
    return `<style id="mux-palette" nonce="${nonce}">${css.replace(/<\//g, '<\\/')}</style>`;
}

function parseHex(value: string): number[] | undefined {
    const normalized = sanitize(value);
    if (!normalized) {
        return undefined;
    }

    return [1, 3, 5].map((i) => parseInt(normalized.substring(i, i + 2), 16));
}

// Accepts #rgb or #rrggbb (with or without '#') and returns lowercase #rrggbb; anything else is rejected so a
// palette value can never inject CSS.
function sanitize(value: string | null | undefined): string | undefined {
    if (typeof value !== 'string') {
        return undefined;
    }

    let hex = value.trim().replace(/^#/, '');
    if (/^[0-9a-fA-F]{3}$/.test(hex)) {
        hex = hex.split('').map((ch) => ch + ch).join('');
    }

    return /^[0-9a-fA-F]{6}$/.test(hex) ? '#' + hex.toLowerCase() : undefined;
}
