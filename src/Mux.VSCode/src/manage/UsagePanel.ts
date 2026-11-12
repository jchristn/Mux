import * as crypto from 'crypto';
import * as os from 'os';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { UsageBreakdownRow, UsageBucket, UsageDistribution, UsageErrorCount, UsageEventRow, UsageHeatmap, UsageSeries, UsageSummary } from '../api/types';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';
import {
    AxisLabelKind,
    FailureReason,
    SPLIT_TOP,
    USAGE_DIMENSIONS,
    USAGE_SPLIT_METRICS,
    UsageChartModeKey,
    UsageDimensionKey,
    UsageSplitMetricKey,
    axisLabelKind,
    breakdownBars,
    bucketWidthMs,
    callParts,
    drillWindow,
    eventsQuery,
    failureReasons,
    formatCount,
    formatPercent,
    formatTokensCompact,
    formatUsd,
    heatmapCellWindow,
    heatmapColumnLabel,
    heatmapGrid,
    heatmapRowLabel,
    heatmapSizes,
    normalizeChartMode,
    normalizeDimension,
    normalizeRange,
    normalizeSplitMetric,
    stackSplitSeries,
    thinLabels,
    tokenParts,
    usageCsv,
    usageCsvTable,
    usageExportChartName,
    usageExportFileName,
} from './usageCharts';

const DAY_MS = 24 * 60 * 60 * 1000;

/** A min/avg/p95/p99/max distribution passed to the chart. */
interface Dist {
    min: number;
    avg: number;
    p95: number;
    p99: number;
    max: number;
    count: number;
}

/** A per-bucket series point sent to the webview for charting. */
interface SeriesPoint {
    label: string;
    xlabel: string;
    from: number;
    to: number;
    ok: number;
    failed: number;
    errorRate: string;
    input: number;
    cached: number;
    output: number;
    tokens: number;
    cost: number;
    latency: Dist;
    ttft: Dist;
    streaming: Dist;
    throughput: Dist;
}

/** One stacked series of the "<Metric> by" chart. */
interface SplitPayload {
    label: string;
    color: number;
    values: number[];
}

/** One bar of the "Usage by" chart. */
interface BreakdownPayload {
    label: string;
    input: number;
    cached: number;
    output: number;
    tokens: number;
    calls: number;
    cost: number;
    text: string;
}

/** The token heatmap: the clock-aligned grid (columns the x-axis, rows the y-axis), its axis labels, and its captions. */
interface HeatmapPayload {
    rows: number;
    cols: number;
    /** One label per row (the y-axis). */
    rowLabels: string[];
    /** One label per column (the x-axis); the webview thins them to the width it has. */
    colLabels: string[];
    /** The columns that may carry a label; whether they are evenly spaced (thinned by stride) or not (month starts). */
    anchors: number[];
    evenAnchors: boolean;
    /** Consecutive columns sharing one label (2 for half-days: each weekday labeled once, over its AM column). */
    colGroup: number;
    cells: { row: number; col: number; level: number; label: string; from: number; to: number; future: boolean; blank: boolean }[];
    max: number;
    caption: string;
    note: string;
}

/** The payload the host posts to the usage webview. */
interface UsagePayload {
    type: 'usage';
    seq: number;
    enabled: boolean;
    range: string;
    endpoint: string;
    model: string;
    dimension: string;
    /** The "<Metric> by" metric and the lines/bars mode, restored when the panel reopens. */
    splitMetric: UsageSplitMetricKey;
    mode: UsageChartModeKey;
    endpoints: string[];
    models: string[];
    kpis: { label: string; value: string }[];
    series: SeriesPoint[];
    /** The split series and the breakdown bars for the current metric. */
    split: SplitPayload[];
    breakdown: BreakdownPayload[];
    /** The split series and breakdown bars for every metric, so the metric picker switches without a reload. */
    splits: Record<UsageSplitMetricKey, SplitPayload[]>;
    breakdowns: Record<UsageSplitMetricKey, BreakdownPayload[]>;
    failures: FailureReason[];
    heatmap: HeatmapPayload;
}

/** The calls of one bucket, posted for the drill-down table. */
interface DrillPayload {
    type: 'drill';
    seq: number;
    title: string;
    rows: { time: string; model: string; endpoint: string; tokens: string; latency: string; status: string; failed: boolean }[];
    note: string;
    error: boolean;
}

/** A message from the webview. */
interface PanelMessage {
    type: string;
    range?: string;
    endpoint?: string;
    model?: string;
    dimension?: string;
    splitMetric?: string;
    mode?: string;
    tab?: string;
    dataUrl?: string;
    from?: number;
    to?: number;
    seq?: number;
}

/** The raw responses of the last load, kept for CSV export. */
interface LoadedData {
    range: string;
    dimension: UsageDimensionKey;
    summary: UsageSummary;
    buckets: UsageBucket[];
    split: UsageSeries[];
    breakdown: UsageBreakdownRow[];
    heatmap: UsageHeatmap | null;
}

/**
 * The native usage dashboard: KPIs and over-time charts drawn from the server's usage telemetry, so the cost and
 * performance view the web dashboard and desktop app give is available in the editor. The extension host fetches
 * the data (the webview cannot reach the authenticated server itself) and posts it in; the webview renders
 * hand-rolled SVG — no charting library — and switches tabs, ranges, filters and the split dimension. Clicking
 * (or pressing Enter on) a column of an over-time chart lists that bucket's calls under the chart.
 */
export class UsagePanel {
    private static current: vscode.WebviewPanel | undefined;
    private static lifecycle: MuxServerLifecycle | undefined;
    private static range = 'day';
    private static endpoint = '';
    private static model = '';
    private static dimension: UsageDimensionKey = 'model';
    private static splitMetric: UsageSplitMetricKey = 'tokens';
    private static mode: UsageChartModeKey = 'bars';
    private static loaded: LoadedData | undefined;
    private static loadSeq = 0;

    /**
     * Opens (or reveals) the usage panel and loads the last-used range (Day at first).
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     */
    public static show(lifecycle: MuxServerLifecycle): void {
        UsagePanel.lifecycle = lifecycle;
        if (UsagePanel.current) {
            UsagePanel.current.reveal();
            void UsagePanel.load();
            return;
        }

        const panel = vscode.window.createWebviewPanel('muxUsage', vscode.l10n.t('mux Usage'), vscode.ViewColumn.Active, {
            enableScripts: true,
            retainContextWhenHidden: true,
        });
        panel.webview.html = UsagePanel.render();
        panel.webview.onDidReceiveMessage((message: PanelMessage) => {
            if (message.type === 'reload') {
                UsagePanel.range = normalizeRange(message.range);
                UsagePanel.endpoint = message.endpoint ?? '';
                UsagePanel.model = message.model ?? '';
                UsagePanel.dimension = normalizeDimension(message.dimension);
                UsagePanel.rememberPrefs(message);
                void UsagePanel.load();
            } else if (message.type === 'prefs') {
                UsagePanel.rememberPrefs(message);
            } else if (message.type === 'drill' && typeof message.from === 'number' && typeof message.to === 'number') {
                void UsagePanel.drill(message.from, message.to, message.seq ?? 0);
            } else if (message.type === 'exportCsv') {
                UsagePanel.rememberPrefs(message);
                void UsagePanel.exportCsv(message.tab ?? '');
            } else if (message.type === 'exportPng') {
                UsagePanel.rememberPrefs(message);
                void UsagePanel.exportPng(message.tab ?? '', message.dataUrl ?? '');
            }
        });
        panel.onDidDispose(() => {
            UsagePanel.current = undefined;
        });
        UsagePanel.current = panel;
        void UsagePanel.load();
    }

    // Keeps the webview's metric and lines/bars choices for this session (restored when the panel reopens).
    private static rememberPrefs(message: PanelMessage): void {
        if (message.splitMetric !== undefined) {
            UsagePanel.splitMetric = normalizeSplitMetric(message.splitMetric);
        }
        if (message.mode !== undefined) {
            UsagePanel.mode = normalizeChartMode(message.mode);
        }
    }

    // The folder the save dialog starts in: the first workspace folder, else the home directory.
    private static exportUri(name: string): vscode.Uri {
        const folder = vscode.workspace.workspaceFolders?.[0]?.uri ?? vscode.Uri.file(os.homedir());
        return vscode.Uri.joinPath(folder, name);
    }

    // Writes the CSV of one chart tab (built from the last load) to a file the user picks.
    private static async exportCsv(tab: string): Promise<void> {
        const data = UsagePanel.loaded;
        const table = data
            ? usageCsvTable(tab, {
                range: data.range, metric: UsagePanel.splitMetric, dimension: data.dimension, buckets: data.buckets, split: data.split,
                breakdown: data.breakdown, heatmap: data.heatmap, fromUnixMs: data.summary.FromUnixMs, toUnixMs: data.summary.ToUnixMs,
            })
            : null;
        if (!data || !table) {
            void vscode.window.showInformationMessage(vscode.l10n.t('This chart has no data to export.'));
            return;
        }
        const name = usageExportFileName(usageExportChartName(tab, UsagePanel.splitMetric, data.dimension), data.range, 'csv');
        await UsagePanel.save(name, { [vscode.l10n.t('CSV files')]: ['csv'] }, vscode.l10n.t('Export CSV'), Buffer.from(usageCsv(table.header, table.rows), 'utf8'));
    }

    // Writes the PNG the webview rendered (a data: URL) to a file the user picks.
    private static async exportPng(tab: string, dataUrl: string): Promise<void> {
        const match = /^data:image\/png;base64,([A-Za-z0-9+/=]+)$/.exec(dataUrl);
        const data = UsagePanel.loaded;
        if (!match || !data) {
            void vscode.window.showInformationMessage(vscode.l10n.t('This chart has no data to export.'));
            return;
        }
        const name = usageExportFileName(usageExportChartName(tab, UsagePanel.splitMetric, data.dimension), data.range, 'png');
        await UsagePanel.save(name, { [vscode.l10n.t('PNG images')]: ['png'] }, vscode.l10n.t('Save PNG'), Buffer.from(match[1], 'base64'));
    }

