namespace Mux.Core.Sessions
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// The single owner of the rules that decide what a valid session label or tag is, and how a raw
    /// user-typed string is canonicalized before it is stored or filtered on. Every surface (TUI, Desktop,
    /// Web, VS Code, headless) and the REST layer normalize through here so "what is a valid tag" is defined
    /// in exactly one place.
    ///
    /// <para>Rules: labels are free-form UTF-8, trimmed, capped at <see cref="MaxLabelLength"/>, and deduped
    /// case-insensitively (first-seen casing wins — that is the caller's job). Tag keys are trimmed,
    /// lowercased (invariant), and slugified to <c>[a-z0-9._-]</c> with internal whitespace runs collapsed to a
    /// single hyphen, then capped at <see cref="MaxTagKeyLength"/>. Tag values are free-form UTF-8, trimmed,
    /// capped at <see cref="MaxTagValueLength"/>. Empty results are rejected.</para>
    /// </summary>
    public static class SessionMetadataNormalizer
    {
        #region Public-Members

        /// <summary>The maximum length of a normalized label, in characters.</summary>
        public const int MaxLabelLength = 128;

        /// <summary>The maximum length of a normalized tag key, in characters.</summary>
        public const int MaxTagKeyLength = 64;

        /// <summary>The maximum length of a tag value, in characters.</summary>
        public const int MaxTagValueLength = 256;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalizes a raw label. Trims surrounding whitespace and collapses internal whitespace runs to a
        /// single space; the result must be non-empty and no longer than <see cref="MaxLabelLength"/>.
        /// </summary>
        /// <param name="raw">The raw label as typed.</param>
        /// <param name="label">The normalized label when the method returns true; otherwise empty.</param>
        /// <param name="error">A human-readable reason when the method returns false; otherwise null.</param>
        /// <returns>True when the label is valid; false otherwise.</returns>
        public static bool TryNormalizeLabel(string? raw, out string label, out string? error)
        {
            label = string.Empty;
            error = null;

            string collapsed = CollapseWhitespace(raw);
            if (collapsed.Length == 0)
            {
                error = "Label cannot be empty.";
                return false;
            }

            if (collapsed.Length > MaxLabelLength)
            {
                error = "Label is too long (max " + MaxLabelLength.ToString(CultureInfo.InvariantCulture) + " characters).";
                return false;
            }

            label = collapsed;
            return true;
        }

        /// <summary>
        /// Normalizes a raw tag key: trims, lowercases (invariant), and slugifies to <c>[a-z0-9._-]</c> with
        /// internal whitespace collapsed to a single hyphen. The result must be non-empty and no longer than
        /// <see cref="MaxTagKeyLength"/>.
        /// </summary>
        /// <param name="raw">The raw key as typed.</param>
        /// <param name="key">The normalized key when the method returns true; otherwise empty.</param>
        /// <param name="error">A human-readable reason when the method returns false; otherwise null.</param>
        /// <returns>True when the key is valid; false otherwise.</returns>
        public static bool TryNormalizeTagKey(string? raw, out string key, out string? error)
        {
            key = string.Empty;
            error = null;

            string trimmed = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (trimmed.Length == 0)
            {
                error = "Tag key cannot be empty.";
                return false;
            }

            StringBuilder builder = new StringBuilder(trimmed.Length);
            bool lastWasHyphen = false;
            foreach (char c in trimmed)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')
                {
                    builder.Append(c);
                    lastWasHyphen = c == '-';
                }
                else if (char.IsWhiteSpace(c))
                {
                    // Collapse a run of whitespace into a single hyphen.
                    if (!lastWasHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasHyphen = true;
                    }
                }
                // Any other character (punctuation, symbols) is dropped.
            }

            string slug = builder.ToString().Trim('-');
            if (slug.Length == 0)
            {
                error = "Tag key must contain at least one letter or digit.";
                return false;
            }

            if (slug.Length > MaxTagKeyLength)
            {
                error = "Tag key is too long (max " + MaxTagKeyLength.ToString(CultureInfo.InvariantCulture) + " characters).";
                return false;
            }

            key = slug;
            return true;
        }

        /// <summary>
        /// Normalizes a raw tag value: trims surrounding whitespace and collapses internal whitespace runs to a
        /// single space. The result must be non-empty and no longer than <see cref="MaxTagValueLength"/>.
        /// </summary>
        /// <param name="raw">The raw value as typed.</param>
        /// <param name="value">The normalized value when the method returns true; otherwise empty.</param>
        /// <param name="error">A human-readable reason when the method returns false; otherwise null.</param>
        /// <returns>True when the value is valid; false otherwise.</returns>
        public static bool TryNormalizeTagValue(string? raw, out string value, out string? error)
        {
            value = string.Empty;
            error = null;

            string collapsed = CollapseWhitespace(raw);
            if (collapsed.Length == 0)
            {
                error = "Tag value cannot be empty.";
                return false;
            }

            if (collapsed.Length > MaxTagValueLength)
            {
                error = "Tag value is too long (max " + MaxTagValueLength.ToString(CultureInfo.InvariantCulture) + " characters).";
                return false;
            }

            value = collapsed;
            return true;
        }

        /// <summary>
        /// Parses and normalizes a raw <c>key:value</c> string (the form typed after <c>/tag</c>). The key and
        /// value are split on the first colon.
        /// </summary>
        /// <param name="raw">The raw <c>key:value</c> string.</param>
        /// <param name="tag">The normalized tag when the method returns true; otherwise null.</param>
        /// <param name="error">A human-readable reason when the method returns false; otherwise null.</param>
        /// <returns>True when the tag is valid; false otherwise.</returns>
        public static bool TryParseTag(string? raw, out SessionTag? tag, out string? error)
        {
            tag = null;
            error = null;

            string text = (raw ?? string.Empty).Trim();
            int colon = text.IndexOf(':');
            if (colon < 0)
            {
                error = "Tag must be in 'key: value' form.";
                return false;
            }

            string rawKey = text.Substring(0, colon);
            string rawValue = text.Substring(colon + 1);

            if (!TryNormalizeTagKey(rawKey, out string key, out error))
            {
                return false;
            }

            if (!TryNormalizeTagValue(rawValue, out string value, out error))
            {
                return false;
            }

            tag = new SessionTag(key, value);
            return true;
        }

        #endregion

        #region Private-Methods

        private static string CollapseWhitespace(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(raw.Length);
            bool lastWasSpace = false;
            foreach (char c in raw.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
            }

            return builder.ToString();
        }

        #endregion
    }
}
