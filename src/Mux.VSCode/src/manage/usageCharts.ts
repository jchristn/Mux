// Pure helpers behind the usage panel's charts: range keys, the bucket width of a (possibly variable-width)
// timeseries, split-series stacking, the breakdown bars, failure-reason rows, the drill-down window and its
// events query, and the token-heatmap grid and axis labels. Nothing here touches the VS Code API, so the logic is unit
// tested with `node --test` and the panel only formats and posts the results.

/** The seven usage ranges, in display order. */
export const USAGE_RANGES = ['hour', 'day', 'week', 'month', 'quarter', 'year', 'all'] as const;

/** A usage range key. */
export type UsageRangeKey = (typeof USAGE_RANGES)[number];

/** The split/breakdown dimensions the shared dimension picker offers, Model first (the default). */
export const USAGE_DIMENSIONS = ['model', 'endpoint', 'provider', 'callkind'] as const;

/** A split/breakdown dimension key. */
export type UsageDimensionKey = (typeof USAGE_DIMENSIONS)[number];

/** The metrics the "<Metric> by <dimension>" chart and the "Usage by" sort offer, Tokens first (the default). */
export const USAGE_SPLIT_METRICS = ['tokens', 'calls', 'cost'] as const;

/** A "<Metric> by <dimension>" metric key. */
export type UsageSplitMetricKey = (typeof USAGE_SPLIT_METRICS)[number];

/** How the over-time stacked charts draw: stacked bars (the default) or one line per series. */
export const USAGE_CHART_MODES = ['bars', 'lines'] as const;

/** A chart mode key. */
export type UsageChartModeKey = (typeof USAGE_CHART_MODES)[number];

/** The number of named series the split query asks for; the rest are folded into Other. */
export const SPLIT_TOP = 6;

/** The most rows the "Usage by" breakdown chart shows. */
export const BREAKDOWN_LIMIT = 10;

/** The page size of the drill-down calls table. */
export const DRILL_PAGE_SIZE = 50;

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** The nominal bucket width of each fixed range (All borrows the Day grid when it has nothing better). */
const NOMINAL_BUCKET_MS: Record<UsageRangeKey, number> = {
    hour: MINUTE,
    day: 15 * MINUTE,
    week: 2 * HOUR,
    month: 12 * HOUR,
    quarter: DAY,
    year: 7 * DAY,
    all: 15 * MINUTE,
};

/** The window each fixed range covers (All borrows the Day window). */
const RANGE_SPAN_MS: Record<UsageRangeKey, number> = {
    hour: HOUR,
    day: DAY,
    week: 7 * DAY,
    month: 30 * DAY,
    quarter: 91 * DAY,
    year: 364 * DAY,
    all: DAY,
};

/** True when a value is one of the seven range keys. */
export function isUsageRange(value: unknown): value is UsageRangeKey {
    return typeof value === 'string' && (USAGE_RANGES as readonly string[]).includes(value);
}

/** Normalizes a range key (case-insensitive), falling back to `day` for anything unknown. */
export function normalizeRange(value: unknown): UsageRangeKey {
    const key = typeof value === 'string' ? value.trim().toLowerCase() : '';
    return isUsageRange(key) ? key : 'day';
}

/** Normalizes a dimension key (case-insensitive, `call kind`/`call_kind` accepted), falling back to `model`. */
export function normalizeDimension(value: unknown): UsageDimensionKey {
    const key = typeof value === 'string' ? value.trim().toLowerCase().replace(/[\s_-]/g, '') : '';
    return (USAGE_DIMENSIONS as readonly string[]).includes(key) ? (key as UsageDimensionKey) : 'model';
}

/** Normalizes a split metric key (case-insensitive), falling back to `tokens`. */
export function normalizeSplitMetric(value: unknown): UsageSplitMetricKey {
    const key = typeof value === 'string' ? value.trim().toLowerCase() : '';
    return (USAGE_SPLIT_METRICS as readonly string[]).includes(key) ? (key as UsageSplitMetricKey) : 'tokens';
}

/** Normalizes a chart mode key (case-insensitive), falling back to `bars`. */
export function normalizeChartMode(value: unknown): UsageChartModeKey {
    const key = typeof value === 'string' ? value.trim().toLowerCase() : '';
    return key === 'lines' ? 'lines' : 'bars';
}

