import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
    BREAKDOWN_LIMIT,
    USAGE_DIMENSIONS,
    USAGE_RANGES,
    axisLabelKind,
    breakdownBars,
    bucketWidthMs,
    callParts,
    csvField,
    csvNumber,
    csvTime,
    drillWindow,
    eventsQuery,
    failureReasons,
    formatPercent,
    formatTokensCompact,
    formatUsd,
    heatmapCellWindow,
    heatmapColumnLabel,
    heatmapGrid,
    heatmapRowLabel,
    heatmapSizes,
    labeledIndices,
    heatmapScale,
    isUsageRange,
    metricValue,
    nominalBucketMs,
    normalizeChartMode,
    normalizeDimension,
    normalizeRange,
    normalizeSplitMetric,
    stackSplitSeries,
    stackedMax,
    placeLabels,
    tokenParts,
    usageCsv,
    usageCsvTable,
    usageExportChartName,
    usageExportFileName,
} from '../../src/manage/usageCharts';

const MIN = 60_000;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;

test('the seven range keys are in display order and normalize safely', () => {
    assert.deepEqual([...USAGE_RANGES], ['hour', 'day', 'week', 'month', 'quarter', 'year', 'all']);
    assert.ok(isUsageRange('quarter'));
    assert.ok(!isUsageRange('decade'));
    assert.equal(normalizeRange('YEAR'), 'year');
    assert.equal(normalizeRange(' all '), 'all');
    assert.equal(normalizeRange('bogus'), 'day');
    assert.equal(normalizeRange(undefined), 'day');
});

test('dimension keys default to model and accept loose spellings of call kind', () => {
    assert.deepEqual([...USAGE_DIMENSIONS], ['model', 'endpoint', 'provider', 'callkind']);
    assert.equal(normalizeDimension('Provider'), 'provider');
    assert.equal(normalizeDimension('call_kind'), 'callkind');
    assert.equal(normalizeDimension('Call kind'), 'callkind');
    assert.equal(normalizeDimension('label'), 'model');
    assert.equal(normalizeDimension(undefined), 'model');
});

test('nominal bucket widths match the spec grid', () => {
    assert.equal(nominalBucketMs('hour'), MIN);
    assert.equal(nominalBucketMs('day'), 15 * MIN);
    assert.equal(nominalBucketMs('week'), 2 * HOUR);
    assert.equal(nominalBucketMs('month'), 12 * HOUR);
    assert.equal(nominalBucketMs('quarter'), DAY);
    assert.equal(nominalBucketMs('year'), 7 * DAY);
    assert.equal(nominalBucketMs('all'), 15 * MIN);
});

test('bucket width comes from the first two bucket starts, so All uses its variable width', () => {
    const t0 = 1_700_000_000_000;
    assert.equal(bucketWidthMs([t0, t0 + 6 * HOUR, t0 + 12 * HOUR], 'all'), 6 * HOUR);
    assert.equal(bucketWidthMs([t0, t0 + 7 * DAY], 'all'), 7 * DAY);
    assert.equal(bucketWidthMs([t0, t0 + 15 * MIN], 'day'), 15 * MIN);
});

test('bucket width falls back to the window for one bucket and the nominal width for none', () => {
    const t0 = 1_700_000_000_000;
    assert.equal(bucketWidthMs([t0], 'all', t0 + 3 * HOUR), 3 * HOUR);
    assert.equal(bucketWidthMs([t0], 'all', t0 + 10), MIN, 'at least a minute');
    assert.equal(bucketWidthMs([t0], 'hour'), HOUR);
    assert.equal(bucketWidthMs([], 'all'), 15 * MIN, 'empty All falls back to the Day grid');
    assert.equal(bucketWidthMs([], 'year'), 7 * DAY);
    assert.equal(bucketWidthMs([t0, t0], 'week'), 2 * HOUR, 'a non-increasing pair is ignored');
});

test('axis labels use dates for long ranges and adapt for All', () => {
    assert.equal(axisLabelKind('hour', MIN, 60), 'time');
    assert.equal(axisLabelKind('day', 15 * MIN, 96), 'time');
    assert.equal(axisLabelKind('week', 2 * HOUR, 84), 'weekday-time');
    assert.equal(axisLabelKind('month', 12 * HOUR, 60), 'date');
    assert.equal(axisLabelKind('quarter', DAY, 91), 'date');
    assert.equal(axisLabelKind('year', 7 * DAY, 52), 'date');
    assert.equal(axisLabelKind('all', 30 * MIN, 90), 'date-time', '90 x 30m is more than a day');
    assert.equal(axisLabelKind('all', 5 * MIN, 90), 'time', '90 x 5m fits in a day');
    assert.equal(axisLabelKind('all', 6 * HOUR, 90), 'date-time');
    assert.equal(axisLabelKind('all', 7 * DAY, 90), 'date');
});

