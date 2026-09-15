import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { UsageDistribution } from '../api/types';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';

/** A min/avg/p95/p99/max distribution passed to the chart. */
interface Dist {
    min: number;
    avg: number;
    p95: number;
    p99: number;
    max: number;
}

/** A per-bucket series point sent to the webview for charting. */
interface SeriesPoint {
    label: string;
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

/** The payload the host posts to the usage webview. */
interface UsagePayload {
    enabled: boolean;
    range: string;
    kpis: { label: string; value: string }[];
    series: SeriesPoint[];
}

/**
 * The native usage dashboard: KPIs and an over-time chart drawn from the server's usage telemetry, so the
 * cost and performance view the web dashboard and desktop app give is available in the editor. The extension
 * host fetches the data (the webview cannot reach the authenticated server itself) and posts it in; the
 * webview renders hand-rolled SVG bars — no charting library — and can switch the plotted metric and range.
 */
export class UsagePanel {
    private static current: vscode.WebviewPanel | undefined;
    private static lifecycle: MuxServerLifecycle | undefined;

    /**
     * Opens (or reveals) the usage panel and loads the default range.
     *
     * @param lifecycle The server lifecycle used to obtain a client.
     */
    public static show(lifecycle: MuxServerLifecycle): void {
        UsagePanel.lifecycle = lifecycle;
        if (UsagePanel.current) {
            UsagePanel.current.reveal();
            void UsagePanel.load('day');
            return;
        }

        const panel = vscode.window.createWebviewPanel('muxUsage', vscode.l10n.t('mux Usage'), vscode.ViewColumn.Active, {
            enableScripts: true,
            retainContextWhenHidden: true,
        });
        panel.webview.html = UsagePanel.render();
        panel.webview.onDidReceiveMessage((message: { type: string; range?: string }) => {
            if (message.type === 'range' && message.range) {
                void UsagePanel.load(message.range);
            }
        });
        panel.onDidDispose(() => {
            UsagePanel.current = undefined;
        });
        UsagePanel.current = panel;
        void UsagePanel.load('day');
    }

    private static async load(range: string): Promise<void> {
        if (!UsagePanel.current || !UsagePanel.lifecycle) {
            return;
        }

        try {
            const client: ApiClient = await UsagePanel.lifecycle.getClient(new vscode.CancellationTokenSource().token);
            const [summary, buckets] = await Promise.all([client.getUsageSummary(range), client.getUsageTimeseries(range)]);
            const m = summary.Metrics;
            const payload: UsagePayload = {
                enabled: true,
                range,
                kpis: [
                    { label: vscode.l10n.t('Calls'), value: fmtNum(m.Calls) },
                    { label: vscode.l10n.t('Tokens'), value: fmtNum(m.TotalTokens) },
                    { label: vscode.l10n.t('Cost'), value: `$${(m.CostUsd ?? 0).toFixed(4)}` },
                    { label: vscode.l10n.t('Errors'), value: fmtNum(m.Errors) },
                    { label: vscode.l10n.t('Avg TTFT'), value: `${Math.round(m.AvgTtftMs ?? 0)} ms` },
                    { label: vscode.l10n.t('Avg latency'), value: `${Math.round(m.AvgTotalMs ?? 0)} ms` },
                ],
                series: buckets.map((b) => ({
                    label: new Date(b.BucketStartUnixMs).toLocaleString(),
                    input: b.Metrics.InputTokens ?? 0,
                    cached: b.Metrics.CachedTokens ?? 0,
                    output: b.Metrics.OutputTokens ?? 0,
                    tokens: b.Metrics.TotalTokens ?? 0,
                    cost: b.Metrics.CostUsd ?? 0,
                    latency: dist(b.Metrics.TotalMsDist),
                    ttft: dist(b.Metrics.TtftMsDist),
                    streaming: dist(b.Metrics.StreamMsDist),
                    throughput: dist(b.Metrics.ThroughputDist),
                })),
            };
            UsagePanel.current.webview.postMessage(payload);
        } catch (error) {
            logError('Failed to load usage.', error);
            UsagePanel.current.webview.postMessage({ enabled: false, range, kpis: [], series: [] });
        }
    }