/** One metric of a bucket or row: total tokens, calls, or cost in US dollars (never negative). */
export function metricValue(m: BucketMetricsLike | undefined, metric: UsageSplitMetricKey): number {
    if (metric === 'calls') {
        return Math.max(0, m?.Calls ?? 0);
    }
    if (metric === 'cost') {
        return Math.max(0, m?.CostUsd ?? 0);
    }
    return Math.max(0, m?.TotalTokens ?? 0);
}

/** The nominal bucket width of a range in milliseconds. */
export function nominalBucketMs(range: string): number {
    return NOMINAL_BUCKET_MS[normalizeRange(range)];
}

/**
 * The width of one bucket of a timeseries. The fixed ranges have a known width, but All picks a "nice" width to
 * fit its span, so the width is the difference between the first two bucket starts. With a single bucket it
 * spans the range's window (for All: from the bucket's start to now, at least a minute); with none, the range's
 * nominal width.
 *
 * @param bucketStarts The `BucketStartUnixMs` values in order.
 * @param range The range key.
 * @param nowUnixMs The current time, used by a single All bucket.
 */
export function bucketWidthMs(bucketStarts: readonly number[], range: string, nowUnixMs: number = Date.now()): number {
    const key = normalizeRange(range);
    if (bucketStarts.length >= 2) {
        const width = bucketStarts[1] - bucketStarts[0];
        if (width > 0) {
            return width;
        }
    }
    if (bucketStarts.length === 1) {
        if (key === 'all') {
            return Math.max(MINUTE, nowUnixMs - bucketStarts[0]);
        }
        return RANGE_SPAN_MS[key];
    }
    return NOMINAL_BUCKET_MS[key];
}

/** How a range's x-axis labels are formatted. */
export type AxisLabelKind = 'time' | 'weekday-time' | 'date-time' | 'date';

/**
 * The x-axis label format: times for Hour and Day, weekday plus hour for Week, and dates for Month, Quarter and
 * Year. All depends on its bucket width: times when buckets are shorter than an hour and the series spans at
 * most a day, date and time below a day, otherwise dates.
 */
export function axisLabelKind(range: string, widthMs: number, bucketCount: number): AxisLabelKind {
    const key = normalizeRange(range);
    if (key === 'hour' || key === 'day') {
        return 'time';
    }
    if (key === 'week') {
        return 'weekday-time';
    }
    if (key !== 'all') {
        return 'date';
    }
    if (widthMs < HOUR && widthMs * bucketCount <= DAY) {
        return 'time';
    }
    return widthMs < DAY ? 'date-time' : 'date';
}

/** A drill-down window: the inclusive Unix-ms bounds of one bucket. */
export interface DrillWindow {
    from: number;
    to: number;
}

/** The window of the bucket starting at `startUnixMs`: `[start, start + width - 1]`. */
export function drillWindow(startUnixMs: number, widthMs: number): DrillWindow {
    const width = Math.max(1, Math.floor(widthMs));
    return { from: startUnixMs, to: startUnixMs + width - 1 };
}

/**
 * The query string for the drill-down calls of a window, with the panel's endpoint and model filters:
 * `from=..&to=..&page=1&pageSize=50[&endpoint=..][&model=..]`.
 */
export function eventsQuery(window: DrillWindow, endpoint?: string, model?: string, pageSize: number = DRILL_PAGE_SIZE): string {
    const parts = [`from=${Math.floor(window.from)}`, `to=${Math.floor(window.to)}`, 'page=1', `pageSize=${Math.max(1, Math.floor(pageSize))}`];
    if (endpoint) {
        parts.push(`endpoint=${encodeURIComponent(endpoint)}`);
    }
    if (model) {
        parts.push(`model=${encodeURIComponent(model)}`);
    }
    return parts.join('&');
}

/** The minimal per-bucket metrics the chart helpers read. */
export interface BucketMetricsLike {
    Calls?: number;
    Errors?: number;
    InputTokens?: number;
    CachedTokens?: number;
    OutputTokens?: number;
    TotalTokens?: number;
    CostUsd?: number;
}

/** The stacked token parts of one bucket or row: uncached input (floored at 0), cached, and output. */
export function tokenParts(m: BucketMetricsLike | undefined): { input: number; cached: number; output: number } {
    const input = m?.InputTokens ?? 0;
    const cached = m?.CachedTokens ?? 0;
    return { input: Math.max(0, input - cached), cached: Math.max(0, cached), output: Math.max(0, m?.OutputTokens ?? 0) };
}

