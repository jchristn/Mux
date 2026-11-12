namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Telemetry;
    using Mux.Desktop.I18n;
    using Mux.Server;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the usage view of the <c>mux serve</c> web dashboard: the seven range buttons, the ten
    /// chart tabs in spec order with the shared dimension picker, the failure-reasons list, the drill-down hooks
    /// that narrow the per-call grid to a bucket window, the API paths each piece calls, the localized strings in
    /// every locale (both the page's own packs and the shared <see cref="DashboardStrings"/> catalog), that the
    /// page's JavaScript parses, and that a live server over a seeded store serves the page and answers each call.
    /// </summary>
    public static class DashboardUsageChartsSuite
    {
        #region Private-Members

        private const string SuiteId = "DashboardUsageCharts";

        private static readonly string[] _NewKeys =
        {
            "us.range", "us.r.hour", "us.r.day", "us.r.week", "us.r.month", "us.r.quarter", "us.r.year", "us.r.all",
            "us.r.hour.tip", "us.r.day.tip", "us.r.week.tip", "us.r.month.tip", "us.r.quarter.tip", "us.r.year.tip", "us.r.all.tip",
            "us.t.calls", "us.t.tokens", "us.t.split", "us.t.cost", "us.t.latency", "us.t.ttft", "us.t.stream", "us.t.throughput", "us.t.breakdown", "us.t.heatmap",
            "us.dim", "us.dim.tip", "us.dim.model", "us.dim.endpoint", "us.dim.provider", "us.dim.callkind",
            "us.other", "us.s.succeeded", "us.s.failed", "us.s.prompt", "us.s.cached", "us.s.output", "us.s.cost",
            "us.errorRate", "us.total", "us.calls", "us.failures", "us.noFailures", "us.empty", "us.bd.line",
            "us.drill", "us.drill.clear", "us.drill.hint", "us.chart.aria",
            "us.hm.none", "us.hm.less", "us.hm.more", "us.hm.quartile",
            "us.hm.axes", "us.hm.row.15m", "us.hm.row.1h", "us.hm.row.6h", "us.hm.row.1d", "us.hm.row.1w", "us.hm.row.13w",
            "us.hm.col.minute", "us.hm.col.minuteofhour", "us.hm.col.hours", "us.hm.col.clock", "us.hm.col.weekday", "us.hm.col.halfday", "us.hm.col.weeks", "us.hm.hint",
            "us.t.by", "us.metric", "us.metric.tip", "us.m.tokens", "us.m.calls", "us.m.cost",
            "us.mode.tip", "us.mode.bars", "us.mode.lines",
            "us.exp.csv", "us.exp.csv.tip", "us.exp.png", "us.exp.png.tip", "us.exp.none", "us.exp.saved"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the dashboard usage-charts suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Web dashboard usage charts: ranges, tabs, dimension picker, failure reasons, drill-down",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "RangeButtons", "Seven range buttons in order (hour, day, week, month, quarter, year, all), each localized", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        string bar = Between(html, "id=\"us_range\"", "</div>");
                        List<string> ranges = new List<string>();
                        foreach (Match m in Regex.Matches(bar, "data-range=\"([a-z]+)\"")) ranges.Add(m.Groups[1].Value);
                        MuxAssert.AreEqual("hour,day,week,month,quarter,year,all", string.Join(",", ranges), "range order");
                        foreach (string r in ranges)
                        {
                            MuxAssert.Contains("data-range=\"" + r + "\"", bar, r + " button");
                            MuxAssert.Contains("data-i18n=\"us.r." + r + "\"", bar, r + " label localized");
                            MuxAssert.Contains("data-i18n-title=\"us.r." + r + ".tip\"", bar, r + " tooltip localized");
                        }

                        MuxAssert.Contains("usageState.range=b.dataset.range", html, "a range click sets the range");
                        MuxAssert.Contains("var p=win?(\"from=\"+win.from+\"&to=\"+win.to):(\"range=\"+usageState.range);", html, "queries carry the range key unless a window is given");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "TabsAndDimensionPicker", "Ten tabs in spec order and one shared dimension picker (model, endpoint, provider, call kind)", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        string tabs = Between(html, "id=\"us_tabs\"", "</div>");
                        List<string> ids = new List<string>();
                        foreach (Match m in Regex.Matches(tabs, "data-tab=\"([a-z]+)\"")) ids.Add(m.Groups[1].Value);
                        MuxAssert.AreEqual("calls,tokens,split,cost,latency,ttft,stream,throughput,breakdown,heatmap", string.Join(",", ids), "tab order");
                        MuxAssert.Contains("id=\"us_dim\"", tabs, "dimension picker inside the chart header");
                        List<string> dims = new List<string>();
                        foreach (Match m in Regex.Matches(Between(tabs, "id=\"us_dim\"", "</select>"), "<option value=\"([a-z]+)\"")) dims.Add(m.Groups[1].Value);
                        MuxAssert.AreEqual("model,endpoint,provider,callkind", string.Join(",", dims), "dimension keys");
                        MuxAssert.Contains("s.textContent=t(\"us.t.by\",{metric:usageMetricLabel(),dim:dim})", html, "<Metric> by <dimension> tab label follows both pickers");
                        MuxAssert.Contains("b.textContent=t(\"us.t.breakdown\",{dim:dim})", html, "Usage by <dimension> tab label follows the picker");
                        MuxAssert.Contains("usageState.dim=this.value", html, "picker change sets the shared dimension");
                        MuxAssert.Contains("USAGE_OTHER_COLOR=\"#9ca3af\"", html, "Other is a neutral grey");
                        MuxAssert.Contains("return rows.slice(0,10);", html, "breakdown shows at most ten rows");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ApiWiring", "Each chart calls its API path with the filters (errors, timeseries/split with top=6, breakdown, events with from/to)", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/timeseries?\"+usageQuery())", html, "total series");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/errors?\"+usageQuery())", html, "failure reasons");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/timeseries/split?\"+usageQuery(dim+\"&top=6\"))", html, "split series");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/breakdown?\"+usageQuery(dim))", html, "breakdown");
                        MuxAssert.Contains("var dim=\"&dimension=\"+encodeURIComponent(usageState.dim);", html, "dimension parameter");
                        MuxAssert.Contains("var q=usageQuery(\"&page=1&pageSize=500\",usageState.drill);", html, "history grid takes the drill window");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/events?\"+q)", html, "history grid calls events");
                        MuxAssert.Contains("p+=usageFilterQuery();", html, "filters apply to every query, the drill window included");
                        MuxAssert.Contains("api(\"/v1.0/api/usage/heatmap?\"+usageQuery(\"&tzOffsetMinutes=\"+new Date().getTimezoneOffset()))", html, "heatmap route with the browser's UTC offset");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "FailureReasonsAndDrillDown", "Failure-reasons container, empty states, and the click/keyboard drill-down with a removable chip", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("id=\"us_failures\"", html, "failure reasons container");
                        MuxAssert.Contains("t(\"us.noFailures\")", html, "empty failure list message");
                        MuxAssert.Contains("t(\"us.empty\")", html, "empty chart message");
                        MuxAssert.Contains("id=\"us_drill\"", html, "drill chip host above the grid");
                        MuxAssert.Contains("function drillUsageBucket(buckets,idx)", html, "drill function");
                        MuxAssert.Contains("usageState.drill={from:from,to:from+w-1};", html, "window is [start, start + width - 1]");
                        MuxAssert.Contains("plot.addEventListener(\"click\",function(ev){drillUsageBucket(buckets,idxAt(ev));});", html, "click drills");
                        MuxAssert.Contains("k===\"Enter\"||k===\" \"", html, "Enter or Space drills from the keyboard");
                        MuxAssert.Contains("k===\"ArrowLeft\"", html, "arrow keys move the column cursor");
                        MuxAssert.Contains("id=\"us_plot\" tabindex=\"0\"", html, "plot is focusable");
                        MuxAssert.Contains("aria-live=\"polite\"", html, "focused column announced");
                        MuxAssert.Contains("id=\"us_drill_clear\"", html, "chip has a clear control");
                        MuxAssert.Contains("function clearUsageDrill(){usageState.drill=null;", html, "clearing restores the grid");
                        MuxAssert.Contains("head.scrollIntoView(", html, "grid scrolls into view");
                        MuxAssert.Contains("drillHeatmapCell(hm,i)", html, "heatmap cells drill too");
                        MuxAssert.Contains("usageState.drill={from:from,to:from+lay.BucketMs-1};", html, "a heatmap cell's window is [start, start + width - 1]");
                        MuxAssert.Contains("usageState.drill=null;refreshUsage();", html, "a range or filter change drops the drill window");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "LongRangeLabels", "Quarter, year, and all use date labels, and All derives its bucket width from the series", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("var USAGE_RANGE_MS={hour:60000,day:900000,week:7200000,month:43200000,quarter:86400000,year:604800000};", html, "fixed widths");
                        MuxAssert.Contains("return buckets[1].BucketStartUnixMs-buckets[0].BucketStartUnixMs;", html, "width from the first two buckets");
                        MuxAssert.Contains("if(rangeId===\"year\")return d.toLocaleDateString([],{month:\"short\",year:\"numeric\"});", html, "year axis shows month and year");
                        MuxAssert.Contains("if(rangeId===\"all\"){", html, "all picks a format from width and span");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "Localized", "Every new string is in all eleven page packs and in DashboardStrings for every locale, with real translations", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        Dictionary<string, Dictionary<string, string>> catalog = DashboardStrings.Build();
                        MuxAssert.AreEqual(11, DashboardStrings.Locales.Count, "eleven locales");
                        foreach (string key in _NewKeys)
                        {
                            MuxAssert.AreEqual(11, Regex.Matches(html, "\"" + Regex.Escape(key) + "\":").Count, key + " in every page pack");
                            foreach (string locale in DashboardStrings.Locales)
                            {
                                MuxAssert.IsTrue(catalog[locale].TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), locale + ":" + key);
                            }
                        }

                        foreach (string key in new[] { "us.failures", "us.noFailures", "us.empty", "us.drill.hint", "us.r.quarter", "us.dim.callkind", "us.m.calls", "us.mode.lines", "us.exp.csv", "us.exp.png", "us.exp.none", "us.metric.tip" })
                        {
                            foreach (string locale in DashboardStrings.Locales)
                            {
                                if (locale == "en") continue;
                                MuxAssert.AreNotEqual(catalog["en"][key], catalog[locale][key], locale + ":" + key + " is translated");
                                MuxAssert.Contains("\"" + key + "\":" + JsonSerializer.Serialize(catalog[locale][key], new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), html, locale + ":" + key + " matches the page pack");
                            }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ScriptParses", "The dashboard's inline JavaScript parses under node --check (skipped without node)", (CancellationToken ct) =>
                    {
                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        string dir = Path.Combine(Path.GetTempPath(), "mux_dashjs_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        try
                        {
                            StringBuilder all = new StringBuilder();
                            foreach (Match script in Regex.Matches(html, "<script(?![^>]*\\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                            {
                                all.Append(script.Groups[1].Value).Append(";\n");
                            }

                            string file = Path.Combine(dir, "dashboard.js");
                            File.WriteAllText(file, all.ToString());
                            ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                            info.ArgumentList.Add("--check");
                            info.ArgumentList.Add(file);
                            using Process process = Process.Start(info)!;
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit(30000);
                            MuxAssert.AreEqual(0, process.ExitCode, "dashboard JavaScript parses: " + error);
                        }
                        finally
                        {
                            TryDeleteDirectory(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapShades", "The heatmap uses themed, validated green steps, draws no usage as a grey fill with no border, and its quartile scale matches Mux.Core's (node part skipped without node)", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.IsFalse(html.Contains("#2d3034", StringComparison.Ordinal), "no near-black empty fill");
                        MuxAssert.IsFalse(html.Contains("░▒▓█", StringComparison.Ordinal), "the legend shows real swatches, not block glyphs");
                        MuxAssert.Contains("--heat-1:#6cc596", html, "light ramp");
                        MuxAssert.Contains("--heat-1:#1f6b48", html, "dark ramp");
                        MuxAssert.Contains(".uhm-cell.l0,.uhm-key.l0{background:var(--heat-0)}", html, "no usage is a solid grey fill");
                        MuxAssert.Contains("--heat-0:#e3e7eb", html, "light no-usage grey");
                        MuxAssert.Contains("--heat-0:#2e3640", html, "dark no-usage grey");
                        MuxAssert.IsFalse(Regex.IsMatch(html, "\\.uhm-(cell|key)[^{]*\\{[^}]*inset"), "no outline on heatmap cells or swatches");
                        MuxAssert.Contains(".uhm-cell:focus-visible{", html, "keyboard focus still shows");
                        MuxAssert.Contains("class=\"uhm-cell l'+level+(", html, "cells take their level class");

                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;
                        string scaleJs = Between(html, "function heatmapScale(values){", "\nfunction tokenHeatmap(");
                        double[] values = { 0, 0, 10, 20, 30, 40, 50, 60, 70, 80, 10000, 5, 5, 0.5 };
                        string dir = Path.Combine(Path.GetTempPath(), "mux_dashheat_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        try
                        {
                            string file = Path.Combine(dir, "scale.js");
                            File.WriteAllText(file, scaleJs + "\nvar v=" + JsonSerializer.Serialize(values)
                                + ";var s=heatmapScale(v);console.log(v.map(function(x){return s.level(x);}).join(','));");
                            ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                            info.ArgumentList.Add(file);
                            using Process process = Process.Start(info)!;
                            string output = process.StandardOutput.ReadToEnd().Trim();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit(30000);
                            MuxAssert.AreEqual(0, process.ExitCode, "heatmap scale runs: " + error);
                            UsageHeatmapScale scale = UsageHeatmapScale.FromValues(values);
                            MuxAssert.AreEqual(string.Join(",", values.Select(v => scale.LevelOf(v))), output, "web levels match Mux.Core's");
                        }
                        finally
                        {
                            TryDeleteDirectory(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapDrawsBothAxes", "The heatmap draws labeled rows and columns, an axis caption, future cells blank, and thins dense row labels (skipped without node)", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("class=\"uhm-y\"", html, "row labels");
                        MuxAssert.Contains("class=\"uhm-x\"", html, "column labels");
                        MuxAssert.Contains("t(\"us.hm.axes\",{rows:t(\"us.hm.row.\"+lay.RowUnit),cols:t(\"us.hm.col.\"+lay.ColumnKind)})", html, "localized axis caption");
                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;
                        string js = Between(html, "function heatmapScale(values){", "\nfunction niceMax(");
                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        string dir = Path.Combine(Path.GetTempPath(), "mux_dashhm_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        try
                        {
                            List<string> outputs = new List<string>();
                            foreach ((UsageRange range, int offset) in new[] { (UsageRange.Week, -300), (UsageRange.Month, 0), (UsageRange.Quarter, 330) })
                            {
                                UsageHeatmap heatmap = new UsageHeatmap(UsageHeatmapLayout.Compute(range, now, offset));
                                for (int i = 0; i < heatmap.TotalTokens.Length; i++) heatmap.TotalTokens[i] = (i * 37) % 11 * 100;
                                string file = Path.Combine(dir, "hm.js");
                                File.WriteAllText(file, "var LOCALE='en',usageState={drill:null};function t(k,p){var s={'us.hm.axes':'Rows: {rows} · Columns: {cols}'}[k]||k;if(p)for(var x in p)s=s.split('{'+x+'}').join(p[x]);return s;}"
                                    + "function esc(s){return (s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}function usAttr(s){return esc(''+s).replace(/\"/g,'&quot;');}\n"
                                    + js + "\nconsole.log(tokenHeatmap(" + JsonSerializer.Serialize(heatmap) + ",900));");
                                ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                                info.ArgumentList.Add(file);
                                using Process process = Process.Start(info)!;
                                string output = process.StandardOutput.ReadToEnd();
                                string error = process.StandardError.ReadToEnd();
                                process.WaitForExit(30000);
                                MuxAssert.AreEqual(0, process.ExitCode, "heatmap renders for " + range + ": " + error);
                                outputs.Add(output);
                                MuxAssert.Contains("Rows: us.hm.row." + heatmap.Layout.RowUnit + " · Columns: us.hm.col." + heatmap.Layout.ColumnKind, output, "caption for " + range);
                                MuxAssert.AreEqual(heatmap.Layout.Rows * heatmap.Layout.Columns, Regex.Matches(output, "class=\"uhm-(cell|future)").Count, "one grid cell per layout cell for " + range);
                                MuxAssert.IsTrue(Regex.Matches(output, "class=\"uhm-y\"").Count >= 2, "row labels for " + range);
                                MuxAssert.IsTrue(Regex.Matches(output, "class=\"uhm-x\"").Count >= 2, "column labels for " + range);
                            }

                            MuxAssert.Contains(">Thu, 11/5<", outputs[0], "week rows start at local midnight in UTC-5 (Thursday the 5th)");
                            MuxAssert.Contains(">Wed, 11/11<", outputs[0], "the last week row is local today");
                            MuxAssert.Contains(">00:00<", outputs[0], "week columns are times of day");
                            MuxAssert.Contains(">22:00<", outputs[0], "every two-hour column labeled at 900 pixels");
                            // Month: a 5-week calendar from Monday the 12th, each weekday labeled once over its two half-days,
                            // and the cells before the last 30 days (Monday the 12th) and after now blank and not clickable.
                            MuxAssert.AreEqual(5, Regex.Matches(outputs[1], "class=\"uhm-y\"").Count, "every month week labeled");
                            MuxAssert.Contains(">Oct 12, 2026<", outputs[1], "the first month row is the week starting Monday the 12th");
                            MuxAssert.Contains(">Nov 9, 2026<", outputs[1], "the last month row is this week");
                            MuxAssert.AreEqual(7, Regex.Matches(outputs[1], "class=\"uhm-x\"").Count, "one column label per weekday");
                            MuxAssert.AreEqual(7, Regex.Matches(outputs[1], "class=\"uhm-x\" style=\"grid-row:1;grid-column:\\d+ / span 2\"").Count, "each weekday label spans its AM and PM");
                            foreach (string day in new[] { ">Mon<", ">Wed<", ">Sun<" }) MuxAssert.Contains(day, outputs[1], "month columns are weekdays: " + day);
                            MuxAssert.AreEqual(60, Regex.Matches(outputs[1], "class=\"uhm-cell ").Count, "60 half-days in the window");
                            MuxAssert.AreEqual(10, Regex.Matches(outputs[1], "class=\"uhm-future\"").Count, "2 half-days before the window and 8 after now are blank");
                            MuxAssert.IsFalse(Regex.IsMatch(outputs[1], "data-idx=\"[01]\""), "cells before the window are not clickable");
                            foreach (string day in new[] { ">Mon<", ">Tue<", ">Sun<" }) MuxAssert.Contains(day, outputs[2], "quarter columns are weekdays: " + day);
                            MuxAssert.Contains("class=\"uhm-future\"", outputs[2], "days after today are blank");
                        }
                        finally
                        {
                            TryDeleteDirectory(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MetricPickerAndLinesToggle", "The <Metric> by chart has a Tokens/Calls/Cost picker shared with Usage by, and the stacked charts toggle between bars and lines", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        string tabs = Between(html, "id=\"us_tabs\"", "</div>");
                        List<string> metrics = new List<string>();
                        foreach (Match m in Regex.Matches(Between(tabs, "id=\"us_metric\"", "</select>"), "<option value=\"([a-z]+)\"")) metrics.Add(m.Groups[1].Value);
                        MuxAssert.AreEqual("tokens,calls,cost", string.Join(",", metrics), "metric keys, Tokens first");
                        MuxAssert.IsTrue(tabs.IndexOf("id=\"us_metric\"", StringComparison.Ordinal) < tabs.IndexOf("id=\"us_dim\"", StringComparison.Ordinal), "metric picker sits next to the dimension picker");
                        MuxAssert.Contains("metric:\"tokens\",mode:\"bars\"", html, "defaults: Tokens and stacked bars");
                        MuxAssert.Contains("usageState.metric=this.value;relabelUsageTabs();drawUsageChart();", html, "a metric change redraws without a reload");
                        MuxAssert.Contains("vals.push(b&&b.Metrics?usageMetricOf(b.Metrics):0);", html, "split series carry the chosen metric");
                        MuxAssert.Contains("metric===\"calls\"?(m.Calls||0):(metric===\"cost\"?(m.CostUsd||0):(m.TotalTokens||0))", html, "calls, cost, or total tokens per bucket");
                        MuxAssert.Contains("function breakdownRows(rows){", html, "Usage by sorts its rows");
                        MuxAssert.Contains("usageMetricOf(b.Metrics,metric)-usageMetricOf(a.Metrics,metric)", html, "Usage by sorts by the chosen metric");
                        MuxAssert.Contains("var line=t(\"us.bd.line\",{tok:fmtTok(m.TotalTokens),cost:fmtUsd(m.CostUsd),calls:(m.Calls||0).toLocaleString()});", html, "Usage by still labels tokens, cost, and calls");

                        string tool = Between(html, "id=\"us_tool\"", "</div>");
                        MuxAssert.Contains("data-mode=\"bars\" class=\"active\" aria-pressed=\"true\"", tool, "bars is the default");
                        MuxAssert.Contains("data-mode=\"lines\" aria-pressed=\"false\"", tool, "lines toggle");
                        MuxAssert.Contains("var USAGE_STACK_TABS={calls:1,tokens:1,split:1,cost:1};", html, "the toggle shows on Calls, Tokens, <Metric> by, and Cost only");
                        MuxAssert.Contains("(cfg.kind===\"dist\")?distChart(buckets,cfg,usageState.range):(usageState.mode===\"lines\"?lineChart(buckets,cfg,usageState.range):barChart(buckets,cfg,usageState.range))", html, "box plots are unaffected");
                        MuxAssert.Contains(".uplot .uline{fill:none;stroke-width:2;vector-effect:non-scaling-stroke", html, "2px lines");
                        MuxAssert.Contains("if(n<=48)pts.forEach(", html, "point markers on short series");
                        MuxAssert.Contains("return plotFrameHtml(svg,cfg,yl,buckets,rangeId);\n}\n/* Distribution", html, "lines reuse the plot frame (tooltip, cursor, drill-down)");

                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;
                        string js = UsageRegion(html) + "\nusageState.range='all';"
                            + "var bk=[0,1,2].map(function(i){return {BucketStartUnixMs:1000*i,Metrics:{Calls:2+i,Errors:0,InputTokens:10*i,CachedTokens:0,OutputTokens:0,TotalTokens:10*i,CostUsd:0.5}};});"
                            + "var cfg={kind:'bar',fmt:function(v){return ''+v;},series:[{n:'a',c:'#111',g:function(m){return m.Calls;}},{n:'b',c:'#222',g:function(m){return m.TotalTokens;}}]};"
                            + "var out=lineChart(bk,cfg,'all');"
                            + "console.log(JSON.stringify({paths:(out.match(/class=\"uline\"/g)||[]).length,dots:(out.match(/class=\"udot\"/g)||[]).length,rects:(out.match(/<rect/g)||[]).length,top:(out.match(/<span>([^<]*)<\\/span>/)||[])[1]}));";
                        using JsonDocument doc = JsonDocument.Parse(RunNode(node, js, null));
                        MuxAssert.AreEqual(2, doc.RootElement.GetProperty("paths").GetInt32(), "one line per series");
                        MuxAssert.AreEqual(6, doc.RootElement.GetProperty("dots").GetInt32(), "a marker per bucket per series");
                        MuxAssert.AreEqual(0, doc.RootElement.GetProperty("rects").GetInt32(), "no bars in lines mode");
                        MuxAssert.AreEqual("20", doc.RootElement.GetProperty("top").GetString(), "lines are not stacked: the axis tops out at the largest single value");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ExportControls", "Export CSV and Save PNG sit in the chart toolbar, download through a Blob link, are disabled without data, and name files mux-usage-<chart>-<range>-<stamp>", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        string tool = Between(html, "id=\"us_tool\"", "</div>");
                        MuxAssert.Contains("id=\"us_csv\" data-i18n=\"us.exp.csv\" data-i18n-title=\"us.exp.csv.tip\"", tool, "Export CSV button, localized");
                        MuxAssert.Contains("id=\"us_png\" data-i18n=\"us.exp.png\" data-i18n-title=\"us.exp.png.tip\"", tool, "Save PNG button, localized");
                        MuxAssert.Contains("function syncUsageTools(){var ok=!!usageCsvTable();", html, "export follows the chart's data");
                        MuxAssert.Contains("b.disabled=!ok;", html, "charts with no data disable export");
                        MuxAssert.Contains("function drawUsageChart(){drawUsageChartBody();syncUsageTools();}", html, "every redraw resyncs the buttons");
                        MuxAssert.Contains("URL.createObjectURL(blob)", html, "downloads through a Blob link");
                        MuxAssert.Contains("new Blob([usageCsv(tb.header,tb.rows)],{type:\"text/csv;charset=utf-8\"})", html, "CSV blob");
                        MuxAssert.Contains("c.width=cap.width*2;c.height=cap.height*2;", html, "PNG renders at 2x");
                        MuxAssert.Contains("c.toBlob(function(b){done(b);},\"image/png\")", html, "PNG blob");
                        MuxAssert.Contains("var USAGE_PNG_SKIP=\".utip,.ucur,.usr,.chartnote,.ufail\";", html, "the PNG leaves out the tooltip, cursor, and hints");

                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;
                        string js = UsageRegion(html) + "\nvar out=[];"
                            + "[['calls','day','model','tokens'],['split','week','endpoint','calls'],['breakdown','all','callkind','cost'],['stream','hour','model','tokens'],['heatmap','quarter','model','tokens']].forEach(function(c){"
                            + "usageState.tab=c[0];usageState.range=c[1];usageState.dim=c[2];usageState.metric=c[3];out.push(usageExportName('csv'));});"
                            + "usageState.tab='latency';usageState.loaded={};out.push(String(usageCsvTable()));"
                            + "usageState.loaded={buckets:true};usageState.buckets=[{BucketStartUnixMs:0,Metrics:{Calls:0}}];out.push(String(usageCsvTable()));"
                            + "console.log(JSON.stringify(out));";
                        using JsonDocument doc = JsonDocument.Parse(RunNode(node, js, null));
                        string[] names = doc.RootElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
                        string[] expect = { "calls-day", "calls-by-endpoint-week", "usage-by-callkind-all", "streaming-hour", "heatmap-quarter" };
                        for (int i = 0; i < expect.Length; i++)
                        {
                            MuxAssert.IsTrue(Regex.IsMatch(names[i], "^mux-usage-" + Regex.Escape(expect[i]) + "-\\d{8}-\\d{6}\\.csv$"), "file name " + names[i]);
                        }

                        MuxAssert.AreEqual("null", names[5], "nothing loaded: nothing to export");
                        MuxAssert.AreEqual("null", names[6], "no calls in range: nothing to export");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "CsvMatchesCore", "The web CSV (and the VS Code CSV when it is compiled) is byte-identical to Mux.Core's UsageCsv for every chart, across a DST change (node part skipped without node)", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("function usageCsv(header,rows){", html, "web CSV writer");
                        string? node = FindOnPath("node");
                        if (node == null) return Task.CompletedTask;

                        // Four six-hour buckets across the end of US daylight saving time (2026-11-01 06:00 UTC).
                        TimeZoneInfo? zone = null;
                        string tz = "America/New_York";
                        try { zone = TimeZoneInfo.FindSystemTimeZoneById(tz); }
                        catch (Exception) { tz = "UTC"; zone = TimeZoneInfo.Utc; }
                        long t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), width = 6 * 3_600_000L;
                        double[] costs = { 0.1234567, 1.5, 0.0000004, 2.0000005 };
                        List<UsageBucket> buckets = new List<UsageBucket>();
                        for (int i = 0; i < 4; i++)
                        {
                            UsageMetrics m = new UsageMetrics { Calls = 10 + i, Errors = i, InputTokens = 1000 * (i + 1), CachedTokens = 100 * i, OutputTokens = 50 + i, CostUsd = costs[i] };
                            m.TotalTokens = m.InputTokens + m.OutputTokens;
                            UsageDistribution d = i == 2 ? new UsageDistribution() : new UsageDistribution { Min = 1.25, Avg = 10.0 / 3, P95 = 40, P99 = 41.5, Max = 99.9999999, Count = 3 + i };
                            m.TotalMsDist = d; m.TtftMsDist = d; m.StreamMsDist = d; m.ThroughputDist = d;
                            buckets.Add(new UsageBucket { BucketStartUnixMs = t0 + (i * width), Metrics = m });
                        }

                        UsageSeries Series(string key, bool other, int scale)
                        {
                            UsageSeries s = new UsageSeries { Key = key, IsOther = other };
                            for (int i = 0; i < 4; i++)
                            {
                                s.Buckets.Add(new UsageBucket { BucketStartUnixMs = t0 + (i * width), Metrics = new UsageMetrics { Calls = scale + i, TotalTokens = (scale * 100) + i, CostUsd = (scale * 0.01) + (i / 3.0) } });
                            }

                            return s;
                        }

                        List<UsageSeries> split = new List<UsageSeries> { Series("gpt-4o, mini", false, 7), Series(" spaced", false, 5), Series("=say \"hi\"", false, 3), Series("Other", true, 1) };
                        UsageBreakdownRow Row(string value, long calls, long input, double cost)
                        {
                            UsageMetrics m = new UsageMetrics { Calls = calls, Errors = calls / 3, InputTokens = input, CachedTokens = input / 4, OutputTokens = input / 10, CostUsd = cost };
                            m.TotalTokens = m.InputTokens + m.OutputTokens;
                            return new UsageBreakdownRow { Value = value, Metrics = m };
                        }

                        // Rows already in the chart's Tokens order (total tokens descending).
                        List<UsageBreakdownRow> breakdown = new List<UsageBreakdownRow> { Row("Primary", 40, 90_000, 1.75), Row("compaction, summary", 9, 12_000, 0.0125), Row("-title", 3, 800, 0.0000015) };
                        long now = t0 + (2L * 86_400_000L) + (13L * 3_600_000L);
                        UsageHeatmap heatmap = new UsageHeatmap(UsageHeatmapLayout.Compute(UsageRange.Week, now, (int)zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(now)).TotalMinutes));
                        for (int i = 0; i < heatmap.TotalTokens.Length; i++) { heatmap.TotalTokens[i] = (i * 37) % 11 * 100; heatmap.Calls[i] = (i * 7) % 5; }
                        long from = t0 - 3_600_000L, to = t0 + (4 * width);
                        List<long> starts = buckets.Select(b => b.BucketStartUnixMs).ToList();

                        Dictionary<string, string> expected = new Dictionary<string, string>
                        {
                            ["calls"] = UsageCsv.Calls(buckets, width, zone),
                            ["tokens"] = UsageCsv.Tokens(buckets, width, zone),
                            ["cost"] = UsageCsv.Cost(buckets, width, zone),
                            ["latency"] = UsageCsv.Distribution(buckets, width, m => m.TotalMsDist, zone),
                            ["ttft"] = UsageCsv.Distribution(buckets, width, m => m.TtftMsDist, zone),
                            ["streaming"] = UsageCsv.Distribution(buckets, width, m => m.StreamMsDist, zone),
                            ["throughput"] = UsageCsv.Distribution(buckets, width, m => m.ThroughputDist, zone),
                            ["split:tokens"] = UsageCsv.Split(starts, width, split, "tokens", zone),
                            ["split:calls"] = UsageCsv.Split(starts, width, split, "calls", zone),
                            ["split:cost"] = UsageCsv.Split(starts, width, split, "cost", zone),
                            ["breakdown"] = UsageCsv.UsageBy(from, to, "callkind", breakdown, zone),
                            ["heatmap"] = UsageCsv.Heatmap(heatmap, zone)
                        };
                        double[] numbers = { 0, -0.0, 1, -1, 12345678901, 0.1, 0.1234565, 0.0000005, 0.0000004, -2.5, 1e-7, 123.4560001, 2.0000005, 99.9999999, double.NaN, double.PositiveInfinity, 1.0 / 3 };
                        string[] cells = { "plain", "a,b", "say \"hi\"", " lead", "trail\t", "line\nbreak", "cr\r", "", "Other", "=HYPERLINK(1)", "-x", "-", "-3.5", "-.5", "=a,b", "\tx", "+1", "@me", "\rx", "a=b", "-\u0663" };

                        JsonSerializerOptions json = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
                        string fixture = JsonSerializer.Serialize(new { buckets, split, breakdown, heatmap, from, to, numbers, cells }, json);
                        string vscodeModule = Path.Combine(FindRepoRoot(), "src", "Mux.VSCode", "out", "src", "manage", "usageCharts.js");
                        bool haveVsCode = File.Exists(vscodeModule);
                        string js = UsageRegion(html) + "\nvar F=JSON.parse(" + JsonSerializer.Serialize(fixture) + ",function(k,v){return v===\"NaN\"?NaN:(v===\"Infinity\"?Infinity:v);});"
                            + "usageState.range='all';usageState.dim='callkind';usageState.buckets=F.buckets;usageState.split=F.split;usageState.breakdown=F.breakdown;usageState.heatmap=F.heatmap;"
                            + "usageState.summary={FromUnixMs:F.from,ToUnixMs:F.to};usageState.loaded={buckets:true,split:true,breakdown:true,heatmap:true};"
                            + "var web={},vs={},cases=[['calls','calls','tokens'],['tokens','tokens','tokens'],['cost','cost','tokens'],['latency','latency','tokens'],['ttft','ttft','tokens'],['streaming','stream','tokens'],['throughput','throughput','tokens'],"
                            + "['split:tokens','split','tokens'],['split:calls','split','calls'],['split:cost','split','cost'],['breakdown','breakdown','tokens'],['heatmap','heatmap','tokens']];"
                            + "cases.forEach(function(c){usageState.tab=c[1];usageState.metric=c[2];var tb=usageCsvTable();web[c[0]]=tb?usageCsv(tb.header,tb.rows):null;});"
                            + (haveVsCode
                                ? "var U=require(" + JsonSerializer.Serialize(vscodeModule) + ");cases.forEach(function(c){var tb=U.usageCsvTable(c[1]==='stream'?'streaming':c[1],{range:'all',metric:c[2],dimension:'callkind',buckets:F.buckets,split:F.split,breakdown:F.breakdown,heatmap:F.heatmap,fromUnixMs:F.from,toUnixMs:F.to});vs[c[0]]=tb?U.usageCsv(tb.header,tb.rows):null;});"
                                  + "vs.numbers=F.numbers.map(U.csvNumber);vs.cells=F.cells.map(U.csvField);"
                                : string.Empty)
                            + "web.numbers=F.numbers.map(csvNum);web.cells=F.cells.map(csvField);console.log(JSON.stringify({web:web,vs:vs}));";
                        using JsonDocument doc = JsonDocument.Parse(RunNode(node, js, tz));
                        foreach (string surface in haveVsCode ? new[] { "web", "vs" } : new[] { "web" })
                        {
                            JsonElement got = doc.RootElement.GetProperty(surface);
                            foreach (KeyValuePair<string, string> pair in expected)
                            {
                                MuxAssert.AreEqual(pair.Value, got.GetProperty(pair.Key).GetString(), surface + " " + pair.Key + " CSV matches UsageCsv");
                            }

                            MuxAssert.AreEqual(string.Join("|", numbers.Select(UsageCsv.FormatNumber)), string.Join("|", got.GetProperty("numbers").EnumerateArray().Select(e => e.GetString())), surface + " numbers match FormatNumber");
                            MuxAssert.AreEqual(string.Join("|", cells.Select(UsageCsv.Escape)), string.Join("|", got.GetProperty("cells").EnumerateArray().Select(e => e.GetString())), surface + " cells match Escape");
                        }

                        MuxAssert.Contains("\n2026-10-31T20:00:00-04:00,2026-11-01T01:00:00-05:00,", expected["calls"], "a daylight-time bucket ending in standard time");
                        MuxAssert.Contains("\n2026-11-01T01:00:00-05:00,2026-11-01T07:00:00-05:00,", expected["calls"], "the next bucket starts in standard time");
                        MuxAssert.Contains("\"gpt-4o, mini\",\" spaced\",\"'=say \"\"hi\"\"\",Other\n", expected["split:tokens"], "quoted and formula-guarded series headers, Other last");
                        MuxAssert.Contains(",'-title,", expected["breakdown"], "a formula-like value gets an apostrophe");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "LiveServerServesChartsAndDrillWindow", "A live server over a seeded store serves the page and answers every call the usage view makes, including a bucket window", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_dashusage_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        try
                        {
                            using SqliteUsageStore store = new SqliteUsageStore(Path.Combine(dir, "usage.db"), 90, 5_000_000);
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            UsageEvent failed = Event(now - 3000, "claude-opus", "anthropic-endpoint");
                            failed.Success = false;
                            failed.ErrorCode = "http_429";
                            UsageEvent old = Event(now - (40L * 86_400_000L), "gpt-4o", "openai-endpoint");
                            store.InsertBatchAsync(new List<UsageEvent> { Event(now - 1000, "gpt-4o", "openai-endpoint"), failed, old }, ct).GetAwaiter().GetResult();
                            UsageQueryService query = new UsageQueryService(store, () => new PricingTable { Version = "t", Models = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase) });

                            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = null };
                            MuxServer? server = null;
                            int port = 0;
                            for (int attempt = 0; attempt < 10 && server == null; attempt++)
                            {
                                port = FreeLoopbackPort();
                                rest.Port = port;
                                MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(dir, "sessions")), () => new List<EndpointConfig>(), null, query, null);
                                try { candidate.Start(); server = candidate; }
                                catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                            }

                            MuxAssert.IsNotNull(server, "server bound");
                            string baseUrl = "http://127.0.0.1:" + port;
                            try
                            {
                                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                                string page = GetWithRetry(http, baseUrl + "/dashboard");
                                MuxAssert.Contains("data-range=\"quarter\"", page, "served page has the new ranges");
                                MuxAssert.Contains("data-tab=\"breakdown\"", page, "served page has the new tabs");
                                MuxAssert.Contains("id=\"us_drill\"", page, "served page has the drill chip host");

                                using (JsonDocument heat = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/heatmap?range=week&tzOffsetMinutes=300&endpoint=anthropic-endpoint")))
                                {
                                    JsonElement layout = heat.RootElement.GetProperty("Layout");
                                    MuxAssert.AreEqual(-300, layout.GetProperty("UtcOffsetMinutes").GetInt32(), "JavaScript offset applied");
                                    MuxAssert.AreEqual(7, layout.GetProperty("Rows").GetInt32(), "week day rows");
                                    long calls = 0;
                                    foreach (JsonElement v in heat.RootElement.GetProperty("Calls").EnumerateArray()) calls += v.GetInt64();
                                    MuxAssert.AreEqual(1L, calls, "endpoint filter applies to the heatmap");
                                }

                                using (JsonDocument errors = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/errors?range=day")))
                                {
                                    MuxAssert.AreEqual("http_429", errors.RootElement.GetProperty("Items")[0].GetProperty("ErrorCode").GetString(), "failure reason");
                                }

                                using (JsonDocument split = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/timeseries/split?range=quarter&dimension=provider&top=6")))
                                {
                                    MuxAssert.AreEqual(91, split.RootElement.GetProperty("Items")[0].GetProperty("Buckets").GetArrayLength(), "quarter split buckets");
                                }

                                using (JsonDocument bd = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/breakdown?range=all&dimension=model")))
                                {
                                    MuxAssert.AreEqual(2, bd.RootElement.GetProperty("Count").GetInt32(), "two models over all time");
                                }

                                using (JsonDocument all = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/events?range=all&page=1&pageSize=500")))
                                {
                                    MuxAssert.AreEqual(3L, all.RootElement.GetProperty("TotalCount").GetInt64(), "all calls without a window");
                                }

                                // The drill-down window for the newest day bucket: explicit from/to win over the range.
                                using (JsonDocument series = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/timeseries?range=day")))
                                {
                                    JsonElement items = series.RootElement.GetProperty("Items");
                                    long width = items[1].GetProperty("BucketStartUnixMs").GetInt64() - items[0].GetProperty("BucketStartUnixMs").GetInt64();
                                    MuxAssert.AreEqual(900_000L, width, "day bucket width");
                                    long from = now - 3000 - ((now - 3000) % width);
                                    long to = from + width - 1;
                                    using JsonDocument window = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/events?from=" + from + "&to=" + to + "&endpoint=anthropic-endpoint&page=1&pageSize=500"));
                                    MuxAssert.AreEqual(1L, window.RootElement.GetProperty("TotalCount").GetInt64(), "the bucket's calls with the endpoint filter");
                                    MuxAssert.AreEqual("http_429", window.RootElement.GetProperty("Items")[0].GetProperty("ErrorCode").GetString(), "the failed call");
                                }
                            }
                            finally
                            {
                                server!.Dispose();
                            }
                        }
                        finally
                        {
                            TryDeleteDirectory(dir);
                        }

                        return Task.CompletedTask;
                    })
                });
        }

        #endregion

        #region Private-Methods

        // The usage view's script, from its banner to the per-call history grid, with stubs for the page helpers it
        // calls (i18n, escaping, DOM lookup, toasts), so its chart and export functions run under node.
        private static string UsageRegion(string html)
        {
            string region = Between(html, "/* ================= Usage analytics ================= */", "/* History table");
            return "var LOCALE='en';function t(k,p){return k;}function esc(s){return ''+s;}function el(){return null;}function toast(){}\n" + region;
        }

        // Runs a script under node (optionally in a time zone) and returns its standard output.
        private static string RunNode(string node, string script, string? timeZone)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux_dashcsv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "run.js");
                File.WriteAllText(file, script);
                ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                info.ArgumentList.Add(file);
                if (timeZone != null) info.Environment["TZ"] = timeZone;
                using Process process = Process.Start(info)!;
                Task<string> error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);
                MuxAssert.AreEqual(0, process.ExitCode, "node script runs: " + error.Result);
                return output.Trim();
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        // The repository root: the nearest parent of the working or base directory that holds src/Mux.Server.
        private static string FindRepoRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo? d = new DirectoryInfo(start); d != null; d = d.Parent)
                {
                    if (Directory.Exists(Path.Combine(d.FullName, "src", "Mux.Server"))) return d.FullName;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        private static string Between(string text, string start, string end)
        {
            int a = text.IndexOf(start, StringComparison.Ordinal);
            MuxAssert.IsTrue(a >= 0, "found " + start);
            int b = text.IndexOf(end, a, StringComparison.Ordinal);
            MuxAssert.IsTrue(b > a, "found the end of " + start);
            return text.Substring(a, b - a);
        }

        private static UsageEvent Event(long ts, string model, string endpoint)
        {
            return new UsageEvent
            {
                TimestampUnixMs = ts,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = endpoint,
                AdapterType = model.StartsWith("gpt", StringComparison.Ordinal) ? "openai" : "anthropic",
                Model = model,
                InputTokens = 1000,
                OutputTokens = 200,
                TotalTokens = 1200,
                TimeToFirstTokenMs = 40,
                StreamingMs = 200,
                TotalMs = 240,
                TokensPerSecond = 10.0,
                Success = true
            };
        }

        private static string GetWithRetry(HttpClient http, string url)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    HttpResponseMessage r = http.GetAsync(url).GetAwaiter().GetResult();
                    if (r.IsSuccessStatusCode) return r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                }

                Thread.Sleep(50);
            }

            return string.Empty;
        }

        private static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string? FindOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(directory, name);
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
