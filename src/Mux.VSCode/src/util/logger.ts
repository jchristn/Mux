import * as vscode from 'vscode';

/**
 * The extension's output channel. Library code writes diagnostics here rather than to the console, so a user
 * can inspect what the extension did without a debugger and nothing leaks to the developer console.
 */
let channel: vscode.OutputChannel | undefined;

/** Creates the output channel. Called once on activation. */
export function initLogger(): vscode.OutputChannel {
    channel = vscode.window.createOutputChannel('mux');
    return channel;
}

/** Writes an informational line to the mux output channel. */
export function log(message: string): void {
    channel?.appendLine(`[${new Date().toISOString()}] ${message}`);
}

/**
 * Writes an error line, appending a readable detail from the error when one is supplied.
 *
 * @param message The context message.
 * @param error The caught error, if any.
 */
export function logError(message: string, error?: unknown): void {
    let detail = '';
    if (error instanceof Error) {
        detail = ` — ${error.name}: ${error.message}`;
    } else if (error !== undefined) {
        detail = ` — ${String(error)}`;
    }

    channel?.appendLine(`[${new Date().toISOString()}] ERROR ${message}${detail}`);
}
