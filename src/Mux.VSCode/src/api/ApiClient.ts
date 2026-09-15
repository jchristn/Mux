/**
 * The single client every extension-to-server call goes through. Wraps the browser/Node `fetch` API (no
 * third-party HTTP dependency), attaches bearer auth, throws a typed {@link ApiError} on any non-2xx, and
 * exposes the streamed run as an async iterable of {@link StreamEvent}. It holds no `vscode` reference so it
 * can be unit-tested against a stub server.
 */

import { ApiError } from './ApiError';
import { drainBuffer } from './streaming';
import {
    ChatStreamRequest,
    CheckpointActionResult,
    CheckpointStatus,
    EndpointDetail,
    EndpointSummary,
    HealthResponse,
    McpServer,
    MuxServerSettings,
    PromptProfile,
    SessionDetail,
    SessionSummary,
    SkillSummary,
    StreamEvent,
    Subagent,
    UsageBucket,
    UsageSummary,
} from './types';

/** Options for constructing an {@link ApiClient}. */
export interface ApiClientOptions {
    /** The server base URL, for example `http://127.0.0.1:8710`. */
    baseUrl: string;

    /** The bearer API key, or null when the server runs with `--no-auth`. */
    apiKey: string | null;
}

/** A thin, typed client over the local mux server. */
export class ApiClient {
    private readonly baseUrl: string;
    private readonly apiKey: string | null;

    /**
     * Creates a client.
     *
     * @param options The base URL and API key. The base URL is normalized to drop a trailing slash.
     */
    public constructor(options: ApiClientOptions) {
        this.baseUrl = options.baseUrl.replace(/\/+$/, '');
        this.apiKey = options.apiKey;
    }

    /** Reads the server health, including the negotiated contract version. */
    public getHealth(signal?: AbortSignal): Promise<HealthResponse> {
        return this.getJson<HealthResponse>('/v1.0/api/health', signal);
    }

    /** Lists the configured endpoints (no secrets). */
    public async getEndpoints(signal?: AbortSignal): Promise<EndpointSummary[]> {
        const response = await this.getJson<{ Items: EndpointSummary[] }>('/v1.0/api/endpoints', signal);
        return response.Items ?? [];
    }

    /** Lists persisted sessions, newest-updated first as the server returns them. */
    public async getSessions(signal?: AbortSignal): Promise<SessionSummary[]> {
        const response = await this.getJson<{ Items: SessionSummary[] }>('/v1.0/api/sessions', signal);
        return (response.Items ?? []).slice().sort((a, b) => b.UpdatedUtc.localeCompare(a.UpdatedUtc));
    }

    /** Loads one session's full transcript. */
    public getSessionDetail(id: string, signal?: AbortSignal): Promise<SessionDetail> {
        return this.getJson<SessionDetail>(`/v1.0/api/sessions/detail?id=${encodeURIComponent(id)}`, signal);
    }

    /**
     * Creates or updates a session. A blank `id` mints a new one server-side; the saved summary is returned.
     */
    public putSession(
        body: { Id: string; Title: string; EndpointName: string; Model: string; Messages: Array<{ Role: string; Content: string }> },
        signal?: AbortSignal,
    ): Promise<SessionSummary> {
        return this.sendJson<SessionSummary>('PUT', '/v1.0/api/sessions', body, signal);
    }

    /** Deletes a session by id. */
    public async deleteSession(id: string, signal?: AbortSignal): Promise<void> {
        await this.send('DELETE', `/v1.0/api/sessions?id=${encodeURIComponent(id)}`, undefined, signal);
    }

    /** Renders a session to Markdown or HTML for download. */
    public getSessionExport(id: string, format: 'md' | 'html', signal?: AbortSignal): Promise<{ format: string; filename: string; content: string }> {
        return this.getJson(`/v1.0/api/sessions/export?id=${encodeURIComponent(id)}&format=${format}`, signal);
    }

    /** Lists the full, editable endpoints (secrets masked). */
    public async getEndpointDetails(signal?: AbortSignal): Promise<EndpointDetail[]> {
        const response = await this.getJson<{ Items: EndpointDetail[] }>('/v1.0/api/endpoints/detail', signal);
        return response.Items ?? [];
    }

