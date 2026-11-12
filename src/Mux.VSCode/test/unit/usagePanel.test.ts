import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as vm from 'node:vm';

// Loads the usage panel against a minimal fake of the VS Code API, so the host's data shaping, message handling
// and the generated webview script can be checked without an extension host.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const Module = require('node:module');

interface FakePanel {
    html: string;
    posted: any[];
    handler: ((message: unknown) => void) | undefined;
    /** Files written through workspace.fs, by path. */
    written?: Record<string, Buffer>;
    /** What the save dialog returns (undefined = cancelled), and the options it was opened with. */
    saveTo?: string;
    saveOptions?: any[];
    info?: string[];
}

function fakeVscode(panel: FakePanel) {
    return {
        l10n: { t: (message: string, ...args: unknown[]) => message.replace(/\{(\d+)\}/g, (_m, i) => String(args[Number(i)])) },
        ViewColumn: { Active: -1 },
        Uri: {
            file: (p: string) => ({ fsPath: p, path: p }),
            joinPath: (base: { fsPath: string }, ...parts: string[]) => ({ fsPath: [base.fsPath, ...parts].join('/'), path: [base.fsPath, ...parts].join('/') }),
        },
        workspace: {
            workspaceFolders: undefined,
            fs: { writeFile: async (uri: { fsPath: string }, bytes: Uint8Array) => { (panel.written ??= {})[uri.fsPath] = Buffer.from(bytes); } },
        },
        CancellationTokenSource: class { public token = {}; },
        window: {
            showSaveDialog: async (options: unknown) => { (panel.saveOptions ??= []).push(options); return panel.saveTo ? { fsPath: panel.saveTo } : undefined; },
            showInformationMessage: async (text: string) => { (panel.info ??= []).push(text); return undefined; },
            showErrorMessage: async (text: string) => { (panel.info ??= []).push('error: ' + text); return undefined; },
            createWebviewPanel: () => ({
                webview: {
                    set html(value: string) { panel.html = value; },
                    get html() { return panel.html; },
                    postMessage: async (message: unknown) => { panel.posted.push(message); return true; },
                    onDidReceiveMessage: (handler: (message: unknown) => void) => { panel.handler = handler; },
                },
                onDidDispose: () => undefined,
                reveal: () => undefined,
            }),
        },
    };
}

function loadPanel(panel: FakePanel): any {
    const original = Module._load;
    Module._load = function (request: string, ...rest: unknown[]) {
        if (request === 'vscode') {
            return fakeVscode(panel);
        }
        return original.call(this, request, ...rest);
    };
    try {
        const resolved = require.resolve('../../src/manage/UsagePanel');
        delete require.cache[resolved];
        return require(resolved).UsagePanel;
    } finally {
        Module._load = original;
    }
}

const t0 = Date.UTC(2026, 0, 1);
const HOUR = 3_600_000;
const dist = { Min: 1, Avg: 2, P95: 3, P99: 4, Max: 5, Count: 1 };
function metrics(calls: number, errors: number, tokens: number) {
    return {
        Calls: calls, Errors: errors, ErrorRate: calls ? errors / calls : 0, InputTokens: tokens, CachedTokens: 0, OutputTokens: 0, TotalTokens: tokens,
        CostUsd: 0.5, AvgTtftMs: 10, AvgTotalMs: 20, AvgStreamMs: 5, AvgTokensPerSec: 50, TotalMsDist: dist, TtftMsDist: dist, StreamMsDist: dist, ThroughputDist: dist,
    };
}