test('the drill-down window is the bucket start through start + width - 1', () => {
    assert.deepEqual(drillWindow(1000, 15 * MIN), { from: 1000, to: 1000 + 15 * MIN - 1 });
    assert.deepEqual(drillWindow(5, 0), { from: 5, to: 5 }, 'width is at least 1 ms');
});

test('the events query carries the window, page size 50 and the panel filters', () => {
    assert.equal(eventsQuery({ from: 100, to: 199 }), 'from=100&to=199&page=1&pageSize=50');
    assert.equal(
        eventsQuery({ from: 100, to: 199 }, 'my endpoint', 'gpt/4o&x'),
        'from=100&to=199&page=1&pageSize=50&endpoint=my%20endpoint&model=gpt%2F4o%26x',
    );
    assert.equal(eventsQuery({ from: 1, to: 2 }, '', undefined, 10), 'from=1&to=2&page=1&pageSize=10');
});

test('token parts floor uncached input at zero', () => {
    assert.deepEqual(tokenParts({ InputTokens: 100, CachedTokens: 30, OutputTokens: 5 }), { input: 70, cached: 30, output: 5 });
    assert.deepEqual(tokenParts({ InputTokens: 10, CachedTokens: 30, OutputTokens: 0 }), { input: 0, cached: 30, output: 0 });
    assert.deepEqual(tokenParts(undefined), { input: 0, cached: 0, output: 0 });
});

test('call parts split succeeded and failed and compute the error rate', () => {
    assert.deepEqual(callParts({ Calls: 8, Errors: 2 }), { ok: 6, failed: 2, errorRate: 0.25 });
    assert.deepEqual(callParts({ Calls: 0, Errors: 0 }), { ok: 0, failed: 0, errorRate: 0 });
    assert.deepEqual(callParts({ Calls: 1, Errors: 5 }), { ok: 0, failed: 1, errorRate: 1 }, 'errors never exceed calls');
    assert.equal(formatPercent(0.25), '25.0%');
    assert.equal(formatPercent(NaN), '0.0%');
});

function series(key: string, tokens: number[], isOther = false) {
    return { Key: key, IsOther: isOther, Buckets: tokens.map((t, i) => ({ BucketStartUnixMs: i, Metrics: { TotalTokens: t } })) };
}

test('split stacking keeps rank colors and puts a grey Other last', () => {
    const stacked = stackSplitSeries([series('Other', [1, 1], true), series('a', [5, 0]), series('b', [0, 3])], 2);
    assert.deepEqual(stacked.map((s) => [s.key, s.color, s.isOther]), [['a', 0, false], ['b', 1, false], ['Other', -1, true]]);
    assert.deepEqual(stacked[2].values, [1, 1]);
    assert.equal(stackedMax(stacked, 2), 6);
});

test('split stacking folds series beyond the top into Other and aligns bucket counts', () => {
    const input = ['a', 'b', 'c', 'd', 'e', 'f', 'g'].map((k) => series(k, [1, 2]));
    input.push(series('Other', [10, 10], true));
    const stacked = stackSplitSeries(input, 3);
    assert.equal(stacked.length, 7);
    assert.deepEqual(stacked.slice(0, 6).map((s) => s.color), [0, 1, 2, 3, 4, 5]);
    assert.deepEqual(stacked[6], { key: 'Other', isOther: true, color: -1, values: [11, 12, 0] });
});

test('split stacking drops empty series without shifting the remaining colors', () => {
    const stacked = stackSplitSeries([series('a', [0, 0]), series('b', [2, 0]), series('Other', [0, 0], true)], 2);
    assert.deepEqual(stacked.map((s) => [s.key, s.color]), [['b', 1]]);
    assert.deepEqual(stackSplitSeries([], 4), []);
});

