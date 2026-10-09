namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The full HTTP reply mux's diagnostic probe received from an MCP server: the request it sent, the status, the
    /// content type and length, every response and content header (sensitive values such as <c>Set-Cookie</c> and
    /// <c>Authorization</c> redacted, auth secrets masked), the start of the body, and the timing. Carried on
    /// <see cref="McpConnectionException.Reply"/> so callers can inspect the reply as data, not only as text.
    /// </summary>
    public sealed class McpProbeReply
    {
        #region Private-Members

        private string _RequestMethod = "POST";
        private string _RequestUrl = string.Empty;
        private string _ReasonPhrase = string.Empty;
        private string _ContentType = string.Empty;
        private string _Body = string.Empty;
        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Members

        /// <summary>The request method (always <c>POST</c> for the initialize probe).</summary>
        public string RequestMethod
        {
            get => _RequestMethod;
            set => _RequestMethod = value ?? string.Empty;
        }

        /// <summary>The URL the probe posted to.</summary>
        public string RequestUrl
        {
            get => _RequestUrl;
            set => _RequestUrl = value ?? string.Empty;
        }

        /// <summary>The HTTP status code.</summary>
        public int StatusCode { get; set; }

        /// <summary>The reason phrase, for example <c>Unauthorized</c>.</summary>
        public string ReasonPhrase
        {
            get => _ReasonPhrase;
            set => _ReasonPhrase = value ?? string.Empty;
        }

        /// <summary>The response content type, or empty when the server sent none.</summary>
        public string ContentType
        {
            get => _ContentType;
            set => _ContentType = value ?? string.Empty;
        }

        /// <summary>The declared content length, or null when the server did not declare one.</summary>
        public long? ContentLength { get; set; }

        /// <summary>
        /// The response and content headers by name (case-insensitive). Values of sensitive headers are replaced with
        /// <c>&lt;redacted&gt;</c>, and auth secrets are masked wherever they appear.
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get => _Headers;
            set => _Headers = value == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The start of the body (at most about 2000 characters), with auth secrets masked.</summary>
        public string Body
        {
            get => _Body;
            set => _Body = value ?? string.Empty;
        }

        /// <summary>Whether <see cref="Body"/> was cut short (too long, or still streaming when the probe stopped reading).</summary>
        public bool BodyTruncated { get; set; }

        /// <summary>Milliseconds from sending the request to receiving the response headers.</summary>
        public long ElapsedMs { get; set; }

        /// <summary>Whether the status is in the 2xx range.</summary>
        public bool IsSuccess => StatusCode >= 200 && StatusCode <= 299;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a header value, or null when the header is absent.
        /// </summary>
        /// <param name="name">The header name (case-insensitive).</param>
        /// <returns>The value, or null.</returns>
        public string? GetHeader(string name)
        {
            return name != null && _Headers.TryGetValue(name, out string? value) ? value : null;
        }

        #endregion
    }
}