/** The succeeded/failed split of one bucket. */
export function callParts(m: BucketMetricsLike | undefined): { ok: number; failed: number; errorRate: number } {
    const calls = Math.max(0, m?.Calls ?? 0);
    const failed = Math.min(calls, Math.max(0, m?.Errors ?? 0));
    return { ok: calls - failed, failed, errorRate: calls > 0 ? failed / calls : 0 };
}

/** A fraction (0..1) as a percentage with one decimal, such as `12.5%`. */
export function formatPercent(fraction: number): string {
    const value = Number.isFinite(fraction) ? Math.max(0, fraction) : 0;
    return `${(value * 100).toFixed(1)}%`;
}

/** One series of the split query on the wire. */
export interface SplitSeriesLike {
    Key: string;
    IsOther: boolean;
    Buckets: { BucketStartUnixMs: number; Metrics: BucketMetricsLike }[];
}

/** A stacked split series ready to draw. `color` is the rank (0..5) or -1 for Other's neutral grey. */
export interface StackedSeries {
    key: string;
    isOther: boolean;
    color: number;
    values: number[];
}

/**
 * Prepares the split series for stacking: the named series in the server's rank order (top `top`, extras
 * folded into Other), then one Other series last. Colors follow rank so a value keeps its color while it keeps
 * its rank, and Other is always the neutral grey. Values are the chosen metric per bucket (total tokens by
 * default, or calls, or cost), aligned to `bucketCount` (missing buckets are 0). Series with nothing at all are
 * dropped.
 */
export function stackSplitSeries(
    series: readonly SplitSeriesLike[],
    bucketCount: number,
    top: number = SPLIT_TOP,
    metric: UsageSplitMetricKey = 'tokens',
): StackedSeries[] {
    const named: StackedSeries[] = [];
    let other: StackedSeries | undefined;
    const values = (s: SplitSeriesLike): number[] => {
        const out = new Array<number>(bucketCount).fill(0);
        (s.Buckets ?? []).forEach((b, i) => {
            if (i < bucketCount) {
                out[i] = metricValue(b.Metrics, metric);
            }
        });
        return out;
    };
    const foldIntoOther = (vals: number[], key: string): void => {
        if (!other) {
            other = { key, isOther: true, color: -1, values: vals };
            return;
        }
        other.values = other.values.map((v, i) => v + vals[i]);
    };

    for (const s of series ?? []) {
        const vals = values(s);
        if (s.IsOther || named.length >= top) {
            foldIntoOther(vals, s.IsOther ? s.Key || 'Other' : 'Other');
        } else {
            named.push({ key: s.Key || 'unknown', isOther: false, color: named.length, values: vals });
        }
    }
    const result = named.filter((s) => s.values.some((v) => v > 0));
    if (other && other.values.some((v) => v > 0)) {
        result.push(other);
    }
    return result;
}

/** The largest stacked column of a set of stacked series. */
export function stackedMax(series: readonly StackedSeries[], bucketCount: number): number {
    let max = 0;
    for (let i = 0; i < bucketCount; i++) {
        let sum = 0;
        for (const s of series) {
            sum += s.values[i] ?? 0;
        }
        max = Math.max(max, sum);
    }
    return max;
}

/** One error code from the errors query on the wire. */
export interface ErrorCountLike {
    ErrorCode?: string | null;
    Count?: number;
}

/** A row of the failure-reasons list. */
export interface FailureReason {
    code: string;
    count: number;
    share: string;
}

/**
 * The failure-reasons list: blank codes become `unknown` (and merge with it), zero counts are dropped, and the
 * rows are sorted most frequent first (ties by code). `share` is each code's percentage of all failures.
 */
export function failureReasons(items: readonly ErrorCountLike[]): FailureReason[] {
    const counts = new Map<string, number>();
    for (const item of items ?? []) {
        const code = (item.ErrorCode ?? '').trim() || 'unknown';
        const count = Math.max(0, item.Count ?? 0);
        if (count > 0) {
            counts.set(code, (counts.get(code) ?? 0) + count);
        }
    }
    const total = [...counts.values()].reduce((a, b) => a + b, 0);
    return [...counts.entries()]
        .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
        .map(([code, count]) => ({ code, count, share: formatPercent(total > 0 ? count / total : 0) }));
}

/** One breakdown row on the wire. */
export interface BreakdownRowLike {
    Value?: string | null;
    Metrics?: BucketMetricsLike;
}

