/**
 * Wire types shared between the extension and the local mux server's REST/SSE surface. These mirror the
 * server DTOs under `Mux.Server/Models`; only the fields the extension reads are declared.
 */

/** A configured endpoint, as returned by `GET /v1.0/api/endpoints`. */
export interface EndpointSummary {
    Name: string;
    AdapterType: string;
    BaseUrl?: string | null;
    Model: string;
    IsDefault: boolean;
}

/** A custom header on an endpoint (secrets blanked on read, with a set flag). */
export interface EndpointHeader {
    Key: string;
    Value: string;
    ValueSet?: boolean;
}

/** A full, editable endpoint as returned by `GET /v1.0/api/endpoints/detail` (secrets masked). */
export interface EndpointDetail {
    Name: string;
    AdapterType: string;
    BaseUrl?: string | null;
    Model: string;
    IsDefault: boolean;
    MaxTokens?: number;
    Temperature?: number;
    ContextWindow?: number;
    TimeoutMs?: number;
    AutoApproveTools?: boolean;
    ShowThinking?: boolean;
    ApiKey?: string;
    ApiKeySet?: boolean;
    /** How the API key is presented for the OpenAI-family adapters: 'bearer' (default), 'header', or 'query'. */
    AuthPlacement?: string;
    /** Header or query-string parameter name that carries the API key for header/query placement. */
    AuthParameterName?: string;
    /** Cloud region (vertex/bedrock). */
    Region?: string;
    /** Google Cloud project id (vertex). */
    Project?: string;
    /** Azure OpenAI api-version. */
    ApiVersion?: string;
    Headers?: EndpointHeader[];
}

/** An MCP server definition as returned by `GET /v1.0/api/mcp-servers`. */
export interface McpServer {
    Name: string;
    Transport?: string;
    Command?: string;
    Args?: string[];
    Env?: string[];
    Url?: string;
    McpPath?: string;
    AuthType?: string;
    AuthHeader?: string;
    AuthSecret?: string;
    AuthSecretSet?: boolean;
    [key: string]: unknown;
}

/** A prompt profile as returned by `GET /v1.0/api/prompts`. All three prompt fields are editable; a blank
 * field inherits the built-in default. */
export interface PromptProfile {
    Name: string;
    IsActive: boolean;
    SystemPrompt: string;
    ToolsDisabledPrompt?: string;
    CompactionPrompt?: string;
    [key: string]: unknown;
}

/** One operational-prompt catalog entry, as returned by `GET /v1.0/api/prompts/catalog`. */
export interface PromptCatalogEntry {
    Key: string;
    Kind: string;
    Scope: string;
    DisplayName: string;
    Description: string;
    Placeholders: string[];
    Default: string;
    Effective: string;
    Overridden: boolean;
    Editable: boolean;
}

/** A request to build a model-context block, sent to `POST /v1.0/api/context/file`. Field names are
 * lower-camel; the server parses them case-insensitively against its PascalCase DTO. */
export interface FileContextRequest {
    /** The file path (for the map note and outline heuristics). */
    path: string;

    /** The full file contents. */
    content: string;

    /** Large-file mode override (`map`/`summarize`/`truncate`); omit to use the server's configured default. */
    mode?: string;

    /** The endpoint whose context window sizes the inline threshold; omit to use the default endpoint. */
    endpointName?: string;

    /** Explicit inline size gate in bytes; omit to derive it from the endpoint's context window. */
    inlineThresholdBytes?: number;

    /** Leading lines to include in a map/truncation. */
    headLines?: number;

    /** Lines per summarizer chunk. */
    summaryChunkLines?: number;
}

/** A built file-context block, as returned by `POST /v1.0/api/context/file`. */
export interface FileContextResponse {
    /** The context block text to inline (whole file, structural map, or summary). */
    Text: string;

    /** The mode actually used: `map`, `summarize`, or `truncate`. */
    Mode: string;

    /** Whether the file was small enough to be inlined whole. */
    Inlined: boolean;

    /** The number of outline entries emitted (0 when inlined or summarized). */
    OutlineEntryCount: number;

    /** Whether a summary was served from the server's cache. */
    FromCache: boolean;
}

/** A subagent definition as returned by `GET /v1.0/api/subagents`. */
export interface Subagent {
    Name: string;
    Description: string;
    SystemPrompt?: string;
    EndpointName?: string | null;
    AllowedTools?: string[];
    MaxIterations?: number | null;
    [key: string]: unknown;
}

/** A skill summary as returned by `GET /v1.0/api/skills`. */
export interface SkillSummary {
    Name: string;
    Title: string;
    Description: string;
    Enabled: boolean;
    Valid: boolean;
    Mutating: boolean;
    Commands: number;
    Errors: string[];
}

/** The masked REST sub-settings. */
export interface RestSettings {
    Enabled: boolean;
    Hostname: string;
    Port: number;
    Ssl: boolean;
    CorsAllowOrigin: string;
    ApiKeySet: boolean;
    ApiKey?: string;
}

