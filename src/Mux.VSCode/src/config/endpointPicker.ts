import * as vscode from 'vscode';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';

/**
 * A status-bar entry and command for choosing the endpoint runs go against. It reads the server's endpoint
 * list and writes the choice to `mux.defaultEndpoint`, so the picker is a thin front end over the same config
 * every surface shares rather than a second store.
 */
export class EndpointPicker implements vscode.Disposable {
    private readonly item: vscode.StatusBarItem;
    private readonly lifecycle: MuxServerLifecycle;

    /**
     * Creates the picker and its status-bar item.
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     */
    public constructor(lifecycle: MuxServerLifecycle) {
        this.lifecycle = lifecycle;
        this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
        this.item.command = 'mux.selectEndpoint';
        this.refresh();
        this.item.show();
    }

    /** Repaints the status-bar label from the current setting. */
    public refresh(): void {
        const current = vscode.workspace.getConfiguration('mux').get<string>('defaultEndpoint', '');
        this.item.text = `$(server) ${current || vscode.l10n.t('mux: default')}`;
        this.item.tooltip = vscode.l10n.t('Choose the mux endpoint for runs');
    }

    /** Prompts for an endpoint and stores the choice. */
    public async pick(): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const endpoints = await client.getEndpoints();
            if (endpoints.length === 0) {
                void vscode.window.showInformationMessage(vscode.l10n.t('No endpoints are configured in mux.'));
                return;
            }

            const picked = await vscode.window.showQuickPick(
                endpoints.map((e) => ({ label: e.Name, description: `${e.AdapterType} · ${e.Model}${e.IsDefault ? ' · default' : ''}` })),
                { placeHolder: vscode.l10n.t('Select the endpoint for mux runs') },
            );
            if (!picked) {
                return;
            }

            await vscode.workspace.getConfiguration('mux').update('defaultEndpoint', picked.label, vscode.ConfigurationTarget.Workspace);
            this.refresh();
        } catch (error) {
            logError('Failed to pick an endpoint.', error);
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
        }
    }

    public dispose(): void {
        this.item.dispose();
    }
}