test('failure reasons map blank codes to unknown, merge duplicates and sort by count', () => {
    const rows = failureReasons([
        { ErrorCode: 'timeout', Count: 2 },
        { ErrorCode: '', Count: 1 },
        { ErrorCode: 'http_500', Count: 5 },
        { ErrorCode: 'unknown', Count: 2 },
        { ErrorCode: 'ignored', Count: 0 },
    ]);
    assert.deepEqual(rows, [
        { code: 'http_500', count: 5, share: '50.0%' },
        { code: 'unknown', count: 3, share: '30.0%' },
        { code: 'timeout', count: 2, share: '20.0%' },
    ]);
    assert.deepEqual(failureReasons([]), []);
});

test('breakdown bars sort by cost then tokens and cap at ten rows', () => {
    const rows = Array.from({ length: 12 }, (_, i) => ({ Value: 'v' + i, Metrics: { Calls: 1, InputTokens: 10 + i, CachedTokens: 4, OutputTokens: 2, CostUsd: i % 3 } }));
    const bars = breakdownBars(rows, undefined, 'cost');
    assert.equal(bars.length, BREAKDOWN_LIMIT);
    assert.deepEqual(bars.slice(0, 4).map((b) => b.value), ['v11', 'v8', 'v5', 'v2']);
    assert.deepEqual({ input: bars[0].input, cached: bars[0].cached, output: bars[0].output, tokens: bars[0].tokens }, { input: 17, cached: 4, output: 2, tokens: 23 });
    assert.deepEqual(breakdownBars([{ Value: '', Metrics: { Calls: 0 } }, { Value: ' ', Metrics: { Calls: 2 } }]).map((b) => b.value), ['unknown']);
});

// A layout shaped like Mux.Core's: `cols` columns of local periods, each `rows` cells of `bucket` (time runs down a
// column, then to the next), cells row-major in display order; or with `across: false` the month calendar's order
// (time runs along a row, then to the next).
function layout(over: Record<string, unknown> = {}, shape: { from?: number; rows?: number; cols?: number; bucket?: number; now?: number; windowFrom?: number; across?: boolean } = {}) {
    const from = shape.from ?? Date.UTC(2026, 10, 9, 5, 0); // Monday 00:00 at UTC-5
    const rows = shape.rows ?? 7;
    const cols = shape.cols ?? 2;
    const bucket = shape.bucket ?? DAY;
    const now = shape.now ?? from + 9 * DAY + HOUR;
    const windowFrom = shape.windowFrom ?? from;
    const across = shape.across ?? true;
    const starts: number[] = [];
    for (let i = 0; i < rows * cols; i++) {
        const r = Math.floor(i / cols), c = i % cols;
        starts.push(from + (across ? c * rows + r : i) * bucket);
    }
    return {
        NowUnixMs: now, UtcOffsetMinutes: -300, FromUnixMs: from, WindowFromUnixMs: windowFrom, BucketMs: bucket, Rows: rows, Columns: cols, ColumnGroup: 1,
        ColumnKind: 'date', RowKind: 'weekday', ColumnUnit: '1w',
        ColumnTimesUnixMs: starts.slice(0, cols), RowTimesUnixMs: Array.from({ length: rows }, (_, r) => starts[r * cols]),
        ColumnLabels: Array.from({ length: cols }, (_, c) => 'c' + c), RowLabels: Array.from({ length: rows }, (_, r) => 'r' + r),
        ColumnLabelAnchors: Array.from({ length: cols }, (_, c) => c), CellStartsUnixMs: starts, OutsideWindow: starts.map((t) => t > now || t < windowFrom),
        ...over,
    };
}