    /** Replaces the endpoint collection. A blank ApiKey on an item preserves the stored one. */
    public async putEndpoints(items: EndpointDetail[], signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/endpoints', { Items: items }, signal);
    }

    /** Deletes an endpoint by name. */
    public async deleteEndpoint(name: string, signal?: AbortSignal): Promise<void> {
        await this.send('DELETE', `/v1.0/api/endpoints?name=${encodeURIComponent(name)}`, undefined, signal);
    }

    /** Lists configured MCP servers. */
    public async getMcpServers(signal?: AbortSignal): Promise<McpServer[]> {
        const response = await this.getJson<{ Items: McpServer[] }>('/v1.0/api/mcp-servers', signal);
        return response.Items ?? [];
    }

    /** Replaces the MCP server collection. */
    public async putMcpServers(items: McpServer[], signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/mcp-servers', { Items: items }, signal);
    }

    /** Deletes an MCP server by name. */
    public async deleteMcpServer(name: string, signal?: AbortSignal): Promise<void> {
        await this.send('DELETE', `/v1.0/api/mcp-servers?name=${encodeURIComponent(name)}`, undefined, signal);
    }

    /** Lists prompt profiles. */
    public async getPrompts(signal?: AbortSignal): Promise<PromptProfile[]> {
        const response = await this.getJson<{ Items: PromptProfile[] }>('/v1.0/api/prompts', signal);
        return response.Items ?? [];
    }

    /** Replaces the prompt profile collection (exactly one should be active). */
    public async putPrompts(items: PromptProfile[], signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/prompts', { Items: items }, signal);
    }

    /** Lists subagents. */
    public async getSubagents(signal?: AbortSignal): Promise<Subagent[]> {
        const response = await this.getJson<{ Items: Subagent[] }>('/v1.0/api/subagents', signal);
        return response.Items ?? [];
    }

    /** Replaces the subagent collection. */
    public async putSubagents(items: Subagent[], signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/subagents', { Items: items }, signal);
    }

    /** Lists skills. */
    public async getSkills(signal?: AbortSignal): Promise<SkillSummary[]> {
        const response = await this.getJson<{ Items: SkillSummary[] }>('/v1.0/api/skills', signal);
        return response.Items ?? [];
    }

    /** Reads one skill's SKILL.md body. */
    public async getSkillBody(id: string, signal?: AbortSignal): Promise<string> {
        const detail = await this.getJson<{ Body?: string }>(`/v1.0/api/skills/detail?id=${encodeURIComponent(id)}`, signal);
        return detail.Body ?? '';
    }

    /** Enables or disables a skill by id. */
    public async setSkillEnabled(id: string, enabled: boolean, signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/skills/enabled', { Id: id, Enabled: enabled }, signal);
    }

    /** Creates a new skill from its SKILL.md body. */
    public async createSkill(name: string, body: string, signal?: AbortSignal): Promise<void> {
        await this.sendJson('POST', '/v1.0/api/skills', { Name: name, Body: body }, signal);
    }

    /** Updates a skill's SKILL.md body. */
    public async setSkillBody(id: string, body: string, signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/skills/body', { Id: id, Body: body }, signal);
    }

    /** Deletes a skill by id. */
    public async deleteSkill(id: string, signal?: AbortSignal): Promise<void> {
        await this.send('DELETE', `/v1.0/api/skills?id=${encodeURIComponent(id)}`, undefined, signal);
    }

    /** Reads the editable settings (secrets masked). */
    public getSettings(signal?: AbortSignal): Promise<MuxServerSettings> {
        return this.getJson<MuxServerSettings>('/v1.0/api/settings', signal);
    }

    /** Updates settings (validated and clamped server-side). */
    public async putSettings(settings: MuxServerSettings, signal?: AbortSignal): Promise<void> {
        await this.sendJson('PUT', '/v1.0/api/settings', settings, signal);
    }

    /** Reads a usage summary for a range (`hour`, `day`, `week`, `month`, `all`). */
    public getUsageSummary(range: string, signal?: AbortSignal): Promise<UsageSummary> {
        return this.getJson<UsageSummary>(`/v1.0/api/usage/summary?range=${encodeURIComponent(range)}`, signal);
    }

