import * as childProcess from 'child_process';
import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { DEFAULT_PORT, readSettings } from '../config/settings';
import { log, logError } from '../util/logger';
import { checkContract } from './contract';

const SECRET_KEY = 'mux.apiKey';

/**
 * Owns the connection to a local `mux serve`. On demand it reuses a reachable, contract-compatible server the
 * extension can authenticate against, or starts one bound to loopback with a key it generates and keeps in
 * VS Code secret storage. A server the extension started is stopped when the extension deactivates; one it
 * merely discovered is left running. The key never touches settings, logs, or the workspace.
 */
export class MuxServerLifecycle implements vscode.Disposable {
    private readonly context: vscode.ExtensionContext;
    private client: ApiClient | undefined;
    private ownedProcess: childProcess.ChildProcess | undefined;
    private connecting: Promise<ApiClient> | undefined;

    /**
     * Creates the lifecycle.
     *
     * @param context The extension context, used for secret storage.
     */
    public constructor(context: vscode.ExtensionContext) {
        this.context = context;
    }

    /**
     * Returns a connected client, connecting on first use and reusing the connection after. Concurrent
     * callers share one in-flight connect rather than racing to start two servers.
     *
     * @param token A cancellation token.
     * @returns The connected client.
     * @throws {Error} When no compatible server can be reached or started.
     */
    public async getClient(token: vscode.CancellationToken): Promise<ApiClient> {
        if (this.client) {
            return this.client;
        }

        if (!this.connecting) {
            this.connecting = this.connect(token).finally(() => {
                this.connecting = undefined;
            });
        }

        return this.connecting;
    }

    private async connect(token: vscode.CancellationToken): Promise<ApiClient> {
        const settings = readSettings();
        const port = settings.port > 0 ? settings.port : DEFAULT_PORT;
        const baseUrl = `http://127.0.0.1:${port}`;

        const existingKey = await this.context.secrets.get(SECRET_KEY);
        const reusable = await this.tryReuse(baseUrl, existingKey ?? null, token);
        if (reusable) {
            this.client = reusable;
            return reusable;
        }

        if (!settings.autoStart) {
            throw new Error(`No mux server is reachable at ${baseUrl} and auto-start is disabled. Start one with "mux serve --allow-tools" or enable mux.server.autoStart.`);
        }

        const key = `mux_${crypto.randomBytes(16).toString('hex')}`;
        await this.spawnServer(settings.muxPath, port, key);
        const client = new ApiClient({ baseUrl, apiKey: key });
        await this.waitForHealth(client, token);
        await this.context.secrets.store(SECRET_KEY, key);
        this.client = client;
        return client;
    }

    private async tryReuse(baseUrl: string, key: string | null, token: vscode.CancellationToken): Promise<ApiClient | undefined> {
        const client = new ApiClient({ baseUrl, apiKey: key });
        try {
            const health = await client.getHealth(this.abortAfter(token, 2000));
            const contract = checkContract(health.ContractVersion);
            if (!contract.compatible) {
                throw new Error(contract.reason);
            }

            // Confirm the stored key actually authenticates against this server before reusing it.
            await client.getEndpoints(this.abortAfter(token, 2000));
            log(`Reusing mux server at ${baseUrl} (contract ${health.ContractVersion}).`);
            return client;
        } catch (error) {
            logError(`Could not reuse a server at ${baseUrl}; will start one.`, error);
            return undefined;
        }
    }

    private spawnServer(muxPath: string, port: number, key: string): Promise<void> {
        return new Promise((resolve, reject) => {
            log(`Starting mux serve on 127.0.0.1:${port}.`);
            const child = childProcess.spawn(
                muxPath,
                ['serve', '--allow-tools', '--host', '127.0.0.1', '--port', String(port), '--api-key', key],
                { stdio: 'ignore', windowsHide: true },
            );

            child.on('error', (error) => {
                logError('Failed to start mux serve.', error);
                reject(new Error(`Could not start "${muxPath} serve". Is mux installed and on PATH? Set mux.server.path otherwise.`));
            });

            this.ownedProcess = child;
            // The process stays up; readiness is confirmed by polling health, not by a spawn event.
            resolve();
        });
    }

    private async waitForHealth(client: ApiClient, token: vscode.CancellationToken): Promise<void> {
        const deadline = Date.now() + 15000;
        for (;;) {
            if (token.isCancellationRequested) {
                throw new Error('Connecting to the mux server was cancelled.');
            }

            try {
                const health = await client.getHealth(this.abortAfter(token, 1500));
                const contract = checkContract(health.ContractVersion);
                if (!contract.compatible) {
                    throw new Error(contract.reason);
                }

                log(`mux server ready (contract ${health.ContractVersion}).`);
                return;
            } catch (error) {
                if (Date.now() > deadline) {
                    throw new Error(`The mux server did not become ready in time. ${error instanceof Error ? error.message : ''}`.trim());
                }

                await delay(300);
            }
        }
    }

    private abortAfter(token: vscode.CancellationToken, milliseconds: number): AbortSignal {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), milliseconds);
        token.onCancellationRequested(() => controller.abort());
        controller.signal.addEventListener('abort', () => clearTimeout(timer));
        return controller.signal;
    }

    /** Stops a server the extension started, and drops the cached client. */
    public dispose(): void {
        if (this.ownedProcess) {
            log('Stopping the mux server the extension started.');
            try {
                this.ownedProcess.kill();
            } catch (error) {
                logError('Failed to stop the mux server.', error);
            }

            this.ownedProcess = undefined;
        }

        this.client = undefined;
    }
}

function delay(milliseconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