/** A bar of the "Usage by" chart. */
export interface BreakdownBar {
    value: string;
    input: number;
    cached: number;
    output: number;
    tokens: number;
    cost: number;
    calls: number;
    /** Failed calls and the reported total tokens (for the CSV export). */
    failed: number;
    totalTokens: number;
}

/**
 * The "Usage by" bars, at most `limit` rows, sorted descending by the chosen metric: tokens (then cost), calls (then
 * tokens), or cost (then tokens). Ties fall back to the value name.
 */
export function breakdownBars(rows: readonly BreakdownRowLike[], limit: number = BREAKDOWN_LIMIT, metric: UsageSplitMetricKey = 'tokens'): BreakdownBar[] {
    const primary = (b: BreakdownBar): number => (metric === 'calls' ? b.calls : metric === 'cost' ? b.cost : b.tokens);
    const secondary = (b: BreakdownBar): number => (metric === 'tokens' ? b.cost : b.tokens);
    return (rows ?? [])
        .map((r) => {
            const parts = tokenParts(r.Metrics);
            return {
                value: (r.Value ?? '').trim() || 'unknown',
                ...parts,
                tokens: parts.input + parts.cached + parts.output,
                cost: Math.max(0, r.Metrics?.CostUsd ?? 0),
                calls: Math.max(0, r.Metrics?.Calls ?? 0),
                failed: Math.max(0, r.Metrics?.Errors ?? 0),
                totalTokens: Math.max(0, r.Metrics?.TotalTokens ?? 0),
            };
        })
        .filter((b) => b.tokens > 0 || b.cost > 0 || b.calls > 0)
        .sort((a, b) => primary(b) - primary(a) || secondary(b) - secondary(a) || a.value.localeCompare(b.value))
        .slice(0, Math.max(0, limit));
}

/** The heatmap response fields the panel reads (see `UsageHeatmap` in the API types). */
export interface HeatmapLike {
    Layout: {
        NowUnixMs: number;
        UtcOffsetMinutes: number;
        FromUnixMs: number;
        /** The first cell inside the window (older servers omit it: the whole grid). */
        WindowFromUnixMs?: number;
        BucketMs: number;
        Rows: number;
        Columns: number;
        /** Consecutive columns sharing one label (older servers omit it: 1). */
        ColumnGroup?: number;
        RowKind: string;
        RowUnit: string;
        ColumnKind: string;
        RowStartsUnixMs: number[];
        ColumnLabels: string[];
    };
    TotalTokens: number[];
    Calls: number[];
}

/** One cell of the token heatmap. */
export interface HeatmapCell {
    index: number;
    row: number;
    col: number;
    tokens: number;
    calls: number;
    level: number;
    /** Inclusive window start and end, Unix ms (the drill-down window). */
    from: number;
    to: number;
    /** True for the rest of the current row, after now. */
    future: boolean;
    /** True outside the window (after now, or before the month calendar's last 30 days): drawn blank, not clickable. */
    blank: boolean;
}

/** The token heatmap on the shared clock-aligned grid. */
export interface HeatmapGrid {
    rows: number;
    cols: number;
    max: number;
    /** Row height in pixels, including the gap. */
    rowHeight: number;
    /** The rows that get a label (all when rows are tall enough, else every 2nd with the first and last). */
    labeledRows: number[];
    cells: HeatmapCell[];
}
/**
 * Heatmap shade levels, mirroring Mux.Core's `UsageHeatmapScale` and the web dashboard: 0 for no usage, 4 at or
 * above the peak (so equal values all take the strongest shade), otherwise 1-4 by quartile (nearest rank) of the
 * used cells. A linear scale would put nearly every cell in the faintest shade, because one busy bucket sets the
 * peak.
 */
export function heatmapScale(values: readonly number[]): (value: number) => number {
    const used = values.filter((v) => v > 0 && Number.isFinite(v)).sort((a, b) => a - b);
    const peak = used.length > 0 ? used[used.length - 1] : 0;
    const bounds: number[] = [];
    for (let i = 1; i < 4 && used.length > 0; i++) {
        const rank = Math.ceil((i / 4) * used.length) - 1;
        bounds.push(used[Math.min(Math.max(rank, 0), used.length - 1)]);
    }
    return (value: number): number => {
        if (!(value > 0)) {
            return 0;
        }
        if (bounds.length === 0 || value >= peak) {
            return 4;
        }
        const index = bounds.findIndex((b) => value <= b);
        return index < 0 ? 4 : index + 1;
    };
}

