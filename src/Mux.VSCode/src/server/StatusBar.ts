import * as vscode from 'vscode';
import { ConnectionState, MuxServerLifecycle } from './lifecycle';

/**
 * A status-bar item that makes the server connection visible at a glance: a check when connected (with the
 * product and contract version in the tooltip), a spinner while connecting, and a warning when not. Clicking
 * it runs the reconnect command. This is the "am I actually talking to mux" signal the chat panel alone did
 * not give.
 */
export class ConnectionStatusBar implements vscode.Disposable {
    private readonly item: vscode.StatusBarItem;
    private readonly subscription: vscode.Disposable;

    /**
     * Creates the status-bar item and subscribes to lifecycle state changes.
     *
     * @param lifecycle The server lifecycle whose state drives the item.
     */
    public constructor(lifecycle: MuxServerLifecycle) {
        this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 99);
        this.render(lifecycle.state);
        this.item.show();
        this.subscription = lifecycle.onDidChangeState((state) => this.render(state));
    }

    private render(state: ConnectionState): void {
        if (state.connected) {
            this.item.text = '$(check) mux';
            this.item.command = 'mux.about';
            this.item.tooltip = new vscode.MarkdownString(
                [
                    `**mux connected**`,
                    ``,
                    `- URL: ${state.baseUrl}`,
                    `- Version: ${state.productVersion} (contract ${state.contractVersion})`,
                    `- ${state.ownedByExtension ? 'Started by the extension' : 'Reusing a running server'}`,
                    ``,
                    `Click for About and the dashboard.`,
                ].join('\n'),
            );
            this.item.backgroundColor = undefined;
            return;
        }

        const connecting = state.detail.startsWith('Connecting') || state.detail.startsWith('Reconnecting');
        this.item.text = connecting ? '$(sync~spin) mux' : '$(warning) mux';
        // When disconnected, clicking opens actionable help (how to start a server); while connecting, it just
        // shows progress, so route it to help too rather than leaving a dead click.
        this.item.command = 'mux.showHelp';
        this.item.tooltip = new vscode.MarkdownString(
            [`**mux ${connecting ? 'connecting' : 'not connected'}**`, ``, state.detail, ``, `Click for help connecting.`].join('\n'),
        );
        this.item.backgroundColor = connecting ? undefined : new vscode.ThemeColor('statusBarItem.warningBackground');
    }

    public dispose(): void {
        this.subscription.dispose();
        this.item.dispose();
    }
}
