namespace Mux.Core.Tools.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Playwright;
    using Mux.Core.Models;

    /// <summary>
    /// Retrieves web pages in a headless browser and returns rendered page content.
    /// </summary>
    public class WebRetrieveTool : IToolExecutor
    {
        #region Private-Members

        private const int DefaultTimeoutMs = 30_000;
        private const int DefaultMaxContentChars = 60_000;
        private const int MaxContentCharsLimit = 500_000;
        private static readonly SemaphoreSlim BrowserInstallLock = new SemaphoreSlim(1, 1);

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public string Name => "web_retrieve";

        /// <inheritdoc />
        public string Description => "Retrieves a URL with a headless browser and returns rendered text, title, final URL, status, and optional HTML.";

        /// <inheritdoc />
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                url = new
                {
                    type = "string",
                    description = "The absolute HTTP or HTTPS URL to retrieve."
                },
                browser = new
                {
                    type = "string",
                    description = "Headless browser to use: chromium or firefox. Defaults to chromium."
                },
                wait_until = new
                {
                    type = "string",
                    description = "Page load state to wait for: load, domcontentloaded, networkidle, or commit. Defaults to domcontentloaded."
                },
                timeout_ms = new
                {
                    type = "integer",
                    description = "Navigation timeout in milliseconds. Defaults to 30000."
                },
                max_content_chars = new
                {
                    type = "integer",
                    description = "Maximum number of characters of text and HTML to return. Defaults to 60000; maximum is 500000."
                },
                include_html = new
                {
                    type = "boolean",
                    description = "Whether to include rendered document HTML in addition to visible text. Defaults to false."
                }
            },
            required = new[] { "url" }
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                Uri uri = GetRequiredHttpUri(arguments, "url");
                string browserName = GetBrowserName(arguments);
                WaitUntilState waitUntil = ParseWaitUntil(GetOptionalString(arguments, "wait_until") ?? "domcontentloaded");
                int timeoutMs = Math.Max(1, GetOptionalInt(arguments, "timeout_ms", DefaultTimeoutMs));
                int maxContentChars = Math.Clamp(GetOptionalInt(arguments, "max_content_chars", DefaultMaxContentChars), 1, MaxContentCharsLimit);
                bool includeHtml = GetOptionalBool(arguments, "include_html", false);

                WebRetrieveResponse response = await RetrieveWithBrowserInstallRetryAsync(
                    uri,
                    browserName,
                    waitUntil,
                    timeoutMs,
                    maxContentChars,
                    includeHtml,
                    cancellationToken).ConfigureAwait(false);

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(response)
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new
                    {
                        error = "web_retrieve_error",
                        message = ex.Message
                    })
                };
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<WebRetrieveResponse> RetrieveWithBrowserInstallRetryAsync(
            Uri uri,
            string browserName,
            WaitUntilState waitUntil,
            int timeoutMs,
            int maxContentChars,
            bool includeHtml,
            CancellationToken cancellationToken)
        {
            try
            {
                return await RetrieveAsync(uri, browserName, waitUntil, timeoutMs, maxContentChars, includeHtml, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (IsMissingBrowserError(ex))
            {
                await InstallBrowserAsync(browserName, cancellationToken).ConfigureAwait(false);
                return await RetrieveAsync(uri, browserName, waitUntil, timeoutMs, maxContentChars, includeHtml, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<WebRetrieveResponse> RetrieveAsync(
            Uri uri,
            string browserName,
            WaitUntilState waitUntil,
            int timeoutMs,
            int maxContentChars,
            bool includeHtml,
            CancellationToken cancellationToken)
        {
            using IPlaywright playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            IBrowserType browserType = browserName.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                ? playwright.Firefox
                : playwright.Chromium;

            await using IBrowser browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = false,
                UserAgent = "mux-web-retrieve/1.0"
            }).ConfigureAwait(false);

            IPage page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultNavigationTimeout(timeoutMs);
            page.SetDefaultTimeout(timeoutMs);

            IResponse? navigationResponse = await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
            {
                Timeout = timeoutMs,
                WaitUntil = waitUntil
            }).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            string? title = await page.TitleAsync().ConfigureAwait(false);
            string? text = await page.EvaluateAsync<string?>("() => document.body ? document.body.innerText : document.documentElement.innerText").ConfigureAwait(false);
            string? html = includeHtml ? await page.ContentAsync().ConfigureAwait(false) : null;

            TruncatedValue truncatedText = Truncate(text ?? string.Empty, maxContentChars);
            TruncatedValue truncatedHtml = Truncate(html, maxContentChars);

            return new WebRetrieveResponse
            {
                Url = uri.AbsoluteUri,
                FinalUrl = page.Url,
                Title = title ?? string.Empty,
                Status = navigationResponse?.Status,
                ContentType = GetHeader(navigationResponse, "content-type"),
                Text = truncatedText.Value,
                TextTruncated = truncatedText.Truncated,
                Html = includeHtml ? truncatedHtml.Value : null,
                HtmlTruncated = includeHtml ? truncatedHtml.Truncated : null
            };
        }

        private static async Task InstallBrowserAsync(string browserName, CancellationToken cancellationToken)
        {
            await BrowserInstallLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", browserName });
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Playwright browser install failed with exit code {exitCode}.");
                }
            }
            finally
            {
                BrowserInstallLock.Release();
            }
        }

        private static bool IsMissingBrowserError(PlaywrightException ex)
        {
            return ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("BrowserType.launch", StringComparison.OrdinalIgnoreCase);
        }

        private static WaitUntilState ParseWaitUntil(string waitUntil)
        {
            return waitUntil.Trim().ToLowerInvariant() switch
            {
                "load" => WaitUntilState.Load,
                "networkidle" => WaitUntilState.NetworkIdle,
                "commit" => WaitUntilState.Commit,
                "domcontentloaded" => WaitUntilState.DOMContentLoaded,
                _ => throw new ArgumentException("Parameter 'wait_until' must be one of: load, domcontentloaded, networkidle, commit.")
            };
        }

        private static string? GetHeader(IResponse? response, string name)
        {
            if (response == null)
            {
                return null;
            }

            return response.Headers.TryGetValue(name, out string? value) ? value : null;
        }

        private static TruncatedValue Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return new TruncatedValue(value, false);
            }

            return new TruncatedValue(value.Substring(0, maxChars), true);
        }

        private static Uri GetRequiredHttpUri(JsonElement arguments, string propertyName)
        {
            string rawValue = GetRequiredString(arguments, propertyName).Trim();
            if (!Uri.TryCreate(rawValue, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"Required parameter '{propertyName}' must be an absolute HTTP or HTTPS URL.");
            }

            return uri;
        }

        private static string GetBrowserName(JsonElement arguments)
        {
            string browser = GetOptionalString(arguments, "browser") ?? "chromium";
            browser = browser.Trim().ToLowerInvariant();

            if (browser != "chromium" && browser != "firefox")
            {
                throw new ArgumentException("Parameter 'browser' must be either 'chromium' or 'firefox'.");
            }

            return browser;
        }

        private static string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private static string? GetOptionalString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static int GetOptionalInt(JsonElement arguments, string propertyName, int defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static bool GetOptionalBool(JsonElement arguments, string propertyName, bool defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return defaultValue;
        }

        private sealed class WebRetrieveResponse
        {
            public string Url { get; set; } = string.Empty;

            public string FinalUrl { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;

            public int? Status { get; set; }

            public string? ContentType { get; set; }

            public string Text { get; set; } = string.Empty;

            public bool TextTruncated { get; set; }

            public string? Html { get; set; }

            public bool? HtmlTruncated { get; set; }
        }

        private sealed class TruncatedValue
        {
            public TruncatedValue(string? value, bool truncated)
            {
                Value = value ?? string.Empty;
                Truncated = truncated;
            }

            public string Value { get; }

            public bool Truncated { get; }
        }

        #endregion
    }
}