test('heatmap grid takes positions, windows, and blanks from the layout and shades by quartile', () => {
    const lay = layout();
    const from = lay.FromUnixMs;
    // Display order: row 0 is Mon of week 1, Mon of week 2; row 1 is Tue, Tue; ...
    const tokens = [0, 100, 50, 1, 33, 67, 99, 100, 0, 0, 0, 0, 0, 0];
    const grid = heatmapGrid({ Layout: lay, TotalTokens: tokens, Calls: tokens.map((t) => (t > 0 ? 1 : 0)) });
    assert.equal(grid.rows, 7);
    assert.equal(grid.cols, 2);
    assert.equal(grid.max, 100);
    assert.deepEqual(grid.cells.slice(0, 4).map((c) => [c.row, c.col]), [[0, 0], [0, 1], [1, 0], [1, 1]]);
    assert.deepEqual(grid.cells.slice(0, 4).map((c) => c.from), [from, from + 7 * DAY, from + DAY, from + 8 * DAY], 'time runs down each column, then across');
    // Quartiles of the used cells [1, 33, 50, 67, 99, 100, 100]: bounds 33, 67, 100; the peak is level 4.
    assert.deepEqual(grid.cells.slice(0, 9).map((c) => c.level), [0, 4, 2, 1, 1, 2, 3, 4, 0]);
    assert.deepEqual([grid.cells[1].from, grid.cells[1].to], [from + 7 * DAY, from + 8 * DAY - 1], 'drill window');
    // Now is Wednesday of week 2 at 01:00: Thursday on in column 1 is in the future.
    assert.deepEqual(grid.cells.filter((c) => c.blank).map((c) => c.index), [7, 9, 11, 13]);
    assert.deepEqual(grid.cells.filter((c) => c.future).map((c) => c.index), [7, 9, 11, 13]);
    assert.deepEqual(heatmapGrid(null), { rows: 0, cols: 0, max: 0, cells: [] });
});

test('heatmap labels both axes in local time, localized from the layout instants', () => {
    const lay = layout();
    assert.equal(heatmapColumnLabel(lay, 0, 'en-US'), '11/9', 'week start dates along the x-axis at local midnight, not UTC');
    assert.equal(heatmapColumnLabel(lay, 1, 'de-DE'), '16.11.', 'localized month-day');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'weekdate' }), 0, 'en-US'), 'Mon 11/9');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'month' }), 1, 'en-US'), 'Nov');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'halfday' }), 0, 'de-DE'), 'Mo');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'datetime' }), 0, 'en-US'), '11/9 00:00');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'fulldate' }), 0, 'en-US'), 'Nov 9, 2026');
    assert.equal(heatmapColumnLabel(layout({ ColumnKind: 'hour' }), 1, 'en-US'), 'c1', 'numeric kinds use the server label');
    assert.equal(heatmapRowLabel(lay, 0, 'en-US'), 'Mon');
    assert.equal(heatmapRowLabel(lay, 6, 'de-DE'), 'So', 'weekdays are localized');
    assert.equal(heatmapRowLabel(layout({ RowKind: 'week' }), 1, 'en-US'), 'Nov 10, 2026');
    assert.equal(heatmapRowLabel(layout({ RowKind: 'clock' }), 2, 'en-US'), 'r2');
    const start = Date.UTC(2026, 10, 11, 15, 0); // 10:00 on Wednesday the 11th at UTC-5
    assert.equal(heatmapCellWindow(layout({ BucketMs: 2 * HOUR }), start, 'en-US'), 'Nov 11, 2026 10:00–12:00');
    assert.equal(heatmapCellWindow(lay, lay.FromUnixMs, 'en-US'), 'Nov 9, 2026');
    assert.deepEqual(labeledIndices(12, 6, true), [0, 2, 4, 6, 8, 11]);
});

test('x-axis labels thin so they never collide, each over the columns it names', () => {
    const all = (n: number) => Array.from({ length: n }, (_, i) => i);
    const cols = (labels: { col: number }[]) => labels.map((l) => l.col);
    const show = (labels: { col: number; align: string }[]) => labels.map((l) => l.col + (l.align === 'start' ? '' : l.align[0])).join(',');
    assert.deepEqual(cols(placeLabels(all(24), 24, 1, 1, true)), all(24), 'room for every label');
    assert.equal(show(placeLabels(all(24), 24, 1, 3, true)), '0,3,6,9,12,15,18,23e', 'every 3rd; the last, ending at its own column, replaces the one before it');
    assert.equal(show(placeLabels(all(13), 13, 1, 2, true)), '0,2,4,6,8,12e', 'the end-aligned last needs room for the label before it');
    assert.equal(show(placeLabels(all(13), 13, 1, 1, true)), all(13).join(','), 'a label that fits in its column starts there, even the last');
    assert.equal(show(placeLabels(all(5), 5, 1, 9, true)), '0e', 'too narrow for two labels: only the first, kept inside the grid');
    // A month: weekday labels centred over their AM and PM columns, never pushed to the grid's end.
    const weekdays = [0, 2, 4, 6, 8, 10, 12];
    assert.equal(show(placeLabels(weekdays, 14, 2, 1, true)), '0c,2c,4c,6c,8c,10c,12c', 'every weekday centred over its half-days');
    assert.equal(show(placeLabels(weekdays, 14, 2, 2, true)), '0c,2c,4c,6c,8c,10c,12c', 'Sun stays over Sun AM and PM');
    assert.equal(show(placeLabels(weekdays, 14, 2, 3, true)), '0c,4c,8c,12c');
    assert.equal(show(placeLabels(weekdays, 14, 2, 7, true)), '0c,12c', 'Sun replaces the stride label it would collide with');
    for (const p of placeLabels(weekdays, 14, 2, 2, true)) {
        assert.equal(p.span, 2, 'a weekday names two columns');
    }
    // A year: month starts (uneven) plus the first and last column.
    const year = [0, 3, 7, 12, 16, 20, 25, 29, 34, 38, 42, 47, 51, 52];
    const picked = placeLabels(year, 53, 1, 3, false);
    assert.equal(picked[0].col, 0);
    assert.equal(picked[picked.length - 1].col, 52);
    assert.equal(picked[picked.length - 1].align, 'end', 'the last column label ends at its own column');
    for (let i = 1; i < picked.length - 1; i++) {
        assert.ok(picked[i].col - picked[i - 1].col >= 3, 'labels never overlap');
        assert.equal(picked[i].align, 'start');
    }
    assert.ok(53 - 3 >= picked[picked.length - 2].col + 3, 'the end-aligned last label has room');
    assert.deepEqual(placeLabels([], 0, 1, 1, true), []);
});

