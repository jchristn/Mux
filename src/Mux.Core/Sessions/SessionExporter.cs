namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Renders a <see cref="SessionSnapshot"/> to a shareable, self-contained document — Markdown or a
    /// single HTML file with inline styles and no external assets. This is the local, server-free session
    /// sharing tier: it turns the same snapshot the session store persists into something a person can read
    /// or hand off, without a running mux, a network call, or any hosted component. Pure and deterministic;
    /// the caller decides where to write the returned string.
    /// </summary>
    public static class SessionExporter
    {
        #region Public-Methods

        /// <summary>
        /// Renders the snapshot as a Markdown transcript: a header with session metadata followed by the
        /// focused conversation, one section per message, with tool calls and tool results shown in fenced
        /// code blocks.
        /// </summary>
        /// <param name="snapshot">The session snapshot to render. Must not be null.</param>
        /// <returns>The Markdown document text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
        public static string ToMarkdown(SessionSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            StringBuilder builder = new StringBuilder();
            string title = string.IsNullOrWhiteSpace(snapshot.Title) ? (string.IsNullOrWhiteSpace(snapshot.Id) ? "mux session" : snapshot.Id) : snapshot.Title;

            builder.Append("# ").AppendLine(title);
            builder.AppendLine();
            builder.Append("- **Session id:** ").AppendLine(EmptyDash(snapshot.Id));
            builder.Append("- **Endpoint:** ").AppendLine(EmptyDash(snapshot.EndpointName));
            builder.Append("- **Model:** ").AppendLine(EmptyDash(snapshot.Model));
            builder.Append("- **Updated (UTC):** ").AppendLine(FormatUtc(snapshot.UpdatedUtc));
            builder.Append("- **Messages:** ").AppendLine(snapshot.ConversationHistory.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();

            foreach (ConversationMessage message in snapshot.ConversationHistory)
            {
                builder.Append("## ").AppendLine(RoleLabel(message.Role));

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    builder.AppendLine();
                    builder.AppendLine(message.Content!.TrimEnd());
                }

                if (message.Role == RoleEnum.Tool)
                {
                    builder.AppendLine();
                    builder.AppendLine("```");
                    builder.AppendLine((message.Content ?? string.Empty).TrimEnd());
                    builder.AppendLine("```");
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    foreach (ToolCall call in message.ToolCalls)
                    {
                        builder.AppendLine();
                        builder.Append("**Tool call:** `").Append(call.Name).AppendLine("`");
                        builder.AppendLine();
                        builder.AppendLine("```json");
                        builder.AppendLine(call.Arguments);
                        builder.AppendLine("```");
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the snapshot as a single, self-contained HTML file: inline CSS only, no scripts, no
        /// external requests, and all dynamic text HTML-escaped so a session transcript is safe to open in
        /// any browser.
        /// </summary>
        /// <param name="snapshot">The session snapshot to render. Must not be null.</param>
        /// <returns>The HTML document text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
        public static string ToHtml(SessionSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            string title = string.IsNullOrWhiteSpace(snapshot.Title) ? (string.IsNullOrWhiteSpace(snapshot.Id) ? "mux session" : snapshot.Id) : snapshot.Title;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<!doctype html>");
            builder.AppendLine("<html lang=\"en\">");
            builder.AppendLine("<head>");
            builder.AppendLine("<meta charset=\"utf-8\">");
            builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            builder.Append("<title>").Append(Escape(title)).AppendLine("</title>");
            builder.AppendLine("<style>");
            builder.AppendLine(Css);
            builder.AppendLine("</style>");
            builder.AppendLine("</head>");
            builder.AppendLine("<body>");
            builder.AppendLine("<main>");
            builder.Append("<h1>").Append(Escape(title)).AppendLine("</h1>");
            builder.AppendLine("<dl class=\"meta\">");
            AppendMeta(builder, "Session id", EmptyDash(snapshot.Id));
            AppendMeta(builder, "Endpoint", EmptyDash(snapshot.EndpointName));
            AppendMeta(builder, "Model", EmptyDash(snapshot.Model));
            AppendMeta(builder, "Updated (UTC)", FormatUtc(snapshot.UpdatedUtc));
            AppendMeta(builder, "Messages", snapshot.ConversationHistory.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("</dl>");

            foreach (ConversationMessage message in snapshot.ConversationHistory)
            {
                string roleClass = message.Role.ToString().ToLowerInvariant();
                builder.Append("<section class=\"msg ").Append(roleClass).AppendLine("\">");
                builder.Append("<div class=\"role\">").Append(Escape(RoleLabel(message.Role))).AppendLine("</div>");

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    if (message.Role == RoleEnum.Tool)
                    {
                        builder.Append("<pre class=\"content\">").Append(Escape(message.Content!.TrimEnd())).AppendLine("</pre>");
                    }
                    else
                    {
                        builder.Append("<div class=\"content\">").Append(Escape(message.Content!.TrimEnd())).AppendLine("</div>");
                    }
                }

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    foreach (ToolCall call in message.ToolCalls)
                    {
                        builder.Append("<div class=\"toolcall\"><span class=\"tool-name\">")
                            .Append(Escape(call.Name))
                            .AppendLine("</span></div>");
                        builder.Append("<pre class=\"args\">").Append(Escape(call.Arguments)).AppendLine("</pre>");
                    }
                }

                builder.AppendLine("</section>");
            }

            builder.AppendLine("</main>");
            builder.AppendLine("<footer>Exported from mux — a local, server-free session snapshot.</footer>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");
            return builder.ToString();
        }

        /// <summary>
        /// Returns the conventional file extension (without the dot) for an export format string, accepting
        /// <c>md</c>/<c>markdown</c> and <c>html</c>/<c>htm</c> case-insensitively.
        /// </summary>
        /// <param name="format">The requested format.</param>
        /// <param name="extension">The resolved extension when recognized.</param>
        /// <returns>True when the format is recognized; otherwise false.</returns>
        public static bool TryNormalizeFormat(string? format, out string extension)
        {
            switch ((format ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "md":
                case "markdown":
                    extension = "md";
                    return true;
                case "html":
                case "htm":
                    extension = "html";
                    return true;
                default:
                    extension = string.Empty;
                    return false;
            }
        }

        /// <summary>
        /// Renders the snapshot in the requested format (<c>md</c> or <c>html</c>).
        /// </summary>
        /// <param name="snapshot">The session snapshot to render. Must not be null.</param>
        /// <param name="format">The format string (<c>md</c>/<c>markdown</c> or <c>html</c>/<c>htm</c>).</param>
        /// <returns>The rendered document.</returns>
        /// <exception cref="ArgumentException">Thrown when the format is not recognized.</exception>
        public static string Render(SessionSnapshot snapshot, string? format)
        {
            if (!TryNormalizeFormat(format, out string extension))
            {
                throw new ArgumentException($"Unknown export format '{format}'. Use 'md' or 'html'.", nameof(format));
            }

            return extension == "html" ? ToHtml(snapshot) : ToMarkdown(snapshot);
        }

        #endregion

        #region Private-Members

        private const string Css =
            "body{margin:0;background:#0f1115;color:#e6e6e6;font-family:-apple-system,Segoe UI,Roboto,sans-serif;line-height:1.5}"
            + "main{max-width:52rem;margin:0 auto;padding:2rem 1.25rem}"
            + "h1{font-size:1.5rem;margin:0 0 1rem}"
            + "dl.meta{display:grid;grid-template-columns:auto 1fr;gap:.15rem .75rem;margin:0 0 1.5rem;font-size:.85rem;color:#9aa4b2}"
            + "dl.meta dt{font-weight:600}dl.meta dd{margin:0}"
            + ".msg{border:1px solid #232833;border-radius:.5rem;padding:.75rem 1rem;margin:.75rem 0;background:#151922;overflow-x:auto}"
            + ".msg .role{font-size:.7rem;text-transform:uppercase;letter-spacing:.05em;color:#7c8698;margin-bottom:.35rem}"
            + ".msg.user{border-color:#2f4b73}.msg.assistant{border-color:#2f5f3f}.msg.tool{border-color:#5a4a2f}"
            + ".content{white-space:pre-wrap;word-wrap:break-word}"
            + "pre{white-space:pre-wrap;word-wrap:break-word;background:#0b0d12;border-radius:.35rem;padding:.6rem;margin:.4rem 0;font-size:.82rem;overflow-x:auto}"
            + ".toolcall{margin-top:.5rem;font-size:.8rem;color:#c9a86a}.tool-name{font-family:monospace}"
            + "footer{max-width:52rem;margin:0 auto;padding:1rem 1.25rem 2rem;color:#5c6472;font-size:.75rem}";

        private static void AppendMeta(StringBuilder builder, string term, string value)
        {
            builder.Append("<dt>").Append(Escape(term)).Append("</dt><dd>").Append(Escape(value)).AppendLine("</dd>");
        }

        private static string RoleLabel(RoleEnum role)
        {
            switch (role)
            {
                case RoleEnum.User:
                    return "User";
                case RoleEnum.Assistant:
                    return "Assistant";
                case RoleEnum.Tool:
                    return "Tool";
                case RoleEnum.System:
                    return "System";
                default:
                    return role.ToString();
            }
        }

        private static string FormatUtc(DateTime value)
        {
            if (value == default)
            {
                return "—";
            }

            return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
        }

        private static string EmptyDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value;
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    case '"':
                        builder.Append("&quot;");
                        break;
                    case '\'':
                        builder.Append("&#39;");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        #endregion
    }
}