/**
 * Picks which of `count` labels to draw so at most about `maxLabels` show: every 1st, 2nd, 3rd, 4th, 6th, 8th, ...
 * label, plus the last when `includeLast` (dropping the stride label just before it when they would crowd). Mirrors
 * Mux.Core's `UsageHeatmapLayout.LabeledIndices` and the web dashboard.
 */
export function labeledIndices(count: number, maxLabels: number, includeLast: boolean): number[] {
    const out: number[] = [];
    if (count <= 0) {
        return out;
    }
    const max = Math.max(1, maxLabels);
    const steps = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];
    let stride = 1;
    for (const step of steps) {
        stride = step;
        if (Math.ceil(count / step) <= max) {
            break;
        }
    }
    for (let i = 0; i < count; i += stride) {
        out.push(i);
    }
    if (includeLast && out[out.length - 1] !== count - 1) {
        if (out.length > 1 && count - 1 - out[out.length - 1] < stride) {
            out.pop();
        }
        out.push(count - 1);
    }
    return out;
}

/** The heatmap row height in pixels: 24 for up to 13 rows, then shrinking to a 12 pixel floor (as on the web). */
export function heatmapRowHeight(rows: number): number {
    return rows <= 13 ? 24 : Math.max(12, Math.min(24, Math.floor(440 / Math.max(1, rows))));
}

/**
 * Shapes a heatmap response into the drawn grid: each cell's level by {@link heatmapScale} on total tokens, its
 * drill-down window, and whether it is still in the future; plus the row height and which rows are labeled
 * (11 pixel labels need 16 pixels, so short rows label every 2nd, always keeping the first and last).
 */
export function heatmapGrid(heatmap: HeatmapLike | undefined | null): HeatmapGrid {
    if (!heatmap || !heatmap.Layout) {
        return { rows: 0, cols: 0, max: 0, rowHeight: 24, labeledRows: [], cells: [] };
    }
    const lay = heatmap.Layout;
    const rows = lay.Rows;
    const cols = lay.Columns;
    const tokens = heatmap.TotalTokens ?? [];
    const calls = heatmap.Calls ?? [];
    const max = tokens.reduce((a, v) => Math.max(a, v || 0), 0);
    const levelOf = heatmapScale(tokens.map((v) => Math.max(0, v || 0)));
    const cells: HeatmapCell[] = [];
    const first = lay.WindowFromUnixMs ?? lay.FromUnixMs;
    for (let index = 0; index < rows * cols; index++) {
        const from = lay.FromUnixMs + index * lay.BucketMs;
        const value = Math.max(0, tokens[index] || 0);
        const future = from > lay.NowUnixMs;
        cells.push({
            index, row: Math.floor(index / cols), col: index % cols, tokens: value, calls: Math.max(0, calls[index] || 0),
            level: levelOf(value), from, to: from + lay.BucketMs - 1, future, blank: future || from < first,
        });
    }
    const rowHeight = heatmapRowHeight(rows);
    const labeledRows = labeledIndices(rows, rowHeight >= 16 ? rows : Math.floor((rows * rowHeight) / 16), true);
    return { rows, cols, max, rowHeight, labeledRows, cells };
}

const HM_TIME: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' };

// Formats an instant as wall-clock time at the layout's fixed UTC offset.
function localFormat(unixMs: number, offsetMinutes: number, options: Intl.DateTimeFormatOptions, locale?: string): string {
    return new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' }).format(new Date(unixMs + offsetMinutes * 60_000));
}

/** A heatmap row's label in local time: a time, date and time, weekday and date, or date by the layout's row kind. */
export function heatmapRowLabel(layout: HeatmapLike['Layout'], row: number, locale?: string): string {
    const ms = layout.RowStartsUnixMs[row];
    const off = layout.UtcOffsetMinutes || 0;
    switch (layout.RowKind) {
        case 'time':
            return localFormat(ms, off, HM_TIME, locale);
        case 'datetime':
            return `${localFormat(ms, off, { month: '2-digit', day: '2-digit' }, locale)} ${localFormat(ms, off, HM_TIME, locale)}`;
        case 'weekdate':
            return localFormat(ms, off, { weekday: 'short', month: 'numeric', day: 'numeric' }, locale);
        default:
            return localFormat(ms, off, { year: 'numeric', month: 'short', day: 'numeric' }, locale);
    }
}

