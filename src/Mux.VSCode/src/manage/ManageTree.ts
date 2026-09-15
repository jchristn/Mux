import * as vscode from 'vscode';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';

/**
 * The kind of a management tree node, used to route context-menu actions and to pick an icon. Section nodes
 * group items; item nodes carry a concrete entity the actions operate on.
 */
export type ManageNodeKind =
    | 'status'
    | 'section-endpoints'
    | 'section-mcp'
    | 'section-prompts'
    | 'section-subagents'
    | 'section-skills'
    | 'section-settings'
    | 'section-usage'
    | 'endpoint'
    | 'mcp'
    | 'prompt'
    | 'subagent'
    | 'skill'
    | 'settings'
    | 'usage'
    | 'info';

/** One node in the management tree. */
export class ManageNode extends vscode.TreeItem {
    /**
     * Creates a node.
     *
     * @param label The display label.
     * @param kind The node kind (routes actions and icon).
     * @param collapsible The collapsible state.
     * @param data The entity this node represents, for item nodes.
     */
    public constructor(
        label: string,
        public readonly kind: ManageNodeKind,
        collapsible: vscode.TreeItemCollapsibleState,
        public readonly data?: unknown,
    ) {
        super(label, collapsible);
        this.contextValue = kind;
    }
}

/**
 * The mux management tree: a single view that surfaces the server's live configuration — connection status,
 * endpoints, MCP servers, prompt profiles, subagents, skills, settings, and usage — so a user has the same
 * visibility and control the terminal UI and desktop app give, without leaving the editor. Sections read the
 * REST API on expand; the context-menu actions in {@link ../manage/actions} mutate through the same API and
 * refresh the tree.
 */
export class ManageTreeProvider implements vscode.TreeDataProvider<ManageNode> {
    private readonly changed = new vscode.EventEmitter<ManageNode | undefined>();
    public readonly onDidChangeTreeData = this.changed.event;

    private readonly lifecycle: MuxServerLifecycle;

    /**
     * Creates the provider and repaints the tree whenever the connection state changes.
     *
     * @param lifecycle The server lifecycle providing the client and connection state.
     */
    public constructor(lifecycle: MuxServerLifecycle) {
        this.lifecycle = lifecycle;
        lifecycle.onDidChangeState(() => this.refresh());
    }

    /** Repaints the whole tree (re-reads the server on the next expand). */
    public refresh(): void {
        this.changed.fire(undefined);
    }

    public getTreeItem(element: ManageNode): vscode.TreeItem {
        return element;
    }

    public async getChildren(element?: ManageNode): Promise<ManageNode[]> {
        if (!element) {
            return this.rootSections();
        }

        try {
            switch (element.kind) {
                case 'section-endpoints':
                    return await this.endpointNodes();
                case 'section-mcp':
                    return await this.mcpNodes();
                case 'section-prompts':
                    return await this.promptNodes();
                case 'section-subagents':
                    return await this.subagentNodes();
                case 'section-skills':
                    return await this.skillNodes();
                case 'section-usage':
                    return await this.usageNodes();
                default:
                    return [];
            }
        } catch (error) {
            logError(`Failed to load ${element.kind}.`, error);
            return [new ManageNode(vscode.l10n.t('Failed to load — is mux connected?'), 'info', vscode.TreeItemCollapsibleState.None)];
        }
    }

    private rootSections(): ManageNode[] {
        const state = this.lifecycle.state;
        const status = new ManageNode(
            state.connected ? vscode.l10n.t('Connected') : vscode.l10n.t('Not connected'),
            'status',
            vscode.TreeItemCollapsibleState.None,
        );
        status.description = state.connected ? `${state.productVersion} · ${state.baseUrl}` : state.detail;
        status.iconPath = new vscode.ThemeIcon(state.connected ? 'pass-filled' : 'warning');
        status.tooltip = state.connected
            ? vscode.l10n.t('{0} (contract {1}) at {2}', state.productVersion, state.contractVersion, state.baseUrl)
            : vscode.l10n.t('{0} — click for help connecting.', state.detail);
        // When connected the status row opens About (versions + dashboard); when not, it opens the help flow.
        status.command = state.connected
            ? { command: 'mux.about', title: vscode.l10n.t('About') }
            : { command: 'mux.showHelp', title: vscode.l10n.t('Help') };

        // Not connected: show only the status row and a help row — the config sections cannot load, and a wall
        // of "failed to load" nodes is noise. The help row tells the user exactly what to do.
        if (!state.connected) {
            const help = new ManageNode(vscode.l10n.t('How to connect'), 'info', vscode.TreeItemCollapsibleState.None);
            help.iconPath = new vscode.ThemeIcon('question');
            help.command = { command: 'mux.showHelp', title: vscode.l10n.t('Help') };
            return [status, help];
        }

        const section = (label: string, kind: ManageNodeKind, icon: string): ManageNode => {
            const node = new ManageNode(label, kind, vscode.TreeItemCollapsibleState.Collapsed);
            node.iconPath = new vscode.ThemeIcon(icon);
            return node;
        };

        const settings = new ManageNode(vscode.l10n.t('Settings'), 'section-settings', vscode.TreeItemCollapsibleState.None);
        settings.iconPath = new vscode.ThemeIcon('settings-gear');
        settings.command = { command: 'mux.manage.editSettings', title: vscode.l10n.t('Edit settings') };

        return [
            status,
            section(vscode.l10n.t('Endpoints'), 'section-endpoints', 'server'),
            section(vscode.l10n.t('MCP Servers'), 'section-mcp', 'plug'),
            section(vscode.l10n.t('Prompts'), 'section-prompts', 'note'),
            section(vscode.l10n.t('Subagents'), 'section-subagents', 'organization'),
            section(vscode.l10n.t('Skills'), 'section-skills', 'lightbulb'),
            settings,
            section(vscode.l10n.t('Usage'), 'section-usage', 'graph'),
        ];
    }

