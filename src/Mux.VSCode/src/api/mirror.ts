/**
 * Pure mapping from the mux **canonical event envelope** — the `eventType`-shaped JSON that the WebSocket
 * bridge frames each run event as, identical to `mux print --output-format jsonl` — into the small set of
 * render events the chat webview already understands. Kept free of `vscode` and `ws` so it is unit-testable
 * in plain Node. {@link MirrorClient} owns the socket and feeds parsed frames through here.
 */

/** A render-ready event derived from a canonical envelope frame. */
export type MirrorEvent =
    | { kind: 'text'; text: string }
    | { kind: 'thinking'; text: string }
    | { kind: 'tool'; id: string; name: string; status: string; ms: number }
    | { kind: 'error'; message: string }
    | { kind: 'done'; status: string };

/**
 * Maps one canonical envelope object to a {@link MirrorEvent}, or null for frames the mirror does not render
 * (connection announcements, heartbeats, run/context status, task-plan updates). Never throws.
 *
 * @param frame A parsed envelope object (from a WebSocket text frame).
 * @returns The render event, or null when the frame should be ignored.
 */
export function mirrorEventFromEnvelope(frame: unknown): MirrorEvent | null {
    if (!frame || typeof frame !== 'object') {
        return null;
    }

    const obj = frame as Record<string, unknown>;
    const eventType = typeof obj.eventType === 'string' ? obj.eventType : '';

    switch (eventType) {
        case 'assistant_text':
            return { kind: 'text', text: asString(obj.text) };
        case 'assistant_thinking':
            return { kind: 'thinking', text: asString(obj.text) };
        case 'tool_call_proposed': {
            const toolCall = (obj.toolCall as Record<string, unknown>) ?? {};
            return { kind: 'tool', id: asString(toolCall.id), name: asString(toolCall.name), status: 'running', ms: 0 };
        }
        case 'tool_call_completed': {
            const result = (obj.result as Record<string, unknown>) ?? {};
            const ok = result.success === true;
            return { kind: 'tool', id: asString(obj.toolCallId), name: asString(obj.toolName), status: ok ? 'ok' : 'fail', ms: asNumber(obj.elapsedMs) };
        }
        case 'error':
            return { kind: 'error', message: asString(obj.message) };
        case 'run_completed':
            return { kind: 'done', status: asString(obj.status) };
        default:
            return null;
    }
}

function asString(value: unknown): string {
    return typeof value === 'string' ? value : '';
}

function asNumber(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}