/**
 * A heatmap column's label: a localized weekday (Monday first) for weekday columns and for both half-day columns of
 * a weekday, else the server's label.
 */
export function heatmapColumnLabel(layout: HeatmapLike['Layout'], col: number, locale?: string): string {
    if (layout.ColumnKind === 'weekday' || layout.ColumnKind === 'halfday') {
        const day = layout.ColumnKind === 'halfday' ? Math.floor(col / 2) : col;
        return new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2024, 0, 1 + day)));
    }
    return layout.ColumnLabels[col] ?? '';
}

/** A heatmap cell's window in local time: "date HH:mm–HH:mm" for sub-day cells, the date or "date – date" otherwise. */
export function heatmapCellWindow(layout: HeatmapLike['Layout'], from: number, locale?: string): string {
    const off = layout.UtcOffsetMinutes || 0;
    const width = layout.BucketMs;
    const date: Intl.DateTimeFormatOptions = { year: 'numeric', month: 'short', day: 'numeric' };
    const day = 24 * 60 * 60 * 1000;
    if (width >= day) {
        const first = localFormat(from, off, date, locale);
        return width === day ? first : `${first} – ${localFormat(from + width - 1, off, date, locale)}`;
    }
    return `${localFormat(from, off, date, locale)} ${localFormat(from, off, HM_TIME, locale)}–${localFormat(from + width, off, HM_TIME, locale)}`;
}

