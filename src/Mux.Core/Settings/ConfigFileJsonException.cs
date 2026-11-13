namespace Mux.Core.Settings
{
    using System;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// A JSON error in one of mux's configuration files (for example <c>endpoints.json</c>), reported with the
    /// file name, its full path, and a 1-based line and column so the user can find and fix it. Derives from
    /// <see cref="JsonException"/>, so code that already catches JSON errors keeps working; the original
    /// parser error is the <see cref="Exception.InnerException"/>.
    /// </summary>
    public sealed class ConfigFileJsonException : JsonException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigFileJsonException"/> class.
        /// </summary>
        /// <param name="filePath">The configuration file that failed to parse.</param>
        /// <param name="inner">The parser error.</param>
        public ConfigFileJsonException(string filePath, JsonException inner)
            : base(BuildMessage(filePath, inner), inner?.Path, inner?.LineNumber, inner?.BytePositionInLine, inner)
        {
            FilePath = filePath ?? string.Empty;
        }

        /// <summary>The full path of the configuration file that failed to parse.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Wraps a parser error raised while reading <paramref name="filePath"/>, or returns it unchanged when it
        /// is already a <see cref="ConfigFileJsonException"/>.
        /// </summary>
        /// <param name="filePath">The configuration file being read.</param>
        /// <param name="inner">The parser error.</param>
        /// <returns>The descriptive exception.</returns>
        public static ConfigFileJsonException For(string filePath, JsonException inner)
        {
            return inner as ConfigFileJsonException ?? new ConfigFileJsonException(filePath, inner);
        }

        private static string BuildMessage(string? filePath, JsonException? inner)
        {
            string name = string.IsNullOrEmpty(filePath) ? "configuration" : System.IO.Path.GetFileName(filePath);
            StringBuilder message = new StringBuilder();
            message.Append("Please check your ").Append(name).Append(" file for errors");
            if (!string.IsNullOrEmpty(filePath)) message.Append(" (").Append(filePath).Append(')');
            message.Append(':');

            if (inner?.LineNumber.HasValue == true)
            {
                // System.Text.Json reports zero-based positions; editors count from 1.
                message.Append(" line ").Append(inner.LineNumber.Value + 1);
                if (inner.BytePositionInLine.HasValue) message.Append(", column ").Append(inner.BytePositionInLine.Value + 1);
                message.Append(':');
            }

            message.Append(' ').Append(Describe(inner));
            if (!string.IsNullOrEmpty(inner?.Path) && inner!.Path != "$")
            {
                message.Append(" (at ").Append(inner.Path).Append(')');
            }

            return message.ToString();
        }

        // The parser's own sentence, without the "Path: ... | LineNumber: ... | BytePositionInLine: ..." suffix
        // that the message above already presents in a friendlier form.
        private static string Describe(JsonException? inner)
        {
            string text = inner?.Message ?? "the file is not valid JSON.";
            int suffix = text.IndexOf(" Path: ", StringComparison.Ordinal);
            if (suffix > 0) text = text.Substring(0, suffix);
            text = text.Trim();
            if (text.Length > 0 && !text.EndsWith(".", StringComparison.Ordinal)) text += ".";
            return text;
        }
    }
}
