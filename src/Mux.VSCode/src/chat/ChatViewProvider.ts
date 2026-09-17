import * as crypto from 'crypto';
import * as fs from 'fs';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { readSettings } from '../config/settings';
import { collectContext, workspaceRootPath } from '../context/providers';
import { ContextItem, composePrompt } from '../context/composePrompt';
import { resolveFileContext } from '../context/fileContext';
import { MuxServerLifecycle } from '../server/lifecycle';
import { MirrorClient } from '../mirror/MirrorClient';
import { log, logError } from '../util/logger';

/** Messages the webview posts to the extension host. */
type InboundMessage =
    | { type: 'send'; text: string }
    | { type: 'stop' }
    | { type: 'approve'; runId: string; toolCallId: string; decision: 'y' | 'always' | 'n' };

/**
 * Drives the chat panel. It owns the workspace-bound session, streams a run over the server's SSE surface,
 * renders each event into the webview, and answers approval prompts through the webview. It holds no run
 * loop of its own beyond one turn at a time; a second send while a turn is active is refused by the webview.
 */
export class ChatViewProvider implements vscode.WebviewViewProvider {
    /** The view id contributed in package.json. */
    public static readonly viewType = 'mux.chat';

    private readonly extensionUri: vscode.Uri;
    private readonly lifecycle: MuxServerLifecycle;
    private view: vscode.WebviewView | undefined;
    private history: Array<{ role: string; content: string; reasoning?: string | null }> = [];
    private sessionId = '';
    private activeRun: AbortController | undefined;
    private activeRunId: string | undefined;
    private mirror: MirrorClient | undefined;
    private syncWatch: MirrorClient | undefined;
    private syncSessionId = '';
    private listWatch: MirrorClient | undefined;
    private client: ApiClient | undefined;

    /**
     * Creates the provider.
     *
     * @param extensionUri The extension root, used to resolve webview assets.
     * @param lifecycle The server lifecycle used to obtain a connected client.
     */
    public constructor(extensionUri: vscode.Uri, lifecycle: MuxServerLifecycle) {
        this.extensionUri = extensionUri;
        this.lifecycle = lifecycle;
    }