    private static render(): string {
        const nonce = crypto.randomBytes(16).toString('hex');
        const csp = `default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';`;
        const strings = JSON.stringify({
            title: vscode.l10n.t('Usage'),
            disabled: vscode.l10n.t('Usage telemetry is disabled or unavailable.'),
            empty: vscode.l10n.t('No usage in this range yet.'),
            metrics: {
                tokens: vscode.l10n.t('Tokens'),
                cost: vscode.l10n.t('Cost'),
                latency: vscode.l10n.t('Latency'),
                ttft: vscode.l10n.t('TTFT'),
                streaming: vscode.l10n.t('Streaming'),
                throughput: vscode.l10n.t('Throughput'),
            },
            legend: { input: vscode.l10n.t('Prompt'), cached: vscode.l10n.t('Cached'), output: vscode.l10n.t('Output') },
            ranges: { hour: vscode.l10n.t('Hour'), day: vscode.l10n.t('Day'), week: vscode.l10n.t('Week'), month: vscode.l10n.t('Month'), all: vscode.l10n.t('All') },
        });

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="${csp}" />
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 18px; }
  h1 { font-size: 1.3em; margin: 0 0 14px; }
  .bar { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 16px; }
  .seg { display: inline-flex; border: 1px solid var(--vscode-panel-border); border-radius: 6px; overflow: hidden; }
  .seg button { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); border: none; padding: 5px 12px; cursor: pointer; }
  .seg button.active { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
  .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 10px; margin-bottom: 20px; }
  .kpi { border: 1px solid var(--vscode-panel-border); border-radius: 8px; padding: 12px; background: var(--vscode-editorWidget-background, var(--vscode-editor-background)); }
  .kpi .k-label { font-size: 0.8em; color: var(--vscode-descriptionForeground); }
  .kpi .k-value { font-size: 1.4em; font-weight: 600; margin-top: 4px; font-variant-numeric: tabular-nums; }
  .chart-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
  svg { width: 100%; height: 260px; display: block; }
  .bar-primary { fill: var(--vscode-charts-green, var(--vscode-textLink-foreground)); }
  .bar-input { fill: var(--vscode-charts-blue, #4c8bf5); }
  .bar-cached { fill: var(--vscode-charts-yellow, #d7a100); }
  .bar-output { fill: var(--vscode-charts-green, #3fb950); }
  .candle-wick { stroke: var(--vscode-descriptionForeground); stroke-width: 1; }
  .candle-box { fill: var(--vscode-charts-green, var(--vscode-textLink-foreground)); opacity: 0.55; }
  .candle-avg { stroke: var(--vscode-foreground); stroke-width: 1.5; }
  .candle-p99 { stroke: var(--vscode-charts-red, #e5534b); stroke-width: 1.5; }
  .axis { stroke: var(--vscode-panel-border); }
  .axis-label { fill: var(--vscode-descriptionForeground); font-size: 10px; }
  .legend { display: flex; gap: 14px; font-size: 0.82em; color: var(--vscode-descriptionForeground); margin-top: 8px; }
  .legend .sw { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 5px; vertical-align: middle; }
  .muted { color: var(--vscode-descriptionForeground); padding: 30px 0; text-align: center; }
</style>
</head>
<body>
<h1 id="title"></h1>
<div class="bar">
  <div class="seg" id="ranges"></div>
</div>
<div class="kpis" id="kpis"></div>
<div class="chart-head">
  <div class="seg" id="metrics"></div>
</div>
<div id="chart"></div>
<div class="legend" id="legend"></div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const S = ${strings};
  const NS = 'http://www.w3.org/2000/svg';
  const DIST_METRICS = { latency: 1, ttft: 1, streaming: 1, throughput: 1 };
  let current = null;
  let metric = 'tokens';
  let range = 'day';

  document.getElementById('title').textContent = S.title;

  const rangesEl = document.getElementById('ranges');
  ['hour','day','week','month','all'].forEach(function(r){
    const b = document.createElement('button');
    b.textContent = S.ranges[r]; b.dataset.range = r;
    if (r === range) b.classList.add('active');
    b.addEventListener('click', function(){ range = r; syncActive(rangesEl, 'range', r); vscode.postMessage({ type: 'range', range: r }); });
    rangesEl.appendChild(b);
  });

  const metricsEl = document.getElementById('metrics');
  ['tokens','cost','latency','ttft','streaming','throughput'].forEach(function(m){
    const b = document.createElement('button');
    b.textContent = S.metrics[m]; b.dataset.metric = m;
    if (m === metric) b.classList.add('active');
    b.addEventListener('click', function(){ metric = m; syncActive(metricsEl, 'metric', m); draw(); });
    metricsEl.appendChild(b);
  });

  function syncActive(container, attr, value){
    Array.prototype.forEach.call(container.children, function(c){ c.classList.toggle('active', c.dataset[attr] === value); });
  }

  function svgEl(name, attrs){
    const e = document.createElementNS(NS, name);
    for (const k in attrs) { e.setAttribute(k, attrs[k]); }
    return e;
  }

  function renderKpis(kpis){
    const el = document.getElementById('kpis'); el.textContent = '';
    kpis.forEach(function(k){
      const card = document.createElement('div'); card.className = 'kpi';
      const l = document.createElement('div'); l.className = 'k-label'; l.textContent = k.label;
      const v = document.createElement('div'); v.className = 'k-value'; v.textContent = k.value;
      card.appendChild(l); card.appendChild(v); el.appendChild(card);
    });
  }

  function fmtVal(v){ return metric === 'cost' ? ('$' + v.toFixed(4)) : (metric === 'throughput' ? (Math.round(v) + ' t/s') : (metric === 'tokens' ? String(Math.round(v)) : Math.round(v) + ' ms')); }
  function axisMax(v){ return metric === 'cost' ? ('$' + v.toFixed(2)) : (metric === 'throughput' ? (Math.round(v) + ' t/s') : (metric === 'tokens' ? String(Math.round(v)) : Math.round(v) + ' ms')); }

  function setLegend(items){
    const el = document.getElementById('legend'); el.textContent = '';
    (items || []).forEach(function(it){
      const span = document.createElement('span');
      const sw = document.createElement('span'); sw.className = 'sw'; sw.style.background = it.color;
      span.appendChild(sw); span.appendChild(document.createTextNode(it.label));
      el.appendChild(span);
    });
  }

  function frame(pts){
    const chart = document.getElementById('chart');
    const W = chart.clientWidth || 600, H = 260, pad = 30;
    const svg = svgEl('svg', { viewBox: '0 0 ' + W + ' ' + H });
    const bw = (W - pad*2) / pts.length;
    return { chart: chart, W: W, H: H, pad: pad, bw: bw, svg: svg, plot: H - pad*2 };
  }

  function drawAxis(f, maxVal){
    f.svg.appendChild(svgEl('line', { class: 'axis', x1: f.pad, y1: f.H - f.pad, x2: f.W - f.pad, y2: f.H - f.pad }));
    const label = svgEl('text', { class: 'axis-label', x: 2, y: f.pad });
    label.textContent = axisMax(maxVal);
    f.svg.appendChild(label);
  }

  function draw(){
    const chart = document.getElementById('chart'); chart.textContent = ''; setLegend([]);
    if (!current || !current.enabled){ return muted(chart, S.disabled); }
    const pts = current.series || [];
    if (pts.length === 0){ return muted(chart, S.empty); }

    if (metric === 'tokens') { return drawStacked(pts); }
    if (metric === 'cost') { return drawBars(pts, function(p){ return p.cost; }); }
    return drawCandles(pts, metric);
  }

  function muted(chart, text){ const d = document.createElement('div'); d.className = 'muted'; d.textContent = text; chart.appendChild(d); }

  function drawBars(pts, val){
    const max = pts.reduce(function(a,p){ return Math.max(a, val(p)); }, 0);
    if (max <= 0){ return muted(document.getElementById('chart'), S.empty); }
    const f = frame(pts);
    pts.forEach(function(p, i){
      const h = Math.max(0, (val(p) / max) * f.plot);
      const rect = svgEl('rect', { class: 'bar-primary', x: (f.pad + i*f.bw + f.bw*0.15).toFixed(1), y: (f.H - f.pad - h).toFixed(1), width: (f.bw*0.7).toFixed(1), height: h.toFixed(1) });
      const title = svgEl('title'); title.textContent = p.label + ' — ' + fmtVal(val(p)); rect.appendChild(title);
      f.svg.appendChild(rect);
    });
    drawAxis(f, max);
    f.chart.appendChild(f.svg);
  }

  function drawStacked(pts){
    const max = pts.reduce(function(a,p){ return Math.max(a, p.input + p.cached + p.output); }, 0);
    if (max <= 0){ return muted(document.getElementById('chart'), S.empty); }
    const f = frame(pts);
    const parts = [['input','bar-input'],['cached','bar-cached'],['output','bar-output']];
    pts.forEach(function(p, i){
      let y = f.H - f.pad;
      parts.forEach(function(part){
        const v = p[part[0]] || 0;
        const h = (v / max) * f.plot;
        if (h > 0){
          const rect = svgEl('rect', { class: part[1], x: (f.pad + i*f.bw + f.bw*0.15).toFixed(1), y: (y - h).toFixed(1), width: (f.bw*0.7).toFixed(1), height: h.toFixed(1) });
          const title = svgEl('title'); title.textContent = p.label + ' — ' + S.legend[part[0]] + ': ' + Math.round(v); rect.appendChild(title);
          f.svg.appendChild(rect);
          y -= h;
        }
      });
    });
    drawAxis(f, max);
    f.chart.appendChild(f.svg);
    setLegend([
      { color: 'var(--vscode-charts-blue, #4c8bf5)', label: S.legend.input },
      { color: 'var(--vscode-charts-yellow, #d7a100)', label: S.legend.cached },
      { color: 'var(--vscode-charts-green, #3fb950)', label: S.legend.output },
    ]);
  }

  function drawCandles(pts, key){
    const max = pts.reduce(function(a,p){ return Math.max(a, p[key].max); }, 0);
    if (max <= 0){ return muted(document.getElementById('chart'), S.empty); }
    const f = frame(pts);
    const y = function(v){ return f.H - f.pad - (v / max) * f.plot; };
    pts.forEach(function(p, i){
      const d = p[key];
      const cx = f.pad + i*f.bw + f.bw*0.5;
      const bx = f.pad + i*f.bw + f.bw*0.3;
      const boxW = f.bw*0.4;
      // wick min..max
      f.svg.appendChild(svgEl('line', { class: 'candle-wick', x1: cx, y1: y(d.min).toFixed(1), x2: cx, y2: y(d.max).toFixed(1) }));
      // box avg..p95
      const top = Math.min(y(d.avg), y(d.p95)), bot = Math.max(y(d.avg), y(d.p95));
      const box = svgEl('rect', { class: 'candle-box', x: bx.toFixed(1), y: top.toFixed(1), width: boxW.toFixed(1), height: Math.max(1, bot - top).toFixed(1) });
      const title = svgEl('title'); title.textContent = p.label + ' — min ' + Math.round(d.min) + ' · avg ' + Math.round(d.avg) + ' · p95 ' + Math.round(d.p95) + ' · p99 ' + Math.round(d.p99) + ' · max ' + Math.round(d.max);
      box.appendChild(title);
      f.svg.appendChild(box);
      // avg line
      f.svg.appendChild(svgEl('line', { class: 'candle-avg', x1: bx.toFixed(1), y1: y(d.avg).toFixed(1), x2: (bx+boxW).toFixed(1), y2: y(d.avg).toFixed(1) }));
      // p99 tick
      f.svg.appendChild(svgEl('line', { class: 'candle-p99', x1: bx.toFixed(1), y1: y(d.p99).toFixed(1), x2: (bx+boxW).toFixed(1), y2: y(d.p99).toFixed(1) }));
    });
    drawAxis(f, max);
    f.chart.appendChild(f.svg);
    setLegend([
      { color: 'var(--vscode-charts-green, #3fb950)', label: 'avg–p95' },
      { color: 'var(--vscode-foreground)', label: 'avg' },
      { color: 'var(--vscode-charts-red, #e5534b)', label: 'p99' },
    ]);
  }

  window.addEventListener('message', function(e){
    current = e.data; range = current.range || range; syncActive(rangesEl, 'range', range);
    renderKpis(current.kpis || []);
    draw();
  });
</script>
</body>
</html>`;
    }
}

function fmtNum(n: number): string {
    return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

function dist(d: UsageDistribution | undefined): Dist {
    if (!d) {
        return { min: 0, avg: 0, p95: 0, p99: 0, max: 0 };
    }
    return { min: d.Min ?? 0, avg: d.Avg ?? 0, p95: d.P95 ?? 0, p99: d.P99 ?? 0, max: d.Max ?? 0 };
}