test('heatmap cells stretch to the width within 10-96 pixels and rows stay short', () => {
    assert.deepEqual(heatmapSizes(1000, 24, 3, 2), { colW: 36, rowH: 28, gap: 1 });
    assert.deepEqual(heatmapSizes(1000, 12, 3, 3), { colW: 75, rowH: 28, gap: 1 }, 'twelve columns fill the width');
    assert.deepEqual(heatmapSizes(1400, 7, 5, 9), { colW: 96, rowH: 28, gap: 1 }, 'capped at 96 pixels');
    assert.deepEqual(heatmapSizes(900, 53, 3, 3), { colW: 12, rowH: 12, gap: 2 }, 'a year of weeks fits 900 pixels');
    assert.equal(heatmapSizes(300, 53, 3, 3).colW, 10, 'never below 10 pixels');
});

test('month heatmap is a calendar of half-days with cells outside its 30-day window blank', () => {
    const from = Date.UTC(2026, 9, 12, 5, 0); // Monday 2026-10-12 00:00 at UTC-5
    const now = Date.UTC(2026, 10, 11, 20, 30); // Wednesday 2026-11-11 15:30 at UTC-5, a PM half-day
    const nowCell = 4 * 14 + 2 * 2 + 1;
    const lay = layout({ ColumnGroup: 2, ColumnKind: 'halfday', RowKind: 'week', ColumnUnit: 'halfday', ColumnLabelAnchors: [0, 2, 4, 6, 8, 10, 12] },
        { from, rows: 5, cols: 14, bucket: 12 * HOUR, now, windowFrom: from + (nowCell - 59) * 12 * HOUR, across: false });
    const grid = heatmapGrid({ Layout: lay, TotalTokens: Array.from({ length: 70 }, () => 5), Calls: [] });
    assert.equal(grid.rows, 5);
    assert.equal(grid.cols, 14);
    const blank = grid.cells.filter((c) => c.blank).map((c) => c.index);
    assert.deepEqual(blank.filter((i) => i < nowCell), [0, 1], 'the half-days before the last 30 days are blank');
    assert.deepEqual(blank.filter((i) => i > nowCell), Array.from({ length: 69 - nowCell }, (_, i) => nowCell + 1 + i), 'the cells after now are blank');
    assert.equal(grid.cells.filter((c) => !c.blank).length, 60, '60 half-days in the window');
    assert.ok(!grid.cells[0].future && grid.cells[0].blank, 'a cell before the window is blank but not future');
    assert.equal(heatmapColumnLabel(lay, 0, 'en-US'), 'Mon');
    assert.equal(heatmapColumnLabel(lay, 1, 'en-US'), 'Mon', 'the PM column shares its weekday');
    assert.equal(heatmapColumnLabel(lay, 13, 'de-DE'), 'So', 'localized');
    assert.equal(heatmapRowLabel(lay, 4, 'en-US'), 'Nov 9, 2026', 'rows are the local Monday each week starts');
});

