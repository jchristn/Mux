import * as vscode from 'vscode';
import { ChatViewProvider } from './chat/ChatViewProvider';
import { EndpointPicker } from './config/endpointPicker';
import { MuxCodeActionProvider, registerInlineCommands } from './commands/inlineCommands';
import { workspaceRootPath } from './context/providers';
import { ManageActions } from './manage/actions';
import { ManageNode, ManageTreeProvider } from './manage/ManageTree';
import { MuxServerLifecycle } from './server/lifecycle';
import { ConnectionStatusBar } from './server/StatusBar';
import { SessionNode, SessionTreeProvider } from './sessions/SessionTree';
import { initLogger, log } from './util/logger';

/**
 * Activates the extension: wires the server lifecycle, the chat panel, the session tree, the endpoint picker,
 * and every contributed command. Activation itself does no network work — the first run or view interaction
 * triggers the connection — so a missing server never blocks the editor from starting.
 *
 * @param context The extension context.
 */
export function activate(context: vscode.ExtensionContext): void {
    context.subscriptions.push(initLogger());
    log('mux extension activating.');

    const lifecycle = new MuxServerLifecycle(context);
    context.subscriptions.push(lifecycle);

    const chat = new ChatViewProvider(context.extensionUri, lifecycle);
    context.subscriptions.push(vscode.window.registerWebviewViewProvider(ChatViewProvider.viewType, chat));

    const sessions = new SessionTreeProvider(lifecycle, (id) => chat.loadSession(id));
    context.subscriptions.push(vscode.window.registerTreeDataProvider('mux.sessions', sessions));

    const endpointPicker = new EndpointPicker(lifecycle);
    context.subscriptions.push(endpointPicker);

    context.subscriptions.push(new ConnectionStatusBar(lifecycle));

    const manage = new ManageTreeProvider(lifecycle);
    context.subscriptions.push(vscode.window.registerTreeDataProvider('mux.manage', manage));
    const manageActions = new ManageActions(lifecycle, () => manage.refresh());
    context.subscriptions.push(
        vscode.commands.registerCommand('mux.manage.refresh', () => manage.refresh()),
        vscode.commands.registerCommand('mux.manage.addEndpoint', () => manageActions.addEndpoint()),
        vscode.commands.registerCommand('mux.manage.editEndpoint', (node: ManageNode) => manageActions.editEndpoint(node)),
        vscode.commands.registerCommand('mux.manage.deleteEndpoint', (node: ManageNode) => manageActions.deleteEndpoint(node)),
        vscode.commands.registerCommand('mux.manage.setDefaultEndpoint', (node: ManageNode) => manageActions.setDefaultEndpoint(node)),
        vscode.commands.registerCommand('mux.manage.addMcp', () => manageActions.addMcpServer()),
        vscode.commands.registerCommand('mux.manage.editMcp', (node: ManageNode) => manageActions.editMcpServer(node)),
        vscode.commands.registerCommand('mux.manage.deleteMcp', (node: ManageNode) => manageActions.deleteMcpServer(node)),
        vscode.commands.registerCommand('mux.manage.activatePrompt', (node: ManageNode) => manageActions.activatePrompt(node)),
        vscode.commands.registerCommand('mux.manage.toggleSkill', (node: ManageNode) => manageActions.toggleSkill(node)),
        vscode.commands.registerCommand('mux.manage.editSettings', () => manageActions.editSettings()),
    );

    // Refresh management and endpoint views after a run, since a turn may have changed config or usage.
    lifecycle.onDidChangeState(() => manage.refresh());

    registerInlineCommands(context, chat);
    context.subscriptions.push(
        vscode.languages.registerCodeActionsProvider(MuxCodeActionProvider.selector, new MuxCodeActionProvider(), {
            providedCodeActionKinds: [vscode.CodeActionKind.QuickFix],
        }),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('mux.newConversation', () => chat.newConversation()),
        vscode.commands.registerCommand('mux.reconnect', () => lifecycle.reconnect(new vscode.CancellationTokenSource().token)),
        vscode.commands.registerCommand('mux.selectEndpoint', () => endpointPicker.pick()),
        vscode.commands.registerCommand('mux.setWorkingDirectory', () => showWorkingDirectory()),
        vscode.commands.registerCommand('mux.reviewChanges', () => vscode.commands.executeCommand('workbench.view.scm')),
        vscode.commands.registerCommand('mux.undo', () => undoRedo(lifecycle, 'undo')),
        vscode.commands.registerCommand('mux.redo', () => undoRedo(lifecycle, 'redo')),
        vscode.commands.registerCommand('mux.sessions.refresh', () => sessions.refresh()),
        vscode.commands.registerCommand('mux.sessions.resume', (node: SessionNode) => sessions.resume(node)),
        vscode.commands.registerCommand('mux.sessions.rename', (node: SessionNode) => sessions.rename(node)),
        vscode.commands.registerCommand('mux.sessions.duplicate', (node: SessionNode) => sessions.duplicate(node)),
        vscode.commands.registerCommand('mux.sessions.export', (node: SessionNode) => sessions.export(node)),
        vscode.commands.registerCommand('mux.sessions.delete', (node: SessionNode) => sessions.delete(node)),
    );

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration((event) => {
            if (event.affectsConfiguration('mux.defaultEndpoint')) {
                endpointPicker.refresh();
            }
        }),
    );
}

/** Deactivates the extension. Disposables registered on the context clean up the server and UI. */
export function deactivate(): void {
    log('mux extension deactivating.');
}

async function undoRedo(lifecycle: MuxServerLifecycle, action: 'undo' | 'redo'): Promise<void> {
    const root = workspaceRootPath();
    if (!root) {
        void vscode.window.showInformationMessage(vscode.l10n.t('Open a folder to undo or redo mux changes.'));
        return;
    }

    try {
        const client = await lifecycle.getClient(new vscode.CancellationTokenSource().token);
        const result = action === 'undo' ? await client.undoCheckpoint(root) : await client.redoCheckpoint(root);
        if (result.Restored) {
            void vscode.window.showInformationMessage(
                action === 'undo'
                    ? vscode.l10n.t('Undid: {0}', result.Label ?? '')
                    : vscode.l10n.t('Redid: {0}', result.Label ?? ''),
            );
        } else {
            void vscode.window.showInformationMessage(
                action === 'undo' ? vscode.l10n.t('Nothing to undo.') : vscode.l10n.t('Nothing to redo.'),
            );
        }
    } catch (error) {
        void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
    }
}

function showWorkingDirectory(): void {
    const root = workspaceRootPath();
    if (root) {
        void vscode.window.showInformationMessage(vscode.l10n.t('mux runs in {0}', root));
    } else {
        void vscode.window.showInformationMessage(vscode.l10n.t('Open a folder to give mux a working directory.'));
    }
}
