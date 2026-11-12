namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Mux.Core.Telemetry;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// A scrollable list of the calls in one usage bucket (time, model, endpoint, tokens, latency, and status or
    /// error code), opened from a <see cref="UsageChartsModal"/> bucket. ↑/↓, PgUp/PgDn, and Home/End scroll; Esc or
    /// Enter closes it and returns to the charts. It renders over the charts in a centered box and performs no I/O.
    /// </summary>
    public sealed class UsageCallsModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;

        private readonly string _Title;
        private readonly List<UsageEventRow> _Rows;
        private readonly long _TotalCount;
        private readonly string _Error;
        private readonly bool _ShowDate;
        private int _Offset;
        private int _VisibleRows = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageCallsModal"/> class.
        /// </summary>
        /// <param name="title">The box title, such as "Calls 11/09 14:00–14:15".</param>
        /// <param name="page">The calls to list (newest first), or null for none.</param>
        /// <param name="error">An error to show instead of the list, or null.</param>
        /// <param name="showDate">True to include the date in each row's time (for buckets of a day or longer).</param>
        public UsageCallsModal(string title, UsageEventPage? page, string? error = null, bool showDate = false)
        {
            _Title = title ?? "Calls";
            _Rows = page?.Items ?? new List<UsageEventRow>();
            _TotalCount = Math.Max(page?.TotalCount ?? 0, _Rows.Count);
            _Error = error ?? string.Empty;
            _ShowDate = showDate;
        }

        #endregion

        #region Public-Members

        /// <summary>The number of calls listed.</summary>
        public int RowCount
        {
            get => _Rows.Count;
        }

        /// <summary>The index of the first visible row.</summary>
        public int Offset
        {
            get => _Offset;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            int max = Math.Max(0, _Rows.Count - _VisibleRows);
            switch (key.Code)
            {
                case KeyCode.Escape:
                case KeyCode.Enter:
                    Close(null);
                    return true;
                case KeyCode.Down:
                    _Offset = Math.Clamp(_Offset + 1, 0, max);
                    return true;
                case KeyCode.Up:
                    _Offset = Math.Clamp(_Offset - 1, 0, max);
                    return true;
                case KeyCode.PageDown:
                    _Offset = Math.Clamp(_Offset + _VisibleRows, 0, max);
                    return true;
                case KeyCode.PageUp:
                    _Offset = Math.Clamp(_Offset - _VisibleRows, 0, max);
                    return true;
                case KeyCode.Home:
                    _Offset = 0;
                    return true;
                case KeyCode.End:
                    _Offset = max;
                    return true;
                default:
                    return true;
            }
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            int screenW = surface.Size.Width, screenH = surface.Size.Height;
            if (screenW <= 0 || screenH <= 0) return;

            int boxW = Math.Min(screenW, Math.Max(20, Math.Min(120, screenW - 4)));
            int boxH = Math.Min(screenH, Math.Max(6, screenH - 4));
            Rect box = new Rect(Math.Max(0, (screenW - boxW) / 2), Math.Max(0, (screenH - boxH) / 2), boxW, boxH);
            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            CellStyle muted = CellStyle.Default.WithForeground(Color.FromPalette(8));
            CellStyle heading = CellStyle.Default.WithForeground(Color.FromPalette(6));
            int x = box.X + 1 + PadX, w = Math.Max(1, boxW - 2 - 2 * PadX);
            int top = box.Y + 1, footer = box.Y + boxH - 2;
            if (footer <= top) return;

            if (_Error.Length > 0)
            {
                surface.DrawText(x, top, UsageChartFormat.Trim(_Error, w), CellStyle.Default.WithForeground(Color.FromPalette(9)));
            }
            else if (_Rows.Count == 0)
            {
                surface.DrawText(x, top, UsageChartFormat.Trim("No calls in this bucket.", w), muted);
            }
            else
            {
                Columns(w, out int timeW, out int modelW, out int endpointW, out int tokensW, out int latencyW, out int statusW);
                surface.DrawText(x, top, UsageChartFormat.Trim(Row(timeW, modelW, endpointW, tokensW, latencyW, statusW, "Time", "Model", "Endpoint", "Tokens", "Latency", "Status"), w), heading);
                _VisibleRows = Math.Max(1, footer - top - 1);
                _Offset = Math.Clamp(_Offset, 0, Math.Max(0, _Rows.Count - _VisibleRows));
                for (int i = 0; i < _VisibleRows && _Offset + i < _Rows.Count; i++)
                {
                    UsageEventRow row = _Rows[_Offset + i];
                    string time = DateTimeOffset.FromUnixTimeMilliseconds(row.TimestampUnixMs).ToLocalTime()
                        .ToString(_ShowDate ? "MM/dd HH:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
                    string latency = row.TotalMs.HasValue ? UsageChartFormat.Ms(row.TotalMs.Value) : "-";
                    string status = Status(row);
                    string line = Row(timeW, modelW, endpointW, tokensW, latencyW, statusW, time, Blank(row.Model), Blank(row.EndpointName),
                        UsageChartFormat.Tokens(row.TotalTokens), latency, status);
                    CellStyle style = row.Success ? CellStyle.Default : CellStyle.Default.WithForeground(Color.FromPalette(1));
                    surface.DrawText(x, top + 1 + i, UsageChartFormat.Trim(line, w), style);
                }
            }

            string shown = _Rows.Count == 0 ? string.Empty
                : "calls " + (_Offset + 1) + "–" + Math.Min(_Rows.Count, _Offset + _VisibleRows) + " of " + _TotalCount
                  + (_TotalCount > _Rows.Count ? " (newest " + _Rows.Count + ")" : string.Empty) + " · ";
            surface.DrawText(x, footer, UsageChartFormat.Trim(shown + "↑/↓ PgUp/PgDn scroll · Esc back", w), muted);
        }

        #endregion

        #region Private-Methods

        private void Columns(int w, out int timeW, out int modelW, out int endpointW, out int tokensW, out int latencyW, out int statusW)
        {
            timeW = _ShowDate ? 15 : 9;
            tokensW = 8;
            latencyW = 8;
            int longest = 6;
            foreach (UsageEventRow row in _Rows) longest = Math.Max(longest, Status(row).Length);
            statusW = Math.Clamp(longest + 2, 8, Math.Max(8, Math.Min(26, w / 3)));
            int rest = Math.Max(2, w - timeW - tokensW - latencyW - statusW);
            modelW = Math.Max(1, rest * 3 / 5);
            endpointW = Math.Max(1, rest - modelW);
        }

        private static string Row(int timeW, int modelW, int endpointW, int tokensW, int latencyW, int statusW,
            string time, string model, string endpoint, string tokens, string latency, string status)
        {
            return Column(time, timeW) + Column(model, modelW) + Column(endpoint, endpointW)
                + Right(tokens, tokensW) + Right(latency, latencyW) + "  " + UsageChartFormat.Trim(status, statusW - 2);
        }

        private static string Column(string text, int width)
        {
            return UsageChartFormat.Trim(text, width - 1).PadRight(width);
        }

        private static string Right(string text, int width)
        {
            return UsageChartFormat.Trim(text, width - 1).PadLeft(width);
        }

        private static string Status(UsageEventRow row)
        {
            return row.Success ? "ok" : (string.IsNullOrWhiteSpace(row.ErrorCode) ? "failed" : row.ErrorCode!);
        }

        private static string Blank(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? "-" : text;
        }

        #endregion
    }
}
