namespace Mux.Core.Sessions
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A single key/value annotation attached to a session. Keys are normalized (lowercased and slugified by
    /// <see cref="SessionMetadataNormalizer"/>) so the same concept always filters as one facet; values are
    /// free-form UTF-8. Instances are compared by their (already-normalized) key, case-insensitively, because a
    /// session holds at most one value per key.
    /// </summary>
    public sealed class SessionTag : IEquatable<SessionTag>
    {
        #region Private-Members

        private string _Key = string.Empty;
        private string _Value = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The normalized tag key (for example <c>env</c>). Never null.
        /// </summary>
        [JsonPropertyName("key")]
        public string Key
        {
            get => _Key;
            set => _Key = value ?? string.Empty;
        }

        /// <summary>
        /// The tag value (for example <c>prod</c>). Free-form UTF-8; never null.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value
        {
            get => _Value;
            set => _Value = value ?? string.Empty;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new, empty <see cref="SessionTag"/> (required for JSON deserialization).
        /// </summary>
        public SessionTag()
        {
        }

        /// <summary>
        /// Initializes a new <see cref="SessionTag"/> with the supplied key and value. The caller is expected to
        /// have normalized them through <see cref="SessionMetadataNormalizer"/> already.
        /// </summary>
        /// <param name="key">The normalized key.</param>
        /// <param name="value">The value.</param>
        public SessionTag(string key, string value)
        {
            Key = key;
            Value = value;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Equals(SessionTag? other)
        {
            if (other is null) return false;
            return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as SessionTag);

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

        /// <inheritdoc />
        public override string ToString() => Key + ":" + Value;

        #endregion
    }
}
