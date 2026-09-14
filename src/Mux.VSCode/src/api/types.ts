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
 * A parsed Server-Sent Event from a streamed run. The `event` names match the server: `token`, `thinking`,
 * `tool`, `approval`, `done`, and `error`.
 */
export type StreamEvent =
    | { event: 'token'; data: string }
    | { event: 'thinking'; data: string }
    | { event: 'tool'; data: ChatToolEvent }
    | { event: 'approval'; data: ChatApprovalRequest }
    | { event: 'done'; data: ChatDone }
    | { event: 'error'; data: string };
