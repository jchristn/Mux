import * as childProcess from 'child_process';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { DEFAULT_PORT, readSettings, readSharedRest } from '../config/settings';
import { log, logError } from '../util/logger';
import { checkContract } from './contract';

const SECRET_KEY = 'mux.apiKey';

/** The current connection state, surfaced to the status bar and management tree. */
export interface ConnectionState {
    /** Whether a compatible server is currently connected. */
    connected: boolean;

    /** The base URL of the connected (or last-attempted) server. */
    baseUrl: string;

    /** The server's product version, when connected. */
    productVersion: string;

    /** The negotiated API contract version, when connected. */
    contractVersion: string;

    /** Whether the extension started this server (vs. reusing an already-running one). */
    ownedByExtension: boolean;

    /** A human-readable reason when not connected. */
    detail: string;
}

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
    private readonly stateChanged = new vscode.EventEmitter<ConnectionState>();
    private currentState: ConnectionState = {
        connected: false,
        baseUrl: '',
        productVersion: '',
        contractVersion: '',
        ownedByExtension: false,
        detail: 'Not connected',
    };

    /** Fires whenever the connection state changes (connected, disconnected, reconnecting). */
    public readonly onDidChangeState = this.stateChanged.event;

    /**
     * Creates the lifecycle.
     *
     * @param context The extension context, used for secret storage.
     */
    public constructor(context: vscode.ExtensionContext) {
        this.context = context;
    }

    /** The current connection state. */
    public get state(): ConnectionState {
        return this.currentState;
    }

    private setState(next: Partial<ConnectionState>): void {
        this.currentState = { ...this.currentState, ...next };
        this.stateChanged.fire(this.currentState);
    }

    /**
     * Drops the cached client and reconnects, firing state changes. Used by the "reconnect" command and after
     * a config change that may have moved the server.
     *
     * @param token A cancellation token.
     */
    public async reconnect(token: vscode.CancellationToken): Promise<void> {
        this.client = undefined;
        this.setState({ connected: false, detail: 'Reconnecting…' });
        try {
            await this.getClient(token);
        } catch (error) {
            this.setState({ connected: false, detail: error instanceof Error ? error.message : String(error) });
        }
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
        // Join the SAME hub as every other surface: prefer mux's shared settings.json (port + apiKey), so
        // the extension reuses the tray agent and speaks with the shared key rather than its own.
        const shared = readSharedRest();
        const port = settings.port > 0 ? settings.port : shared.port && shared.port > 0 ? shared.port : DEFAULT_PORT;
        const baseUrl = `http://127.0.0.1:${port}`;

        this.setState({ baseUrl, detail: 'Connecting…' });

        // The shared key is authoritative; fall back to a previously-stored one only when settings.json has none.
        const sharedKey = shared.apiKey ?? null;
        const storedKey = sharedKey ?? (await this.context.secrets.get(SECRET_KEY)) ?? null;
        const reusable = await this.tryReuse(baseUrl, storedKey, token);
        if (reusable) {
            this.client = reusable;
            return reusable;
        }

        if (!settings.autoStart) {
            const message = `No mux server is reachable at ${baseUrl} and auto-start is disabled. Start one with "mux serve --allow-tools" or enable mux.server.autoStart.`;
            this.setState({ connected: false, detail: message });
            throw new Error(message);
        }

        // Spawn without imposing a key when settings.json has none — `mux serve` generates and persists one
        // there, which we then read back so the whole system shares it. When a shared key exists, use it.
        await this.spawnServer(settings.muxPath, port, sharedKey);
        const key = sharedKey ?? (await this.waitForSharedKey(token));
        const client = new ApiClient({ baseUrl, apiKey: key });
        const health = await this.waitForHealth(client, token);
        if (key) {
            await this.context.secrets.store(SECRET_KEY, key);
        }
        this.client = client;
        this.setState({
            connected: true,
            baseUrl,
            productVersion: health.Version,
            contractVersion: health.ContractVersion,
            ownedByExtension: true,
            detail: 'Started by the extension',
        });
        return client;
    }

    // Polls mux's shared settings.json for the API key a freshly-spawned `mux serve` persists there, so the
    // extension uses the same key every other surface does. Returns null (no-auth) if none appears.
    private async waitForSharedKey(token: vscode.CancellationToken): Promise<string | null> {
        const deadline = Date.now() + 8000;
        for (;;) {
            if (token.isCancellationRequested) {
                return null;
            }

            const key = readSharedRest().apiKey ?? null;
            if (key) {
                return key;
            }

            if (Date.now() > deadline) {
                return null;
            }

            await delay(250);
        }
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
            this.setState({
                connected: true,
                baseUrl,
                productVersion: health.Version,
                contractVersion: health.ContractVersion,
                ownedByExtension: false,
                detail: 'Reusing a running server',
            });
            return client;
        } catch (error) {
            logError(`Could not reuse a server at ${baseUrl}; will start one.`, error);
            return undefined;
        }
    }

    private spawnServer(muxPath: string, port: number, key: string | null): Promise<void> {
        return new Promise((resolve, reject) => {
            log(`Starting mux serve on 127.0.0.1:${port}.`);
            const args = ['serve', '--allow-tools', '--host', '127.0.0.1', '--port', String(port)];
            if (key) {
                // Impose the shared key. With none, `mux serve` generates and persists one to settings.json.
                args.push('--api-key', key);
            }

            const child = childProcess.spawn(muxPath, args, { stdio: 'ignore', windowsHide: true });

            child.on('error', (error) => {
                logError('Failed to start mux serve.', error);
                reject(new Error(`Could not start "${muxPath} serve". Is mux installed and on PATH? Set mux.server.path otherwise.`));
            });

            this.ownedProcess = child;
            // The process stays up; readiness is confirmed by polling health, not by a spawn event.
            resolve();
        });
    }

    private async waitForHealth(client: ApiClient, token: vscode.CancellationToken): Promise<import('../api/types').HealthResponse> {
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
                return health;
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
        this.setState({ connected: false, detail: 'Extension deactivated' });
        this.stateChanged.dispose();
    }
}

function delay(milliseconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
