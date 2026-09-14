import { execFile } from 'child_process';
import * as path from 'path';
import * as vscode from 'vscode';
import { ContextSource } from '../config/settings';
import { logError } from '../util/logger';
import { ContextItem } from './composePrompt';

/** The result of collecting editor context. */
export interface CollectedContext {
    /** The attached items, in the order the sources were requested. */
    items: ContextItem[];

    /** Labels of sources that were requested but could not be provided, so the caller can say so. */
    unavailable: string[];
}

/**
 * Reads the requested editor context into items ready for {@link composePrompt}. A source with nothing to
 * offer (no selection, no diagnostics) is quietly skipped; a source the editor cannot provide (terminal
 * output, which the stable API does not expose) is reported as unavailable rather than dropped in silence.
 *
 * @param sources The enabled context sources.
 * @param workspaceRoot The workspace root used for git operations, or undefined when there is no folder.
 * @returns The collected items and any unavailable sources.
 */
export async function collectContext(sources: ContextSource[], workspaceRoot: string | undefined): Promise<CollectedContext> {
    const items: ContextItem[] = [];
    const unavailable: string[] = [];
    const editor = vscode.window.activeTextEditor;

    for (const source of sources) {
        switch (source) {
            case 'activeFile':
                if (editor) {
                    items.push({
                        kind: 'activeFile',
                        label: vscode.l10n.t('Active file: {0}', relativePath(editor.document.uri)),
                        content: editor.document.getText(),
                    });
                }
                break;

            case 'selection':
                if (editor && !editor.selection.isEmpty) {
                    items.push({
                        kind: 'selection',
                        label: vscode.l10n.t('Selection ({0})', relativePath(editor.document.uri)),
                        content: editor.document.getText(editor.selection),
                    });
                }
                break;

            case 'diagnostics':
                if (editor) {
                    const rendered = renderDiagnostics(editor.document.uri);
                    if (rendered) {
                        items.push({ kind: 'diagnostics', label: vscode.l10n.t('Diagnostics'), content: rendered });
                    }
                }
                break;

            case 'openTabs': {
                const open = vscode.window.tabGroups.all
                    .flatMap((group) => group.tabs)
                    .map((tab) => (tab.input instanceof vscode.TabInputText ? relativePath(tab.input.uri) : undefined))
                    .filter((value): value is string => value !== undefined);
                if (open.length > 0) {
                    items.push({ kind: 'openTabs', label: vscode.l10n.t('Open files'), content: open.join('\n') });
                }
                break;
            }

            case 'gitDiff': {
                const diff = await gitDiff(workspaceRoot);
                if (diff) {
                    items.push({ kind: 'gitDiff', label: vscode.l10n.t('Working-tree diff'), content: diff });
                }
                break;
            }

            case 'terminal':
                // The stable VS Code API does not expose a terminal's scrollback, so this source cannot be
                // honored yet. Report it rather than pretend it was attached.
                unavailable.push(vscode.l10n.t('Terminal output'));
                break;

            default:
                break;
        }
    }

    return { items, unavailable };
}

function relativePath(uri: vscode.Uri): string {
    return vscode.workspace.asRelativePath(uri, false);
}

function renderDiagnostics(uri: vscode.Uri): string {
    const diagnostics = vscode.languages.getDiagnostics(uri);
    if (diagnostics.length === 0) {
        return '';
    }

    return diagnostics
        .map((d) => {
            const line = d.range.start.line + 1;
            const severity = severityLabel(d.severity);
            return `${severity} [line ${line}]: ${d.message}`;
        })
        .join('\n');
}

function severityLabel(severity: vscode.DiagnosticSeverity): string {
    switch (severity) {
        case vscode.DiagnosticSeverity.Error:
            return 'error';
        case vscode.DiagnosticSeverity.Warning:
            return 'warning';
        case vscode.DiagnosticSeverity.Information:
            return 'info';
        default:
            return 'hint';
    }
}

function gitDiff(workspaceRoot: string | undefined): Promise<string> {
    if (!workspaceRoot) {
        return Promise.resolve('');
    }

    return new Promise((resolve) => {
        execFile('git', ['diff', '--no-color'], { cwd: workspaceRoot, maxBuffer: 4 * 1024 * 1024 }, (error, stdout) => {
            if (error) {
                logError('git diff failed while collecting context.', error);
                resolve('');
                return;
            }

            resolve(stdout ?? '');
        });
    });
}

/** Resolves the first workspace folder's path, or undefined when no folder is open. */
export function workspaceRootPath(): string | undefined {
    const folder = vscode.workspace.workspaceFolders?.[0];
    return folder ? path.normalize(folder.uri.fsPath) : undefined;
}
