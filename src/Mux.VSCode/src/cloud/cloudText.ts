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
 * The state key of a status: `not_signed_in`, `off`, `connecting`, `connected`, `reconnecting`, `signed_out`, or
 * `removed`. Hubs older than the StateKey field are mapped from the state name.
 *
 * @param status The status.
 * @returns The state key.
 */
export function stateKey(status: CloudStatus): string {
    if (status.StateKey) {
        return status.StateKey;
    }

    switch (status.StateName) {
        case 'Connecting':
            return 'connecting';
        case 'Connected':
            return 'connected';
        case 'Reconnecting':
            return 'reconnecting';
        case 'SignedOut':
            return 'signed_out';
        case 'Revoked':
            return 'removed';
        default:
            return status.MachineId ? 'off' : 'not_signed_in';
    }
}

/**
 * The translated state label, for example "Not signed in" or "Connected".
 *
 * @param status The status.
 * @returns The label.
 */
export function stateLabel(status: CloudStatus): string {
    switch (stateKey(status)) {
        case 'not_signed_in':
            return vscode.l10n.t('Not signed in');
        case 'off':
            return vscode.l10n.t('Off');
        case 'connecting':
            return vscode.l10n.t('Connecting');
        case 'connected':
            return vscode.l10n.t('Connected');
        case 'reconnecting':
            return vscode.l10n.t('Reconnecting');
        case 'signed_out':
            return vscode.l10n.t('Signed out');
        case 'removed':
            return vscode.l10n.t('Removed');
        default:
            return status.StateLabel || status.StateName;
    }
}

/**
 * The translated sentence for a reason code, or undefined when the code is unknown or empty.
 *
 * @param code The reason code.
 * @returns The sentence, or undefined.
 */
export function reasonMessage(code: string | undefined | null): string | undefined {
    switch (code) {
        case 'not_signed_in':
            return vscode.l10n.t('This machine is not signed in to Mux Cloud.');
        case 'turned_off':
            return vscode.l10n.t('The Mux Cloud connection is turned off on this machine.');
        case 'agent_not_running':
            return vscode.l10n.t('The mux agent is not running. The connection starts when it runs.');
        case 'another_process':
        case 'not_hosted_here':
            return vscode.l10n.t('Another mux process on this machine runs the Mux Cloud connection.');
        case 'clock_skew':
            return vscode.l10n.t("This machine's clock is off by more than 2 minutes. Fix the clock and mux will reconnect.");
        case 'credential_rejected':
            return vscode.l10n.t("Mux Cloud no longer accepts this machine's sign-in. Sign in again.");
        case 'removed':
            return vscode.l10n.t('This machine was removed from Mux Cloud. Sign in again to reconnect it.');
        case 'upgrade_required':
            return vscode.l10n.t('Mux Cloud needs a newer version of mux. Upgrade mux, then reconnect.');
        case 'unreachable':
            return vscode.l10n.t('Mux Cloud cannot be reached. mux keeps trying.');
        case 'connection_dropped':
            return vscode.l10n.t('The connection to Mux Cloud dropped. mux keeps trying.');
        case 'heartbeat_timeout':
            return vscode.l10n.t('The connection to Mux Cloud stopped responding. mux keeps trying.');
        case 'replaced':
            return vscode.l10n.t('Another connection for this machine replaced this one.');
        case 'server_restarting':
            return vscode.l10n.t('Mux Cloud is restarting. mux reconnects when it is back.');
        case 'rate_limited':
            return vscode.l10n.t('Too many failed attempts. mux waits before trying again.');
        case 'handshake_rejected':
            return vscode.l10n.t('Mux Cloud did not accept the connection. mux keeps trying.');
        default:
            return undefined;
    }
}

/**
 * The reason line under the state: the translated sentence for a known code, else the hub's English reason. Empty
 * when the label already says it (a machine that is not signed in).
 *
 * @param status The status.
 * @returns The reason line, possibly empty.
 */
export function reasonText(status: CloudStatus): string {
    if (stateKey(status) === 'not_signed_in' && (!status.ReasonCode || status.ReasonCode === 'not_signed_in')) {
        return '';
    }

    return reasonMessage(status.ReasonCode) ?? status.Reason ?? '';
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
            return vscode.l10n.t('Reconnecting to {0}: {1}', status.ServerUrl, reasonMessage(status.ReasonCode) ?? (status.Reason || ''));
        case 'SignedOut':
            return reasonMessage(status.ReasonCode) ?? vscode.l10n.t('Signed out of Mux Cloud: {0}', status.Reason || '');
        case 'Revoked':
            return vscode.l10n.t('This machine was removed from Mux Cloud. Sign in again to reconnect it.');
        default:
            return reasonMessage(status.ReasonCode) ?? (status.Reason || vscode.l10n.t('Not connected to Mux Cloud'));
    }
}

/**
 * A one-line summary for notifications and pickers: the translated state, then the machine when connected or the
 * reason otherwise, without repeating the state.
 *
 * @param status The status.
 * @returns The summary, for example "Mux Cloud: Connected · studio-mac".
 */
export function statusSummary(status: CloudStatus): string {
    const head = vscode.l10n.t('Mux Cloud: {0}', stateLabel(status));
    if (stateKey(status) === 'connected') {
        return `${head} · ${status.MachineName}`;
    }

    const reason = reasonText(status);
    return reason ? `${head}. ${reason}` : head;
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
