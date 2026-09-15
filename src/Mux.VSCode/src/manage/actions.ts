import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { EndpointDetail, McpServer, MuxServerSettings } from '../api/types';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';
import { FormField, FormPanel } from './FormPanel';
import { ManageNode } from './ManageTree';

/** Adapter types the endpoint form offers, matching the server's kebab-case values. */
const ADAPTERS = ['ollama', 'openai', 'openai-compatible', 'vllm', 'anthropic', 'gemini', 'azure-openai', 'vertex', 'bedrock'];

/**
 * The management actions the tree's context menus invoke. Each obtains a client, mutates through the REST API
 * (the same one the dashboard and CLI use), and refreshes the tree. Editing a member of a collection follows
 * the server's contract: read the whole collection, replace the one item, PUT it all back — a blank secret
 * preserves the stored one. Errors surface as a message rather than failing silently.
 */
export class ManageActions {
    private readonly lifecycle: MuxServerLifecycle;
    private readonly refresh: () => void;

    /**
     * Creates the action set.
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     * @param refresh Callback that repaints the management tree after a change.
     */
    public constructor(lifecycle: MuxServerLifecycle, refresh: () => void) {
        this.lifecycle = lifecycle;
        this.refresh = refresh;
    }

    /** Adds a new endpoint. */
    public addEndpoint(): Promise<void> {
        return this.editEndpointForm(undefined);
    }

    /** Edits the endpoint on a tree node. */
    public async editEndpoint(node: ManageNode): Promise<void> {
        const summary = node.data as { Name: string } | undefined;
        if (!summary) {
            return;
        }

        await this.withClient(async (client) => {
            const all = await client.getEndpointDetails();
            const existing = all.find((e) => e.Name === summary.Name);
            await this.editEndpointFormWith(client, all, existing);
        });
    }

    private editEndpointForm(existing: EndpointDetail | undefined): Promise<void> {
        return this.withClient(async (client) => {
            const all = await client.getEndpointDetails();
            await this.editEndpointFormWith(client, all, existing);
        });
    }

    private async editEndpointFormWith(client: ApiClient, all: EndpointDetail[], existing: EndpointDetail | undefined): Promise<void> {
        const fields: FormField[] = [
            { key: 'Name', label: vscode.l10n.t('Name'), type: 'text', value: existing?.Name ?? '', required: true },
            { key: 'AdapterType', label: vscode.l10n.t('Adapter'), type: 'select', options: ADAPTERS, value: existing?.AdapterType ?? 'openai-compatible' },
            { key: 'BaseUrl', label: vscode.l10n.t('Base URL'), type: 'text', value: existing?.BaseUrl ?? '', hint: vscode.l10n.t('The API base URL, e.g. http://localhost:11434/v1') },
            { key: 'Model', label: vscode.l10n.t('Model'), type: 'text', value: existing?.Model ?? '', required: true },
            { key: 'ApiKey', label: vscode.l10n.t('API key'), type: 'password', value: existing?.ApiKeySet ? true : '', hint: vscode.l10n.t('Leave blank to keep the stored key.') },
            { key: 'MaxTokens', label: vscode.l10n.t('Max tokens'), type: 'number', value: existing?.MaxTokens ?? 8192 },
            { key: 'Temperature', label: vscode.l10n.t('Temperature'), type: 'number', value: existing?.Temperature ?? 0.1 },
            { key: 'ContextWindow', label: vscode.l10n.t('Context window'), type: 'number', value: existing?.ContextWindow ?? 32768 },
            { key: 'TimeoutMs', label: vscode.l10n.t('Timeout (ms)'), type: 'number', value: existing?.TimeoutMs ?? 120000 },
            { key: 'IsDefault', label: vscode.l10n.t('Default endpoint'), type: 'checkbox', value: existing?.IsDefault ?? false },
            { key: 'AutoApproveTools', label: vscode.l10n.t('Auto-approve tools for this endpoint'), type: 'checkbox', value: existing?.AutoApproveTools ?? false },
            { key: 'ShowThinking', label: vscode.l10n.t('Show model thinking'), type: 'checkbox', value: existing?.ShowThinking ?? false },
        ];

        const result = await FormPanel.show(existing ? vscode.l10n.t('Edit endpoint') : vscode.l10n.t('Add endpoint'), fields);
        if (!result) {
            return;
        }

        const edited: EndpointDetail = {
            ...(existing ?? {}),
            Name: String(result.Name).trim(),
            AdapterType: String(result.AdapterType),
            BaseUrl: String(result.BaseUrl).trim(),
            Model: String(result.Model).trim(),
            IsDefault: Boolean(result.IsDefault),
            MaxTokens: Number(result.MaxTokens) || undefined,
            Temperature: Number(result.Temperature),
            ContextWindow: Number(result.ContextWindow) || undefined,
            TimeoutMs: Number(result.TimeoutMs) || undefined,
            AutoApproveTools: Boolean(result.AutoApproveTools),
            ShowThinking: Boolean(result.ShowThinking),
        };

        // A blank API key preserves the stored one (write-only field).
        const key = String(result.ApiKey ?? '').trim();
        edited.ApiKey = key.length > 0 ? key : '';

        // When this endpoint becomes the default, clear the flag on the others.
        let next = all.filter((e) => e.Name !== (existing?.Name ?? edited.Name));
        if (edited.IsDefault) {
            next = next.map((e) => ({ ...e, IsDefault: false, ApiKey: '' }));
        }
        next.push(edited);

        await client.putEndpoints(next);
        void vscode.window.showInformationMessage(vscode.l10n.t('Saved endpoint "{0}".', edited.Name));
        this.refresh();
    }