/** Formats an integer with thousands separators (`1,234,567`). */
export function formatCount(n: number): string {
    return String(Math.round(n || 0)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/** Formats a token count compactly: `950`, `12.3K`, `4.5M`, `1.2B`. */
export function formatTokensCompact(n: number): string {
    const v = Math.max(0, n || 0);
    if (v >= 1e9) {
        return `${(v / 1e9).toFixed(1)}B`;
    }
    if (v >= 1e6) {
        return `${(v / 1e6).toFixed(1)}M`;
    }
    if (v >= 1e3) {
        return `${(v / 1e3).toFixed(1)}K`;
    }
    return String(Math.round(v));
}

/** Formats US dollars: four decimals below $1, two above. */
export function formatUsd(n: number): string {
    const v = n || 0;
    return `$${Math.abs(v) < 1 ? v.toFixed(4) : v.toFixed(2)}`;
}

// ---- CSV export -------------------------------------------------------------------------------------------------
// The same format as Mux.Core's UsageCsv and the web dashboard's usageCsv (a Touchstone case and a unit test compare
// them): a header row, then one row per bucket, value or cell; every line, the last included, ends in LF; numbers
// invariant (`.` decimal, no thousands separators, no exponent, rounded to six decimals with halves away from zero,
// trailing zeros dropped; NaN and infinity are empty cells); a cell is quoted (inner quotes doubled) when it holds a
// comma, quote, CR or LF or starts or ends with a space or tab, after a leading apostrophe on text a spreadsheet would
// run as a formula (see csvIsFormulaLike); times in ISO 8601 local time with the UTC offset in
// effect then (`2026-11-11T10:15:00-05:00`).

/** A CSV cell: a string (quoted when needed) or a number (invariant form). */
export type CsvCell = string | number;

/** A number in invariant CSV form: `1234`, `0.0125`, `-3.5`; NaN and infinity are empty. */
export function csvNumber(value: number): string {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
        return '';
    }
    const rounded = Math.round(Math.abs(value) * 1e6) / 1e6;
    if (rounded === 0) {
        return '0';
    }
    const sign = value < 0 ? '-' : '';
    if (rounded === Math.floor(rounded)) {
        return sign + rounded.toFixed(0);
    }
    return sign + rounded.toFixed(6).replace(/0+$/, '').replace(/\.$/, '');
}

/**
 * True when a spreadsheet would read a cell as a formula: it starts with `=`, `+`, `@`, a tab or CR, or with a `-`
 * that does not begin a number (the next character is not a digit or `.`; a lone `-` counts). Mirrors
 * `UsageCsv.IsFormulaLike`.
 */
export function csvIsFormulaLike(text: string): boolean {
    if (!text) {
        return false;
    }
    const first = text[0];
    if (first === '=' || first === '+' || first === '@' || first === '\t' || first === '\r') {
        return true;
    }
    return first === '-' && (text.length === 1 || !/^[\p{Nd}.]/u.test(text.slice(1)));
}

/**
 * A CSV field: numbers in invariant form; text that looks like a formula gets a leading apostrophe, then is quoted
 * (inner quotes doubled) when it holds a comma, quote, CR or LF or has edge whitespace.
 */
export function csvField(value: CsvCell | null | undefined): string {
    if (typeof value === 'number') {
        return csvNumber(value);
    }
    let text = value ?? '';
    if (csvIsFormulaLike(text)) {
        text = "'" + text;
    }
    return /[",\r\n]|^[ \t]|[ \t]$/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/**
 * An instant as ISO 8601 local time with its UTC offset, `yyyy-MM-ddTHH:mm:ss+HH:mm`. The offset defaults to the
 * local zone's offset at that instant (east of UTC positive).
 */
export function csvTime(unixMs: number, offsetMinutes: number = -new Date(unixMs).getTimezoneOffset()): string {
    const off = Math.round(offsetMinutes);
    const d = new Date(unixMs + off * 60_000);
    const pad = (n: number, w = 2): string => String(n).padStart(w, '0');
    const sign = off < 0 ? '-' : '+';
    const abs = Math.abs(off);
    return `${pad(d.getUTCFullYear(), 4)}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`
        + `${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
}

/** The CSV text of a table: the header row, then each row, every line (the last included) ending in LF. */
export function usageCsv(header: readonly string[], rows: readonly (readonly CsvCell[])[]): string {
    return [header, ...rows].map((row) => row.map(csvField).join(',') + '\n').join('');
}

/** A CSV table before formatting. */
export interface CsvTable {
    header: string[];
    rows: CsvCell[][];
}

/** The export file name, `mux-usage-<chart>-<range>-<yyyyMMdd-HHmmss>.<ext>`, stamped in local time. */
export function usageExportFileName(chart: string, range: string, ext: 'csv' | 'png', now: Date = new Date()): string {
    const pad = (n: number): string => String(n).padStart(2, '0');
    const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
    const slug = (s: string): string => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'chart';
    return `mux-usage-${slug(chart)}-${slug(range)}-${stamp}.${ext}`;
}

/** The chart slug used in export file names: `calls`, `tokens`, `calls-by-endpoint`, `usage-by-model`, `heatmap`, ... */
export function usageExportChartName(tab: string, metric: UsageSplitMetricKey, dimension: UsageDimensionKey): string {
    if (tab === 'split') {
        return `${metric}-by-${dimension}`;
    }
    if (tab === 'breakdown') {
        return `usage-by-${dimension}`;
    }
    return tab;
}

/** A bucket the CSV builders read. */
export interface CsvBucketLike {
    BucketStartUnixMs: number;
    Metrics: BucketMetricsLike & {
        TotalMsDist?: DistributionLike;
        TtftMsDist?: DistributionLike;
        StreamMsDist?: DistributionLike;
        ThroughputDist?: DistributionLike;
    };
}

/** A min/avg/p95/p99/max distribution on the wire. */
export interface DistributionLike {
    Min?: number;
    Avg?: number;
    P95?: number;
    P99?: number;
    Max?: number;
    Count?: number;
}

const DIST_FIELDS: Record<string, keyof CsvBucketLike['Metrics']> = {
    latency: 'TotalMsDist',
    ttft: 'TtftMsDist',
    streaming: 'StreamMsDist',
    stream: 'StreamMsDist',
    throughput: 'ThroughputDist',
};

/** What the CSV builders need: the loaded series and the window they cover. */
export interface UsageCsvInput {
    range: string;
    metric: UsageSplitMetricKey;
    dimension: UsageDimensionKey;
    buckets: readonly CsvBucketLike[];
    split: readonly SplitSeriesLike[];
    breakdown: readonly BreakdownRowLike[];
    heatmap?: HeatmapLike | null;
    /** The summary window, used for the "Usage by" rows. */
    fromUnixMs?: number;
    toUnixMs?: number;
    nowUnixMs?: number;
    /** A fixed UTC offset in minutes (east positive) for the times; the local zone when omitted. */
    offsetMinutes?: number;
}

/**
 * The CSV table of one chart tab, or `null` when the chart has no data (export is then disabled):
 * - calls: `succeeded`, `failed`; tokens: `uncached_input`, `cached_input`, `output`; cost: `cost_usd`;
 * - split: one column per dimension value in rank order, `Other` last, holding the chosen metric;
 * - latency, ttft, streaming, throughput: `min`, `avg`, `p95`, `p99`, `max`, `count` (the stats empty when count is 0);
 * each after `bucket_start` and `bucket_end` (the next bucket's start);
 * - breakdown: one row per value in the chart's order with the range window, then the dimension key, `calls`,
 *   `failed`, `uncached_input`, `cached_input`, `output`, `total_tokens`, `cost_usd`;
 * - heatmap: one row per cell up to now, `cell_start`, `cell_end`, `tokens`, `calls`.
 */
export function usageCsvTable(tab: string, input: UsageCsvInput): CsvTable | null {
    const time = (ms: number): string => (input.offsetMinutes === undefined ? csvTime(ms) : csvTime(ms, input.offsetMinutes));
    const buckets = input.buckets ?? [];
    const width = bucketWidthMs(buckets.map((b) => b.BucketStartUnixMs), input.range, input.nowUnixMs ?? Date.now());
    const span = (b: CsvBucketLike): CsvCell[] => [time(b.BucketStartUnixMs), time(b.BucketStartUnixMs + width)];
    const anyCalls = buckets.some((b) => (b.Metrics?.Calls ?? 0) > 0);

    if (tab === 'heatmap') {
        const grid = heatmapGrid(input.heatmap ?? null);
        if (!(grid.max > 0)) {
            return null;
        }
        const rows = grid.cells.filter((c) => !c.future).map((c) => [time(c.from), time(c.to + 1), c.tokens, c.calls]);
        return { header: ['cell_start', 'cell_end', 'tokens', 'calls'], rows };
    }
    if (tab === 'breakdown') {
        const bars = breakdownBars(input.breakdown, BREAKDOWN_LIMIT, input.metric);
        if (bars.length === 0) {
            return null;
        }
        const from = input.fromUnixMs ?? (buckets[0]?.BucketStartUnixMs ?? 0);
        const to = input.toUnixMs ?? (buckets.length ? buckets[buckets.length - 1].BucketStartUnixMs + width : from);
        return {
            header: ['bucket_start', 'bucket_end', input.dimension, 'calls', 'failed', 'uncached_input', 'cached_input', 'output', 'total_tokens', 'cost_usd'],
            rows: bars.map((b) => [time(from), time(to), b.value, b.calls, b.failed, b.input, b.cached, b.output, b.totalTokens, b.cost]),
        };
    }
    if (tab === 'split') {
        const series = stackSplitSeries(input.split, buckets.length, SPLIT_TOP, input.metric);
        if (series.length === 0 || buckets.length === 0) {
            return null;
        }
        return {
            header: ['bucket_start', 'bucket_end', ...series.map((s) => (s.isOther ? 'Other' : s.key))],
            rows: buckets.map((b, i) => [...span(b), ...series.map((s) => s.values[i] ?? 0)]),
        };
    }
    if (buckets.length === 0 || !anyCalls) {
        return null;
    }
    if (tab === 'calls') {
        return {
            header: ['bucket_start', 'bucket_end', 'succeeded', 'failed'],
            rows: buckets.map((b) => [...span(b), Math.max(0, (b.Metrics?.Calls ?? 0) - (b.Metrics?.Errors ?? 0)), b.Metrics?.Errors ?? 0]),
        };
    }
    if (tab === 'tokens') {
        return {
            header: ['bucket_start', 'bucket_end', 'uncached_input', 'cached_input', 'output'],
            rows: buckets.map((b) => { const t = tokenParts(b.Metrics); return [...span(b), t.input, t.cached, t.output]; }),
        };
    }
    if (tab === 'cost') {
        return { header: ['bucket_start', 'bucket_end', 'cost_usd'], rows: buckets.map((b) => [...span(b), b.Metrics?.CostUsd ?? 0]) };
    }
    const field = DIST_FIELDS[tab];
    if (!field) {
        return null;
    }
    return {
        header: ['bucket_start', 'bucket_end', 'min', 'avg', 'p95', 'p99', 'max', 'count'],
        rows: buckets.map((b) => {
            const d = (b.Metrics?.[field] as DistributionLike | undefined) ?? {};
            const count = Math.max(0, d.Count ?? 0);
            return count > 0 ? [...span(b), d.Min ?? 0, d.Avg ?? 0, d.P95 ?? 0, d.P99 ?? 0, d.Max ?? 0, count] : [...span(b), '', '', '', '', '', 0];
        }),
    };
}