    private static async save(name: string, filters: Record<string, string[]>, saveLabel: string, bytes: Uint8Array): Promise<void> {
        const uri = await vscode.window.showSaveDialog({ defaultUri: UsagePanel.exportUri(name), filters, saveLabel });
        if (!uri) {
            return;
        }
        try {
            await vscode.workspace.fs.writeFile(uri, bytes);
            void vscode.window.showInformationMessage(vscode.l10n.t('Saved {0}', uri.fsPath));
        } catch (error) {
            logError('Failed to save a usage export.', error);
            void vscode.window.showErrorMessage(vscode.l10n.t('Could not save {0}: {1}', uri.fsPath, error instanceof Error ? error.message : String(error)));
        }
    }

    private static async load(): Promise<void> {
        if (!UsagePanel.current || !UsagePanel.lifecycle) {
            return;
        }

        const seq = ++UsagePanel.loadSeq;
        const range = UsagePanel.range;
        const dimension = UsagePanel.dimension;
        try {
            const client: ApiClient = await UsagePanel.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const endpoint = UsagePanel.endpoint || undefined;
            const model = UsagePanel.model || undefined;
            const [summary, buckets, filters, errors, split, breakdown, heat] = await Promise.all([
                client.getUsageSummary(range, endpoint, model),
                client.getUsageTimeseries(range, endpoint, model),
                client.getUsageFilters().catch(() => ({ Enabled: true, Endpoints: [], Models: [] })),
                client.getUsageErrors(range, endpoint, model).catch((): UsageErrorCount[] => []),
                client.getUsageSplit(range, dimension, endpoint, model, SPLIT_TOP).catch((): UsageSeries[] => []),
                client.getUsageBreakdown(range, dimension, endpoint, model).catch((): UsageBreakdownRow[] => []),
                client.getUsageHeatmap(range, new Date().getTimezoneOffset(), endpoint, model).catch((): UsageHeatmap | null => null),
            ]);
            if (seq !== UsagePanel.loadSeq || !UsagePanel.current) {
                return;
            }
            UsagePanel.loaded = { range, dimension, summary, buckets, split, breakdown, heatmap: heat };
            const m = summary.Metrics;
            const payload: UsagePayload = {
                type: 'usage',
                seq,
                enabled: true,
                range,
                endpoint: UsagePanel.endpoint,
                model: UsagePanel.model,
                dimension,
                splitMetric: UsagePanel.splitMetric,
                mode: UsagePanel.mode,
                endpoints: filters.Endpoints ?? [],
                models: filters.Models ?? [],
                kpis: [
                    { label: vscode.l10n.t('Calls'), value: formatCount(m.Calls) },
                    { label: vscode.l10n.t('Tokens'), value: formatCount(m.TotalTokens) },
                    { label: vscode.l10n.t('Cost'), value: `$${(m.CostUsd ?? 0).toFixed(4)}` },
                    { label: vscode.l10n.t('Errors'), value: formatCount(m.Errors) },
                    { label: vscode.l10n.t('Error rate'), value: formatPercent(callParts(m).errorRate) },
                    { label: vscode.l10n.t('Avg TTFT'), value: `${Math.round(m.AvgTtftMs ?? 0)} ms` },
                    { label: vscode.l10n.t('Avg latency'), value: `${Math.round(m.AvgTotalMs ?? 0)} ms` },
                ],
                ...UsagePanel.charts(range, buckets, split, breakdown),
                heatmap: UsagePanel.heatmap(heat),
                failures: failureReasons(errors),
            };
            await UsagePanel.current.webview.postMessage(payload);
        } catch (error) {
            logError('Failed to load usage.', error);
            if (seq !== UsagePanel.loadSeq || !UsagePanel.current) {
                return;
            }
            UsagePanel.loaded = undefined;
            const none = { tokens: [], calls: [], cost: [] };
            await UsagePanel.current.webview.postMessage({
                type: 'usage', seq, enabled: false, range, endpoint: UsagePanel.endpoint, model: UsagePanel.model, dimension,
                splitMetric: UsagePanel.splitMetric, mode: UsagePanel.mode,
                endpoints: [], models: [], kpis: [], series: [], split: [], breakdown: [], splits: none, breakdowns: none, failures: [], heatmap: UsagePanel.heatmap(null),
            });
        }
    }

    // Shapes the timeseries, split and breakdown responses into the chart payloads (split and breakdown per metric).
    private static charts(
        range: string,
        buckets: UsageBucket[],
        split: UsageSeries[],
        breakdown: UsageBreakdownRow[],
    ): Pick<UsagePayload, 'series' | 'split' | 'breakdown' | 'splits' | 'breakdowns'> {
        const starts = buckets.map((b) => b.BucketStartUnixMs);
        const width = bucketWidthMs(starts, range);
        const kind = axisLabelKind(range, width, buckets.length);
        const series: SeriesPoint[] = buckets.map((b) => {
            const w = drillWindow(b.BucketStartUnixMs, width);
            const calls = callParts(b.Metrics);
            const tokens = tokenParts(b.Metrics);
            return {
                label: windowLabel(w.from, w.to, width),
                xlabel: axisLabel(b.BucketStartUnixMs, kind),
                from: w.from,
                to: w.to,
                ok: calls.ok,
                failed: calls.failed,
                errorRate: formatPercent(calls.errorRate),
                input: tokens.input,
                cached: tokens.cached,
                output: tokens.output,
                tokens: b.Metrics.TotalTokens ?? 0,
                cost: b.Metrics.CostUsd ?? 0,
                latency: dist(b.Metrics.TotalMsDist),
                ttft: dist(b.Metrics.TtftMsDist),
                streaming: dist(b.Metrics.StreamMsDist),
                throughput: dist(b.Metrics.ThroughputDist),
            };
        });

        const splits = {} as Record<UsageSplitMetricKey, SplitPayload[]>;
        const breakdowns = {} as Record<UsageSplitMetricKey, BreakdownPayload[]>;
        for (const metric of USAGE_SPLIT_METRICS) {
            splits[metric] = stackSplitSeries(split, buckets.length, SPLIT_TOP, metric).map((s) => ({
                label: s.isOther ? vscode.l10n.t('Other') : s.key,
                color: s.color,
                values: s.values,
            }));
            breakdowns[metric] = breakdownBars(breakdown, undefined, metric).map((b) => ({
                label: b.value,
                input: b.input,
                cached: b.cached,
                output: b.output,
                tokens: b.tokens,
                calls: b.calls,
                cost: b.cost,
                text: vscode.l10n.t('{0} tokens · {1} · {2} calls', formatTokensCompact(b.tokens), formatUsd(b.cost), formatCount(b.calls)),
            }));
        }

        const metric = UsagePanel.splitMetric;
        return { series, split: splits[metric], breakdown: breakdowns[metric], splits, breakdowns };
    }

    // Shapes the heatmap response (the shared clock-aligned grid) into the drawn grid with both axes labeled.
    private static heatmap(heat: UsageHeatmap | null): HeatmapPayload {
        const grid = heatmapGrid(heat);
        if (!heat || grid.rows === 0) {
            return { rows: 0, cols: 0, rowLabels: [], colLabels: [], anchors: [], evenAnchors: true, colGroup: 1, cells: [], max: 0, caption: '', note: '' };
        }
        const lay = heat.Layout;
        const columnNames: Record<string, string> = {
            '5m': vscode.l10n.t('5 minutes starting'),
            '1h': vscode.l10n.t('hour of day'),
            '1d': vscode.l10n.t('day'),
            halfday: vscode.l10n.t('day of the week (AM, PM)'),
            '1w': vscode.l10n.t('week starting'),
            month: vscode.l10n.t('week (by month)'),
            period: vscode.l10n.t('period starting'),
        };
        const rowNames: Record<string, string> = {
            minute: vscode.l10n.t('minutes after the column start'),
            minuteofhour: vscode.l10n.t('minute of the hour'),
            hours: vscode.l10n.t('hours after the column start'),
            clock: vscode.l10n.t('time of day'),
            weekday: vscode.l10n.t('day of the week'),
            week: vscode.l10n.t('week starting'),
            weeks: vscode.l10n.t('weeks after the column start'),
        };
        const group = Math.max(1, lay.ColumnGroup || 1);
        const anchors = lay.ColumnLabelAnchors?.length ? lay.ColumnLabelAnchors : Array.from({ length: Math.ceil(grid.cols / group) }, (_, i) => i * group);
        return {
            rows: grid.rows,
            cols: grid.cols,
            rowLabels: Array.from({ length: grid.rows }, (_, r) => heatmapRowLabel(lay, r)),
            colLabels: Array.from({ length: grid.cols }, (_, c) => heatmapColumnLabel(lay, c)),
            anchors,
            evenAnchors: lay.ColumnKind !== 'month',
            colGroup: group,
            cells: grid.cells.map((c) => ({
                row: c.row, col: c.col, level: c.level, from: c.from, to: c.to, future: c.future, blank: c.blank,
                label: vscode.l10n.t('{0}: {1} tokens · {2} calls', heatmapCellWindow(lay, c.from), formatCount(c.tokens), formatCount(c.calls)),
            })),
            max: grid.max,
            caption: vscode.l10n.t('Columns: {0} · Rows: {1}', columnNames[lay.ColumnUnit] ?? columnNames.period, rowNames[lay.RowKind] ?? rowNames.weeks),
            note: vscode.l10n.t('peak {0} tokens', formatCount(grid.max)),
        };
    }

