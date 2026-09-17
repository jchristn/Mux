/**
 * Pure resolution of how a file becomes model context, kept free of `vscode` so the inline / map-or-summarize
 * / truncate decision is unit-testable. A small file inlines whole; a large file is handed to the server's
 * `POST /v1.0/api/context/file` route (which alone can map or summarize it); if that call fails, the original
 * content is returned untouched so the caller's per-item cap truncates it — the explicit fallback.
 */

import { FileContextResponse } from '../api/types';

/** The default inline size gate in bytes (64 KiB), used when the endpoint reports no context window. */
export const DEFAULT_INLINE_THRESHOLD_BYTES = 65536;

/** Fraction of the context window a file may occupy before it is mapped/summarized (local heuristic mirroring
 * the server default; the server applies the authoritative, configurable value when it builds the block). */
export const INLINE_CONTEXT_WINDOW_FRACTION = 0.25;

/** Estimated characters (≈ bytes) per token, mirroring the server's default token-estimation ratio. */
export const CHARS_PER_TOKEN = 3.5;

/** Builds a file-context block from the server. Injected so the resolver stays testable and `vscode`-free. */
export type FileContextFetcher = (request: {
    path: string;
    content: string;
    mode?: string;
    endpointName?: string;
}) => Promise<FileContextResponse>;

/**
 * Computes the local inline-vs-fetch byte threshold from an endpoint's context window: a file at or below
 * `fraction × window × charsPerToken` inlines locally (no round-trip), larger files are fetched from the
 * server (which re-decides with the authoritative, configurable threshold). Falls back to
 * {@link DEFAULT_INLINE_THRESHOLD_BYTES} when the window is unknown or non-positive.
 *
 * @param contextWindowTokens The selected endpoint's context window in tokens, if known.
 * @returns The inline threshold in bytes (floored at 1024).
 */
export function computeInlineThresholdBytes(contextWindowTokens?: number): number {
    if (!contextWindowTokens || contextWindowTokens <= 0) {
        return DEFAULT_INLINE_THRESHOLD_BYTES;
    }
    return Math.max(1024, Math.floor(INLINE_CONTEXT_WINDOW_FRACTION * contextWindowTokens * CHARS_PER_TOKEN));
}

/** The resolved context for a file. */
export interface ResolvedFileContext {
    /** The content to inline: the whole file (small), a server-built map/summary (large), or the original (fallback). */
    content: string;

    /** Whether the content is a bounded block that must not be re-truncated by the per-item cap. */
    noTruncate: boolean;

    /** The mode used: `inline`, `map`, `summarize`, or `truncate` (the fallback). */
    mode: string;

    /** Whether the server call failed and the original content was returned for the caller to truncate. */
    usedFallback: boolean;
}

/** The UTF-8 byte length of a string, matching the server's size measurement. */
export function utf8ByteLength(text: string): number {
    return new TextEncoder().encode(text ?? '').length;
}

/**
 * Resolves the context for a file. A file at or below `inlineThresholdBytes` is inlined whole; a larger file
 * is fetched from the server (map or summary), falling back to the original content — marked truncatable — if
 * the fetch throws.
 *
 * @param path The file path sent to the server.
 * @param content The full file contents.
 * @param options The threshold, an optional mode override (undefined lets the server decide), the endpoint
 * whose context window the server sizes against, and the fetcher.
 * @returns The resolved content and how it was produced.
 */
export async function resolveFileContext(
    path: string,
    content: string,
    options: { inlineThresholdBytes?: number; mode?: string; endpointName?: string; fetcher: FileContextFetcher },
): Promise<ResolvedFileContext> {
    const body = content ?? '';
    const threshold = options.inlineThresholdBytes ?? DEFAULT_INLINE_THRESHOLD_BYTES;

    if (utf8ByteLength(body) <= threshold) {
        return { content: body, noTruncate: true, mode: 'inline', usedFallback: false };
    }

    try {
        const response = await options.fetcher({ path, content: body, mode: options.mode, endpointName: options.endpointName });
        return { content: response.Text ?? body, noTruncate: true, mode: response.Mode || 'map', usedFallback: false };
    } catch {
        // Server unreachable or errored: keep the original content and let the per-item cap truncate it.
        return { content: body, noTruncate: false, mode: 'truncate', usedFallback: true };
    }
}