    /** Deletes the endpoint on a node after confirmation. */
    public async deleteEndpoint(node: ManageNode): Promise<void> {
        const summary = node.data as { Name: string } | undefined;
        if (!summary) {
            return;
        }

        const confirm = await vscode.window.showWarningMessage(
            vscode.l10n.t('Delete endpoint "{0}"?', summary.Name),
            { modal: true },
            vscode.l10n.t('Delete'),
        );
        if (!confirm) {
            return;
        }

        await this.withClient(async (client) => {
            await client.deleteEndpoint(summary.Name);
            this.refresh();
        });
    }

    /** Sets the endpoint on a node as the default. */
    public async setDefaultEndpoint(node: ManageNode): Promise<void> {
        const summary = node.data as { Name: string } | undefined;
        if (!summary) {
            return;
        }

        await this.withClient(async (client) => {
            const all = await client.getEndpointDetails();
            const next = all.map((e) => ({ ...e, IsDefault: e.Name === summary.Name, ApiKey: '' }));
            await client.putEndpoints(next);
            this.refresh();
        });
    }

    /** Adds a new MCP server. */
    public addMcpServer(): Promise<void> {
        return this.editMcpForm(undefined);
    }

    /** Edits the MCP server on a node. */
    public async editMcpServer(node: ManageNode): Promise<void> {
        const server = node.data as McpServer | undefined;
        await this.editMcpForm(server);
    }

    private editMcpForm(existing: McpServer | undefined): Promise<void> {
        return this.withClient(async (client) => {
            const all = await client.getMcpServers();
            const fields: FormField[] = [
                { key: 'Name', label: vscode.l10n.t('Name'), type: 'text', value: existing?.Name ?? '', required: true },
                { key: 'Transport', label: vscode.l10n.t('Transport'), type: 'select', options: ['stdio', 'http'], value: (existing?.Transport as string) ?? 'stdio' },
                { key: 'Command', label: vscode.l10n.t('Command (stdio)'), type: 'text', value: (existing?.Command as string) ?? '' },
                { key: 'Args', label: vscode.l10n.t('Args (one per line, stdio)'), type: 'textarea', value: ((existing?.Args as string[]) ?? []).join('\n') },
                { key: 'Url', label: vscode.l10n.t('URL (http)'), type: 'text', value: (existing?.Url as string) ?? '' },
            ];

            const result = await FormPanel.show(existing ? vscode.l10n.t('Edit MCP server') : vscode.l10n.t('Add MCP server'), fields);
            if (!result) {
                return;
            }

            const edited: McpServer = {
                ...(existing ?? {}),
                Name: String(result.Name).trim(),
                Transport: String(result.Transport),
                Command: String(result.Command).trim() || undefined,
                Args: String(result.Args)
                    .split('\n')
                    .map((a) => a.trim())
                    .filter((a) => a.length > 0),
                Url: String(result.Url).trim() || undefined,
            };

            const next = all.filter((s) => s.Name !== (existing?.Name ?? edited.Name));
            next.push(edited);
            await client.putMcpServers(next);
            void vscode.window.showInformationMessage(vscode.l10n.t('Saved MCP server "{0}".', edited.Name));
            this.refresh();
        });
    }

    /** Deletes the MCP server on a node after confirmation. */
    public async deleteMcpServer(node: ManageNode): Promise<void> {
        const server = node.data as McpServer | undefined;
        if (!server) {
            return;
        }

        const confirm = await vscode.window.showWarningMessage(
            vscode.l10n.t('Delete MCP server "{0}"?', server.Name),
            { modal: true },
            vscode.l10n.t('Delete'),
        );
        if (!confirm) {
            return;
        }

        await this.withClient(async (client) => {
            await client.deleteMcpServer(server.Name);
            this.refresh();
        });
    }

