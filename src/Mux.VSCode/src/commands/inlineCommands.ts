import { execFile } from 'child_process';
import * as vscode from 'vscode';
import { ChatViewProvider } from '../chat/ChatViewProvider';
import { workspaceRootPath } from '../context/providers';

/**
 * The inline commands that turn a location in the editor into a mux run without composing a prompt. Each one
 * builds a request from what the editor already knows and funnels it through the chat panel, so a user reads
 * the answer in the same place as a typed conversation. The selection-anchored commands are also offered as
 * code actions by {@link MuxCodeActionProvider}.
 */
export function registerInlineCommands(context: vscode.ExtensionContext, chat: ChatViewProvider): void {
    const run = (text: string): Promise<void> => chat.runFromCommand(text);

    context.subscriptions.push(
        vscode.commands.registerCommand('mux.explainSelection', () => {
            const selection = selectedText();
            if (selection) {
                void run(vscode.l10n.t('Explain this code:\n\n{0}', selection));
            }
        }),
        vscode.commands.registerCommand('mux.refactorSelection', () => {
            const selection = selectedText();
            if (selection) {
                void run(vscode.l10n.t('Refactor this code for clarity, keeping behavior identical:\n\n{0}', selection));
            }
        }),
        vscode.commands.registerCommand('mux.generateTests', () => {
            const selection = selectedText() ?? activeFileText();
            if (selection) {
                void run(vscode.l10n.t('Write tests for this code, including error and edge cases:\n\n{0}', selection));
            }
        }),
        vscode.commands.registerCommand('mux.fixDiagnostic', () => {
            const described = describeNearestDiagnostic();
            if (described) {
                void run(vscode.l10n.t('Fix this problem:\n\n{0}', described));
            } else {
                void vscode.window.showInformationMessage(vscode.l10n.t('No diagnostic at the cursor.'));
            }
        }),
        vscode.commands.registerCommand('mux.reviewFile', () => {
            const text = activeFileText();
            if (text) {
                void run(vscode.l10n.t('Review this file for bugs, risks, and clarity, and summarize what you find.'));
            }
        }),
        vscode.commands.registerCommand('mux.summarizeDiff', () => {
            void run(vscode.l10n.t('Summarize the working-tree changes.'));
        }),
        vscode.commands.registerCommand('mux.commitMessage', async () => {
            const staged = await stagedDiff(workspaceRootPath());
            if (!staged) {
                void vscode.window.showInformationMessage(vscode.l10n.t('Nothing is staged to describe.'));
                return;
            }

            void run(vscode.l10n.t('Write a concise conventional-commit message for this staged diff:\n\n{0}', staged));
        }),
    );
}

/** Offers the selection-anchored commands as code actions on a non-empty selection or a diagnostic. */
export class MuxCodeActionProvider implements vscode.CodeActionProvider {
    public static readonly selector: vscode.DocumentSelector = { scheme: 'file' };

    public provideCodeActions(
        _document: vscode.TextDocument,
        range: vscode.Range | vscode.Selection,
        context: vscode.CodeActionContext,
    ): vscode.CodeAction[] {
        const actions: vscode.CodeAction[] = [];
        if (!range.isEmpty) {
            actions.push(this.action(vscode.l10n.t('mux: Explain selection'), 'mux.explainSelection'));
            actions.push(this.action(vscode.l10n.t('mux: Refactor selection'), 'mux.refactorSelection'));
        }
        if (context.diagnostics.length > 0) {
            actions.push(this.action(vscode.l10n.t('mux: Fix this problem'), 'mux.fixDiagnostic'));
        }

        return actions;
    }

    private action(title: string, command: string): vscode.CodeAction {
        const action = new vscode.CodeAction(title, vscode.CodeActionKind.QuickFix);
        action.command = { command, title };
        return action;
    }
}

function selectedText(): string | undefined {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.selection.isEmpty) {
        return undefined;
    }

    return editor.document.getText(editor.selection);
}

function activeFileText(): string | undefined {
    return vscode.window.activeTextEditor?.document.getText();
}

function describeNearestDiagnostic(): string | undefined {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        return undefined;
    }

    const diagnostics = vscode.languages.getDiagnostics(editor.document.uri);
    const cursor = editor.selection.active;
    const hit = diagnostics.find((d) => d.range.contains(cursor)) ?? diagnostics[0];
    if (!hit) {
        return undefined;
    }

    const line = hit.range.start.line;
    const snippet = editor.document.lineAt(line).text;
    return `${hit.message}\n\nAt ${vscode.workspace.asRelativePath(editor.document.uri, false)}:${line + 1}\n${snippet}`;
}

function stagedDiff(cwd: string | undefined): Promise<string> {
    if (!cwd) {
        return Promise.resolve('');
    }

    return new Promise((resolve) => {
        execFile('git', ['diff', '--cached', '--no-color'], { cwd, maxBuffer: 4 * 1024 * 1024 }, (error, stdout) => {
            resolve(error ? '' : stdout ?? '');
        });
    });
}
