import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { readSettings, resolveDisplayLocale } from '../config/settings';
import { formatRelativeTime } from '../i18n/format';
import { MuxServerLifecycle } from '../server/lifecycle';
import { SessionSummary } from '../api/types';
import { logError } from '../util/logger';

/** A tree node wrapping a persisted session. */
export class SessionNode extends vscode.TreeItem {
    /**
     * Creates a node.
     *
     * @param session The session summary.
     * @param locale The display locale for the relative-time description.
     * @param nowMs The current epoch milliseconds, for the relative-time description.
     */
    public constructor(public readonly session: SessionSummary, locale: string, nowMs: number) {
        super(session.Title || session.Id, vscode.TreeItemCollapsibleState.None);
        this.id = session.Id;
        const meta = SessionNode.metadataSummary(session);
        this.description = meta.length > 0
            ? `${session.Model} · ${meta} · ${formatRelativeTime(locale, session.UpdatedUtc, nowMs)}`
            : `${session.Model} · ${formatRelativeTime(locale, session.UpdatedUtc, nowMs)}`;
        this.tooltip = vscode.l10n.t('{0} — {1} messages', session.Title || session.Id, String(session.MessageCount));
        this.contextValue = 'muxSession';
        this.iconPath = new vscode.ThemeIcon('comment-discussion');
        this.command = { command: 'mux.sessions.resume', title: vscode.l10n.t('Resume'), arguments: [this] };
    }

    /** A compact "#label key:value" summary of a session's metadata, or an empty string when it has none. */
    private static metadataSummary(session: SessionSummary): string {
        const parts: string[] = [];
        for (const label of session.Labels ?? []) {
            parts.push(`#${label}`);
        }
        for (const tag of session.Tags ?? []) {
            parts.push(`${tag.Key}:${tag.Value}`);
        }
        return parts.join(' ');
    }
}

/**
 * Lists the shared session store as an editor tree and hosts the management verbs. Because it reads the same
 * store the TUI and desktop write, a session created in any surface appears here on refresh, and every verb
 * here — resume, rename, duplicate, export, delete — is visible in the others.
 */
export class SessionTreeProvider implements vscode.TreeDataProvider<SessionNode> {
    private readonly changed = new vscode.EventEmitter<void>();
    public readonly onDidChangeTreeData = this.changed.event;

    private readonly lifecycle: MuxServerLifecycle;
    private readonly onResume: (id: string) => Promise<void>;

    /**
     * Creates the provider.
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     * @param onResume Callback that loads a session into the chat panel.
     */
    public constructor(lifecycle: MuxServerLifecycle, onResume: (id: string) => Promise<void>) {
        this.lifecycle = lifecycle;
        this.onResume = onResume;
    }

    /** Re-reads the store and repaints the tree. */
    public refresh(): void {
        this.changed.fire();
    }

    public getTreeItem(element: SessionNode): vscode.TreeItem {
        return element;
    }

