/**
 * Pure composition of a run prompt from a user message plus attached editor context. Kept free of `vscode`
 * so the truncation and assembly rules are unit-testable; the providers that read the editor live in
 * `providers.ts` and hand their results here.
 */

/** One attached piece of editor context. */
export interface ContextItem {
    /** A stable machine kind (`activeFile`, `selection`, `diagnostics`, `gitDiff`, `openTabs`, `terminal`). */
    kind: string;

    /** A short human label, already localized by the caller (for example `Active file: src/app.ts`). */
    label: string;

    /** The raw content. */
    content: string;

    /** The file's path, when the item is backed by a file (used to build server-side large-file context). */
    path?: string;

    /** When true, the item is exempt from the per-item cap — its content is already a bounded map or summary. */
    noTruncate?: boolean;
}

/** The result of composing a prompt. */
export interface ComposedPrompt {
    /** The full prompt text sent to the model. */
    prompt: string;

    /** Labels of the context items that were truncated to fit the cap. */
    truncated: string[];

    /** A rough token estimate for the whole prompt. */
    estimatedTokens: number;
}

/** A rough token estimate: characters divided by four, the usual English approximation. */
export function estimateTokens(text: string): number {
    return Math.ceil(text.length / 4);
}

/**
 * Composes the prompt. Each context item is fenced under its label; an item longer than `perItemCharCap` is
 * cut at the cap with a truncation marker and its label recorded, so the caller can tell the user what was
 * trimmed rather than silently sending less. The user's message comes last, after the context, matching how
 * the agent reads a turn.
 *
 * @param userMessage The user's request text.
 * @param items The attached context, in display order.
 * @param perItemCharCap The maximum characters kept per item. Defaults to 8000.
 * @returns The composed prompt, the list of truncated item labels, and a token estimate.
 */
export function composePrompt(userMessage: string, items: ContextItem[], perItemCharCap = 8000): ComposedPrompt {
    const truncated: string[] = [];
    const blocks: string[] = [];

    for (const item of items) {
        let body = item.content ?? '';
        if (!item.noTruncate && body.length > perItemCharCap) {
            body = `${body.slice(0, perItemCharCap)}\n… [truncated ${body.length - perItemCharCap} characters]`;
            truncated.push(item.label);
        }

        blocks.push(`--- ${item.label} ---\n${body}`);
    }

    const contextSection = blocks.length > 0 ? `${blocks.join('\n\n')}\n\n` : '';
    const prompt = `${contextSection}${userMessage}`.trim();
    return { prompt, truncated, estimatedTokens: estimateTokens(prompt) };
}