function fakeClient(calls: string[]) {
    return {
        getUsageSummary: async (range: string) => { calls.push('summary:' + range); return { FromUnixMs: 0, ToUnixMs: 0, Metrics: metrics(8, 2, 300) }; },
        getUsageTimeseries: async () => [0, 1, 2].map((i) => ({ BucketStartUnixMs: t0 + i * 6 * HOUR, Metrics: metrics(4, i === 1 ? 2 : 0, 100 * i) })),
        getUsageFilters: async () => ({ Enabled: true, Endpoints: ['e1'], Models: ['m1'] }),
        getUsageErrors: async () => [{ ErrorCode: '', Count: 2 }],
        getUsageSplit: async (_r: string, dimension: string) => {
            calls.push('split:' + dimension);
            return [{ Key: 'm1', IsOther: false, Buckets: [0, 1, 2].map((i) => ({ BucketStartUnixMs: t0 + i * 6 * HOUR, Metrics: metrics(1, 0, 10) })) }];
        },
        getUsageBreakdown: async () => { throw new Error('400'); },
        getUsageHeatmap: async (range: string, tz: number) => {
            calls.push('heatmap:' + range + ':' + tz);
            // A day of four 6-hour cells in one column, as Mux.Core lays out all time: rows are steps down the column.
            return {
                Layout: {
                    Range: range, NowUnixMs: t0 + 17 * HOUR, UtcOffsetMinutes: 0, FromUnixMs: t0, WindowFromUnixMs: t0, ToUnixMs: t0 + 24 * HOUR, BucketMs: 6 * HOUR,
                    Rows: 4, Columns: 1, ColumnGroup: 1, CellCount: 4, ColumnKind: 'weekdate', RowKind: 'clock', ColumnUnit: 'period',
                    ColumnTimesUnixMs: [t0], RowTimesUnixMs: [0, 1, 2, 3].map((r) => t0 + r * 6 * HOUR), ColumnLabels: ['Thu 1/1'], RowLabels: ['00:00', '06:00', '12:00', '18:00'],
                    ColumnLabelAnchors: [0], CellStartsUnixMs: [0, 1, 2, 3].map((r) => t0 + r * 6 * HOUR), OutsideWindow: [false, false, false, true],
                    Caption: 'Columns: period starting · Rows: time of day',
                },
                TotalTokens: [0, 100, 200, 0], Calls: [0, 1, 2, 0],
            };
        },
        getUsageEvents: async (query: string) => {
            calls.push('events:' + query);
            return { Items: [{ Id: 1, TimestampUnixMs: t0, CallKind: 'Primary', EndpointName: 'e1', Model: 'm1', InputTokens: 1, CachedTokens: 0, OutputTokens: 1, TotalTokens: 2, TotalMs: 1234, Success: false, ErrorCode: 'timeout', CostUsd: 0 }], TotalCount: 75, PageNumber: 1, PageSize: 50 };
        },
    };
}

async function settle(): Promise<void> {
    for (let i = 0; i < 10; i++) {
        await new Promise((resolve) => setImmediate(resolve));
    }
}

