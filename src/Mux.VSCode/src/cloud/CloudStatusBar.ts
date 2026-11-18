import * as vscode from 'vscode';
import { CloudStatus } from '../api/types';
import { MuxServerLifecycle } from '../server/lifecycle';
import { describeStatus, stateIcon } from './cloudText';

/**
 * A status-bar item showing this machine's Mux Cloud connection with the same state names every mux surface uses
 * (Disabled, Connecting, Connected, Reconnecting, SignedOut, Revoked). It polls the local hub every ten seconds while
 * connected to it, hides while the machine has never been signed in, and opens the Mux Cloud command when clicked.
 */
export class CloudStatusBar implements vscode.Disposable {
    private readonly item: vscode.StatusBarItem;
    private readonly timer: ReturnType<typeof setInterval>;
    private readonly subscription: vscode.Disposable;
    private last: CloudStatus | undefined;

    /**
     * Creates the item and starts polling.
     *
     * @param lifecycle The server lifecycle used to reach the local hub.
     */
    public constructor(private readonly lifecycle: MuxServerLifecycle) {
        this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 98);
        this.item.command = 'mux.cloud';
        this.timer = setInterval(() => void this.refresh(), 10000);
        this.subscription = lifecycle.onDidChangeState((state) => {
            if (state.connected) {
                void this.refresh();
            }
        });
        void this.refresh();
    }

    /** The last status read, if any. */
    public get status(): CloudStatus | undefined {
        return this.last;
    }

    /** Reads the status now and redraws. */
    public async refresh(): Promise<void> {
        if (!this.lifecycle.state.connected) {
            return;
        }

        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            this.render(await client.getCloudStatus());
        } catch {
            // The hub may predate Mux Cloud or be restarting; keep the last state.
        }
    }

    private render(status: CloudStatus): void {
        this.last = status;
        const enrolled = !!status.MachineId;
        if (!enrolled && status.StateName === 'Disabled') {
            this.item.hide();
            return;
        }

        this.item.text = `${stateIcon(status.StateName)} ${vscode.l10n.t('Mux Cloud')}`;
        this.item.tooltip = new vscode.MarkdownString(
            [
                `**Mux Cloud: ${status.StateName}**`,
                '',
                describeStatus(status),
                '',
                status.AllowRemoteToolApproval
                    ? vscode.l10n.t('Remote tool approval is on.')
                    : vscode.l10n.t('Remote tool approval is off.'),
            ].join('\n'),
        );
        this.item.backgroundColor =
            status.StateName === 'SignedOut' || status.StateName === 'Revoked' ? new vscode.ThemeColor('statusBarItem.warningBackground') : undefined;
        this.item.show();
    }

    public dispose(): void {
        clearInterval(this.timer);
        this.subscription.dispose();
        this.item.dispose();
    }
}
