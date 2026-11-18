import * as vscode from 'vscode';
import { CloudStatus } from '../api/types';

/**
 * The notice every Mux Cloud connect screen shows before signing in, identical in meaning on every mux surface.
 *
 * @returns The localized notice.
 */
export function remoteAccessNotice(): string {
    return vscode.l10n.t(
        'Anyone signed in to this Mux Cloud account can use this machine remotely, as if they were at it, including approving tools that edit files and run commands. To keep remote use read-only for those tools, turn off remote tool approval (Mux Cloud command, or set cloud.allowRemoteToolApproval to false).',
    );
}

/**
 * A one-line description of a status, matching the wording the terminal and desktop use.
 *
 * @param status The status.
 * @returns The description.
 */
export function describeStatus(status: CloudStatus): string {
    switch (status.StateName) {
        case 'Connected':
            return vscode.l10n.t('Connected to {0} as {1}', status.ServerUrl, status.MachineName);
        case 'Connecting':
            return vscode.l10n.t('Connecting to {0}', status.ServerUrl);
        case 'Reconnecting':
            return vscode.l10n.t('Reconnecting to {0}: {1}', status.ServerUrl, status.Reason || '');
        case 'SignedOut':
            return vscode.l10n.t('Signed out of Mux Cloud: {0}', status.Reason || '');
        case 'Revoked':
            return vscode.l10n.t('This machine was removed from Mux Cloud. Sign in again to reconnect it.');
        default:
            return status.Reason || vscode.l10n.t('Not connected to Mux Cloud');
    }
}

/**
 * A one-line summary for notifications and pickers: the state name, then the machine when connected or the reason
 * otherwise, without repeating the state.
 *
 * @param status The status.
 * @returns The summary, for example "Mux Cloud: Connected · studio-mac".
 */
export function statusSummary(status: CloudStatus): string {
    if (status.StateName === 'Connected') {
        return `Mux Cloud: Connected · ${status.MachineName}`;
    }

    return `Mux Cloud: ${status.StateName}. ${describeStatus(status)}`;
}

/**
 * The codicon for a state.
 *
 * @param stateName The state name.
 * @returns A codicon reference.
 */
export function stateIcon(stateName: string): string {
    switch (stateName) {
        case 'Connected':
            return '$(cloud)';
        case 'Connecting':
        case 'Reconnecting':
            return '$(sync~spin)';
        case 'SignedOut':
        case 'Revoked':
            return '$(warning)';
        default:
            return '$(cloud-upload)';
    }
}

/**
 * The signup page of a Mux Cloud server, for someone who has no account yet.
 *
 * @param serverUrl The configured server; blank uses https://app.usemux.ai.
 * @returns For example `https://app.usemux.ai/signup`.
 */
export function signupUrl(serverUrl?: string | null): string {
    const server = (serverUrl ?? '').trim() || 'https://app.usemux.ai';
    return server.replace(/\/+$/, '') + '/signup';
}

/**
 * The one-line hint shown next to every Mux Cloud sign-in prompt.
 *
 * @param serverUrl The configured server; blank uses the default.
 * @returns For example `No account yet? Create one at https://app.usemux.ai/signup`.
 */
export function signupHint(serverUrl?: string | null): string {
    return vscode.l10n.t('No account yet? Create one at {0}', signupUrl(serverUrl));
}
