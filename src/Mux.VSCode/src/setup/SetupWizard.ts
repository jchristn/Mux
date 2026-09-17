import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { EndpointDetail, MuxServerSettings } from '../api/types';
import { ManageActions } from '../manage/actions';
import { MuxServerLifecycle } from '../server/lifecycle';
import { log, logError } from '../util/logger';

/**
 * The first-run setup wizard. Guides a new user through defining their first endpoint, checking connectivity,
 * and sending their first prompt, then records completion in the shared server settings
 * (`SetupCompleted`) so no surface — terminal, desktop, web, or this extension — offers it again. It reuses
 * the adaptive endpoint form from {@link ManageActions} rather than duplicating field logic, and treats the
 * server flag (not VS Code globalState) as the source of truth so completing setup anywhere silences it here.
 */
export class SetupWizard {
    private readonly lifecycle: MuxServerLifecycle;
    private readonly manageActions: ManageActions;

    /**
     * Creates the wizard.
     *
     * @param lifecycle The server lifecycle, used to obtain a connected client.
     * @param manageActions The management actions, whose adaptive endpoint form the wizard reuses.
     */
    public constructor(lifecycle: MuxServerLifecycle, manageActions: ManageActions) {
        this.lifecycle = lifecycle;
        this.manageActions = manageActions;
    }

    /**
     * On activation, runs the wizard automatically when setup is needed. Network work is deferred (activation
     * stays fast) and every failure path is silent: if no server is reachable yet, or setup is not needed, the
     * wizard simply does not appear. A user can always start it manually via the `mux.setup` command.
     */
    public async maybeRunOnStartup(): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const needed = await this.isSetupNeeded(client);
            if (needed) {
                await this.run(client);
            }
        } catch (error) {
            // Not connected yet, or a transient read failure: never nag on startup.
            log(`Setup wizard startup check skipped: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Runs the wizard on demand (the `mux.setup` command). Connects if necessary and always shows the wizard,
     * regardless of whether setup is otherwise needed.
     */
    public async runManually(): Promise<void> {
        let client: ApiClient;
        try {
            client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
        } catch (error) {
            void vscode.window.showErrorMessage(
                vscode.l10n.t('Could not reach a mux server to run setup: {0}', error instanceof Error ? error.message : String(error)),
            );
            return;
        }

        await this.run(client);
    }

    private async isSetupNeeded(client: ApiClient): Promise<boolean> {
        const [endpoints, settings] = await Promise.all([client.getEndpointDetails(), client.getSettings()]);
        return !settings.SetupCompleted && !SetupWizard.hasUsableEndpoint(endpoints);
    }

    private static hasUsableEndpoint(endpoints: EndpointDetail[]): boolean {
        return endpoints.some(
            (endpoint) =>
                typeof endpoint.Model === 'string' && endpoint.Model.trim().length > 0 && !SetupWizard.isSeedEndpoint(endpoint),
        );
    }

    // The untouched first-run seed (ollama-local / qwen2.5-coder:7b) ships on every fresh install, so it must
    // not count as user configuration when deciding whether to offer the wizard. Mirrors SettingsLoader.IsSeedEndpoint.
    private static isSeedEndpoint(endpoint: EndpointDetail): boolean {
        return (
            endpoint.Name === 'ollama-local' &&
            endpoint.AdapterType === 'ollama' &&
            endpoint.Model === 'qwen2.5-coder:7b' &&
            (endpoint.BaseUrl ?? '').replace(/\/+$/, '') === 'http://localhost:11434'
        );
    }

    private async run(client: ApiClient): Promise<void> {
        // Step 1 — welcome.
        const setUp = vscode.l10n.t('Set up');
        const skip = vscode.l10n.t('Skip');
        const welcome = await vscode.window.showInformationMessage(
            vscode.l10n.t('Welcome to mux'),
            {
                modal: true,
                detail: vscode.l10n.t(
                    "Let's set up your first endpoint (the model server mux talks to), check it's reachable, and send your first message.",
                ),
            },
            setUp,
            skip,
        );

        if (welcome !== setUp) {
            // Skipped or dismissed: remember it so the wizard does not reappear; the command re-runs it.
            await this.markComplete(client);
            return;
        }

        // Step 2 — define an endpoint, reusing the adaptive form.
        await this.manageActions.addEndpoint();

        const endpoints = await client.getEndpointDetails();
        if (!SetupWizard.hasUsableEndpoint(endpoints)) {
            void vscode.window.showInformationMessage(
                vscode.l10n.t('No endpoint was added. Run "mux: Run setup wizard" when you are ready to finish.'),
            );
            return;
        }

        // Step 3 — connectivity check. There is no per-endpoint probe over REST, so confirm the shared mux
        // server is reachable (which the endpoint runs through) and that the endpoint persisted.
        try {
            const health = await client.getHealth();
            void vscode.window.showInformationMessage(
                vscode.l10n.t('Connected to mux {0}. Your endpoint is saved and ready.', health.Version),
            );
        } catch (error) {
            void vscode.window.showWarningMessage(
                vscode.l10n.t(
                    'Your endpoint was saved, but the mux server could not be reached right now: {0}',
                    error instanceof Error ? error.message : String(error),
                ),
            );
        }

        // Step 4 — first prompt: open and focus the chat view with a hint.
        try {
            await vscode.commands.executeCommand('mux.chat.focus');
        } catch (error) {
            logError('Could not focus the chat view during setup.', error);
        }

        void vscode.window.showInformationMessage(
            vscode.l10n.t('You are all set — type your first message in the mux chat panel and press Enter.'),
        );

        // Step 5 — record completion.
        await this.markComplete(client);
    }

    private async markComplete(client: ApiClient): Promise<void> {
        try {
            const settings = await client.getSettings();
            if (settings.SetupCompleted) {
                return;
            }

            const next: MuxServerSettings = { ...settings, SetupCompleted: true };
            await client.putSettings(next);
        } catch (error) {
            logError('Could not record setup completion.', error);
        }
    }
}