    // Lists the calls in one bucket's window (with the panel's endpoint/model filters) for the drill-down table.
    private static async drill(from: number, to: number, seq: number): Promise<void> {
        if (!UsagePanel.current || !UsagePanel.lifecycle) {
            return;
        }

        const width = to - from + 1;
        const title = vscode.l10n.t('Calls {0}', windowLabel(from, to, width));
        try {
            const client: ApiClient = await UsagePanel.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const page = await client.getUsageEvents(eventsQuery({ from, to }, UsagePanel.endpoint || undefined, UsagePanel.model || undefined));
            const rows = page.Items.map((e) => eventRow(e, width));
            let note = '';
            if (rows.length === 0) {
                note = vscode.l10n.t('No calls in this bucket.');
            } else if (page.TotalCount > rows.length) {
                note = vscode.l10n.t('Showing {0} of {1} calls.', formatCount(rows.length), formatCount(page.TotalCount));
            }
            const payload: DrillPayload = { type: 'drill', seq, title, rows, note, error: false };
            await UsagePanel.current?.webview.postMessage(payload);
        } catch (error) {
            logError('Failed to load the calls for a usage bucket.', error);
            const payload: DrillPayload = { type: 'drill', seq, title, rows: [], note: vscode.l10n.t('Could not load the calls for this bucket.'), error: true };
            await UsagePanel.current?.webview.postMessage(payload);
        }
    }

