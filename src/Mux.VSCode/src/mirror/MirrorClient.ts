/**
 * A WebSocket client that live-mirrors a run happening on a mux server (started by another surface — the
 * desktop app's embedded server, the web dashboard, or a `mux serve`) into the editor. It connects to the
 * server's `/v1.0/ws` bridge, subscribes by session id, and translates each canonical envelope frame into a
 * {@link MirrorEvent} via the pure {@link mirrorEventFromEnvelope} mapper. Uses the `ws` package because the
 * VS Code extension host's Node runtime has no stable global WebSocket.
 */

import type WebSocket from 'ws';
import { MirrorEvent, mirrorEventFromEnvelope } from '../api/mirror';

/** Callbacks for a mirror session. */
export interface MirrorHandlers {
    /** A render event arrived. */
    onEvent: (event: MirrorEvent) => void;

    /** A transport or server error occurred (the session ends after this). */
    onError: (message: string) => void;

    /** The socket closed (run ended, server closed, or {@link MirrorClient.stop} was called). */
    onClose: () => void;

    /** The hub reported a conversation-list change (only in `all` mode). Refresh the list. */
    onSessionsChanged?: (sessionId: string) => void;
}

/** Live-mirrors a session's run over the mux WebSocket bridge. */
export class MirrorClient {
    private readonly baseUrl: string;
    private readonly apiKey: string | null;
    private socket: WebSocket | undefined;
    private stopped = false;
    private sessionId = '';
    private all = false;
    private handlers: MirrorHandlers | undefined;

    /**
     * Creates a mirror client.
     *
     * @param baseUrl The server base URL (for example `http://127.0.0.1:8710`).
     * @param apiKey The bearer API key, or null when the server runs without one.
     */
    public constructor(baseUrl: string, apiKey: string | null) {
        this.baseUrl = baseUrl.replace(/\/+$/, '');
        this.apiKey = apiKey;
    }

    /**
     * Opens the socket and subscribes to a session's live run, reconnecting automatically if the hub is not
     * yet listening or the socket drops. Call once per client; {@link stop} ends it for good.
     *
     * @param sessionId The session to mirror.
     * @param handlers Event/error/close callbacks. `onClose` fires only when finally stopped, not on a
     * transient reconnect.
     */
    public start(sessionId: string, handlers: MirrorHandlers): void {
        this.stopped = false;
        this.all = false;
        this.sessionId = sessionId;
        this.handlers = handlers;
        this.connect();
    }

    /**
     * Subscribes to global conversation-list changes (reconnecting like {@link start}). `handlers.onSessionsChanged`
     * fires whenever the hub reports a list change so the caller can refresh its session list.
     *
     * @param handlers Callbacks; only `onSessionsChanged` is used in this mode.
     */
    public startAll(handlers: MirrorHandlers): void {
        this.stopped = false;
        this.all = true;
        this.handlers = handlers;
        this.connect();
    }

    private connect(): void {
        if (this.stopped) {
            return;
        }

        // Load `ws` lazily and defensively: it is an optional runtime dependency, so a missing/unbundled copy
        // must degrade to "mirroring unavailable" rather than throw where it could disrupt the caller.
        let WebSocketImpl: typeof WebSocket;
        try {
            // eslint-disable-next-line @typescript-eslint/no-var-requires
            WebSocketImpl = require('ws') as typeof WebSocket;
        } catch {
            this.handlers?.onError('Live mirroring is unavailable (the ws module could not be loaded).');
            this.handlers?.onClose();
            return;
        }

        const socket = new WebSocketImpl(this.wsUrl());
        this.socket = socket;

        socket.on('open', () => {
            try {
                socket.send(JSON.stringify(this.all ? { action: 'subscribe', all: true } : { action: 'subscribe', sessionId: this.sessionId }));
            } catch {
                // The close handler will reconnect.
            }
        });

        socket.on('message', (data: WebSocket.RawData) => {
            let frame: unknown;
            try {
                frame = JSON.parse(data.toString());
            } catch {
                return;
            }

            const obj = frame as Record<string, unknown>;
            if (obj && obj.eventType === 'error') {
                this.handlers?.onError(typeof obj.message === 'string' ? obj.message : 'The mirror subscription was rejected.');
                return;
            }

            if (obj && obj.eventType === 'sessions_changed') {
                this.handlers?.onSessionsChanged?.(typeof obj.sessionId === 'string' ? obj.sessionId : '');
                return;
            }

            const event = mirrorEventFromEnvelope(frame);
            if (event) {
                this.handlers?.onEvent(event);
            }
        });

        socket.on('error', () => {
            /* the close handler reconnects */
        });
        socket.on('close', () => {
            if (this.stopped) {
                this.handlers?.onClose();
            } else {
                // Hub not ready yet, or the socket dropped — retry so the subscription self-heals.
                setTimeout(() => this.connect(), 2000);
            }
        });
    }

    /** Closes the socket and stops reconnecting. Idempotent. */
    public stop(): void {
        this.stopped = true;
        try {
            this.socket?.close();
        } catch {
            // Best-effort.
        }
        this.socket = undefined;
    }

    private wsUrl(): string {
        const base = this.baseUrl.replace(/^http/i, 'ws');
        const query = this.apiKey ? `?apiKey=${encodeURIComponent(this.apiKey)}` : '';
        return `${base}/v1.0/ws${query}`;
    }
}