test('number formatting helpers', () => {
    assert.equal(formatTokensCompact(950), '950');
    assert.equal(formatTokensCompact(12_345), '12.3K');
    assert.equal(formatTokensCompact(4_500_000), '4.5M');
    assert.equal(formatUsd(0.01234), '$0.0123');
    assert.equal(formatUsd(12.5), '$12.50');
});

test('heatmap scale splits used cells into quartiles and matches Mux.Core on edge cases', () => {
    // The same values as Mux.Core's HeatmapScaleUsesQuartiles case: a skewed peak no longer flattens the rest.
    const levelOf = heatmapScale([0, 0, 10, 20, 30, 40, 50, 60, 70, 80, 10000]);
    assert.deepEqual([0, 10, 20, 30, 40, 50, 60, 70, 80, 10000].map(levelOf), [0, 1, 1, 1, 2, 2, 3, 3, 4, 4]);
    assert.equal(heatmapScale([5, 5, 5])(5), 4, 'equal values take the strongest shade');
    assert.equal(heatmapScale([])(3), 4, 'no bounds: any usage is the strongest shade');
    assert.equal(heatmapScale([NaN, Infinity, -3, 7])(NaN), 0, 'NaN is no usage');
    assert.equal(heatmapScale([7])(-1), 0, 'negative is no usage');
});

test('the metric picker keys default to tokens and the chart mode to bars', () => {
    assert.equal(normalizeSplitMetric('Calls'), 'calls');
    assert.equal(normalizeSplitMetric(' cost '), 'cost');
    assert.equal(normalizeSplitMetric('latency'), 'tokens');
    assert.equal(normalizeSplitMetric(undefined), 'tokens');
    assert.equal(normalizeChartMode('LINES'), 'lines');
    assert.equal(normalizeChartMode('pie'), 'bars');
    const m = { Calls: 4, TotalTokens: 900, CostUsd: 0.25 };
    assert.deepEqual([metricValue(m, 'tokens'), metricValue(m, 'calls'), metricValue(m, 'cost')], [900, 4, 0.25]);
});

test('split series carry the chosen metric and breakdown bars sort by it', () => {
    const bucket = (calls: number, tokens: number, cost: number) => ({ BucketStartUnixMs: 0, Metrics: { Calls: calls, TotalTokens: tokens, CostUsd: cost } });
    const split = [
        { Key: 'a', IsOther: false, Buckets: [bucket(1, 500, 0.5), bucket(2, 0, 0)] },
        { Key: 'b', IsOther: false, Buckets: [bucket(9, 10, 0), bucket(0, 0, 0)] },
    ];
    assert.deepEqual(stackSplitSeries(split, 2, 6, 'calls').map((s) => s.values), [[1, 2], [9, 0]]);
    assert.deepEqual(stackSplitSeries(split, 2, 6, 'cost').map((s) => s.key), ['a'], 'a series with no cost is dropped from the Cost chart');
    assert.deepEqual(stackSplitSeries(split, 2).map((s) => s.values), [[500, 0], [10, 0]], 'tokens by default');

    const rows = [
        { Value: 'many-calls', Metrics: { Calls: 50, InputTokens: 100, OutputTokens: 0, TotalTokens: 100, CostUsd: 0.01 } },
        { Value: 'many-tokens', Metrics: { Calls: 2, InputTokens: 9000, OutputTokens: 0, TotalTokens: 9000, CostUsd: 0.02 } },
        { Value: 'expensive', Metrics: { Calls: 1, InputTokens: 10, OutputTokens: 0, TotalTokens: 10, CostUsd: 3 } },
    ];
    assert.deepEqual(breakdownBars(rows).map((b) => b.value), ['many-tokens', 'many-calls', 'expensive'], 'tokens by default');
    assert.deepEqual(breakdownBars(rows, 10, 'calls').map((b) => b.value), ['many-calls', 'many-tokens', 'expensive']);
    assert.deepEqual(breakdownBars(rows, 10, 'cost').map((b) => b.value), ['expensive', 'many-tokens', 'many-calls']);
});