    private static render(): string {
        const nonce = crypto.randomBytes(16).toString('hex');
        // img-src data: lets Save PNG rasterize the chart (an SVG data: URL drawn onto a canvas).
        const csp = `default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}'; img-src data:;`;
        const dimensionLabels: Record<UsageDimensionKey, string> = {
            model: vscode.l10n.t('Model'),
            endpoint: vscode.l10n.t('Endpoint'),
            provider: vscode.l10n.t('Provider'),
            callkind: vscode.l10n.t('Call kind'),
        };
        const strings = JSON.stringify({
            title: vscode.l10n.t('Usage'),
            disabled: vscode.l10n.t('Usage telemetry is disabled or unavailable.'),
            empty: vscode.l10n.t('No usage in this range.'),
            tabs: {
                calls: vscode.l10n.t('Calls'),
                tokens: vscode.l10n.t('Tokens'),
                split: {
                    tokens: vscode.l10n.t('Tokens by {0}', '{0}'),
                    calls: vscode.l10n.t('Calls by {0}', '{0}'),
                    cost: vscode.l10n.t('Cost by {0}', '{0}'),
                },
                cost: vscode.l10n.t('Cost'),
                latency: vscode.l10n.t('Latency'),
                ttft: vscode.l10n.t('TTFT'),
                streaming: vscode.l10n.t('Streaming'),
                throughput: vscode.l10n.t('Throughput'),
                breakdown: vscode.l10n.t('Usage by {0}', '{0}'),
                heatmap: vscode.l10n.t('Heatmap'),
            },
            legend: {
                input: vscode.l10n.t('Prompt'), cached: vscode.l10n.t('Cached'), output: vscode.l10n.t('Output'),
                ok: vscode.l10n.t('Succeeded'), failed: vscode.l10n.t('Failed'), errorRate: vscode.l10n.t('Error rate'),
                less: vscode.l10n.t('Less'), more: vscode.l10n.t('More'), none: vscode.l10n.t('No usage'),
            },
            ranges: {
                hour: vscode.l10n.t('Hour'), day: vscode.l10n.t('Day'), week: vscode.l10n.t('Week'), month: vscode.l10n.t('Month'),
                quarter: vscode.l10n.t('Quarter'), year: vscode.l10n.t('Year'), all: vscode.l10n.t('All'),
            },
            filters: { allEndpoints: vscode.l10n.t('All endpoints'), allModels: vscode.l10n.t('All models') },
            dimensionTitle: vscode.l10n.t('Split by'),
            dimensions: USAGE_DIMENSIONS.map((d) => ({ key: d, label: dimensionLabels[d] })),
            metricTitle: vscode.l10n.t('Metric'),
            metrics: [
                { key: 'tokens', label: vscode.l10n.t('Tokens') },
                { key: 'calls', label: vscode.l10n.t('Calls') },
                { key: 'cost', label: vscode.l10n.t('Cost') },
            ],
            modeTitle: vscode.l10n.t('Chart style'),
            modes: { bars: vscode.l10n.t('Bars'), lines: vscode.l10n.t('Lines') },
            exportCsv: vscode.l10n.t('Export CSV'),
            exportCsvTip: vscode.l10n.t('Save the data of this chart as a CSV file'),
            savePng: vscode.l10n.t('Save PNG'),
            savePngTip: vscode.l10n.t('Save this chart as a PNG image'),
            noExport: vscode.l10n.t('This chart has no data to export.'),
            failureReasons: vscode.l10n.t('Failure reasons'),
            noFailures: vscode.l10n.t('No failures in this range.'),
            heatmapTitle: vscode.l10n.t('Token activity heatmap'),
            heatmapHint: vscode.l10n.t('Click a cell, or focus it and press Enter, to list its calls.'),
            chartHint: vscode.l10n.t('Usage chart. Use the Left and Right arrow keys to choose a bucket and Enter to list its calls.'),
            clickHint: vscode.l10n.t('Click a column, or focus the chart and press Enter, to list the calls in that bucket.'),
            loadingCalls: vscode.l10n.t('Loading calls…'),
            close: vscode.l10n.t('Close'),
            columns: [vscode.l10n.t('Time'), vscode.l10n.t('Model'), vscode.l10n.t('Endpoint'), vscode.l10n.t('Tokens'), vscode.l10n.t('Latency'), vscode.l10n.t('Status')],
        });

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="${csp}" />
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 18px; }
  h1 { font-size: 1.3em; margin: 0 0 14px; }
  h2 { font-size: 1em; margin: 18px 0 8px; }
  .bar { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 16px; }
  .seg { display: inline-flex; flex-wrap: wrap; border: 1px solid var(--vscode-panel-border); border-radius: 6px; overflow: hidden; }
  .seg button { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); border: none; padding: 5px 12px; cursor: pointer; }
  .seg button.active { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
  button:focus-visible, select:focus-visible, svg:focus-visible, .hm:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 1px; }
  .hm.sel { outline: 2px solid var(--vscode-foreground); outline-offset: 0; }
  select { background: var(--vscode-dropdown-background); color: var(--vscode-dropdown-foreground); border: 1px solid var(--vscode-dropdown-border, var(--vscode-panel-border)); border-radius: 6px; padding: 5px 8px; cursor: pointer; }
  .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 10px; margin-bottom: 20px; }
  .kpi { border: 1px solid var(--vscode-panel-border); border-radius: 8px; padding: 12px; background: var(--vscode-editorWidget-background, var(--vscode-editor-background)); }
  .kpi .k-label { font-size: 0.8em; color: var(--vscode-descriptionForeground); }
  .kpi .k-value { font-size: 1.4em; font-weight: 600; margin-top: 4px; font-variant-numeric: tabular-nums; }
  .chart-head { display: flex; justify-content: space-between; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
  .hidden { display: none !important; }
  svg { width: 100%; height: 455px; display: block; }
  svg.time { cursor: pointer; }
  .hit { fill: transparent; }
  .cursor { fill: var(--vscode-list-hoverBackground, var(--vscode-editor-selectionBackground)); opacity: 0.6; }
  .cursor.selected { fill: var(--vscode-editor-selectionBackground, var(--vscode-list-activeSelectionBackground)); opacity: 0.8; }
  .grid { stroke: var(--vscode-panel-border); stroke-opacity: 0.4; }
  .bar-primary { fill: var(--vscode-charts-green, var(--vscode-textLink-foreground)); }
  .bar-input { fill: var(--vscode-charts-blue, #4c8bf5); }
  .bar-cached { fill: var(--vscode-charts-yellow, #d7a100); }
  .bar-output { fill: var(--vscode-charts-green, #3fb950); }
  .bar-ok { fill: var(--vscode-charts-green, #3fb950); }
  .bar-failed { fill: var(--vscode-charts-red, #e5534b); }
  /* Lines mode: one 2px line per series in the series color (not stacked), with point markers on short series. */
  .ln { fill: none !important; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; pointer-events: none; }
  .pt { pointer-events: none; }
  .ln.bar-primary, .ln.bar-ok, .ln.bar-output, .ln.s3 { stroke: var(--vscode-charts-green, #3fb950); }
  .ln.bar-input, .ln.s0 { stroke: var(--vscode-charts-blue, #4c8bf5); }
  .ln.bar-cached, .ln.s4 { stroke: var(--vscode-charts-yellow, #d7a100); }
  .ln.bar-failed, .ln.s5 { stroke: var(--vscode-charts-red, #e5534b); }
  .ln.s1 { stroke: var(--vscode-charts-orange, #d18616); }
  .ln.s2 { stroke: var(--vscode-charts-purple, #a371f7); }
  .ln.sother { stroke: var(--vscode-disabledForeground, #8b8b8b); }
  .s0 { fill: var(--vscode-charts-blue, #4c8bf5); background: var(--vscode-charts-blue, #4c8bf5); }
  .s1 { fill: var(--vscode-charts-orange, #d18616); background: var(--vscode-charts-orange, #d18616); }
  .s2 { fill: var(--vscode-charts-purple, #a371f7); background: var(--vscode-charts-purple, #a371f7); }
  .s3 { fill: var(--vscode-charts-green, #3fb950); background: var(--vscode-charts-green, #3fb950); }
  .s4 { fill: var(--vscode-charts-yellow, #d7a100); background: var(--vscode-charts-yellow, #d7a100); }
  .s5 { fill: var(--vscode-charts-red, #e5534b); background: var(--vscode-charts-red, #e5534b); }
  .sother { fill: var(--vscode-disabledForeground, #8b8b8b); background: var(--vscode-disabledForeground, #8b8b8b); }
  .candle-wick { stroke: var(--vscode-descriptionForeground); stroke-width: 1; }
  .candle-box { fill: var(--vscode-charts-green, var(--vscode-textLink-foreground)); opacity: 0.55; }
  .candle-avg { stroke: var(--vscode-foreground); stroke-width: 1.5; }
  .candle-p99 { stroke: var(--vscode-charts-red, #e5534b); stroke-width: 1.5; }
  .axis { stroke: var(--vscode-panel-border); }
  .axis-label { fill: var(--vscode-descriptionForeground); font-size: 10px; }
  .legend { display: flex; flex-wrap: wrap; gap: 14px; font-size: 0.82em; color: var(--vscode-descriptionForeground); margin-top: 8px; }
  .legend .sw { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 5px; vertical-align: middle; }
  .hint { font-size: 0.8em; color: var(--vscode-descriptionForeground); margin-top: 6px; }
  .muted { color: var(--vscode-descriptionForeground); padding: 30px 0; text-align: center; }
  .reasons { list-style: none; padding: 0; margin: 0; max-width: 520px; }
  .reasons li { display: flex; justify-content: space-between; gap: 12px; padding: 4px 0; border-bottom: 1px solid var(--vscode-panel-border); font-variant-numeric: tabular-nums; }
  .reasons code { font-family: var(--vscode-editor-font-family); color: var(--vscode-charts-red, var(--vscode-errorForeground)); }
  .brows { display: grid; grid-template-columns: minmax(80px, 220px) 1fr; gap: 6px 12px; align-items: center; padding: 6px 0; }
  .brow-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.9em; }
  .brow-track { display: flex; flex-direction: column; gap: 2px; }
  .brow-bar { display: flex; height: 14px; border-radius: 3px; overflow: hidden; }
  .brow-bar span { display: block; height: 100%; }
  .brow-bar .input { background: var(--vscode-charts-blue, #4c8bf5); }
  .brow-bar .cached { background: var(--vscode-charts-yellow, #d7a100); }
  .brow-bar .output { background: var(--vscode-charts-green, #3fb950); }
  .brow-bar .single { background: var(--vscode-charts-blue, #4c8bf5); }
  .tools { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .tool { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); border: 1px solid var(--vscode-panel-border); border-radius: 6px; padding: 5px 10px; cursor: pointer; }
  .tool:hover:not(:disabled) { background: var(--vscode-button-secondaryHoverBackground, var(--vscode-button-secondaryBackground)); }
  .tool:disabled { opacity: 0.5; cursor: default; }
  .brow-text { font-size: 0.78em; color: var(--vscode-descriptionForeground); font-variant-numeric: tabular-nums; }
  .heatmap { display: grid; gap: 3px; margin: 10px 0 6px; align-items: center; justify-content: start; max-width: 100%; overflow-x: auto; }
  .hm-cap { font-size: 0.85em; color: var(--vscode-descriptionForeground); margin-top: 2px; }
  .hm-y { font-size: 11px; color: var(--vscode-descriptionForeground); white-space: nowrap; padding-inline-end: 8px; line-height: 1; align-self: center; font-variant-numeric: tabular-nums; }
  .hm-x { font-size: 11px; color: var(--vscode-descriptionForeground); white-space: nowrap; line-height: 1; padding-bottom: 3px; font-variant-numeric: tabular-nums; }
  .hm-x.end { text-align: end; }
  .heatmap .hm { height: calc(100% - 1px); cursor: pointer; }
  /* Heatmap shades 1-4 (low to high): one green hue, validated for monotone lightness, visible steps, and >= 2:1
     contrast for the step nearest the background. Light themes go light to dark, dark themes dark to bright. No
     usage is a solid neutral grey (never white, never a border): #e3e7eb on light themes, #2e3640 on dark ones. */
  .hm { border-radius: 3px; --hm0: #e3e7eb; --hm1: #6cc596; --hm2: #35a571; --hm3: #17784f; --hm4: #0a4d33; }
  body.vscode-dark .hm, body.vscode-high-contrast:not(.vscode-high-contrast-light) .hm { --hm0: #2e3640; --hm1: #1f6b48; --hm2: #2f9a66; --hm3: #4fc98a; --hm4: #9be8bd; }
  .hm.l0 { background: var(--hm0); }
  .hm.l1 { background: var(--hm1); }
  .hm.l2 { background: var(--hm2); }
  .hm.l3 { background: var(--hm3); }
  .hm.l4 { background: var(--hm4); }
  .hm-key { display: inline-flex; gap: 3px; vertical-align: middle; margin: 0 4px; }
  .hm-key .hm { width: 10px; height: 10px; display: inline-block; }
  .drill { margin-top: 18px; border: 1px solid var(--vscode-panel-border); border-radius: 8px; padding: 10px 12px; }
  .drill-head { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
  .drill-head h2 { margin: 0; }
  .drill-close { background: transparent; color: var(--vscode-foreground); border: none; cursor: pointer; font-size: 1.2em; line-height: 1; padding: 2px 6px; border-radius: 4px; }
  .drill-close:hover { background: var(--vscode-toolbar-hoverBackground, var(--vscode-list-hoverBackground)); }
  table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 0.88em; }
  th, td { text-align: left; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); white-space: nowrap; }
  th { color: var(--vscode-descriptionForeground); font-weight: 600; }
  td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
  td.fail { color: var(--vscode-errorForeground, var(--vscode-charts-red)); }
  .table-wrap { overflow-x: auto; }
  .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
</head>
<body>
<h1 id="title"></h1>
<div class="bar">
  <div class="seg" id="ranges" role="group"></div>
  <select id="endpoint" title="Endpoint"></select>
  <select id="model" title="Model"></select>
</div>
<div class="kpis" id="kpis"></div>
<div class="chart-head">
  <div class="seg" id="metrics" role="tablist"></div>
  <div class="tools">
    <select id="splitMetric"></select>
    <select id="dimension"></select>
    <div class="seg" id="mode" role="group"></div>
    <button class="tool" id="exportCsv"></button>
    <button class="tool" id="exportPng"></button>
  </div>
</div>
<div id="capture">
<div id="chart"></div>
<div class="legend" id="legend"></div>
</div>
<div class="hint" id="hint"></div>
<div id="aside"></div>
<div class="drill hidden" id="drill">
  <div class="drill-head"><h2 id="drillTitle"></h2><button class="drill-close" id="drillClose">×</button></div>
  <div class="hint" id="drillNote"></div>
  <div class="table-wrap"><table id="drillTable"></table></div>
</div>
<div class="sr" id="live" aria-live="polite"></div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const S = ${strings};
  const NS = 'http://www.w3.org/2000/svg';
  const TABS = ['calls','tokens','split','cost','latency','ttft','streaming','throughput','breakdown','heatmap'];
  const TIME_TABS = { calls: 1, tokens: 1, split: 1, cost: 1, latency: 1, ttft: 1, streaming: 1, throughput: 1 };
  const DIST_TABS = { latency: 1, ttft: 1, streaming: 1, throughput: 1 };
  const STACK_TABS = { calls: 1, tokens: 1, split: 1, cost: 1 };
  const SERIES_COLORS = ['blue','orange','purple','green','yellow','red'];
  let current = null;
  let metric = 'tokens';
  let range = 'day';
  let endpoint = '';
  let model = '';
  let dimension = 'model';
  let splitMetric = 'tokens'; // what the "<Metric> by" chart shows and the "Usage by" bars sort by
  let mode = 'bars';          // stacked bars or one line per series on the stacked over-time charts
  let exportable = false;     // whether the drawn chart has data (export is disabled otherwise)
  let cursor = -1;      // bucket the keyboard cursor is on
  let selected = -1;    // bucket whose calls the drill-down table shows
  let selectedWindow = null; // start of the heatmap cell whose calls the drill-down table shows
  let drillSeq = 0;
  let plot = null;      // geometry of the drawn over-time chart

  document.getElementById('title').textContent = S.title;
  document.getElementById('drillClose').setAttribute('aria-label', S.close);
  document.getElementById('drillClose').title = S.close;

  function reload(){ vscode.postMessage({ type: 'reload', range: range, endpoint: endpoint, model: model, dimension: dimension, splitMetric: splitMetric, mode: mode }); }
  function savePrefs(){ vscode.postMessage({ type: 'prefs', splitMetric: splitMetric, mode: mode }); }

  const rangesEl = document.getElementById('ranges');
  ['hour','day','week','month','quarter','year','all'].forEach(function(r){
    const b = document.createElement('button');
    b.textContent = S.ranges[r]; b.dataset.range = r;
    if (r === range) b.classList.add('active');
    b.addEventListener('click', function(){ range = r; syncActive(rangesEl, 'range', r); reload(); });
    rangesEl.appendChild(b);
  });

  const endpointEl = document.getElementById('endpoint');
  const modelEl = document.getElementById('model');
  endpointEl.addEventListener('change', function(){ endpoint = endpointEl.value; reload(); });
  modelEl.addEventListener('change', function(){ model = modelEl.value; reload(); });

  const dimensionEl = document.getElementById('dimension');
  dimensionEl.title = S.dimensionTitle;
  dimensionEl.setAttribute('aria-label', S.dimensionTitle);
  S.dimensions.forEach(function(d){
    const opt = document.createElement('option'); opt.value = d.key; opt.textContent = d.label; dimensionEl.appendChild(opt);
  });
  dimensionEl.addEventListener('change', function(){ dimension = dimensionEl.value; labelTabs(); reload(); });

  const splitMetricEl = document.getElementById('splitMetric');
  splitMetricEl.title = S.metricTitle;
  splitMetricEl.setAttribute('aria-label', S.metricTitle);
  S.metrics.forEach(function(m){
    const opt = document.createElement('option'); opt.value = m.key; opt.textContent = m.label; splitMetricEl.appendChild(opt);
  });
  splitMetricEl.addEventListener('change', function(){ splitMetric = splitMetricEl.value; labelTabs(); savePrefs(); draw(); });

  const modeEl = document.getElementById('mode');
  modeEl.setAttribute('aria-label', S.modeTitle);
  ['bars','lines'].forEach(function(k){
    const b = document.createElement('button');
    b.textContent = S.modes[k]; b.dataset.mode = k; b.setAttribute('aria-pressed', k === mode ? 'true' : 'false');
    if (k === mode) b.classList.add('active');
    b.addEventListener('click', function(){ mode = k; syncMode(); savePrefs(); draw(); });
    modeEl.appendChild(b);
  });
  function syncMode(){
    syncActive(modeEl, 'mode', mode);
    Array.prototype.forEach.call(modeEl.children, function(c){ c.setAttribute('aria-pressed', c.dataset.mode === mode ? 'true' : 'false'); });
  }

  const exportCsvEl = document.getElementById('exportCsv');
  const exportPngEl = document.getElementById('exportPng');
  exportCsvEl.textContent = S.exportCsv; exportCsvEl.title = S.exportCsvTip;
  exportPngEl.textContent = S.savePng; exportPngEl.title = S.savePngTip;
  exportCsvEl.addEventListener('click', function(){ if (exportable) vscode.postMessage({ type: 'exportCsv', tab: metric, splitMetric: splitMetric, mode: mode }); });
  exportPngEl.addEventListener('click', function(){
    if (!exportable) return;
    chartPng(function(url){ if (url) vscode.postMessage({ type: 'exportPng', tab: metric, splitMetric: splitMetric, mode: mode, dataUrl: url }); });
  });

  function splitSeries(){ return (current && current.splits && current.splits[splitMetric]) || (current && current.split) || []; }
  function breakdownRows(){ return (current && current.breakdowns && current.breakdowns[splitMetric]) || (current && current.breakdown) || []; }

  function dimensionLabel(){
    const d = S.dimensions.filter(function(x){ return x.key === dimension; })[0];
    return d ? d.label : dimension;
  }

  // (Re)fills a filter <select> with an "all" sentinel plus the distinct values, preserving the selection.
  function fillSelect(el, allLabel, values, sel){
    el.textContent = '';
    const opts = [{ v: '', t: allLabel }].concat((values || []).map(function(v){ return { v: v, t: v }; }));
    opts.forEach(function(o){
      const opt = document.createElement('option');
      opt.value = o.v; opt.textContent = o.t;
      if (o.v === sel) opt.selected = true;
      el.appendChild(opt);
    });
  }

  const metricsEl = document.getElementById('metrics');
  TABS.forEach(function(m){
    const b = document.createElement('button');
    b.dataset.metric = m; b.setAttribute('role', 'tab');
    if (m === metric) b.classList.add('active');
    b.addEventListener('click', function(){ metric = m; syncActive(metricsEl, 'metric', m); draw(); });
    metricsEl.appendChild(b);
  });
  function labelTabs(){
    Array.prototype.forEach.call(metricsEl.children, function(b){
      const label = b.dataset.metric === 'split' ? S.tabs.split[splitMetric] : S.tabs[b.dataset.metric];
      b.textContent = label.replace('{0}', dimensionLabel());
    });
  }
  labelTabs();

  function syncActive(container, attr, value){
    Array.prototype.forEach.call(container.children, function(c){
      const on = c.dataset[attr] === value;
      c.classList.toggle('active', on);
      if (c.getAttribute('role') === 'tab') c.setAttribute('aria-selected', on ? 'true' : 'false');
    });
  }

  function svgEl(name, attrs){
    const e = document.createElementNS(NS, name);
    for (const k in attrs) { e.setAttribute(k, attrs[k]); }
    return e;
  }
  function titled(node, text){ const t = svgEl('title', {}); t.textContent = text; node.appendChild(t); return node; }

  function renderKpis(kpis){
    const el = document.getElementById('kpis'); el.textContent = '';
    kpis.forEach(function(k){
      const card = document.createElement('div'); card.className = 'kpi';
      const l = document.createElement('div'); l.className = 'k-label'; l.textContent = k.label;
      const v = document.createElement('div'); v.className = 'k-value'; v.textContent = k.value;
      card.appendChild(l); card.appendChild(v); el.appendChild(card);
    });
  }

  function compact(v){ v = Math.max(0, v || 0); if (v >= 1e9) return (v/1e9).toFixed(1) + 'B'; if (v >= 1e6) return (v/1e6).toFixed(1) + 'M'; if (v >= 1e3) return (v/1e3).toFixed(1) + 'K'; return String(Math.round(v)); }
  // The value kind of the current tab: the "<Metric> by" chart takes its metric's kind.
  function valueKind(){ return metric === 'split' ? splitMetric : metric; }
  function fmtVal(v){
    const k = valueKind();
    if (k === 'cost') return '$' + v.toFixed(4);
    if (k === 'throughput') return Math.round(v) + ' t/s';
    if (k === 'calls' || k === 'tokens') return String(Math.round(v));
    return Math.round(v) + ' ms';
  }
  function axisVal(v){
    const k = valueKind();
    if (k === 'cost') return '$' + v.toFixed(2);
    if (k === 'throughput') return Math.round(v) + ' t/s';
    if (k === 'calls') return String(Math.round(v));
    if (k === 'tokens') return compact(v);
    return Math.round(v) + ' ms';
  }

  function setLegend(items){
    const el = document.getElementById('legend'); el.textContent = '';
    (items || []).forEach(function(it){
      const span = document.createElement('span');
      const sw = document.createElement('span'); sw.className = 'sw' + (it.cls ? ' ' + it.cls : '');
      if (it.color) sw.style.background = it.color;
      span.appendChild(sw); span.appendChild(document.createTextNode(it.label));
      el.appendChild(span);
    });
  }

  function frame(n){
    const chart = document.getElementById('chart');
    const W = chart.clientWidth || 600, H = 455;
    const padL = 56, padR = 16, padT = 14, padB = 34;
    const svg = svgEl('svg', { viewBox: '0 0 ' + W + ' ' + H });
    const plotW = W - padL - padR, plotH = H - padT - padB;
    return { chart: chart, W: W, H: H, x0: padL, y0: H - padB, plotW: plotW, plotH: plotH, n: n, bw: plotW / n, svg: svg };
  }

  // Y-axis gridlines and value labels (0 → max in quarters) plus the X baseline.
  function drawAxis(f, maxVal){
    [0, 0.25, 0.5, 0.75, 1].forEach(function(frac){
      const yy = f.y0 - frac * f.plotH;
      f.svg.appendChild(svgEl('line', { class: 'grid', x1: f.x0, y1: yy.toFixed(1), x2: (f.x0 + f.plotW).toFixed(1), y2: yy.toFixed(1) }));
      const label = svgEl('text', { class: 'axis-label', x: f.x0 - 6, y: (yy + 3).toFixed(1), 'text-anchor': 'end' });
      label.textContent = axisVal(maxVal * frac);
      f.svg.appendChild(label);
    });
    f.svg.appendChild(svgEl('line', { class: 'axis', x1: f.x0, y1: f.y0, x2: (f.x0 + f.plotW).toFixed(1), y2: f.y0 }));
  }

  // X-axis time labels: up to six, evenly spaced across the buckets.
  function drawXLabels(f, pts){
    const n = pts.length;
    const step = Math.max(1, Math.ceil(n / Math.min(6, n)));
    for (var i = 0; i < n; i += step){
      const cx = f.x0 + i*f.bw + f.bw*0.5;
      const label = svgEl('text', { class: 'axis-label', x: cx.toFixed(1), y: (f.y0 + 16).toFixed(1), 'text-anchor': 'middle' });
      label.textContent = pts[i].xlabel;
      f.svg.appendChild(label);
    }
  }

  // The text of one bucket for its tooltip and the screen-reader announcement on the current tab.
  function bucketText(i){
    const p = current.series[i];
    if (metric === 'calls') return p.label + ' — ' + S.legend.ok + ': ' + p.ok + ' · ' + S.legend.failed + ': ' + p.failed + ' · ' + S.legend.errorRate + ': ' + p.errorRate;
    if (metric === 'tokens') return p.label + ' — ' + S.legend.input + ': ' + p.input + ' · ' + S.legend.cached + ': ' + p.cached + ' · ' + S.legend.output + ': ' + p.output;
    if (metric === 'split') return p.label + ' — ' + splitSeries().map(function(s){ return s.label + ': ' + fmtVal(s.values[i] || 0); }).join(' · ');
    if (metric === 'cost') return p.label + ' — ' + fmtVal(p.cost);
    const d = p[metric];
    return p.label + ' — min ' + Math.round(d.min) + ' · avg ' + Math.round(d.avg) + ' · p95 ' + Math.round(d.p95) + ' · p99 ' + Math.round(d.p99) + ' · max ' + Math.round(d.max);
  }

  // The heatmap redraws on resize so its cells keep filling the width and its labels re-thin.
  let heatmapResize = null;
  window.addEventListener('resize', function(){
    if (metric !== 'heatmap' || !current) return;
    clearTimeout(heatmapResize); heatmapResize = setTimeout(function(){ if (metric === 'heatmap') draw(); }, 150);
  });

  function draw(){
    const chart = document.getElementById('chart'); chart.textContent = ''; setLegend([]);
    document.getElementById('aside').textContent = '';
    document.getElementById('hint').textContent = '';
    const byDimension = metric === 'split' || metric === 'breakdown';
    dimensionEl.classList.toggle('hidden', !byDimension);
    splitMetricEl.classList.toggle('hidden', !byDimension);
    modeEl.classList.toggle('hidden', !STACK_TABS[metric]);
    plot = null;
    setExportable(false);
    if (!current || !current.enabled){ return muted(chart, S.disabled); }
    const pts = current.series || [];
    if (metric === 'calls') renderFailures();
    if (metric === 'breakdown') return drawBreakdown();
    if (metric === 'heatmap') return drawHeatmap();
    if (pts.length === 0){ return muted(chart, S.empty); }

    if (metric === 'calls') {
      return drawStack(pts, [
        { cls: 'bar-ok', label: S.legend.ok, values: pts.map(function(p){ return p.ok; }), color: 'var(--vscode-charts-green, #3fb950)' },
        { cls: 'bar-failed', label: S.legend.failed, values: pts.map(function(p){ return p.failed; }), color: 'var(--vscode-charts-red, #e5534b)' },
      ]);
    }
    if (metric === 'tokens') {
      return drawStack(pts, [
        { cls: 'bar-input', label: S.legend.input, values: pts.map(function(p){ return p.input; }), color: 'var(--vscode-charts-blue, #4c8bf5)' },
        { cls: 'bar-cached', label: S.legend.cached, values: pts.map(function(p){ return p.cached; }), color: 'var(--vscode-charts-yellow, #d7a100)' },
        { cls: 'bar-output', label: S.legend.output, values: pts.map(function(p){ return p.output; }), color: 'var(--vscode-charts-green, #3fb950)' },
      ]);
    }
    if (metric === 'split') {
      return drawStack(pts, splitSeries().map(function(s){
        return { cls: s.color < 0 ? 'sother' : 's' + s.color, label: s.label, values: s.values };
      }));
    }
    if (metric === 'cost') {
      return drawStack(pts, [{ cls: 'bar-primary', label: S.tabs.cost, values: pts.map(function(p){ return p.cost; }), noLegend: true }]);
    }
    return drawCandles(pts, metric);
  }

  function muted(chart, text){ setExportable(false); const d = document.createElement('div'); d.className = 'muted'; d.textContent = text; chart.appendChild(d); }
  function setExportable(on){ exportable = !!on; exportCsvEl.disabled = !on; exportPngEl.disabled = !on; }

  // Starts an over-time chart: transparent per-bucket hit areas (whole-column tooltips) and the cursor highlight.
  function beginTime(f){
    f.cursorRect = svgEl('rect', { class: 'cursor', x: 0, y: (f.y0 - f.plotH).toFixed(1), width: f.bw.toFixed(1), height: f.plotH.toFixed(1), visibility: 'hidden' });
    f.svg.appendChild(f.cursorRect);
    for (var i = 0; i < f.n; i++){
      f.svg.appendChild(titled(svgEl('rect', { class: 'hit', x: (f.x0 + i*f.bw).toFixed(1), y: (f.y0 - f.plotH).toFixed(1), width: f.bw.toFixed(1), height: f.plotH.toFixed(1) }), bucketText(i)));
    }
  }

  // Finishes an over-time chart: axis labels, keyboard and click wiring, hint text.
  function endTime(f, pts){
    drawXLabels(f, pts);
    const svg = f.svg;
    svg.classList.add('time');
    svg.setAttribute('tabindex', '0');
    svg.setAttribute('role', 'group');
    svg.setAttribute('aria-label', S.chartHint);
    svg.addEventListener('click', function(e){
      const r = svg.getBoundingClientRect();
      if (r.width <= 0) return;
      const x = (e.clientX - r.left) * (f.W / r.width);
      const i = Math.floor((x - f.x0) / f.bw);
      if (i >= 0 && i < f.n){ cursor = i; openDrill(i); }
    });
    svg.addEventListener('keydown', function(e){
      var i = cursor < 0 ? (selected >= 0 ? selected : f.n - 1) : cursor;
      if (e.key === 'ArrowLeft') i = Math.max(0, i - 1);
      else if (e.key === 'ArrowRight') i = Math.min(f.n - 1, i + 1);
      else if (e.key === 'Home') i = 0;
      else if (e.key === 'End') i = f.n - 1;
      else if (e.key === 'Enter' || e.key === ' '){ e.preventDefault(); if (cursor < 0) cursor = i; openDrill(cursor); return; }
      else return;
      e.preventDefault();
      cursor = i; moveCursor();
      document.getElementById('live').textContent = bucketText(i);
    });
    f.chart.appendChild(svg);
    plot = f;
    moveCursor();
    document.getElementById('hint').textContent = S.clickHint;
  }

  function moveCursor(){
    if (!plot || !plot.cursorRect) return;
    const i = cursor >= 0 ? cursor : selected;
    if (i < 0 || i >= plot.n){ plot.cursorRect.setAttribute('visibility', 'hidden'); return; }
    plot.cursorRect.setAttribute('x', (plot.x0 + i*plot.bw).toFixed(1));
    plot.cursorRect.setAttribute('visibility', 'visible');
    plot.cursorRect.classList.toggle('selected', i === selected);
  }

  // Stacked (or single) columns per bucket, or one line per layer in lines mode; each layer is { cls, label, values[] }.
  function drawStack(pts, layers){
    var max = 0;
    pts.forEach(function(_, i){ var s = 0; layers.forEach(function(l){ s += Math.max(0, l.values[i] || 0); }); max = Math.max(max, s); });
    if (max <= 0){ return muted(document.getElementById('chart'), S.empty); }
    if (mode === 'lines') return drawLines(pts, layers);
    const f = frame(pts.length);
    drawAxis(f, max);
    beginTime(f);
    pts.forEach(function(p, i){
      var y = f.y0;
      layers.forEach(function(l){
        const v = Math.max(0, l.values[i] || 0);
        const h = (v / max) * f.plotH;
        if (h > 0){
          const rect = svgEl('rect', { class: l.cls, x: (f.x0 + i*f.bw + f.bw*0.15).toFixed(1), y: (y - h).toFixed(1), width: (f.bw*0.7).toFixed(1), height: h.toFixed(1) });
          // The Calls segments carry the whole bucket (succeeded, failed, error rate); others name their layer.
          const tip = metric === 'calls' ? bucketText(i) : (layers.length > 1 ? (p.label + ' — ' + l.label + ': ' + fmtVal(v)) : (p.label + ' — ' + fmtVal(v)));
          f.svg.appendChild(titled(rect, tip));
          y -= h;
        }
      });
    });
    endTime(f, pts);
    setLegend(layers.filter(function(l){ return !l.noLegend; }).map(function(l){ return { cls: l.color ? '' : l.cls, color: l.color, label: l.label }; }));
    setExportable(true);
  }

  // Lines mode: each layer is its own 2px line through the bucket centers (not stacked, so the y-axis tops out at
  // the largest single value), in the layer's color, with point markers when there are few buckets. The column hit
  // areas from beginTime keep the whole-bucket tooltips and the click/keyboard drill-down.
  function drawLines(pts, layers){
    var max = 0;
    layers.forEach(function(l){ pts.forEach(function(_, i){ max = Math.max(max, l.values[i] || 0); }); });
    const f = frame(pts.length);
    drawAxis(f, max);
    beginTime(f);
    const markers = pts.length <= 48;
    layers.forEach(function(l){
      const xy = pts.map(function(_, i){ return [f.x0 + i*f.bw + f.bw*0.5, f.y0 - (Math.max(0, l.values[i] || 0) / max) * f.plotH]; });
      const d = xy.map(function(q, i){ return (i ? 'L' : 'M') + q[0].toFixed(1) + ' ' + q[1].toFixed(1); }).join(' ');
      f.svg.appendChild(svgEl('path', { class: 'ln ' + l.cls, d: xy.length === 1 ? d + ' h0.01' : d }));
      if (markers) xy.forEach(function(q){ f.svg.appendChild(svgEl('circle', { class: 'pt ' + l.cls, cx: q[0].toFixed(1), cy: q[1].toFixed(1), r: 3 })); });
    });
    endTime(f, pts);
    setLegend(layers.filter(function(l){ return !l.noLegend; }).map(function(l){ return { cls: l.color ? '' : l.cls, color: l.color, label: l.label }; }));
    setExportable(true);
  }

  function drawCandles(pts, key){
    const max = pts.reduce(function(a,p){ return Math.max(a, p[key].max); }, 0);
    if (max <= 0){ return muted(document.getElementById('chart'), S.empty); }
    const f = frame(pts.length);
    drawAxis(f, max);
    beginTime(f);
    const y = function(v){ return f.y0 - (v / max) * f.plotH; };
    pts.forEach(function(p, i){
      const d = p[key];
      if (!(d.count > 0) && d.max <= 0) return;
      const cx = f.x0 + i*f.bw + f.bw*0.5;
      const bx = f.x0 + i*f.bw + f.bw*0.3;
      const boxW = f.bw*0.4;
      // wick min..max
      f.svg.appendChild(svgEl('line', { class: 'candle-wick', x1: cx, y1: y(d.min).toFixed(1), x2: cx, y2: y(d.max).toFixed(1) }));
      // box avg..p95
      const top = Math.min(y(d.avg), y(d.p95)), bot = Math.max(y(d.avg), y(d.p95));
      const box = svgEl('rect', { class: 'candle-box', x: bx.toFixed(1), y: top.toFixed(1), width: boxW.toFixed(1), height: Math.max(1, bot - top).toFixed(1) });
      f.svg.appendChild(titled(box, bucketText(i)));
      // avg line
      f.svg.appendChild(svgEl('line', { class: 'candle-avg', x1: bx.toFixed(1), y1: y(d.avg).toFixed(1), x2: (bx+boxW).toFixed(1), y2: y(d.avg).toFixed(1) }));
      // p99 tick
      f.svg.appendChild(svgEl('line', { class: 'candle-p99', x1: bx.toFixed(1), y1: y(d.p99).toFixed(1), x2: (bx+boxW).toFixed(1), y2: y(d.p99).toFixed(1) }));
    });
    endTime(f, pts);
    setLegend([
      { color: 'var(--vscode-charts-green, #3fb950)', label: 'avg–p95' },
      { color: 'var(--vscode-descriptionForeground)', label: 'min–max' },
      { color: 'var(--vscode-foreground)', label: 'avg' },
      { color: 'var(--vscode-charts-red, #e5534b)', label: 'p99' },
    ]);
    setExportable(true);
  }

  // "Usage by <dimension>": one horizontal bar per value with tokens, cost and calls, sorted by the chosen metric.
  // Tokens stack the token types; Calls and Cost draw one bar of that metric.
  function drawBreakdown(){
    const chart = document.getElementById('chart');
    const rows = breakdownRows();
    if (rows.length === 0){ return muted(chart, S.empty); }
    const key = splitMetric === 'calls' ? 'calls' : (splitMetric === 'cost' ? 'cost' : 'tokens');
    const max = rows.reduce(function(a, r){ return Math.max(a, r[key] || 0); }, 0) || 1;
    const grid = document.createElement('div'); grid.className = 'brows';
    rows.forEach(function(r){
      const label = document.createElement('div'); label.className = 'brow-label'; label.textContent = r.label; label.title = r.label;
      const track = document.createElement('div'); track.className = 'brow-track';
      const bar = document.createElement('div'); bar.className = 'brow-bar';
      const parts = key === 'tokens' ? [['input', r.input], ['cached', r.cached], ['output', r.output]] : [['single', r[key] || 0]];
      parts.forEach(function(part){
        if (part[1] <= 0) return;
        const s = document.createElement('span'); s.className = part[0];
        s.style.width = ((part[1] / max) * 100).toFixed(2) + '%';
        s.title = r.label + ' — ' + (part[0] === 'single' ? S.tabs[key] + ': ' + (key === 'cost' ? '$' + part[1].toFixed(4) : part[1]) : S.legend[part[0]] + ': ' + part[1]);
        bar.appendChild(s);
      });
      const text = document.createElement('div'); text.className = 'brow-text'; text.textContent = r.text;
      track.appendChild(bar); track.appendChild(text);
      grid.appendChild(label); grid.appendChild(track);
    });
    chart.appendChild(grid);
    setLegend(key === 'tokens' ? [
      { color: 'var(--vscode-charts-blue, #4c8bf5)', label: S.legend.input },
      { color: 'var(--vscode-charts-yellow, #d7a100)', label: S.legend.cached },
      { color: 'var(--vscode-charts-green, #3fb950)', label: S.legend.output },
    ] : [{ color: 'var(--vscode-charts-blue, #4c8bf5)', label: S.tabs[key] }]);
    setExportable(true);
  }

  // Token heatmap on the shared clock-aligned grid: time runs left to right, each column one local period (labeled
  // along the top) and each row one step within it (labeled on the left); the month is a calendar of half-day
  // columns and week rows. Cells stretch to the panel's width (10-96 pixels); x labels thin so they never collide,
  // the first and last always labeled (the last right-aligned to the end). Cells outside the window (after now, or
  // before the month's or year's window) are blank. A cell lists its calls. thinLabels and heatmapSizes are the
  // host's own functions (usageCharts.ts), so the panel and its unit tests share one implementation.
  ${thinLabels.toString()}
  ${heatmapSizes.toString()}
  function drawHeatmap(){
    const chart = document.getElementById('chart');
    const hm = current.heatmap || { cells: [], max: 0, rows: 0, cols: 0 };
    if (!(hm.max > 0)){ return muted(chart, S.empty); }
    const head = document.createElement('strong'); head.textContent = S.heatmapTitle; chart.appendChild(head);
    const cap = document.createElement('div'); cap.className = 'hm-cap'; cap.textContent = hm.caption; chart.appendChild(cap);
    const longest = function(list){ return list.reduce(function(m, l){ return Math.max(m, String(l).length); }, 0); };
    const z = heatmapSizes(chart.clientWidth || 800, hm.cols, longest(hm.rowLabels), longest(hm.colLabels));
    const grid = document.createElement('div'); grid.className = 'heatmap';
    grid.setAttribute('role', 'grid'); grid.setAttribute('aria-label', hm.caption);
    grid.style.gridTemplateColumns = 'auto repeat(' + hm.cols + ', ' + z.colW + 'px)';
    grid.style.gridTemplateRows = 'auto repeat(' + hm.rows + ', ' + z.rowH + 'px)';
    const anchors = hm.anchors && hm.anchors.length ? hm.anchors : [0];
    const end = anchors[anchors.length - 1];
    thinLabels(anchors, hm.cols, hm.colGroup || 1, z.gap, hm.evenAnchors !== false).forEach(function(c, k){
      const last = k > 0 && c === end, span = Math.max(1, Math.min(Math.max(hm.colGroup || 1, z.gap), last ? hm.cols : hm.cols - c));
      const x = document.createElement('div'); x.className = last ? 'hm-x end' : 'hm-x'; x.textContent = hm.colLabels[c];
      x.style.gridRow = '1'; x.style.gridColumn = (last ? hm.cols + 2 - span : c + 2) + ' / span ' + span;
      grid.appendChild(x);
    });
    hm.rowLabels.forEach(function(label, r){
      if (z.rowH < 14 && r % 2 === 1 && r !== hm.rows - 1) return;
      const y = document.createElement('div'); y.className = 'hm-y'; y.textContent = label;
      y.style.gridRow = String(r + 2); y.style.gridColumn = '1';
      grid.appendChild(y);
    });
    hm.cells.forEach(function(c){
      const cell = document.createElement('div');
      cell.style.gridRow = String(c.row + 2); cell.style.gridColumn = String(c.col + 2);
      if (!c.blank){
        cell.className = 'hm l' + c.level + (selectedWindow === c.from ? ' sel' : '');
        cell.tabIndex = 0; cell.setAttribute('role', 'button'); cell.setAttribute('aria-label', c.label); cell.title = c.label;
        cell.addEventListener('click', function(){ openDrillWindow(c.from, c.to, c.label); });
        cell.addEventListener('keydown', function(e){ if (e.key === 'Enter' || e.key === ' '){ e.preventDefault(); openDrillWindow(c.from, c.to, c.label); } });
      }
      grid.appendChild(cell);
    });
    chart.appendChild(grid);
    document.getElementById('hint').textContent = S.heatmapHint;
    const note = document.getElementById('legend');
    note.textContent = '';
    const key = document.createElement('span');
    const none = document.createElement('span'); none.className = 'hm-key';
    const empty = document.createElement('span'); empty.className = 'hm l0'; none.appendChild(empty);
    key.appendChild(none); key.appendChild(document.createTextNode(S.legend.none + ' · ' + S.legend.less));
    const sw = document.createElement('span'); sw.className = 'hm-key';
    [1,2,3,4].forEach(function(l){ const s = document.createElement('span'); s.className = 'hm l' + l; sw.appendChild(s); });
    key.appendChild(sw); key.appendChild(document.createTextNode(S.legend.more + ' · ' + hm.note));
    note.appendChild(key);
    setExportable(true);
  }

  // Drill-down from a heatmap cell: the calls in that cell's window.
  function openDrillWindow(from, to, label){
    selected = -1; selectedWindow = from;
    drillSeq += 1;
    document.getElementById('drill').classList.remove('hidden');
    document.getElementById('drillTitle').textContent = label;
    document.getElementById('drillNote').textContent = S.loadingCalls;
    document.getElementById('drillTable').textContent = '';
    draw();
    vscode.postMessage({ type: 'drill', from: from, to: to, seq: drillSeq });
  }

  // Save PNG: redraws the chart and its legend as one standalone SVG (every box, text run and the chart's own SVG at
  // their on-screen positions, with the theme's computed colors written in, since the image cannot see the webview's
  // CSS variables), draws it onto a canvas at 2x and hands back a PNG data: URL.
  function captureSvg(root, title){
    const box = root.getBoundingClientRect();
    const head = title ? 30 : 0;
    const pad = 12, W = Math.ceil(box.width) + 2 * pad, H = Math.ceil(box.height) + head + pad;
    const out = document.createElementNS(NS, 'svg');
    function add(name, attrs, parent){ const e = document.createElementNS(NS, name); for (const k in attrs) e.setAttribute(k, attrs[k]); (parent || out).appendChild(e); return e; }
    out.setAttribute('xmlns', NS); out.setAttribute('width', String(W)); out.setAttribute('height', String(H)); out.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    function solid(c){ return !!c && c !== 'transparent' && !(c.indexOf('rgba(') === 0 && parseFloat(c.slice(c.lastIndexOf(',') + 1)) === 0); }
    const page = getComputedStyle(document.body), html = getComputedStyle(document.documentElement);
    add('rect', { x: 0, y: 0, width: W, height: H, fill: solid(page.backgroundColor) ? page.backgroundColor : (solid(html.backgroundColor) ? html.backgroundColor : '#ffffff') });
    if (title){ const t = add('text', { x: pad, y: 20, fill: page.color, 'font-family': page.fontFamily, 'font-size': 14, 'font-weight': 600 }); t.textContent = title; }
    const dx = pad - box.left, dy = head - box.top;
    function shown(cs){ return cs.display !== 'none' && cs.visibility !== 'hidden' && parseFloat(cs.opacity) !== 0; }
    function text(node, cs){
      const str = node.textContent.trim(); if (!str) return;
      const range = document.createRange(); range.selectNodeContents(node);
      const r = range.getBoundingClientRect(); if (!(r.width > 0)) return;
      const t = add('text', { x: (r.left + dx).toFixed(1), y: (r.top + dy + r.height / 2).toFixed(1), 'dominant-baseline': 'central', fill: cs.color, 'font-family': cs.fontFamily, 'font-size': cs.fontSize, 'font-weight': cs.fontWeight });
      t.textContent = str;
    }
    const PROPS = ['fill', 'stroke', 'stroke-width', 'stroke-opacity', 'fill-opacity', 'opacity', 'visibility', 'font-size', 'font-family', 'font-weight', 'text-anchor', 'dominant-baseline', 'vector-effect', 'stroke-linecap', 'stroke-linejoin'];
    function embed(svg, r){
      const copy = svg.cloneNode(true);
      const src = [svg].concat(Array.prototype.slice.call(svg.querySelectorAll('*')));
      const dst = [copy].concat(Array.prototype.slice.call(copy.querySelectorAll('*')));
      src.forEach(function(e, i){
        const cs = getComputedStyle(e), d = dst[i];
        PROPS.forEach(function(p){ const v = cs.getPropertyValue(p); if (v) d.setAttribute(p, v); });
        d.removeAttribute('class');
        if (cs.display === 'none' || e.tagName === 'title') d.setAttribute('display', 'none');
      });
      copy.removeAttribute('tabindex'); copy.removeAttribute('style');
      copy.setAttribute('x', (r.left + dx).toFixed(1)); copy.setAttribute('y', (r.top + dy).toFixed(1));
      copy.setAttribute('width', r.width.toFixed(1)); copy.setAttribute('height', r.height.toFixed(1));
      out.appendChild(copy);
    }
    function walk(el){
      const cs = getComputedStyle(el); if (!shown(cs)) return;
      const r = el.getBoundingClientRect();
      if (el.tagName.toLowerCase() === 'svg'){ if (r.width > 0) embed(el, r); return; }
      if (r.width > 0 && r.height > 0){
        if (solid(cs.backgroundColor)) add('rect', { x: (r.left + dx).toFixed(1), y: (r.top + dy).toFixed(1), width: r.width.toFixed(1), height: r.height.toFixed(1), rx: parseFloat(cs.borderTopLeftRadius) || 0, fill: cs.backgroundColor });
        [['Top', 0, 0, r.width, 0], ['Bottom', 0, r.height, r.width, r.height], ['Left', 0, 0, 0, r.height], ['Right', r.width, 0, r.width, r.height]].forEach(function(b){
          const w = parseFloat(cs['border' + b[0] + 'Width']) || 0;
          if (w > 0 && cs['border' + b[0] + 'Style'] !== 'none' && solid(cs['border' + b[0] + 'Color'])) add('line', { x1: (r.left + dx + b[1]).toFixed(1), y1: (r.top + dy + b[2]).toFixed(1), x2: (r.left + dx + b[3]).toFixed(1), y2: (r.top + dy + b[4]).toFixed(1), stroke: cs['border' + b[0] + 'Color'], 'stroke-width': w });
        });
      }
      Array.prototype.forEach.call(el.childNodes, function(c){ if (c.nodeType === 3) text(c, cs); else if (c.nodeType === 1) walk(c); });
    }
    walk(root);
    return { svg: out, width: W, height: H };
  }
  function rasterize(cap, done){
    const xml = new XMLSerializer().serializeToString(cap.svg);
    const img = new Image();
    img.onload = function(){
      const canvas = document.createElement('canvas');
      canvas.width = cap.width * 2; canvas.height = cap.height * 2;
      const g = canvas.getContext('2d'); g.scale(2, 2); g.drawImage(img, 0, 0, cap.width, cap.height);
      try { done(canvas.toDataURL('image/png')); } catch (e) { done(null); }
    };
    img.onerror = function(){ done(null); };
    img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
  }
  function chartPng(done){
    const tab = metricsEl.querySelector('[data-metric="' + metric + '"]');
    const title = 'mux · ' + (tab ? tab.textContent : metric) + ' · ' + (S.ranges[range] || range);
    rasterize(captureSvg(document.getElementById('capture'), title), done);
  }

  // Failure reasons for the Calls tab: error codes with counts and shares, most frequent first.
  function renderFailures(){
    const aside = document.getElementById('aside');
    const h = document.createElement('h2'); h.textContent = S.failureReasons; aside.appendChild(h);
    const items = current.failures || [];
    if (items.length === 0){ const d = document.createElement('div'); d.className = 'hint'; d.textContent = S.noFailures; aside.appendChild(d); return; }
    const ul = document.createElement('ul'); ul.className = 'reasons';
    items.forEach(function(f){
      const li = document.createElement('li');
      const code = document.createElement('code'); code.textContent = f.code;
      const count = document.createElement('span'); count.textContent = f.count + ' (' + f.share + ')';
      li.appendChild(code); li.appendChild(count); ul.appendChild(li);
    });
    aside.appendChild(ul);
  }

  // Drill-down: ask the host for the calls in bucket i's window and show a loading table until they arrive.
  function openDrill(i){
    const p = current && current.series[i];
    if (!p) return;
    selected = i; selectedWindow = null; moveCursor();
    drillSeq += 1;
    document.getElementById('drill').classList.remove('hidden');
    document.getElementById('drillTitle').textContent = p.label;
    document.getElementById('drillNote').textContent = S.loadingCalls;
    document.getElementById('drillTable').textContent = '';
    vscode.postMessage({ type: 'drill', from: p.from, to: p.to, seq: drillSeq });
  }

  function closeDrill(){
    const fromHeatmap = selectedWindow !== null;
    selected = -1; selectedWindow = null; drillSeq += 1;
    if (fromHeatmap) draw();
    document.getElementById('drill').classList.add('hidden');
    moveCursor();
    if (plot) plot.svg.focus();
  }
  document.getElementById('drillClose').addEventListener('click', closeDrill);
  document.addEventListener('keydown', function(e){ if (e.key === 'Escape' && (selected >= 0 || selectedWindow !== null)) closeDrill(); });

  function renderDrill(d){
    if (d.seq !== drillSeq) return;
    document.getElementById('drillTitle').textContent = d.title;
    document.getElementById('drillNote').textContent = d.note;
    const table = document.getElementById('drillTable'); table.textContent = '';
    if (!d.rows.length) return;
    const head = document.createElement('tr');
    S.columns.forEach(function(c, i){ const th = document.createElement('th'); th.textContent = c; if (i === 3 || i === 4) th.className = 'num'; head.appendChild(th); });
    table.appendChild(head);
    d.rows.forEach(function(r){
      const tr = document.createElement('tr');
      [r.time, r.model, r.endpoint, r.tokens, r.latency, r.status].forEach(function(v, i){
        const td = document.createElement('td'); td.textContent = v;
        if (i === 3 || i === 4) td.className = 'num';
        if (i === 5 && r.failed) td.className = 'fail';
        tr.appendChild(td);
      });
      table.appendChild(tr);
    });
  }

  window.addEventListener('message', function(e){
    const msg = e.data || {};
    if (msg.type === 'drill') return renderDrill(msg);
    current = msg; range = current.range || range; syncActive(rangesEl, 'range', range);
    endpoint = current.endpoint || ''; model = current.model || ''; dimension = current.dimension || dimension;
    splitMetric = current.splitMetric || splitMetric; mode = current.mode || mode;
    dimensionEl.value = dimension; splitMetricEl.value = splitMetric; syncMode(); labelTabs();
    fillSelect(endpointEl, S.filters.allEndpoints, current.endpoints, endpoint);
    fillSelect(modelEl, S.filters.allModels, current.models, model);
    renderKpis(current.kpis || []);
    // New data means new buckets, so a drill-down from the previous load no longer applies.
    cursor = -1;
    if (selected >= 0 || selectedWindow !== null){ selected = -1; selectedWindow = null; drillSeq += 1; document.getElementById('drill').classList.add('hidden'); }
    draw();
  });
</script>
</body>
</html>`;
    }
}

// A compact x-axis label for a bucket start in the range's label format.
function axisLabel(unixMs: number, kind: AxisLabelKind): string {
    const d = new Date(unixMs);
    switch (kind) {
        case 'time':
            return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        case 'weekday-time':
            return `${d.toLocaleDateString([], { weekday: 'short' })} ${d.toLocaleTimeString([], { hour: '2-digit' })}`;
        case 'date-time':
            return `${d.toLocaleDateString([], { month: 'numeric', day: 'numeric' })} ${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
        default:
            return d.toLocaleDateString([], { month: 'numeric', day: 'numeric' });
    }
}

// A bucket's window for tooltips and the drill-down title: "date time – time" for sub-day buckets, the date for a
// one-day bucket, and "date – date" for longer ones.
function windowLabel(from: number, to: number, widthMs: number): string {
    const start = new Date(from);
    const dateOpts: Intl.DateTimeFormatOptions = { year: 'numeric', month: 'short', day: 'numeric' };
    if (widthMs >= DAY_MS) {
        const first = start.toLocaleDateString([], dateOpts);
        return widthMs <= DAY_MS ? first : `${first} – ${new Date(to).toLocaleDateString([], dateOpts)}`;
    }
    const time: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' };
    return `${start.toLocaleDateString([], dateOpts)} ${start.toLocaleTimeString([], time)} – ${new Date(to + 1).toLocaleTimeString([], time)}`;
}

// One drill-down table row from a raw call.
function eventRow(e: UsageEventRow, widthMs: number): DrillPayload['rows'][number] {
    const at = new Date(e.TimestampUnixMs);
    const failed = !e.Success;
    let status = vscode.l10n.t('Succeeded');
    if (failed) {
        status = e.ErrorCode ? vscode.l10n.t('Failed ({0})', e.ErrorCode) : vscode.l10n.t('Failed');
    }
    return {
        time: widthMs >= DAY_MS ? at.toLocaleString() : at.toLocaleTimeString(),
        model: e.Model || '—',
        endpoint: e.EndpointName || '—',
        tokens: formatCount(e.TotalTokens ?? 0),
        latency: typeof e.TotalMs === 'number' ? `${formatCount(e.TotalMs)} ms` : '—',
        status,
        failed,
    };
}

function dist(d: UsageDistribution | undefined): Dist {
    if (!d) {
        return { min: 0, avg: 0, p95: 0, p99: 0, max: 0, count: 0 };
    }
    return { min: d.Min ?? 0, avg: d.Avg ?? 0, p95: d.P95 ?? 0, p99: d.P99 ?? 0, max: d.Max ?? 0, count: d.Count ?? 0 };
}
