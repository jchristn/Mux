namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Maps heatmap cell values to shade levels: 0 for no usage, and 1 to 4 by quartile of the non-zero values, so
    /// each shade holds about a quarter of the used cells. A linear scale (value / peak) would put nearly every cell
    /// in the faintest shade, because one busy bucket sets the peak and usage is heavily skewed. Every surface (the
    /// terminal, desktop, web dashboard, and VS Code) uses this rule; the web and VS Code scripts mirror it.
    /// </summary>
    public sealed class UsageHeatmapScale
    {
        #region Public-Members

        /// <summary>The number of shades for used cells (levels 1 to 4); level 0 means no usage.</summary>
        public const int Levels = 4;

        /// <summary>
        /// The inclusive upper bound of levels 1 to 3 (the 25th, 50th, and 75th percentile of the non-zero values,
        /// nearest rank). Level 4 holds everything above the last bound. Empty when there are no non-zero values.
        /// </summary>
        public IReadOnlyList<double> UpperBounds
        {
            get => _UpperBounds;
        }

        /// <summary>The largest value, or 0 when there are none.</summary>
        public double Peak
        {
            get => _Peak;
        }

        #endregion

        #region Private-Members

        private readonly double[] _UpperBounds;
        private readonly double _Peak;

        #endregion

        #region Constructors-and-Factories

        private UsageHeatmapScale(double[] upperBounds, double peak)
        {
            _UpperBounds = upperBounds;
            _Peak = peak;
        }

        /// <summary>
        /// Builds the scale for a set of cell values. Zero, negative, and non-finite values count as no usage.
        /// </summary>
        /// <param name="values">The cell values (for example total tokens per bucket). Null is treated as empty.</param>
        /// <returns>The scale.</returns>
        public static UsageHeatmapScale FromValues(IEnumerable<double>? values)
        {
            double[] used = (values ?? Array.Empty<double>())
                .Where(v => v > 0 && !double.IsInfinity(v) && !double.IsNaN(v))
                .OrderBy(v => v)
                .ToArray();
            if (used.Length == 0)
            {
                return new UsageHeatmapScale(Array.Empty<double>(), 0);
            }

            double[] bounds = new double[Levels - 1];
            for (int i = 0; i < bounds.Length; i++)
            {
                // Nearest-rank percentile: the smallest value with at least p of the used cells at or below it.
                double p = (i + 1) / (double)Levels;
                int rank = (int)Math.Ceiling(p * used.Length) - 1;
                bounds[i] = used[Math.Clamp(rank, 0, used.Length - 1)];
            }

            return new UsageHeatmapScale(bounds, used[used.Length - 1]);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the shade level of a value: 0 for no usage, <see cref="Levels"/> at or above the peak (so equal
        /// values all take the strongest shade), otherwise the first quartile whose upper bound holds it.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The level.</returns>
        public int LevelOf(double value)
        {
            if (double.IsNaN(value) || value <= 0)
            {
                return 0;
            }

            if (_UpperBounds.Length == 0 || value >= _Peak)
            {
                return Levels;
            }

            for (int i = 0; i < _UpperBounds.Length; i++)
            {
                if (value <= _UpperBounds[i])
                {
                    return i + 1;
                }
            }

            return Levels;
        }

        #endregion
    }
}
