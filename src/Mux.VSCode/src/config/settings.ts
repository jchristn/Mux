import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { resolveLocale } from '../i18n/locales';

/** Which editor context sources a run may attach. */
export type ContextSource = 'activeFile' | 'selection' | 'diagnostics' | 'openTabs' | 'gitDiff' | 'symbols' | 'terminal';

/** How mutating tools are handled during a run. */
export type ApprovalPosture = 'prompt' | 'auto-safe';

/** How a large active file becomes context: inherit the server's setting, or force a mode. */
export type LargeFileMode = 'inherit' | 'map' | 'summarize' | 'truncate';

/** The extension's resolved settings. */
export interface MuxSettings {
    /** Start a local server when none is reachable. */
    autoStart: boolean;

    /** The server port; 0 means use the extension default. */
    port: number;

    /** The `mux` executable name or full path. */
    muxPath: string;

    /** The endpoint name to run against; empty means the server's default. */
    defaultEndpoint: string;

    /** How mutating tools are handled. */
    approvalPosture: ApprovalPosture;

    /** The enabled context sources. */
    contextSources: ContextSource[];

    /** How a large active file becomes context: inherit the server's setting, or force map/summarize/truncate. */
    largeFileMode: LargeFileMode;

    /** The display locale override; empty follows the editor language. */
    locale: string;
}

/** The extension's default server port, chosen to sit beside a user's own `mux serve`. */
export const DEFAULT_PORT = 8710;

/** The `rest` block from mux's shared `settings.json` — the single source of truth every surface uses. */
export interface SharedRest {
    /** The configured hub port, or undefined when unreadable. */
    port?: number;

    /** The shared API key (or null for no-auth), or undefined when unreadable. */
    apiKey?: string | null;
}

/**
 * Reads mux's shared `settings.json` (`MUX_CONFIG_DIR` or `~/.mux`) so the extension joins the SAME hub as
 * every other surface: same port, and — critically — the same `rest.apiKey`. Without this the extension used
 * its own key and could neither reuse the tray agent nor be reached by desktop/TUI runs. Returns an empty
 * object when the file is missing or unreadable (a fresh install with no server yet).
 */
export function readSharedRest(): SharedRest {
    try {
        const envDir = process.env.MUX_CONFIG_DIR;
        const dir = envDir && envDir.trim().length > 0 ? envDir : path.join(os.homedir(), '.mux');
        const raw = fs.readFileSync(path.join(dir, 'settings.json'), 'utf8');
        const json = JSON.parse(raw) as { rest?: { port?: unknown; apiKey?: unknown } };
        const rest = json.rest ?? {};
        return {
            port: typeof rest.port === 'number' && rest.port > 0 ? rest.port : undefined,
            apiKey: typeof rest.apiKey === 'string' ? rest.apiKey : null,
        };
    } catch {
        return {};
    }
}

/** Reads the current extension settings from the `mux` configuration section. */
export function readSettings(): MuxSettings {
    const config = vscode.workspace.getConfiguration('mux');
    return {
        autoStart: config.get<boolean>('server.autoStart', true),
        port: config.get<number>('server.port', 0),
        muxPath: config.get<string>('server.path', 'mux'),
        defaultEndpoint: config.get<string>('defaultEndpoint', ''),
        approvalPosture: config.get<ApprovalPosture>('approvalPosture', 'prompt'),
        contextSources: config.get<ContextSource[]>('context.sources', ['activeFile', 'selection', 'diagnostics']),
        largeFileMode: config.get<LargeFileMode>('context.largeFileMode', 'inherit'),
        locale: config.get<string>('locale', ''),
    };
}

/**
 * Resolves the display locale for formatting: the explicit `mux.locale` override, then the editor language,
 * then English, mapped through the extension's locale registry.
 *
 * @param settings The current settings.
 * @returns The BCP 47 code to format with.
 */
export function resolveDisplayLocale(settings: MuxSettings): string {
    const requested = settings.locale || vscode.env.language;
    return resolveLocale(requested).code;
}