test('CSV numbers, cells and times use the shared invariant format', () => {
    assert.deepEqual([0, -0, 12, -3.5, 0.1234565, 0.0000004, 0.0000005, 1e-7, 2.0000005, 1 / 3, NaN, Infinity, 12345678901].map(csvNumber),
        ['0', '0', '12', '-3.5', '0.123457', '0', '0.000001', '0', '2.000001', '0.333333', '', '', '12345678901']);
    assert.deepEqual(['plain', 'a,b', 'say "hi"', ' lead', 'trail\t', 'two\nlines', '', 7].map(csvField),
        ['plain', '"a,b"', '"say ""hi"""', '" lead"', '"trail\t"', '"two\nlines"', '', '7']);
    assert.deepEqual(['=HYPERLINK(1)', '-x', '-', '-3.5', '-.5', '=a,b', '\tx', '+1', '@me', '\rx', 'a=b'].map(csvField),
        ["'=HYPERLINK(1)", "'-x", "'-", '-3.5', '-.5', `"'=a,b"`, "'\tx", "'+1", "'@me", `"'\rx"`, 'a=b'], 'formula-like text gets a leading apostrophe, then quoting');
    const t = Date.UTC(2026, 10, 11, 15, 15, 0);
    assert.equal(csvTime(t, -300), '2026-11-11T10:15:00-05:00');
    assert.equal(csvTime(t, 330), '2026-11-11T20:45:00+05:30');
    assert.equal(csvTime(t, 0), '2026-11-11T15:15:00+00:00');
    assert.equal(usageCsv(['a', 'b'], [[1, 'x,y'], [0.5, '']]), 'a,b\n1,"x,y"\n0.5,\n');
});

test('CSV tables: one row per bucket with named series columns, and null when the chart is empty', () => {
    const t0 = Date.UTC(2026, 0, 1);
    const dist = { Min: 1, Avg: 2.5, P95: 3, P99: 4, Max: 5, Count: 2 };
    const buckets = [0, 1].map((i) => ({
        BucketStartUnixMs: t0 + i * HOUR,
        Metrics: { Calls: 3, Errors: i, InputTokens: 100, CachedTokens: 40, OutputTokens: 7, TotalTokens: 107, CostUsd: 0.125, TotalMsDist: i ? { Count: 0 } : dist },
    }));
    const input = { range: 'all', metric: 'calls' as const, dimension: 'endpoint' as const, buckets, split: [], breakdown: [], offsetMinutes: 0 };
    const csv = (tab: string, over: Record<string, unknown> = {}): string | null => {
        const table = usageCsvTable(tab, { ...input, ...over });
        return table ? usageCsv(table.header, table.rows) : null;
    };
    assert.equal(csv('calls'), 'bucket_start,bucket_end,succeeded,failed\n2026-01-01T00:00:00+00:00,2026-01-01T01:00:00+00:00,3,0\n2026-01-01T01:00:00+00:00,2026-01-01T02:00:00+00:00,2,1\n');
    assert.match(csv('tokens') ?? '', /^bucket_start,bucket_end,uncached_input,cached_input,output\n[^\n]+,60,40,7\n/);
    assert.match(csv('cost') ?? '', /^bucket_start,bucket_end,cost_usd\n[^\n]+,0\.125\n/);
    assert.match(csv('latency') ?? '', /^bucket_start,bucket_end,min,avg,p95,p99,max,count\n[^\n]+,1,2\.5,3,4,5,2\n[^\n]+,,,,,,0\n$/, 'a bucket without samples has empty stats');
    const split = [
        { Key: 'e1', IsOther: false, Buckets: buckets.map((b) => ({ BucketStartUnixMs: b.BucketStartUnixMs, Metrics: { Calls: 2, TotalTokens: 50, CostUsd: 0.1 } })) },
        { Key: 'Other', IsOther: true, Buckets: buckets.map((b) => ({ BucketStartUnixMs: b.BucketStartUnixMs, Metrics: { Calls: 1, TotalTokens: 5, CostUsd: 0 } })) },
    ];
    assert.match(csv('split', { split }) ?? '', /^bucket_start,bucket_end,e1,Other\n[^\n]+,2,1\n/, 'the chosen metric per value, Other last');
    const breakdown = [{ Value: 'e1', Metrics: { Calls: 6, Errors: 1, InputTokens: 200, CachedTokens: 80, OutputTokens: 14, TotalTokens: 214, CostUsd: 0.25 } }];
    assert.equal(csv('breakdown', { breakdown, fromUnixMs: t0, toUnixMs: t0 + DAY }),
        'bucket_start,bucket_end,endpoint,calls,failed,uncached_input,cached_input,output,total_tokens,cost_usd\n2026-01-01T00:00:00+00:00,2026-01-02T00:00:00+00:00,e1,6,1,120,80,14,214,0.25\n');
    // Two 12-hour columns of two 6-hour rows: display order is [00:00, 12:00, 06:00, 18:00]; the CSV is chronological.
    const heatmap = { Layout: layout({ UtcOffsetMinutes: 0 }, { from: t0, rows: 2, cols: 2, bucket: 6 * HOUR, now: t0 + 13 * HOUR }), TotalTokens: [1, 9, 5, 0], Calls: [1, 2, 1, 0] };
    assert.equal(csv('heatmap', { heatmap }), 'cell_start,cell_end,tokens,calls\n2026-01-01T00:00:00+00:00,2026-01-01T06:00:00+00:00,1,1\n'
        + '2026-01-01T06:00:00+00:00,2026-01-01T12:00:00+00:00,5,1\n2026-01-01T12:00:00+00:00,2026-01-01T18:00:00+00:00,9,2\n', 'oldest first; cells outside the window are left out');

    assert.equal(csv('calls', { buckets: [] }), null);
    assert.equal(csv('tokens', { buckets: buckets.map((b) => ({ ...b, Metrics: { Calls: 0 } })) }), null, 'no calls: nothing to export');
    assert.equal(csv('breakdown'), null);
    assert.equal(csv('split'), null);
    assert.equal(csv('heatmap', { heatmap: null }), null);
});

