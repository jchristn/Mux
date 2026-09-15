import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { readSettings } from '../config/settings';
import { collectContext, workspaceRootPath } from '../context/providers';
import { composePrompt } from '../context/composePrompt';
import { MuxServerLifecycle } from '../server/lifecycle';
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
    private history: Array<{ role: string; content: string }> = [];
    private sessionId = '';
    private activeRun: AbortController | undefined;
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
        this.history = [];
        this.sessionId = '';
        this.post({ type: 'reset' });
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
            const detail = await client.getSessionDetail(id);
            this.sessionId = detail.Id;
            this.history = detail.Messages
                .filter((m) => m.Role === 'user' || m.Role === 'assistant')
                .map((m) => ({ role: m.Role, content: m.Content }));
            this.post({ type: 'load', messages: this.history, title: detail.Title });
        } catch (error) {
            logError('Failed to load a session into the panel.', error);
            this.post({ type: 'error', message: error instanceof Error ? error.message : String(error) });
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
                await this.runTurn(message.text);
                break;
            case 'stop':
                this.activeRun?.abort();
                break;
            case 'approve':
                await this.answerApproval(message.runId, message.toolCallId, message.decision);
                break;
            default:
                break;
        }
    }

    private async answerApproval(runId: string, toolCallId: string, decision: 'y' | 'always' | 'n'): Promise<void> {
        try {
            const client = this.client ?? (await this.lifecycle.getClient(new vscode.CancellationTokenSource().token));
            await client.approve(runId, toolCallId, decision);
        } catch (error) {
            logError('Failed to post an approval decision.', error);
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
                        }
                        this.post({ type: 'done', stats: event.data.Stats });
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
            this.post({ type: 'busy', busy: false });
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
<script nonce="${nonce}" src="${asset('markdown.js')}"></script>
<script nonce="${nonce}" src="${asset('main.js')}"></script>
</body>
</html>`;
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
