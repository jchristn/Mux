namespace Mux.Server
{
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Renders the self-contained single-page dashboard served at <c>/dashboard</c>. All CSS/JS/logo assets
    /// are inlined so the page has no external dependencies. The configured API key is injected so the page
    /// can call the (loopback) API on the user's behalf.
    /// </summary>
    public static class DashboardPage
    {
        /// <summary>
        /// Render the dashboard HTML with the API key, version, and logo assets injected.
        /// </summary>
        /// <param name="apiKey">The local API key to embed (may be null/empty in no-auth mode).</param>
        /// <param name="version">Product version.</param>
        /// <returns>A complete HTML document.</returns>
        public static string Render(string? apiKey, string version)
        {
            return Render(apiKey, version, string.Empty);
        }

        /// <summary>
        /// Render the dashboard HTML with the API key, version, base path, and logo assets injected.
        /// </summary>
        /// <param name="apiKey">The local API key to embed (may be null/empty in no-auth mode, and is empty when
        /// the page is served through the Mux Cloud relay, which authenticates on the machine's behalf).</param>
        /// <param name="version">Product version.</param>
        /// <param name="basePath">Path prefix every API and WebSocket URL is built under, for example
        /// <c>/m/mch_abc</c> behind the Mux Cloud relay. Empty for a direct local page. Values that are not a safe
        /// path (see <see cref="RelayContext.SanitizeBasePath"/>) render as empty.</param>
        /// <returns>A complete HTML document.</returns>
        public static string Render(string? apiKey, string version, string? basePath)
        {
            string logoDark = LoadLogoDataUri("Mux.Server.logo-white.png");
            string logoLight = LoadLogoDataUri("Mux.Server.logo-black.png");
            string logoGrey = LoadLogoDataUri("Mux.Server.logo-grey.png");

            return DashboardTemplate.Html
                .Replace("__MUX_API_KEY__", JsString(apiKey ?? string.Empty))
                .Replace("__MUX_BASE_PATH__", JsString(RelayContext.SanitizeBasePath(basePath)))
                .Replace("__MUX_VERSION__", HtmlEscape(version))
                .Replace("__LOGO_DARK__", logoDark)
                .Replace("__LOGO_LIGHT__", logoLight)
                .Replace("__LOGO_GREY__", logoGrey)
                .Replace("__SLASH_COMPLETION_JS__", LoadSlashCompletionScript());
        }

        private static string LoadSlashCompletionScript()
        {
            using Stream stream = typeof(DashboardPage).Assembly.GetManifestResourceStream("Mux.Server.slash-completion.js")
                ?? throw new InvalidOperationException("Missing slash-completion asset.");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string LoadLogoDataUri(string resourceName)
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null) return string.Empty;
                using (stream)
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string JsString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c");
        }

        private static string HtmlEscape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

    }
}