test('export file names are mux-usage-<chart>-<range>-<yyyyMMdd-HHmmss>', () => {
    const now = new Date(2026, 10, 11, 9, 5, 7);
    assert.equal(usageExportFileName('calls', 'day', 'csv', now), 'mux-usage-calls-day-20261111-090507.csv');
    assert.equal(usageExportFileName(usageExportChartName('split', 'calls', 'callkind'), 'week', 'png', now), 'mux-usage-calls-by-callkind-week-20261111-090507.png');
    assert.equal(usageExportChartName('breakdown', 'cost', 'endpoint'), 'usage-by-endpoint');
    assert.equal(usageExportChartName('streaming', 'tokens', 'model'), 'streaming');
});

test('the CSV writer matches the web dashboard writer byte for byte', () => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const fs = require('node:fs');
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const path = require('node:path');
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const vm = require('node:vm');
    const template: string = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'Mux.Server', 'DashboardTemplate.cs'), 'utf8');
    const start = template.indexOf('function csvNum(v){');
    const end = template.indexOf('\n/* The current chart', start);
    assert.ok(start > 0 && end > start, 'found the web CSV functions');
    const web: Record<string, (...args: unknown[]) => string> = {};
    vm.runInNewContext(template.slice(start, end) + '\nout.csvNum=csvNum;out.csvField=csvField;out.csvTime=csvTime;out.usageCsv=usageCsv;', { out: web });
    const numbers = [0, -0, 1, -1, 0.1, 0.1234565, 0.0000005, 0.0000004, -2.5, 99.9999999, 2.0000005, NaN, Infinity, 1 / 3, 12345678901];
    assert.deepEqual(numbers.map((n) => web.csvNum(n)), numbers.map(csvNumber));
    const cells = ['plain', 'a,b', 'say "hi"', ' lead', 'trail\t', 'cr\r', 'line\nbreak', '', '=HYPERLINK(1)', '-x', '-', '-3.5', '=a,b', '\tx', '@a', '+b', '-\u0663'];
    assert.deepEqual(cells.map((c) => web.csvField(c)), cells.map(csvField));
    const times = [Date.UTC(2026, 10, 1, 5, 59, 59), Date.UTC(2026, 10, 1, 6), Date.UTC(2026, 2, 8, 7, 30), 0];
    for (const off of [-300, -240, 0, 330, 545]) {
        assert.deepEqual(times.map((t) => web.csvTime(t, off)), times.map((t) => csvTime(t, off)), 'offset ' + off);
    }
    assert.deepEqual(times.map((t) => web.csvTime(t)), times.map((t) => csvTime(t)), 'the local zone by default');
    const header = ['bucket_start', 'gpt-4o, mini', 'Other'];
    const rows = [[csvTime(times[0], -240), 1.5, 0], [csvTime(times[1], -300), NaN, 0.0000004]];
    assert.equal(web.usageCsv(header, rows), usageCsv(header, rows));
});