    private async client() {
        return this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
    }

    private async endpointNodes(): Promise<ManageNode[]> {
        const endpoints = await (await this.client()).getEndpoints();
        if (endpoints.length === 0) {
            return [this.emptyNode(vscode.l10n.t('No endpoints — add one'))];
        }

        return endpoints.map((endpoint) => {
            const node = new ManageNode(endpoint.Name, 'endpoint', vscode.TreeItemCollapsibleState.None, endpoint);
            node.description = `${endpoint.AdapterType} · ${endpoint.Model}${endpoint.IsDefault ? ' · default' : ''}`;
            node.iconPath = new vscode.ThemeIcon(endpoint.IsDefault ? 'star-full' : 'server');
            node.tooltip = endpoint.BaseUrl ?? endpoint.Name;
            ManageTreeProvider.openOnClick(node, 'mux.manage.editEndpoint');
            return node;
        });
    }

    private async mcpNodes(): Promise<ManageNode[]> {
        const servers = await (await this.client()).getMcpServers();
        if (servers.length === 0) {
            return [this.emptyNode(vscode.l10n.t('No MCP servers — add one'))];
        }

        return servers.map((server) => {
            const node = new ManageNode(server.Name, 'mcp', vscode.TreeItemCollapsibleState.None, server);
            node.description = server.Transport ?? '';
            node.iconPath = new vscode.ThemeIcon('plug');
            ManageTreeProvider.openOnClick(node, 'mux.manage.editMcp');
            return node;
        });
    }

    private async promptNodes(): Promise<ManageNode[]> {
        const prompts = await (await this.client()).getPrompts();
        return prompts.map((prompt) => {
            const node = new ManageNode(prompt.Name, 'prompt', vscode.TreeItemCollapsibleState.None, prompt);
            node.description = prompt.IsActive ? vscode.l10n.t('active') : '';
            node.iconPath = new vscode.ThemeIcon(prompt.IsActive ? 'pass-filled' : 'note');
            ManageTreeProvider.openOnClick(node, 'mux.manage.editPrompt');
            return node;
        });
    }

    private async subagentNodes(): Promise<ManageNode[]> {
        const subagents = await (await this.client()).getSubagents();
        if (subagents.length === 0) {
            return [this.emptyNode(vscode.l10n.t('No subagents configured'))];
        }

        return subagents.map((subagent) => {
            const node = new ManageNode(subagent.Name, 'subagent', vscode.TreeItemCollapsibleState.None, subagent);
            node.description = subagent.Description;
            node.iconPath = new vscode.ThemeIcon('person');
            node.tooltip = subagent.Description;
            ManageTreeProvider.openOnClick(node, 'mux.manage.editSubagent');
            return node;
        });
    }

    private async skillNodes(): Promise<ManageNode[]> {
        const skills = await (await this.client()).getSkills();
        if (skills.length === 0) {
            return [this.emptyNode(vscode.l10n.t('No skills discovered'))];
        }

        return skills.map((skill) => {
            const node = new ManageNode(skill.Title || skill.Name, 'skill', vscode.TreeItemCollapsibleState.None, skill);
            node.description = skill.Enabled ? vscode.l10n.t('enabled') : vscode.l10n.t('disabled');
            node.iconPath = new vscode.ThemeIcon(skill.Enabled ? 'check' : 'circle-slash');
            node.tooltip = skill.Description;
            ManageTreeProvider.openOnClick(node, 'mux.manage.editSkill');
            return node;
        });
    }

    private async usageNodes(): Promise<ManageNode[]> {
        const summary = await (await this.client()).getUsageSummary('day');
        const m = summary.Metrics;
        const row = (label: string, value: string): ManageNode => {
            const node = new ManageNode(label, 'usage', vscode.TreeItemCollapsibleState.None);
            node.description = value;
            return node;
        };

        return [
            row(vscode.l10n.t('Calls (24h)'), String(m.Calls)),
            row(vscode.l10n.t('Tokens'), String(m.TotalTokens)),
            row(vscode.l10n.t('Cost'), `$${(m.CostUsd ?? 0).toFixed(4)}`),
            row(vscode.l10n.t('Errors'), String(m.Errors)),
            row(vscode.l10n.t('Avg TTFT'), `${Math.round(m.AvgTtftMs ?? 0)} ms`),
            row(vscode.l10n.t('Avg latency'), `${Math.round(m.AvgTotalMs ?? 0)} ms`),
        ];
    }

    private emptyNode(label: string): ManageNode {
        return new ManageNode(label, 'info', vscode.TreeItemCollapsibleState.None);
    }

    /**
     * Wires an item node so a single click opens its editor — the same command the context menu runs, invoked
     * with the node as its argument. This makes the whole row actionable, not just the right-click menu.
     *
     * @param node The item node to make clickable.
     * @param command The edit command to run, passed the node.
     */
    private static openOnClick(node: ManageNode, command: string): void {
        node.command = { command, title: vscode.l10n.t('Edit'), arguments: [node] };
    }
}