    /** Reads a dense, evenly-spaced usage timeseries for charting a range. */
    public async getUsageTimeseries(range: string, signal?: AbortSignal): Promise<UsageBucket[]> {
        const response = await this.getJson<{ Items: UsageBucket[] }>(`/v1.0/api/usage/timeseries?range=${encodeURIComponent(range)}`, signal);
        return response.Items ?? [];
    }

    /** Reads whether a turn's changes can be undone or redone in a working directory. */
    public getCheckpointStatus(workingDirectory: string, signal?: AbortSignal): Promise<CheckpointStatus> {
        return this.getJson<CheckpointStatus>(`/v1.0/api/checkpoints?workingDirectory=${encodeURIComponent(workingDirectory)}`, signal);
    }

    /** Undoes the last turn's file changes in a working directory. */
    public undoCheckpoint(workingDirectory: string, signal?: AbortSignal): Promise<CheckpointActionResult> {
        return this.sendJson<CheckpointActionResult>('POST', '/v1.0/api/checkpoints/undo', { WorkingDirectory: workingDirectory }, signal);
    }

    /** Redoes an undone turn's file changes in a working directory. */
    public redoCheckpoint(workingDirectory: string, signal?: AbortSignal): Promise<CheckpointActionResult> {
        return this.sendJson<CheckpointActionResult>('POST', '/v1.0/api/checkpoints/redo', { WorkingDirectory: workingDirectory }, signal);
    }

    /** Answers an approval prompt raised during an interactive run. */
    public async approve(runId: string, toolCallId: string, decision: 'y' | 'always' | 'n', signal?: AbortSignal): Promise<void> {
        await this.sendJson('POST', '/v1.0/api/chat/approve', { RunId: runId, ToolCallId: toolCallId, Decision: decision }, signal);
    }

    /**
     * Runs a streamed agentic turn, yielding each event as it arrives. The caller drives the loop and may
     * abort via the signal; aborting closes the stream and drops the incomplete turn server-side.
     *
     * @param request The run request (endpoint, session id, working directory, messages).
     * @param signal An abort signal to cancel the run.
     * @returns An async iterable of typed stream events.
     * @throws {ApiError} When the server rejects the request before streaming (for example a missing working directory).
     */
    public async *streamChat(request: ChatStreamRequest, signal?: AbortSignal): AsyncGenerator<StreamEvent> {
        const response = await fetch(this.url('/v1.0/api/chat/stream'), {
            method: 'POST',
            headers: this.headers({ 'Content-Type': 'application/json', Accept: 'text/event-stream' }),
            body: JSON.stringify(request),
            signal,
        });

        if (!response.ok || !response.body) {
            throw new ApiError(response.status, await this.safeText(response));
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        for (;;) {
            const { done, value } = await reader.read();
            if (done) {
                break;
            }

            buffer += decoder.decode(value, { stream: true });
            const drained = drainBuffer(buffer);
            buffer = drained.rest;
            for (const event of drained.events) {
                yield event;
            }
        }
    }

    private async getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
        return this.sendJson<T>('GET', path, undefined, signal);
    }

    private async sendJson<T>(method: string, path: string, body: unknown, signal?: AbortSignal): Promise<T> {
        const response = await this.send(method, path, body, signal);
        const text = await this.safeText(response);
        return (text ? JSON.parse(text) : {}) as T;
    }

    private async send(method: string, path: string, body: unknown, signal?: AbortSignal): Promise<Response> {
        const headers = this.headers(body === undefined ? {} : { 'Content-Type': 'application/json' });
        const response = await fetch(this.url(path), {
            method,
            headers,
            body: body === undefined ? undefined : JSON.stringify(body),
            signal,
        });

        if (!response.ok) {
            throw new ApiError(response.status, await this.safeText(response));
        }

        return response;
    }

    private headers(extra: Record<string, string>): Record<string, string> {
        const headers: Record<string, string> = { ...extra };
        if (this.apiKey) {
            headers.Authorization = `Bearer ${this.apiKey}`;
        }

        return headers;
    }

    private url(path: string): string {
        return `${this.baseUrl}${path}`;
    }

    private async safeText(response: Response): Promise<string> {
        try {
            return await response.text();
        } catch {
            return '';
        }
    }
}
