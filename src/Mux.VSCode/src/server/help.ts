import * as vscode from 'vscode';
import { MuxServerLifecycle } from './lifecycle';
import { DEFAULT_PORT, readSettings } from '../config/settings';

/**
 * Shows actionable guidance when the extension cannot reach a mux server. Rather than leaving the user with a
 * bare error, it explains the usual cause — no server running — and offers the concrete next steps: retry the
 * connection, start one from a terminal with `mux serve`, or open the settings that control auto-start and the
 * port. The steps map to why a connection fails in practice, in order of likelihood.
 *
 * @param lifecycle The server lifecycle, used to retry the connection.
 */
export async function showConnectionHelp(lifecycle: MuxServerLifecycle): Promise<void> {
    const state = lifecycle.state;
    if (state.connected) {
        void vscode.window.showInformationMessage(
            vscode.l10n.t('mux is connected to {0} (contract {1}).', state.productVersion, state.contractVersion),
        );
        return;
    }

    const settings = readSettings();
    const port = settings.port > 0 ? settings.port : DEFAULT_PORT;

    const retry = vscode.l10n.t('Retry connection');
    const openTerminal = vscode.l10n.t('Start mux serve');
    const openSettings = vscode.l10n.t('Open settings');

    const detail = [
        vscode.l10n.t("The extension couldn't reach a mux server on 127.0.0.1:{0}.", String(port)),
        '',
        vscode.l10n.t('What it means: {0}', state.detail),
        '',
        vscode.l10n.t('To fix it:'),
        vscode.l10n.t('• Make sure the mux CLI is installed and on your PATH (set mux.server.path otherwise).'),
        vscode.l10n.t('• Start a server yourself in a terminal: mux serve --allow-tools'),
        vscode.l10n.t('• Or let the extension start one — enable mux.server.autoStart — and retry.'),
        vscode.l10n.t('• If mux was just updated, an older server may still be running on the port; stop it and retry.'),
    ].join('\n');

    const choice = await vscode.window.showWarningMessage(
        vscode.l10n.t('mux is not connected'),
        { modal: true, detail },
        retry,
        openTerminal,
        openSettings,
    );

    if (choice === retry) {
        await vscode.commands.executeCommand('mux.reconnect');
    } else if (choice === openTerminal) {
        const terminal = vscode.window.createTerminal('mux serve');
        terminal.show();
        terminal.sendText(`${settings.muxPath} serve --allow-tools --port ${port}`, false);
    } else if (choice === openSettings) {
        await vscode.commands.executeCommand('workbench.action.openSettings', 'mux.server');
    }
}