    /** Marks the prompt profile on a node as the active one. */
    public async activatePrompt(node: ManageNode): Promise<void> {
        const prompt = node.data as { Name: string } | undefined;
        if (!prompt) {
            return;
        }

        await this.withClient(async (client) => {
            const all = await client.getPrompts();
            const next = all.map((p) => ({ ...p, IsActive: p.Name === prompt.Name }));
            await client.putPrompts(next);
            this.refresh();
        });
    }

    /** Toggles the skill on a node. */
    public async toggleSkill(node: ManageNode): Promise<void> {
        const skill = node.data as { Name: string; Enabled: boolean } | undefined;
        if (!skill) {
            return;
        }

        await this.withClient(async (client) => {
            await client.setSkillEnabled(skill.Name, !skill.Enabled);
            this.refresh();
        });
    }

    /** Opens the settings editor. */
    public editSettings(): Promise<void> {
        return this.withClient(async (client) => {
            const settings = await client.getSettings();
            const fields: FormField[] = [
                { key: 'DefaultApprovalPolicy', label: vscode.l10n.t('Default approval policy'), type: 'select', options: ['ask', 'auto', 'deny'], value: settings.DefaultApprovalPolicy },
                { key: 'MaxAgentIterations', label: vscode.l10n.t('Max agent iterations'), type: 'number', value: settings.MaxAgentIterations, hint: '1–100' },
                { key: 'MaxConcurrency', label: vscode.l10n.t('Max concurrency'), type: 'number', value: settings.MaxConcurrency, hint: '1–32' },
                { key: 'ToolTimeoutMs', label: vscode.l10n.t('Tool timeout (ms)'), type: 'number', value: settings.ToolTimeoutMs },
                { key: 'ProcessTimeoutMs', label: vscode.l10n.t('Process timeout (ms)'), type: 'number', value: settings.ProcessTimeoutMs },
                { key: 'AutoCompactEnabled', label: vscode.l10n.t('Auto-compaction'), type: 'checkbox', value: settings.AutoCompactEnabled },
                { key: 'CompactionStrategy', label: vscode.l10n.t('Compaction strategy'), type: 'select', options: ['summary', 'trim'], value: settings.CompactionStrategy },
                { key: 'CompactionPreserveTurns', label: vscode.l10n.t('Compaction preserve turns'), type: 'number', value: settings.CompactionPreserveTurns, hint: '1–10' },
                { key: 'ContextWarningThresholdPercent', label: vscode.l10n.t('Context warning threshold %'), type: 'number', value: settings.ContextWarningThresholdPercent, hint: '50–95' },
                { key: 'SkillsEnabled', label: vscode.l10n.t('Skills enabled'), type: 'checkbox', value: settings.SkillsEnabled },
                { key: 'TaskPlanningEnabled', label: vscode.l10n.t('Task planning'), type: 'checkbox', value: settings.TaskPlanningEnabled },
                { key: 'TaskParallelismEnabled', label: vscode.l10n.t('Task parallelism'), type: 'checkbox', value: settings.TaskParallelismEnabled },
                { key: 'IgnoreCertErrors', label: vscode.l10n.t('Ignore TLS certificate errors'), type: 'checkbox', value: settings.IgnoreCertErrors },
            ];

            const result = await FormPanel.show(vscode.l10n.t('mux settings'), fields);
            if (!result) {
                return;
            }

            const next: MuxServerSettings = {
                ...settings,
                DefaultApprovalPolicy: String(result.DefaultApprovalPolicy),
                MaxAgentIterations: Number(result.MaxAgentIterations),
                MaxConcurrency: Number(result.MaxConcurrency),
                ToolTimeoutMs: Number(result.ToolTimeoutMs),
                ProcessTimeoutMs: Number(result.ProcessTimeoutMs),
                AutoCompactEnabled: Boolean(result.AutoCompactEnabled),
                CompactionStrategy: String(result.CompactionStrategy),
                CompactionPreserveTurns: Number(result.CompactionPreserveTurns),
                ContextWarningThresholdPercent: Number(result.ContextWarningThresholdPercent),
                SkillsEnabled: Boolean(result.SkillsEnabled),
                TaskPlanningEnabled: Boolean(result.TaskPlanningEnabled),
                TaskParallelismEnabled: Boolean(result.TaskParallelismEnabled),
                IgnoreCertErrors: Boolean(result.IgnoreCertErrors),
            };

            await client.putSettings(next);
            void vscode.window.showInformationMessage(vscode.l10n.t('Settings saved.'));
            this.refresh();
        });
    }

    private async withClient(action: (client: ApiClient) => Promise<void>): Promise<void> {
        try {
            const client = await this.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            await action(client);
        } catch (error) {
            logError('A management action failed.', error);
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
        }
    }
}