    /** Resolves the webview, wires its message handler, and renders the initial HTML. */
    public resolveWebviewView(view: vscode.WebviewView): void {
        this.view = view;
        view.webview.options = {
            enableScripts: true,
            localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'dashboard')],
        };
        view.webview.html = this.renderHtml(view.webview);
        view.webview.onDidReceiveMessage((message: InboundMessage) => this.onMessage(message));
    }

    /** Starts a new, empty conversation bound to this workspace. */
    public newConversation(): void {
        this.stopMirror();
        this.stopSessionSync();
        this.history = [];
        this.sessionId = '';
        this.post({ type: 'reset' });
    }

    /**
     * Live-mirrors a session's run into the panel, read-only: subscribes to the mux server's WebSocket bridge
     * by session id and renders the canonical events as they arrive. Used to watch a run started on another
     * surface (the desktop app's embedded server, the dashboard, or a `mux serve`). Any in-flight local turn
     * or prior mirror is stopped first.
     *
     * @param sessionId The session to mirror.
     */
    public async startMirror(sessionId: string): Promise<void> {
        if (!sessionId) {
            return;
        }

        await vscode.commands.executeCommand('mux.chat.focus');
        this.cancelActiveRun();
        this.stopMirror();

        let client: ApiClient;
        try {
            client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
        } catch (error) {
            this.post({ type: 'error', message: error instanceof Error ? error.message : String(error) });
            return;
        }

        const mirror = new MirrorClient(client.serverBaseUrl, client.serverApiKey);
        this.mirror = mirror;
        this.post({ type: 'notice', message: vscode.l10n.t('Mirroring the live run for this session (read-only).') });
        this.post({ type: 'busy', busy: true });

        mirror.start(sessionId, {
            onEvent: (event) => {
                switch (event.kind) {
                    case 'text':
                        this.post({ type: 'token', text: event.text });
                        break;
                    case 'thinking':
                        this.post({ type: 'thinking', text: event.text });
                        break;
                    case 'tool':
                        this.post({ type: 'tool', tool: { Id: event.id, Name: event.name, Status: event.status, ElapsedMs: event.ms } });
                        break;
                    case 'error':
                        this.post({ type: 'error', message: event.message });
                        break;
                    case 'done':
                        this.post({ type: 'notice', message: vscode.l10n.t('Mirrored run finished ({0}).', event.status) });
                        break;
                    default:
                        break;
                }
            },
            onError: (message) => {
                this.post({ type: 'error', message });
            },
            onClose: () => {
                if (this.mirror === mirror) {
                    this.mirror = undefined;
                }
                this.post({ type: 'busy', busy: false });
            },
        });
    }

    /** Stops any active mirror session. Safe to call when none is running. */
    public stopMirror(): void {
        this.mirror?.stop();
        this.mirror = undefined;
    }

    /**
     * Loads a persisted session into the panel — its transcript and id — so the user can continue a
     * conversation started on any surface. Reveals the panel first.
     *
     * @param id The session id to load.
     */
    public async loadSession(id: string): Promise<void> {
        await vscode.commands.executeCommand('mux.chat.focus');
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            this.client = client;
            const detail = await client.getSessionDetail(id);
            this.sessionId = detail.Id;
            this.history = detail.Messages
                .filter((m) => m.Role === 'user' || m.Role === 'assistant')
                .map((m) => ({ role: m.Role, content: m.Content, reasoning: m.Reasoning ?? null }));
            this.post({ type: 'load', messages: this.history, title: detail.Title });
            this.startSessionSync(client, detail.Id);
        } catch (error) {
            logError('Failed to load a session into the panel.', error);
            this.post({ type: 'error', message: error instanceof Error ? error.message : String(error) });
        }
    }

    /**
     * Keeps the open conversation in sync across surfaces (mirroring on by default): subscribes to the
     * session on the hub and, when a run finishes for it elsewhere (the desktop, the terminal, the dashboard,
     * another window), reloads the transcript so new messages appear without a manual refresh. A locally-driven
     * turn is ignored here (the streaming path already renders it). Replaces any prior sync watch.
     *
     * @param client The connected API client (source of the hub URL + key).
     * @param sessionId The session to keep in sync.
     */
    private startSessionSync(client: ApiClient, sessionId: string): void {
        if (!sessionId) {
            return;
        }

        // Already watching this session — keep the live subscription rather than tearing it down and racing a
        // reconnect on every turn.
        if (this.syncWatch && this.syncSessionId === sessionId) {
            return;
        }

        this.stopSessionSync();
        this.syncSessionId = sessionId;
        this.ensureListWatch(client);

        const watch = new MirrorClient(client.serverBaseUrl, client.serverApiKey);
        this.syncWatch = watch;
        watch.start(sessionId, {
            onEvent: (event) => {
                // Only react to a run finishing elsewhere; ignore our own in-flight turn.
                if (event.kind === 'done' && !this.activeRun && this.sessionId === sessionId) {
                    void this.reloadSessionSilent(sessionId);
                }
            },
            onTranscriptChanged: () => {
                // A turn was persisted for this session by a surface that did not stream a run through this
                // hub (an in-process TUI/desktop turn, or an upsert). Reload the same way.
                if (!this.activeRun && this.sessionId === sessionId) {
                    void this.reloadSessionSilent(sessionId);
                }
            },
            onError: () => {
                /* best-effort sync */
            },
            onClose: () => {
                if (this.syncWatch === watch) {
                    this.syncWatch = undefined;
                }
            },
        });
    }

    /**
     * Starts a single, persistent global "all" watch for the panel's lifetime (reconnecting internally). This
     * is the reliable cross-surface signal: the hub emits sessions_changed on EVERY store write — including
     * in-process desktop/TUI turns and turns whose session-scoped transcript_changed raced the subscription —
     * so reloading the open transcript here guarantees content lands even when the per-session socket misses a
     * frame. Idempotent: it attaches once and is reused across session switches.
     *
     * @param client The connected API client (source of the hub URL + key).
     */
    private ensureListWatch(client: ApiClient): void {
        if (this.listWatch) {
            return;
        }

        const watch = new MirrorClient(client.serverBaseUrl, client.serverApiKey);
        this.listWatch = watch;
        watch.startAll({
            onEvent: () => {
                /* list mode carries no run frames */
            },
            onSessionsChanged: (sid: string) => {
                void vscode.commands.executeCommand('mux.sessions.refresh');
                if (sid && sid === this.sessionId && !this.activeRun) {
                    void this.reloadSessionSilent(this.sessionId);
                }
            },
            onError: () => {
                /* best-effort */
            },
            onClose: () => {
                if (this.listWatch === watch) {
                    this.listWatch = undefined;
                }
            },
        });
    }

    /** Stops the cross-surface sync watcher. Safe to call when none is running. */
    private stopSessionSync(): void {
        this.syncWatch?.stop();
        this.syncWatch = undefined;
        this.syncSessionId = '';
    }

    private async reloadSessionSilent(id: string): Promise<void> {
        // The producer publishes run_completed slightly BEFORE it finishes persisting the turn, so a single
        // fetch can read stale content. Poll until the store has more messages than we're showing (the new
        // turn landed), or give up after a few tries.
        const before = this.history.length;
        for (let attempt = 0; attempt < 8; attempt++) {
            await new Promise((resolve) => setTimeout(resolve, 350));
            if (this.sessionId !== id || this.activeRun) {
                return;
            }

            try {
                const client = this.client ?? (await this.lifecycle.getClient(new vscode.CancellationTokenSource().token));
                const detail = await client.getSessionDetail(id);
                if (this.sessionId !== id) {
                    return;
                }

                const messages = detail.Messages
                    .filter((m) => m.Role === 'user' || m.Role === 'assistant')
                    .map((m) => ({ role: m.Role, content: m.Content, reasoning: m.Reasoning ?? null }));
                if (messages.length > before || attempt === 7) {
                    this.history = messages;
                    this.post({ type: 'load', messages: this.history, title: detail.Title });
                    await vscode.commands.executeCommand('mux.sessions.refresh');
                    return;
                }
            } catch (error) {
                logError('Failed to sync a session update.', error);
                return;
            }
        }
    }

    /**
     * Runs a turn from a command (an inline action), showing the panel and echoing the composed request so
     * the user sees what was sent.
     *
     * @param text The user request text; context is attached per settings.
     */
    public async runFromCommand(text: string): Promise<void> {
        await vscode.commands.executeCommand('mux.chat.focus');
        this.post({ type: 'echo', text });
        await this.runTurn(text);
    }

    private async onMessage(message: InboundMessage): Promise<void> {
        switch (message.type) {
            case 'send':
                if (message.text.trim().startsWith('/')) {
                    await this.handleSlash(message.text.trim());
                } else {
                    await this.runTurn(message.text);
                }
                break;
            case 'stop':
                this.cancelActiveRun();
                this.stopMirror();
                break;
            case 'approve':
                await this.answerApproval(message.runId, message.toolCallId, message.decision);
                break;
            default:
                break;
        }
    }

    // Handles a slash command typed in the composer, mirroring the TUI and desktop: some print help or clear
    // the view, others open the corresponding editor surface. Unknown commands show the help card.
    private async handleSlash(input: string): Promise<void> {
        const command = input.split(/\s+/)[0].toLowerCase();
        switch (command) {
            case '/?':
            case '/help':
                this.postHelp();
                break;
            case '/clear':
                this.post({ type: 'reset' });
                break;
            case '/new':
                this.newConversation();
                break;
            case '/endpoints':
            case '/endpoint':
            case '/models':
            case '/model':
                await vscode.commands.executeCommand('mux.selectEndpoint');
                break;
            case '/usage':
                await vscode.commands.executeCommand('mux.usage');
                break;
            case '/settings':
                await vscode.commands.executeCommand('mux.manage.editSettings');
                break;
            case '/about':
                await vscode.commands.executeCommand('mux.about');
                break;
            case '/reconnect':
                await vscode.commands.executeCommand('mux.reconnect');
                break;
            case '/cwd':
                await vscode.commands.executeCommand('mux.setWorkingDirectory');
                break;
            default:
                this.post({ type: 'notice', message: vscode.l10n.t('Unknown command: {0}', command) });
                this.postHelp();
                break;
        }
    }

    private postHelp(): void {
        this.post({
            type: 'help',
            title: vscode.l10n.t('Chat commands'),
            items: [
                { cmd: '/help  /?', desc: vscode.l10n.t('Show this list') },
                { cmd: '/new', desc: vscode.l10n.t('Start a new conversation') },
                { cmd: '/clear', desc: vscode.l10n.t('Clear the transcript') },
                { cmd: '/endpoints', desc: vscode.l10n.t('Choose the endpoint / model') },
                { cmd: '/usage', desc: vscode.l10n.t('Open the usage dashboard') },
                { cmd: '/settings', desc: vscode.l10n.t('Edit mux settings') },
                { cmd: '/cwd', desc: vscode.l10n.t('Show the working directory') },
                { cmd: '/reconnect', desc: vscode.l10n.t('Reconnect to the server') },
                { cmd: '/about', desc: vscode.l10n.t('About mux') },
            ],
        });
    }

    private async answerApproval(runId: string, toolCallId: string, decision: 'y' | 'always' | 'n'): Promise<void> {
        try {
            const client = this.client ?? (await this.lifecycle.getClient(new vscode.CancellationTokenSource().token));
            await client.approve(runId, toolCallId, decision);
        } catch (error) {
            logError('Failed to post an approval decision.', error);
        }
    }

    /**
     * Replaces a large active-file context item's content with a server-built map or summary instead of
     * letting it be hard-truncated. Small files are left whole; when the server call fails the original
     * content is kept and marked truncatable, so {@link composePrompt}'s per-item cap applies as the fallback.
     */
    private async applyLargeFileContext(items: ContextItem[], client: ApiClient, signal: AbortSignal): Promise<void> {
        for (const item of items) {
            if (item.kind !== 'activeFile' || !item.path) {
                continue;
            }

            const resolved = await resolveFileContext(item.path, item.content, {
                fetcher: (request) => client.buildFileContext(request, signal),
            });
            item.content = resolved.content;
            item.noTruncate = resolved.noTruncate;
        }
    }

    private async runTurn(userText: string): Promise<void> {
        const text = userText.trim();
        if (!text || this.activeRun) {
            return;
        }

        const controller = new AbortController();
        this.activeRun = controller;
        this.post({ type: 'busy', busy: true });

        try {
            const token = new vscode.CancellationTokenSource().token;
            const client = await this.lifecycle.getClient(token);
            this.client = client;

            const endpoint = await this.resolveEndpoint(client);
            if (!endpoint) {
                this.post({ type: 'error', message: vscode.l10n.t('No endpoint is configured. Add one in mux, then try again.') });
                return;
            }

            const settings = readSettings();
            const collected = await collectContext(settings.contextSources, workspaceRootPath());
            await this.applyLargeFileContext(collected.items, client, controller.signal);
            const composed = composePrompt(text, collected.items);
            if (collected.unavailable.length > 0) {
                this.post({ type: 'notice', message: vscode.l10n.t('Not attached: {0}', collected.unavailable.join(', ')) });
            }
            if (composed.truncated.length > 0) {
                this.post({ type: 'notice', message: vscode.l10n.t('Truncated to fit: {0}', composed.truncated.join(', ')) });
            }

            this.history.push({ role: 'user', content: composed.prompt });
            let assistant = '';

            for await (const event of client.streamChat(
                {
                    endpoint,
                    id: this.sessionId || undefined,
                    workingDirectory: workspaceRootPath(),
                    messages: this.history,
                },
                controller.signal,
            )) {
                switch (event.event) {
                    case 'run':
                        this.activeRunId = event.data.RunId;
                        break;
                    case 'token':
                        assistant += event.data;
                        this.post({ type: 'token', text: event.data });
                        break;
                    case 'thinking':
                        this.post({ type: 'thinking', text: event.data });
                        break;
                    case 'tool':
                        this.post({ type: 'tool', tool: event.data });
                        break;
                    case 'approval':
                        this.post({ type: 'approval', request: event.data });
                        break;
                    case 'done':
                        assistant = event.data.Content || assistant;
                        if (event.data.Id) {
                            this.sessionId = event.data.Id;
                            // Now that this conversation is persisted with an id, keep it live-synced so a turn
                            // added to it on another surface reloads here — a locally-created conversation was
                            // previously never subscribed (only manual tree-resume was), so it never updated.
                            this.startSessionSync(client, this.sessionId);
                        }
                        // Send the server's authoritative full content so the final render can't be missing a
                        // token that slipped during streaming.
                        this.post({ type: 'done', stats: event.data.Stats, content: event.data.Content });
                        break;
                    case 'error':
                        this.post({ type: 'error', message: event.data });
                        break;
                    default:
                        break;
                }
            }

            this.history.push({ role: 'assistant', content: assistant });
            log(`Turn complete for session ${this.sessionId || '(new)'}.`);
            await vscode.commands.executeCommand('mux.sessions.refresh');
        } catch (error) {
            if (controller.signal.aborted) {
                this.history.pop();
                this.post({ type: 'stopped' });
            } else {
                logError('A chat turn failed.', error);
                this.post({ type: 'error', message: error instanceof Error ? error.message : String(error) });
            }
        } finally {
            this.activeRun = undefined;
            this.activeRunId = undefined;
            this.post({ type: 'busy', busy: false });
        }
    }

    /**
     * Cancels the active turn: aborts the local stream and asks the server to cancel the run so it stops
     * server-side (not just in this editor). Safe to call when nothing is running. Invoked by the composer
     * Stop button and the <c>mux.cancelRun</c> command.
     */
    public cancelActiveRun(): void {
        const runId = this.activeRunId;
        const client = this.client;
        this.activeRun?.abort();
        if (runId && client) {
            void client.cancelRun(runId).catch(() => {
                // Best-effort: the run may already have finished.
            });
        }
    }

    private async resolveEndpoint(client: ApiClient): Promise<string | undefined> {
        const configured = readSettings().defaultEndpoint;
        if (configured) {
            return configured;
        }

        const endpoints = await client.getEndpoints();
        const preferred = endpoints.find((e) => e.IsDefault) ?? endpoints[0];
        return preferred?.Name;
    }

    private post(message: Record<string, unknown>): void {
        this.view?.webview.postMessage(message);
    }

    private renderHtml(webview: vscode.Webview): string {
        const nonce = crypto.randomBytes(16).toString('hex');
        const asset = (...parts: string[]): vscode.Uri =>
            webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'dashboard', 'chat', ...parts));
        const strings = JSON.stringify(this.uiStrings());
        const csp = [
            "default-src 'none'",
            `style-src ${webview.cspSource}`,
            `script-src 'nonce-${nonce}'`,
            `font-src ${webview.cspSource}`,
        ].join('; ');

        return `<!DOCTYPE html>
<html lang="${vscode.env.language}">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="${csp}" />
<link rel="stylesheet" href="${asset('style.css')}" />
</head>
<body>
<div id="transcript" aria-live="polite"></div>
<div id="notice" role="status"></div>
<form id="composer">
  <textarea id="input" rows="1" placeholder="${this.uiStrings()['composer.aria']}" aria-label="${this.uiStrings()['composer.aria']}"></textarea>
  <button id="send" type="submit">${this.uiStrings()['composer.send']}</button>
</form>
<script nonce="${nonce}">window.MUX_STRINGS = ${strings};</script>
<script nonce="${nonce}">${this.readAsset('markdown.js')}</script>
<script nonce="${nonce}" src="${asset('main.js')}"></script>
</body>
</html>`;
    }

    // Reads a dashboard asset from disk to inline into the page. The Markdown renderer is inlined (rather than
    // loaded as a separate <script src>) so it is guaranteed to be defined before main.js runs — a separate
    // resource load can silently fail and leave replies rendering as plain text.
    private readAsset(name: string): string {
        try {
            return fs.readFileSync(vscode.Uri.joinPath(this.extensionUri, 'dashboard', 'chat', name).fsPath, 'utf8');
        } catch (error) {
            logError(`Failed to read webview asset ${name}.`, error);
            return '';
        }
    }

    private uiStrings(): Record<string, string> {
        return {
            'composer.send': vscode.l10n.t('Send'),
            'composer.stop': vscode.l10n.t('Stop'),
            'composer.aria': vscode.l10n.t('Message mux'),
            'empty.title': vscode.l10n.t('Ask mux about your code'),
            'empty.hint': vscode.l10n.t('Attach the file, selection, diagnostics, or diff and ask a question.'),
            'approval.title': vscode.l10n.t('Allow mux to run {0}?'),
            'approval.approve': vscode.l10n.t('Approve'),
            'approval.always': vscode.l10n.t('Approve for session'),
            'approval.deny': vscode.l10n.t('Deny'),
            'thinking.label': vscode.l10n.t('Thinking'),
            'stopped': vscode.l10n.t('(stopped)'),
            'role.you': vscode.l10n.t('You'),
            'role.mux': vscode.l10n.t('mux'),
            'role.error': vscode.l10n.t('Error'),
            'stats.tokens': vscode.l10n.t('tokens'),
            'stats.ttft': vscode.l10n.t('Time to first token'),
            'stats.streaming': vscode.l10n.t('Streaming'),
            'stats.total': vscode.l10n.t('Total time'),
            'stats.input': vscode.l10n.t('Input tokens'),
            'stats.output': vscode.l10n.t('Output tokens'),
            'stats.totalTokens': vscode.l10n.t('Total tokens'),
        };
    }
}