/** The editable settings object as returned by `GET /v1.0/api/settings`. */
export interface MuxServerSettings {
    DefaultApprovalPolicy: string;
    MaxAgentIterations: number;
    MaxConcurrency: number;
    ToolTimeoutMs: number;
    ProcessTimeoutMs: number;
    AutoCompactEnabled: boolean;
    CompactionStrategy: string;
    CompactionPreserveTurns: number;
    ContextWarningThresholdPercent: number;
    SkillsEnabled: boolean;
    TaskPlanningEnabled: boolean;
    TaskParallelismEnabled: boolean;
    IgnoreCertErrors: boolean;
    ShowBoundaryLines: boolean;
    DefaultEnqueueBehavior: string;
    Rest: RestSettings;
    [key: string]: unknown;
}

/** A min/avg/p95/p99/max distribution, used for latency/TTFT/streaming/throughput. */
export interface UsageDistribution {
    Min: number;
    Avg: number;
    P95: number;
    P99: number;
    Max: number;
    Count: number;
}

/** Aggregate usage metrics from `GET /v1.0/api/usage/summary`. */
export interface UsageMetrics {
    Calls: number;
    Errors: number;
    ErrorRate: number;
    InputTokens: number;
    CachedTokens: number;
    OutputTokens: number;
    TotalTokens: number;
    CostUsd: number;
    AvgTtftMs: number;
    AvgTotalMs: number;
    AvgStreamMs: number;
    AvgTokensPerSec: number;
    TotalMsDist?: UsageDistribution;
    TtftMsDist?: UsageDistribution;
    StreamMsDist?: UsageDistribution;
    ThroughputDist?: UsageDistribution;
    [key: string]: unknown;
}

/** The usage summary response. */
export interface UsageSummary {
    FromUnixMs: number;
    ToUnixMs: number;
    Metrics: UsageMetrics;
}

/** One time bucket in a usage timeseries. */
export interface UsageBucket {
    BucketStartUnixMs: number;
    Metrics: UsageMetrics;
}

/** Distinct endpoints/models for usage filter controls, from `GET /v1.0/api/usage/filters`. */
export interface UsageFilters {
    Enabled: boolean;
    Endpoints: string[];
    Models: string[];
}

/** A persisted session summary, as returned by `GET /v1.0/api/sessions`. */
export interface SessionSummary {
    Id: string;
    Title: string;
    EndpointName: string;
    Model: string;
    CreatedUtc: string;
    UpdatedUtc: string;
    MessageCount: number;
}

/** A tool call carried on a persisted message. */
export interface ToolCallDto {
    Id: string;
    Name: string;
    Arguments: string;
}

/** One message in a session's transcript, as returned by `GET /v1.0/api/sessions/detail`. */
export interface ChatMessageDto {
    Role: string;
    Content: string;
    Reasoning?: string | null;
    ToolCalls?: ToolCallDto[] | null;
    ToolCallId?: string | null;
}

/** The full transcript of a session, as returned by `GET /v1.0/api/sessions/detail`. */
export interface SessionDetail {
    Id: string;
    Title: string;
    EndpointName: string;
    Model: string;
    Messages: ChatMessageDto[];
}

/** The health/readiness response, including the API contract version the extension negotiates against. */
export interface HealthResponse {
    Status: string;
    Product: string;
    Version: string;
    ContractVersion: string;
    Pid: number;
}

/** Undo/redo availability for a working directory, from `GET /v1.0/api/checkpoints`. */
export interface CheckpointStatus {
    WorkingDirectory: string;
    IsRepository: boolean;
    CanUndo: boolean;
    CanRedo: boolean;
}

/** The result of an undo or redo, from `POST /v1.0/api/checkpoints/undo|redo`. */
export interface CheckpointActionResult {
    Restored: boolean;
    Label?: string | null;
    CanUndo: boolean;
    CanRedo: boolean;
}

/** The request body for `POST /v1.0/api/chat/stream`. */
export interface ChatStreamRequest {
    endpoint: string;
    id?: string;
    workingDirectory?: string;
    messages: Array<{ role: string; content: string }>;
}

/** Per-turn statistics carried on the terminal `done` event. */
export interface ChatStats {
    TtftMs: number;
    StreamingMs: number;
    TotalMs: number;
    InputTokens: number;
    OutputTokens: number;
    TotalTokens: number;
}

/** The terminal `done` payload of a streamed run. */
export interface ChatDone {
    Id: string;
    Content: string;
    Model: string;
    Stats: ChatStats;
}

/** The run/session announcement streamed first, as `run`, so the client can address the run (cancel, subscribe). */
export interface ChatRunEvent {
    RunId: string;
    SessionId: string;
}

/** A tool lifecycle event streamed as `tool`. */
export interface ChatToolEvent {
    Id: string;
    Name: string;
    Status: string;
    ElapsedMs: number;
}

/** An approval prompt streamed as `approval` when the server runs with `--allow-tools`. */
export interface ChatApprovalRequest {
    RunId: string;
    ToolCallId: string;
    Name: string;
    Arguments: string;
}

/**
 * A parsed Server-Sent Event from a streamed run. The `event` names match the server: `run`, `token`,
 * `thinking`, `tool`, `approval`, `done`, `canceled`, and `error`.
 */
export type StreamEvent =
    | { event: 'run'; data: ChatRunEvent }
    | { event: 'token'; data: string }
    | { event: 'thinking'; data: string }
    | { event: 'tool'; data: ChatToolEvent }
    | { event: 'approval'; data: ChatApprovalRequest }
    | { event: 'done'; data: ChatDone }
    | { event: 'canceled'; data: ChatRunEvent }
    | { event: 'error'; data: string };
