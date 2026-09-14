import * as vscode from 'vscode';
import { resolveLocale } from '../i18n/locales';

/** Which editor context sources a run may attach. */
export type ContextSource = 'activeFile' | 'selection' | 'diagnostics' | 'openTabs' | 'gitDiff' | 'terminal';

/** How mutating tools are handled during a run. */
export type ApprovalPosture = 'prompt' | 'auto-safe';

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

    /** The display locale override; empty follows the editor language. */
    locale: string;
}

/** The extension's default server port, chosen to sit beside a user's own `mux serve`. */
export const DEFAULT_PORT = 8710;

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
