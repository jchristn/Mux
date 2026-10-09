namespace Mux.Core.McpServer
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Wraps stderr while <c>mux mcp serve</c> runs over stdio. Voltaic's stdio server logs every message it receives
    /// and sends in full, and MCP clients keep that log, so prompts sent to <c>run</c> and the answers would land in
    /// client logs. This writer shortens those lines to the method, the request id, and the size, and passes every
    /// other line through unchanged. Lines are buffered until their newline so a message is never split.
    /// </summary>
    public sealed class McpLogFilterWriter : TextWriter
    {
        #region Private-Members

        private static readonly Regex _MessageLine = new Regex(@"^(\[[^\]]*\] )(Received|Sent): (.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private readonly object _Sync = new object();
        private readonly TextWriter _Inner;
        private readonly StringBuilder _Line = new StringBuilder();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpLogFilterWriter"/> class.
        /// </summary>
        /// <param name="inner">The real stderr writer. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public McpLogFilterWriter(TextWriter inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc/>
        public override Encoding Encoding => _Inner.Encoding;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Shortens one log line when it carries a full MCP message; returns other lines unchanged.
        /// </summary>
        /// <param name="line">The line, without its newline.</param>
        /// <returns>The line to write.</returns>
        public static string Summarize(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line ?? string.Empty;
            }

            Match match = _MessageLine.Match(line);
            if (!match.Success)
            {
                return line;
            }

            string payload = match.Groups[3].Value;
            string kind = "message";
            string id = string.Empty;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(payload))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("method", out JsonElement method) && method.ValueKind == JsonValueKind.String)
                        {
                            kind = method.GetString() ?? kind;
                        }
                        else if (root.TryGetProperty("error", out _))
                        {
                            kind = "error response";
                        }
                        else if (root.TryGetProperty("result", out _))
                        {
                            kind = "response";
                        }

                        if (root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind != JsonValueKind.Null)
                        {
                            id = idElement.ToString();
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Array)
                    {
                        kind = "batch of " + root.GetArrayLength();
                    }
                }
            }
            catch (JsonException)
            {
                kind = "unparsed message";
            }

            return match.Groups[1].Value + match.Groups[2].Value + ": " + kind + (id.Length > 0 ? " (id " + id + ", " : " (") + Encoding.UTF8.GetByteCount(payload) + " bytes)";
        }

        /// <inheritdoc/>
        public override void Write(char value)
        {
            lock (_Sync)
            {
                if (value == '\n')
                {
                    FlushLineNoLock();
                }
                else if (value != '\r')
                {
                    _Line.Append(value);
                }
            }
        }

        /// <inheritdoc/>
        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }

            foreach (char c in value)
            {
                Write(c);
            }
        }

        /// <inheritdoc/>
        public override void WriteLine(string? value)
        {
            lock (_Sync)
            {
                Write(value);
                FlushLineNoLock();
            }
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            lock (_Sync)
            {
                _Inner.Flush();
            }
        }

        #endregion

        #region Private-Methods

        private void FlushLineNoLock()
        {
            _Inner.WriteLine(Summarize(_Line.ToString()));
            _Inner.Flush();
            _Line.Clear();
        }

        #endregion
    }
}
