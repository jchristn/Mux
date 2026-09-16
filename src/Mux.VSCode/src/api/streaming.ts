/**
 * Pure parsing of the mux server's Server-Sent Events stream. Kept free of `vscode` and `fetch` so the
 * event-shaping logic is unit-testable in plain Node. {@link ApiClient} owns the transport and feeds raw
 * blocks through here.
 */

import { ChatApprovalRequest, ChatDone, ChatRunEvent, ChatToolEvent, StreamEvent } from './types';

/** A raw SSE block split into its `event` name and concatenated `data` payload. */
export interface RawSseBlock {
    event: string;
    data: string;
}

/**
 * Parses one raw SSE block (the text between two blank-line separators) into its event name and data. Lines
 * beginning `event:` set the event name; `data:` lines are concatenated with newlines, matching the SSE
 * spec. Returns null when the block carries no data.
 *
 * @param block The raw block text.
 * @returns The parsed block, or null when there is nothing to dispatch.
 */
export function parseSseBlock(block: string): RawSseBlock | null {
    let event = 'message';
    let data = '';
    for (const rawLine of block.split('\n')) {
        const line = rawLine.replace(/\r$/, '');
        if (line.startsWith('event:')) {
            event = line.slice('event:'.length).trim();
        } else if (line.startsWith('data:')) {
            const piece = line.slice('data:'.length).replace(/^ /, '');
            data = data ? `${data}\n${piece}` : piece;
        }
    }

    if (!data) {
        return null;
    }

    return { event, data };
}

/**
 * Maps a raw block to a typed {@link StreamEvent}. Text events (`token`, `thinking`, `error`) carry a
 * JSON-encoded string; structured events (`tool`, `approval`, `done`) carry a JSON object. Returns null for
 * an unknown event name or a payload that does not parse, so a malformed frame is skipped rather than
 * throwing mid-stream.
 *
 * @param block The raw parsed block.
 * @returns The typed event, or null when it cannot be mapped.
 */
export function toStreamEvent(block: RawSseBlock): StreamEvent | null {
    let payload: unknown;
    try {
        payload = JSON.parse(block.data);
    } catch {
        return null;
    }

    switch (block.event) {
        case 'run':
            return { event: 'run', data: payload as ChatRunEvent };
        case 'canceled':
            return { event: 'canceled', data: payload as ChatRunEvent };
        case 'token':
            return typeof payload === 'string' ? { event: 'token', data: payload } : null;
        case 'thinking':
            return typeof payload === 'string' ? { event: 'thinking', data: payload } : null;
        case 'error':
            return typeof payload === 'string' ? { event: 'error', data: payload } : null;
        case 'tool':
            return { event: 'tool', data: payload as ChatToolEvent };
        case 'approval':
            return { event: 'approval', data: payload as ChatApprovalRequest };
        case 'done':
            return { event: 'done', data: payload as ChatDone };
        default:
            return null;
    }
}

/**
 * Splits a growing buffer into complete SSE blocks, returning the typed events and the unconsumed remainder.
 * A stream reader calls this on each chunk, dispatching the returned events and carrying `rest` forward.
 *
 * @param buffer The accumulated, not-yet-fully-consumed text.
 * @returns The events parsed from complete blocks, plus the trailing partial block to keep.
 */
export function drainBuffer(buffer: string): { events: StreamEvent[]; rest: string } {
    const events: StreamEvent[] = [];
    let rest = buffer;
    let separator = rest.indexOf('\n\n');
    while (separator >= 0) {
        const block = rest.slice(0, separator);
        rest = rest.slice(separator + 2);
        const raw = parseSseBlock(block);
        if (raw) {
            const event = toStreamEvent(raw);
            if (event) {
                events.push(event);
            }
        }
        separator = rest.indexOf('\n\n');
    }

    return { events, rest };
}
