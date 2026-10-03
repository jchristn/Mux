namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One metric measurement observed by <see cref="TelemetryCapture"/>: the instrument name, the value, and
    /// its tags rendered as strings.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        #region Private-Members

        private readonly string _Name;
        private readonly double _Value;
        private readonly Dictionary<string, string?> _Tags;

        #endregion

        #region Public-Members

        /// <summary>
        /// The instrument name.
        /// </summary>
        public string Name
        {
            get => _Name;
        }

        /// <summary>
        /// The measured value.
        /// </summary>
        public double Value
        {
            get => _Value;
        }

        /// <summary>
        /// The measurement tags (values rendered with <see cref="object.ToString"/>).
        /// </summary>
        public IReadOnlyDictionary<string, string?> Tags
        {
            get => _Tags;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a captured measurement.
        /// </summary>
        /// <param name="name">The instrument name. Must not be null.</param>
        /// <param name="value">The measured value.</param>
        /// <param name="tags">The tags. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or <paramref name="tags"/> is null.</exception>
        public CapturedMeasurement(string name, double value, Dictionary<string, string?> tags)
        {
            _Name = name ?? throw new ArgumentNullException(nameof(name));
            _Value = value;
            _Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether every <c>key=value</c> pair is present on this measurement.
        /// </summary>
        /// <param name="tagPairs">Pairs formatted as <c>key=value</c>.</param>
        /// <returns>True when all pairs match.</returns>
        public bool HasTags(params string[] tagPairs)
        {
            foreach (string pair in tagPairs)
            {
                int split = pair.IndexOf('=');
                string key = pair.Substring(0, split);
                string value = pair.Substring(split + 1);
                if (!_Tags.TryGetValue(key, out string? actual) || !string.Equals(actual, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
