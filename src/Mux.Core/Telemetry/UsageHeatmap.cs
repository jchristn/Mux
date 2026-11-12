namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The token heatmap for one range and filter: the shared clock-aligned <see cref="UsageHeatmapLayout"/> and one
    /// value per cell (row-major, oldest first). Returned by <c>GET /v1.0/api/usage/heatmap</c> and built in process
    /// by the desktop app and the terminal.
    /// </summary>
    public sealed class UsageHeatmap
    {
        #region Public-Members

        /// <summary>The grid: window, bucket width, rows, columns, cell starts, and axis labels.</summary>
        public UsageHeatmapLayout Layout { get; }

        /// <summary>Total tokens per cell in display order (row-major); <see cref="UsageHeatmapLayout.CellCount"/> entries.</summary>
        public long[] TotalTokens { get; }

        /// <summary>Calls per cell in display order (row-major); <see cref="UsageHeatmapLayout.CellCount"/> entries.</summary>
        public long[] Calls { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with zeroed cells.
        /// </summary>
        /// <param name="layout">The layout. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="layout"/> is null.</exception>
        public UsageHeatmap(UsageHeatmapLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            TotalTokens = new long[layout.CellCount];
            Calls = new long[layout.CellCount];
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Adds usage at a timestamp to the cell holding it; timestamps outside the grid are ignored.
        /// </summary>
        /// <param name="unixMs">The timestamp, Unix epoch milliseconds.</param>
        /// <param name="tokens">The tokens to add.</param>
        /// <param name="calls">The calls to add.</param>
        public void Add(long unixMs, long tokens, long calls)
        {
            int index = Layout.IndexOf(unixMs);
            if (index < 0) return;
            TotalTokens[index] += tokens;
            Calls[index] += calls;
        }

        /// <summary>Whether any cell recorded a call or a token.</summary>
        /// <returns>True when there is usage to draw.</returns>
        public bool HasUsage()
        {
            for (int i = 0; i < TotalTokens.Length; i++)
            {
                if (TotalTokens[i] > 0 || Calls[i] > 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Sums several heatmaps over the same layout (for example one per matching endpoint).
        /// </summary>
        /// <param name="layout">The shared layout. Required.</param>
        /// <param name="parts">The heatmaps to add; null entries and mismatched sizes are skipped.</param>
        /// <returns>The combined heatmap.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="layout"/> is null.</exception>
        public static UsageHeatmap Merge(UsageHeatmapLayout layout, IEnumerable<UsageHeatmap?>? parts)
        {
            UsageHeatmap merged = new UsageHeatmap(layout);
            foreach (UsageHeatmap? part in parts ?? Array.Empty<UsageHeatmap?>())
            {
                if (part == null || part.TotalTokens.Length != merged.TotalTokens.Length) continue;
                for (int i = 0; i < merged.TotalTokens.Length; i++)
                {
                    merged.TotalTokens[i] += part.TotalTokens[i];
                    merged.Calls[i] += part.Calls[i];
                }
            }

            return merged;
        }

        #endregion
    }
}
