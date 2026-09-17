import * as vscode from 'vscode';
import { ChatViewProvider } from './chat/ChatViewProvider';
import { EndpointPicker } from './config/endpointPicker';
import { MuxCodeActionProvider, registerInlineCommands } from './commands/inlineCommands';
import { workspaceRootPath } from './context/providers';
import { AboutPanel } from './manage/AboutPanel';
import { ManageActions } from './manage/actions';
import { ManageNode, ManageTreeProvider } from './manage/ManageTree';
import { UsagePanel } from './manage/UsagePanel';
import { showConnectionHelp, showMuxNotInstalled } from './server/help';
import { MirrorClient } from './mirror/MirrorClient';
import { MuxServerLifecycle } from './server/lifecycle';
import { SetupWizard } from './setup/SetupWizard';
import { readSettings } from './config/settings';
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

    // Global conversation-list sync: once connected, subscribe to the hub's list-change notifications and
    // refresh the session tree live (a run finishing anywhere, or a rename/delete on any surface). No manual
    // refresh needed. Started on first connect so activation stays network-free.
    let sessionsWatch: MirrorClient | undefined;
    const ensureSessionsWatch = async (): Promise<void> => {
        if (sessionsWatch) {
            return;
        }
        try {
            const client = await lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const watcher = new MirrorClient(client.serverBaseUrl, client.serverApiKey);
            sessionsWatch = watcher;
            watcher.startAll({
                onEvent: () => {},
                onError: () => {},
                onClose: () => {},
                onSessionsChanged: () => void vscode.commands.executeCommand('mux.sessions.refresh'),
            });
            context.subscriptions.push({ dispose: () => watcher.stop() });
        } catch {
            /* not connected yet; retry on the next state change */
        }
    };
    context.subscriptions.push(lifecycle.onDidChangeState((state) => { if (state.connected) { void ensureSessionsWatch(); } }));

    const endpointPicker = new EndpointPicker(lifecycle);
    context.subscriptions.push(endpointPicker);

    context.subscriptions.push(new ConnectionStatusBar(lifecycle));

    const manage = new ManageTreeProvider(lifecycle);
    context.subscriptions.push(vscode.window.registerTreeDataProvider('mux.manage', manage));
    const manageActions = new ManageActions(lifecycle, () => manage.refresh());
    const setupWizard = new SetupWizard(lifecycle, manageActions);
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
        vscode.commands.registerCommand('mux.manage.addPrompt', () => manageActions.addPrompt()),
        vscode.commands.registerCommand('mux.manage.editPrompt', (node: ManageNode) => manageActions.editPrompt(node)),
        vscode.commands.registerCommand('mux.manage.deletePrompt', (node: ManageNode) => manageActions.deletePrompt(node)),
        vscode.commands.registerCommand('mux.manage.editCatalog', (node: ManageNode) => manageActions.editCatalog(node)),
        vscode.commands.registerCommand('mux.manage.resetCatalog', (node: ManageNode) => manageActions.resetCatalog(node)),
        vscode.commands.registerCommand('mux.manage.addSubagent', () => manageActions.addSubagent()),
        vscode.commands.registerCommand('mux.manage.editSubagent', (node: ManageNode) => manageActions.editSubagent(node)),
        vscode.commands.registerCommand('mux.manage.deleteSubagent', (node: ManageNode) => manageActions.deleteSubagent(node)),
        vscode.commands.registerCommand('mux.manage.toggleSkill', (node: ManageNode) => manageActions.toggleSkill(node)),
        vscode.commands.registerCommand('mux.manage.addSkill', () => manageActions.addSkill()),
        vscode.commands.registerCommand('mux.manage.editSkill', (node: ManageNode) => manageActions.editSkill(node)),
        vscode.commands.registerCommand('mux.manage.deleteSkill', (node: ManageNode) => manageActions.deleteSkill(node)),
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
        vscode.commands.registerCommand('mux.cancelRun', () => chat.cancelActiveRun()),
        vscode.commands.registerCommand('mux.mirrorSession', async () => {
            try {
                const client = await lifecycle.getClient(new vscode.CancellationTokenSource().token);
                const sessions = await client.getSessions();
                if (sessions.length === 0) {
                    void vscode.window.showInformationMessage(vscode.l10n.t('No sessions to mirror yet.'));
                    return;
                }

                const pick = await vscode.window.showQuickPick(
                    sessions.map((s) => ({ label: s.Title || s.Id, description: s.Id })),
                    { placeHolder: vscode.l10n.t('Pick a session to mirror its live run') },
                );
                if (pick?.description) {
                    await chat.startMirror(pick.description);
                }
            } catch (error) {
                void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
            }
        }),
        vscode.commands.registerCommand('mux.reconnect', () => lifecycle.reconnect(new vscode.CancellationTokenSource().token)),
        vscode.commands.registerCommand('mux.showHelp', () => showConnectionHelp(lifecycle)),
        vscode.commands.registerCommand('mux.about', () => AboutPanel.show(context, lifecycle.state)),
        vscode.commands.registerCommand('mux.usage', () => UsagePanel.show(lifecycle)),
        vscode.commands.registerCommand('mux.setup', () => setupWizard.runManually()),
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

    // The extension is a thin client for the mux CLI: with auto-start on, it runs `mux serve` to provide the
    // server that powers everything. If mux isn't installed there is no server, so warn once, up front, rather
    // than letting the first chat fail with a confusing timeout. Runs in the background so activation stays
    // network-free, and is shown at most once per install (until mux appears, then it re-arms).
    void notifyIfMuxMissing(context, lifecycle);

    // First run: when a reachable server has no usable endpoint and setup has not been completed, guide the
    // user through defining one, checking connectivity, and sending a first prompt. Runs in the background so
    // activation stays network-free; it is silent when no server is reachable or setup is not needed.
    void setupWizard.maybeRunOnStartup();
}

const MUX_MISSING_NOTIFIED_KEY = 'mux.notifiedMuxMissing';

async function notifyIfMuxMissing(context: vscode.ExtensionContext, lifecycle: MuxServerLifecycle): Promise<void> {
    const settings = readSettings();
    // When auto-start is off the user is running their own server, so a missing local CLI is not the problem.
    if (!settings.autoStart) {
        return;
    }

    const available = await lifecycle.isMuxCliAvailable();
    if (available) {
        // Re-arm the one-shot notice so a later removal of mux warns again.
        await context.globalState.update(MUX_MISSING_NOTIFIED_KEY, false);
        return;
    }

    if (context.globalState.get<boolean>(MUX_MISSING_NOTIFIED_KEY)) {
        return;
    }

    await context.globalState.update(MUX_MISSING_NOTIFIED_KEY, true);
    await showMuxNotInstalled(settings.muxPath);
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
