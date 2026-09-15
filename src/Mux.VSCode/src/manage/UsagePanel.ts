import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ApiClient } from '../api/ApiClient';
import { MuxServerLifecycle } from '../server/lifecycle';
import { logError } from '../util/logger';

/** A compact per-bucket series point sent to the webview for charting. */
interface SeriesPoint {
    label: string;
    tokens: number;
    cost: number;
    ttft: number;
    total: number;
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
                    tokens: b.Metrics.TotalTokens ?? 0,
                    cost: b.Metrics.CostUsd ?? 0,
                    ttft: Math.round(b.Metrics.AvgTtftMs ?? 0),
                    total: Math.round(b.Metrics.AvgTotalMs ?? 0),
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
            metricTokens: vscode.l10n.t('Tokens'),
            metricCost: vscode.l10n.t('Cost'),
            metricLatency: vscode.l10n.t('Latency'),
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
  .bars rect { fill: var(--vscode-charts-green, var(--vscode-textLink-foreground)); }
  .axis { stroke: var(--vscode-panel-border); }
  .axis-label { fill: var(--vscode-descriptionForeground); font-size: 10px; }
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
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const S = ${strings};
  let current = null;
  let metric = 'tokens';
  let range = 'day';

  document.getElementById('title').textContent = S.title;

  const ranges = ['hour','day','week','month','all'];
  const rangesEl = document.getElementById('ranges');
  ranges.forEach(function(r){
    const b = document.createElement('button');
    b.textContent = S.ranges[r]; b.dataset.range = r;
    if (r === range) b.classList.add('active');
    b.addEventListener('click', function(){ range = r; syncActive(rangesEl, 'range', r); vscode.postMessage({ type: 'range', range: r }); });
    rangesEl.appendChild(b);
  });

  const metrics = [['tokens', S.metricTokens],['cost', S.metricCost],['latency', S.metricLatency]];
  const metricsEl = document.getElementById('metrics');
  metrics.forEach(function(m){
    const b = document.createElement('button');
    b.textContent = m[1]; b.dataset.metric = m[0];
    if (m[0] === metric) b.classList.add('active');
    b.addEventListener('click', function(){ metric = m[0]; syncActive(metricsEl, 'metric', m[0]); draw(); });
    metricsEl.appendChild(b);
  });

  function syncActive(container, attr, value){
    Array.prototype.forEach.call(container.children, function(c){ c.classList.toggle('active', c.dataset[attr] === value); });
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

  function valueFor(p){ return metric === 'tokens' ? p.tokens : metric === 'cost' ? p.cost : p.total; }

  function draw(){
    const chart = document.getElementById('chart'); chart.textContent = '';
    if (!current || !current.enabled){ const d = document.createElement('div'); d.className = 'muted'; d.textContent = S.disabled; chart.appendChild(d); return; }
    const pts = current.series || [];
    const max = pts.reduce(function(a,p){ return Math.max(a, valueFor(p)); }, 0);
    if (pts.length === 0 || max <= 0){ const d = document.createElement('div'); d.className = 'muted'; d.textContent = S.empty; chart.appendChild(d); return; }

    const W = chart.clientWidth || 600, H = 260, pad = 28;
    const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    const bw = (W - pad*2) / pts.length;
    const g = document.createElementNS('http://www.w3.org/2000/svg','g'); g.setAttribute('class','bars');
    pts.forEach(function(p, i){
      const v = valueFor(p);
      const h = Math.max(0, (v / max) * (H - pad*2));
      const rect = document.createElementNS('http://www.w3.org/2000/svg','rect');
      rect.setAttribute('x', (pad + i*bw + bw*0.15).toFixed(1));
      rect.setAttribute('y', (H - pad - h).toFixed(1));
      rect.setAttribute('width', (bw*0.7).toFixed(1));
      rect.setAttribute('height', h.toFixed(1));
      const title = document.createElementNS('http://www.w3.org/2000/svg','title');
      title.textContent = p.label + ' — ' + (metric === 'cost' ? ('$' + v.toFixed(4)) : v + (metric === 'latency' ? ' ms' : ''));
      rect.appendChild(title);
      g.appendChild(rect);
    });
    const axis = document.createElementNS('http://www.w3.org/2000/svg','line');
    axis.setAttribute('class','axis');
    axis.setAttribute('x1', pad); axis.setAttribute('y1', H - pad); axis.setAttribute('x2', W - pad); axis.setAttribute('y2', H - pad);
    const maxLabel = document.createElementNS('http://www.w3.org/2000/svg','text');
    maxLabel.setAttribute('class','axis-label'); maxLabel.setAttribute('x', 2); maxLabel.setAttribute('y', pad);
    maxLabel.textContent = metric === 'cost' ? ('$' + max.toFixed(2)) : String(max);
    svg.appendChild(axis); svg.appendChild(maxLabel); svg.appendChild(g);
    chart.appendChild(svg);
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