test('usage panel posts an All series with derived bucket windows, failure reasons and error rate', async () => {
    const panel: FakePanel = { html: '', posted: [], handler: undefined };
    const UsagePanel = loadPanel(panel);
    const calls: string[] = [];
    UsagePanel.show({ getClient: async () => fakeClient(calls) });
    await settle();
    panel.handler!({ type: 'reload', range: 'all', endpoint: '', model: 'm1', dimension: 'provider' });
    await settle();

    const payload = panel.posted[panel.posted.length - 1];
    assert.equal(payload.type, 'usage');
    assert.equal(payload.range, 'all');
    assert.equal(payload.dimension, 'provider');
    assert.ok(calls.includes('split:provider'));
    assert.deepEqual(payload.series.map((p: any) => [p.from, p.to]), [0, 1, 2].map((i) => [t0 + i * 6 * HOUR, t0 + (i + 1) * 6 * HOUR - 1]));
    assert.deepEqual(payload.series.map((p: any) => [p.ok, p.failed, p.errorRate]), [[4, 0, '0.0%'], [2, 2, '50.0%'], [4, 0, '0.0%']]);
    assert.deepEqual(payload.failures, [{ code: 'unknown', count: 2, share: '100.0%' }]);
    assert.deepEqual(payload.kpis.find((k: any) => k.label === 'Error rate'), { label: 'Error rate', value: '25.0%' });
    assert.equal(payload.split.length, 1);
    assert.deepEqual(payload.breakdown, [], 'a failed breakdown query degrades to an empty chart');
    assert.ok(calls.includes('heatmap:all:' + new Date().getTimezoneOffset()), 'heatmap route with the JavaScript UTC offset');
    assert.equal(payload.heatmap.cells.length, 4);
    assert.deepEqual(payload.heatmap.cells.map((c: any) => c.level), [0, 1, 4, 0], "quartile shades: the smaller used cell is the lowest step, the peak the highest");
    assert.deepEqual(payload.heatmap.cells.map((c: any) => c.future), [false, false, false, true], 'cells after now are future');
    assert.deepEqual(payload.heatmap.cells.map((c: any) => c.blank), [false, false, false, true], 'only future cells are blank in a full window');
    assert.equal(payload.heatmap.colGroup, 1, 'one label per column without a column group');
    assert.equal(payload.heatmap.caption, 'Columns: period starting · Rows: time of day');
    assert.deepEqual(payload.heatmap.rowLabels, ['00:00', '06:00', '12:00', '18:00'], 'every row labeled on the y-axis');
    assert.deepEqual(payload.heatmap.colLabels, ['Thu 1/1'], 'the column is localized from its instant');
    assert.deepEqual(payload.heatmap.anchors, [0]);
    assert.equal(payload.heatmap.evenAnchors, true);
    assert.deepEqual(payload.heatmap.cells.map((c: any) => [c.row, c.col]), [[0, 0], [1, 0], [2, 0], [3, 0]], 'rows x columns from the layout');
    assert.deepEqual([payload.heatmap.cells[1].from, payload.heatmap.cells[1].to], [t0 + 6 * HOUR, t0 + 12 * HOUR - 1], 'a cell drills into its window');
    assert.match(payload.heatmap.cells[2].label, /200 tokens · 2 calls/);
});

test('usage panel drill-down lists the bucket calls with the current filters', async () => {
    const panel: FakePanel = { html: '', posted: [], handler: undefined };
    const UsagePanel = loadPanel(panel);
    const calls: string[] = [];
    UsagePanel.show({ getClient: async () => fakeClient(calls) });
    await settle();
    panel.handler!({ type: 'reload', range: 'quarter', endpoint: 'e1', model: '', dimension: 'model' });
    await settle();
    panel.handler!({ type: 'drill', from: 1000, to: 1999, seq: 7 });
    await settle();

    assert.ok(calls.includes('events:from=1000&to=1999&page=1&pageSize=50&endpoint=e1'));
    const drill = panel.posted[panel.posted.length - 1];
    assert.equal(drill.type, 'drill');
    assert.equal(drill.seq, 7);
    assert.equal(drill.rows.length, 1);
    assert.equal(drill.rows[0].status, 'Failed (timeout)');
    assert.equal(drill.rows[0].latency, '1,234 ms');
    assert.equal(drill.rows[0].failed, true);
    assert.equal(drill.note, 'Showing 1 of 75 calls.');
});

