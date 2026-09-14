/**
 * Locale-aware display formatting. Every user-visible number, time, duration, or list runs through here with
 * an explicit locale, so no surface hand-builds `5m ago` or joins a list with `', '`. Built on the platform
 * `Intl` APIs; no `vscode` dependency, so the formatters are unit-testable.
 */

/** Formats an integer count for display in the given locale. */
export function formatNumber(locale: string, value: number): string {
    return new Intl.NumberFormat(locale).format(value);
}

/**
 * Formats a token count compactly (for example `1.2K`, `3.4M`), which keeps stat rows narrow in a side panel.
 *
 * @param locale The display locale.
 * @param value The token count.
 * @returns The compact string.
 */
export function formatTokens(locale: string, value: number): string {
    return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
}

/** Formats a millisecond duration as seconds with one decimal, or milliseconds under a second. */
export function formatDuration(locale: string, milliseconds: number): string {
    if (milliseconds < 1000) {
        return `${formatNumber(locale, Math.round(milliseconds))} ms`;
    }

    const seconds = milliseconds / 1000;
    return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(seconds)} s`;
}

/**
 * Formats a UTC timestamp as a locale-aware relative time (`2 hours ago`, `just now`). Falls back to an
 * absolute date/time when the input does not parse.
 *
 * @param locale The display locale.
 * @param isoUtc The ISO-8601 UTC timestamp.
 * @param nowMs The current time in epoch milliseconds (injected so the function stays pure and testable).
 * @returns The relative-time string.
 */
export function formatRelativeTime(locale: string, isoUtc: string, nowMs: number): string {
    const then = Date.parse(isoUtc);
    if (Number.isNaN(then)) {
        return isoUtc;
    }

    const deltaSeconds = Math.round((then - nowMs) / 1000);
    const relative = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
    const abs = Math.abs(deltaSeconds);
    if (abs < 60) {
        return relative.format(Math.round(deltaSeconds), 'second');
    }
    if (abs < 3600) {
        return relative.format(Math.round(deltaSeconds / 60), 'minute');
    }
    if (abs < 86400) {
        return relative.format(Math.round(deltaSeconds / 3600), 'hour');
    }

    return relative.format(Math.round(deltaSeconds / 86400), 'day');
}

/** Joins a list of items with the locale's list conjunction, never a hand-rolled `', '`. */
export function formatList(locale: string, items: string[]): string {
    return new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' }).format(items);
}
