import * as vscode from 'vscode';
import { ApiError } from '../api/ApiError';
import { MuxServerLifecycle } from '../server/lifecycle';
import { CloudStatusBar } from './CloudStatusBar';
import { remoteAccessNotice, statusSummary } from './cloudText';

/**
 * `mux: Connect to Mux Cloud`: signs this machine in (email and masked password; the local hub sends them to Mux
 * Cloud once and never stores the password), or, when already signed in, offers status, the remote tool approval
 * switch, opening Mux Cloud, and signing out.
 *
 * @param lifecycle The server lifecycle used to reach the local hub.
 * @param bar The status bar to refresh after a change.
 */
export async function runCloudCommand(lifecycle: MuxServerLifecycle, bar: CloudStatusBar): Promise<void> {
    const token = new vscode.CancellationTokenSource().token;
    let client;
    try {
        client = await lifecycle.getClient(token);
    } catch (error) {
        void vscode.window.showErrorMessage(vscode.l10n.t('mux is not running: {0}', String((error as Error).message ?? error)));
        return;
    }

    const settings = await client.getSettings();
    const cloud = settings.Cloud;
    if (cloud?.MachineSecretSet) {
        const status = await client.getCloudStatus();
        const remote = cloud.AllowRemoteToolApproval;
        const showStatus = vscode.l10n.t('Show status');
        const toggle = remote ? vscode.l10n.t('Turn remote tool approval off') : vscode.l10n.t('Turn remote tool approval on');
        const open = vscode.l10n.t('Open Mux Cloud');
        const signOut = vscode.l10n.t('Sign out of Mux Cloud');
        const choice = await vscode.window.showQuickPick([showStatus, toggle, open, signOut], {
            placeHolder: statusSummary(status),
        });
        if (choice === showStatus) {
            void vscode.window.showInformationMessage(statusSummary(status));
        } else if (choice === toggle) {
            settings.Cloud = { ...cloud, AllowRemoteToolApproval: !remote };
            await client.putSettings(settings);
            void vscode.window.showInformationMessage(
                remote ? vscode.l10n.t('Remote tool approval is off.') : vscode.l10n.t('Remote tool approval is on.'),
            );
        } else if (choice === open) {
            void vscode.env.openExternal(vscode.Uri.parse(cloud.ServerUrl || 'https://app.usemux.ai'));
        } else if (choice === signOut) {
            await client.cloudLogout();
            void vscode.window.showInformationMessage(vscode.l10n.t('Signed this machine out of Mux Cloud.'));
        }

        await bar.refresh();
        return;
    }

    const proceed = vscode.l10n.t('Continue');
    const accepted = await vscode.window.showWarningMessage(remoteAccessNotice(), { modal: true }, proceed);
    if (accepted !== proceed) {
        return;
    }

    const email = await vscode.window.showInputBox({
        title: vscode.l10n.t('Connect to Mux Cloud'),
        prompt: vscode.l10n.t('Mux Cloud email'),
        value: cloud?.AccountEmail ?? '',
        ignoreFocusOut: true,
        validateInput: (v) => (v.trim() ? undefined : vscode.l10n.t('Enter your email.')),
    });
    if (!email) {
        return;
    }

    const password = await vscode.window.showInputBox({
        title: vscode.l10n.t('Connect to Mux Cloud'),
        prompt: vscode.l10n.t('Mux Cloud password (sent once, never stored)'),
        password: true,
        ignoreFocusOut: true,
        validateInput: (v) => (v ? undefined : vscode.l10n.t('Enter your password.')),
    });
    if (!password) {
        return;
    }

    try {
        const status = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: vscode.l10n.t('Signing in to Mux Cloud…') },
            () => client.cloudLogin(email.trim(), password, cloud?.ServerUrl, cloud?.MachineName ?? undefined),
        );
        void vscode.window.showInformationMessage(statusSummary(status));
    } catch (error) {
        const message = error instanceof ApiError ? error.message : String((error as Error).message ?? error);
        void vscode.window.showErrorMessage(vscode.l10n.t('Could not sign in to Mux Cloud: {0}', message));
    }

    await bar.refresh();
}