test('usage panel webview script parses and lists all seven ranges and ten tabs', () => {
    const panel: FakePanel = { html: '', posted: [], handler: undefined };
    const UsagePanel = loadPanel(panel);
    UsagePanel.show({ getClient: async () => { throw new Error('offline'); } });
    const scripts = [...panel.html.matchAll(/<script nonce="[0-9a-f]+">([\s\S]*?)<\/script>/g)].map((m) => m[1]);
    assert.equal(scripts.length, 1);
    assert.doesNotThrow(() => new vm.Script(scripts[0]));
    assert.match(scripts[0], /\['hour','day','week','month','quarter','year','all'\]/);
    assert.match(scripts[0], /\['calls','tokens','split','cost','latency','ttft','streaming','throughput','breakdown','heatmap'\]/);
    assert.ok(!/style="/.test(panel.html), 'no inline style attributes (blocked by the CSP)');
    assert.ok(!/\.hm\.l0[^}]*(inset|border)/.test(panel.html), 'no-usage cells have no outline');
    assert.match(panel.html, /--hm0: #e3e7eb/, 'light no-usage grey');
    assert.match(panel.html, /--hm0: #2e3640/, 'dark no-usage grey');
    assert.match(scripts[0], /openDrillWindow\(c\.from, c\.to, c\.label\)/, 'heatmap cells drill into their window');
    assert.match(scripts[0], /className = 'hm-x';/, 'column labels, one alignment for all');
    assert.match(scripts[0], /x\.style\.gridColumn = String\(col \+ 2\)/, 'each label starts at its own column');
    assert.ok(!/hm-x\.(center|end)/.test(panel.html), 'no centred or end-aligned labels');
    assert.match(scripts[0], /className = 'hm-y'/, 'row labels');
    assert.match(scripts[0], /function placeLabels\(/, "the host's label placement runs in the webview");
    assert.match(scripts[0], /function heatmapSizes\(/, "the host's cell sizing runs in the webview");
    assert.match(scripts[0], /addEventListener\('resize'/, 'the heatmap redraws on resize');
    assert.match(panel.html, /<select id="splitMetric"><\/select>\s*<select id="dimension"><\/select>/, 'the metric picker sits next to the dimension picker');
    assert.match(panel.html, /<div class="seg" id="mode" role="group"><\/div>/, 'lines/bars toggle');
    assert.match(panel.html, /id="exportCsv"/);
    assert.match(panel.html, /id="exportPng"/);
    assert.match(panel.html, /img-src data:;/, 'Save PNG may draw its SVG data: URL onto a canvas');
    assert.match(scripts[0], /const STACK_TABS = \{ calls: 1, tokens: 1, split: 1, cost: 1 \};/, 'the toggle applies to the stacked charts only');
    assert.match(scripts[0], /if \(mode === 'lines'\) return drawLines\(pts, layers\);/);
    assert.match(scripts[0], /canvas\.width = cap\.width \* 2; canvas\.height = cap\.height \* 2;/, 'PNG at 2x');
    assert.match(scripts[0], /type: 'exportCsv', tab: metric/);
    assert.match(scripts[0], /type: 'exportPng', tab: metric/);
    assert.match(scripts[0], /exportCsvEl\.disabled = !on; exportPngEl\.disabled = !on;/, 'charts with no data disable export');
    const strings = JSON.parse(/const S = (\{.*\});/.exec(scripts[0])![1]);
    assert.deepEqual(strings.tabs.split, { tokens: 'Tokens by {0}', calls: 'Calls by {0}', cost: 'Cost by {0}' }, 'the tab title follows both pickers');
    assert.deepEqual(strings.metrics.map((m: any) => m.key), ['tokens', 'calls', 'cost']);
});

test('usage panel posts every metric of the split and breakdown and remembers the metric and mode', async () => {
    const panel: FakePanel = { html: '', posted: [], handler: undefined };
    const UsagePanel = loadPanel(panel);
    const calls: string[] = [];
    UsagePanel.show({ getClient: async () => ({ ...fakeClient(calls), getUsageBreakdown: async () => [
        { Dimension: 'model', Value: 'cheap', Metrics: metrics(9, 0, 5000) },
        { Dimension: 'model', Value: 'pricey', Metrics: { ...metrics(1, 0, 10), CostUsd: 3 } },
    ] }) });
    await settle();
    let payload = panel.posted[panel.posted.length - 1];
    assert.equal(payload.splitMetric, 'tokens');
    assert.equal(payload.mode, 'bars');
    assert.deepEqual(Object.keys(payload.splits), ['tokens', 'calls', 'cost']);
    assert.deepEqual(payload.splits.calls[0].values, [1, 1, 1], 'calls per bucket');
    assert.deepEqual(payload.splits.cost[0].values, [0.5, 0.5, 0.5], 'cost per bucket');
    assert.deepEqual(payload.breakdowns.tokens.map((b: any) => b.label), ['cheap', 'pricey']);
    assert.deepEqual(payload.breakdowns.cost.map((b: any) => b.label), ['pricey', 'cheap'], 'Usage by sorts by the chosen metric');

    panel.handler!({ type: 'prefs', splitMetric: 'cost', mode: 'lines' });
    panel.handler!({ type: 'reload', range: 'day', endpoint: '', model: '', dimension: 'model' });
    await settle();
    payload = panel.posted[panel.posted.length - 1];
    assert.equal(payload.splitMetric, 'cost');
    assert.equal(payload.mode, 'lines');
    assert.deepEqual(payload.split, payload.splits.cost, 'the current metric is posted as the split');
});

test('usage panel exports CSV and PNG through the save dialog and workspace.fs', async () => {
    const panel: FakePanel = { html: '', posted: [], handler: undefined, saveTo: '/tmp/out.csv' };
    const UsagePanel = loadPanel(panel);
    const calls: string[] = [];
    UsagePanel.show({ getClient: async () => fakeClient(calls) });
    await settle();
    panel.handler!({ type: 'exportCsv', tab: 'split', splitMetric: 'calls' });
    await settle();
    const opts = panel.saveOptions![0];
    assert.match(opts.defaultUri.fsPath, /mux-usage-calls-by-model-day-\d{8}-\d{6}\.csv$/);
    assert.deepEqual(Object.values(opts.filters), [['csv']]);
    const csv = panel.written!['/tmp/out.csv'].toString('utf8');
    assert.match(csv, /^bucket_start,bucket_end,m1\n/);
    assert.equal(csv.split('\n').length, 5, 'header, three buckets, final newline');
    assert.match(csv.split('\n')[1], /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d,[^,]+,1$/);
    assert.equal(panel.info![0], 'Saved /tmp/out.csv');

    panel.saveTo = '/tmp/chart.png';
    const png = Buffer.from([0x89, 0x50, 0x4e, 0x47]);
    panel.handler!({ type: 'exportPng', tab: 'heatmap', dataUrl: 'data:image/png;base64,' + png.toString('base64') });
    await settle();
    assert.match(panel.saveOptions![1].defaultUri.fsPath, /mux-usage-heatmap-day-\d{8}-\d{6}\.png$/);
    assert.deepEqual([...panel.written!['/tmp/chart.png']], [...png]);

    panel.handler!({ type: 'exportPng', tab: 'tokens', dataUrl: 'javascript:alert(1)' });
    panel.handler!({ type: 'exportCsv', tab: 'breakdown' });
    await settle();
    assert.equal(panel.saveOptions!.length, 2, 'a bad image or an empty chart never opens the dialog');
    assert.deepEqual(panel.info!.slice(-2), ['This chart has no data to export.', 'This chart has no data to export.']);

    panel.saveTo = undefined;
    panel.handler!({ type: 'exportCsv', tab: 'calls' });
    await settle();
    assert.equal(Object.keys(panel.written!).length, 2, 'cancelling the dialog writes nothing');
});

test('every usage panel string has a translation in every runtime l10n bundle', () => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const fs = require('node:fs');
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const path = require('node:path');
    const root = path.join(__dirname, '..', '..', '..');
    const source: string = fs.readFileSync(path.join(root, 'src', 'manage', 'UsagePanel.ts'), 'utf8');
    const keys = [...new Set([...source.matchAll(/l10n\.t\('((?:[^'\\]|\\.)*)'/g)].map((m) => m[1]))];
    assert.ok(keys.length > 30);
    for (const file of fs.readdirSync(path.join(root, 'l10n'))) {
        const bundle = JSON.parse(fs.readFileSync(path.join(root, 'l10n', file), 'utf8'));
        const missing = keys.filter((k) => typeof bundle[k] !== 'string' || bundle[k].length === 0);
        assert.deepEqual(missing, [], file + ' is missing usage panel strings');
    }
});
