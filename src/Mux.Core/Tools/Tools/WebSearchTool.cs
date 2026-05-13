namespace Mux.Core.Tools.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Search.Models;
    using Mux.Search.Services;

    /// <summary>
    /// Searches the public web through configured external providers and returns structured results.
    /// </summary>
    public class WebSearchTool : IToolExecutor
    {
        private readonly IWebSearchService _SearchService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSearchTool"/> class.
        /// </summary>
        /// <param name="searchService">The search service to execute.</param>
        public WebSearchTool(IWebSearchService searchService)
        {
            _SearchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        /// <inheritdoc />
        public string Name => "web_search";

        /// <inheritdoc />
        public string Description => "Searches the public web using configured external search providers and returns structured results with URLs and snippets.";

        /// <inheritdoc />
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "The search query to run."
                },
                max_results = new
                {
                    type = "integer",
                    description = "Maximum number of results to return. Defaults to 5."
                },
                provider = new
                {
                    type = "string",
                    description = "Optional configured provider name or provider type to prefer for this request."
                },
                freshness = new
                {
                    type = "string",
                    description = "Optional freshness or relative time filter such as day, week, or month when supported."
                },
                include_answer = new
                {
                    type = "boolean",
                    description = "Whether to request a provider-generated answer or summary when supported."
                },
                include_images = new
                {
                    type = "boolean",
                    description = "Whether to request images when supported."
                },
                offset = new
                {
                    type = "integer",
                    description = "Optional result offset for paging when supported."
                },
                include_domains = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Optional domains to include."
                },
                exclude_domains = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Optional domains to exclude."
                }
            },
            required = new[] { "query" }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                WebSearchRequest request = new WebSearchRequest
                {
                    Query = GetRequiredString(arguments, "query"),
                    MaxResults = GetOptionalInt(arguments, "max_results", 5),
                    Offset = GetOptionalInt(arguments, "offset", 0),
                    PreferredProvider = GetOptionalString(arguments, "provider"),
                    Freshness = GetOptionalString(arguments, "freshness"),
                    IncludeAnswer = GetOptionalBool(arguments, "include_answer", true),
                    IncludeImages = GetOptionalBool(arguments, "include_images", false),
                    IncludeDomains = GetOptionalStringArray(arguments, "include_domains"),
                    ExcludeDomains = GetOptionalStringArray(arguments, "exclude_domains")
                };

                WebSearchResponse response = await _SearchService.SearchAsync(request, cancellationToken).ConfigureAwait(false);

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
                        error = "web_search_error",
                        message = ex.Message
                    })
                };
            }
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

        private static List<string> GetOptionalStringArray(JsonElement arguments, string propertyName)
        {
            List<string> values = new List<string>();

            if (!arguments.TryGetProperty(propertyName, out JsonElement array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    values.Add(item.GetString()!.Trim());
                }
            }

            return values;
        }
    }
}