    public async getChildren(): Promise<SessionNode[]> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const sessions = await client.getSessions();
            const locale = resolveDisplayLocale(readSettings());
            const now = Date.now();
            return sessions.map((session) => new SessionNode(session, locale, now));
        } catch (error) {
            logError('Failed to list sessions.', error);
            return [];
        }
    }

    /** Loads the selected session into the chat panel. */
    public async resume(node: SessionNode): Promise<void> {
        await this.onResume(node.session.Id);
    }

    /** Renames a session, pinning the new title. */
    public async rename(node: SessionNode): Promise<void> {
        const title = await vscode.window.showInputBox({
            prompt: vscode.l10n.t('New title'),
            value: node.session.Title,
        });
        if (title === undefined || title.trim().length === 0) {
            return;
        }

        await this.withClient(async (client) => {
            const detail = await client.getSessionDetail(node.session.Id);
            await client.putSession({
                Id: node.session.Id,
                Title: title.trim(),
                EndpointName: detail.EndpointName,
                Model: detail.Model,
                Messages: detail.Messages.map((m) => ({ Role: m.Role, Content: m.Content })),
            });
            this.refresh();
        });
    }

    /** Duplicates a session under a new id with a "(copy)" title. */
    public async duplicate(node: SessionNode): Promise<void> {
        await this.withClient(async (client) => {
            const detail = await client.getSessionDetail(node.session.Id);
            await client.putSession({
                Id: '',
                Title: `${detail.Title} (copy)`,
                EndpointName: detail.EndpointName,
                Model: detail.Model,
                Messages: detail.Messages.map((m) => ({ Role: m.Role, Content: m.Content })),
            });
            this.refresh();
        });
    }

    /** Edits a session's labels: prompts for a comma-separated set and reconciles it against the current one. */
    public async label(node: SessionNode): Promise<void> {
        const existing = (node.session.Labels ?? []).join(', ');
        const entered = await vscode.window.showInputBox({
            prompt: vscode.l10n.t('Labels (comma-separated)'),
            value: existing,
        });
        if (entered === undefined) {
            return;
        }

        const desired = entered
            .split(',')
            .map((s) => s.trim())
            .filter((s) => s.length > 0);
        const desiredLower = new Set(desired.map((s) => s.toLowerCase()));
        const current = node.session.Labels ?? [];
        const removeLabels = current.filter((l) => !desiredLower.has(l.toLowerCase()));

        await this.withClient(async (client) => {
            await client.patchSessionMetadata(node.session.Id, { AddLabels: desired, RemoveLabels: removeLabels });
            this.refresh();
        });
    }

    /** Edits a session's tags: prompts for a comma-separated key:value set and reconciles it. */
    public async tag(node: SessionNode): Promise<void> {
        const existing = (node.session.Tags ?? []).map((t) => `${t.Key}: ${t.Value}`).join(', ');
        const entered = await vscode.window.showInputBox({
            prompt: vscode.l10n.t('Tags (key: value, comma-separated)'),
            value: existing,
        });
        if (entered === undefined) {
            return;
        }

        const setTags: Array<{ Key: string; Value: string }> = [];
        for (const part of entered.split(',')) {
            const colon = part.indexOf(':');
            if (colon < 0) {
                continue;
            }
            const key = part.slice(0, colon).trim();
            const value = part.slice(colon + 1).trim();
            if (key.length > 0 && value.length > 0) {
                setTags.push({ Key: key, Value: value });
            }
        }

        const keptKeys = new Set(setTags.map((t) => t.Key.toLowerCase().replace(/\s+/g, '-')));
        const removeTagKeys = (node.session.Tags ?? [])
            .map((t) => t.Key)
            .filter((k) => !keptKeys.has(k.toLowerCase()));

        await this.withClient(async (client) => {
            await client.patchSessionMetadata(node.session.Id, { SetTags: setTags, RemoveTagKeys: removeTagKeys });
            this.refresh();
        });
    }

    /** Exports a session to a file the user chooses. */
    public async export(node: SessionNode): Promise<void> {
        const picked = await vscode.window.showQuickPick(
            [
                { label: vscode.l10n.t('Markdown'), format: 'md' as const },
                { label: vscode.l10n.t('HTML'), format: 'html' as const },
            ],
            { placeHolder: vscode.l10n.t('Export format') },
        );
        if (!picked) {
            return;
        }

        await this.withClient(async (client) => {
            const rendered = await client.getSessionExport(node.session.Id, picked.format);
            const target = await vscode.window.showSaveDialog({ saveLabel: vscode.l10n.t('Export session'), defaultUri: vscode.Uri.file(rendered.filename) });
            if (!target) {
                return;
            }

            await vscode.workspace.fs.writeFile(target, Buffer.from(rendered.content, 'utf8'));
            void vscode.window.showInformationMessage(vscode.l10n.t('Session exported.'));
        });
    }

    /** Deletes a session after a modal confirmation. */
    public async delete(node: SessionNode): Promise<void> {
        const confirm = await vscode.window.showWarningMessage(
            vscode.l10n.t('Delete "{0}"? This cannot be undone.', node.session.Title || node.session.Id),
            { modal: true },
            vscode.l10n.t('Delete'),
        );
        if (!confirm) {
            return;
        }

        await this.withClient(async (client) => {
            await client.deleteSession(node.session.Id);
            this.refresh();
        });
    }

    private async withClient(action: (client: ApiClient) => Promise<void>): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            await action(client);
        } catch (error) {
            logError('A session operation failed.', error);
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
        }
    }
}
