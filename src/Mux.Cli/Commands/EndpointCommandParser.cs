namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Supported interactive endpoint command actions.
    /// </summary>
    public enum EndpointCommandAction
    {
        /// <summary>
        /// List configured endpoints.
        /// </summary>
        List,

        /// <summary>
        /// Switch the current interactive session to a named endpoint or model.
        /// </summary>
        Switch,

        /// <summary>
        /// Show details for a configured endpoint.
        /// </summary>
        Show,

        /// <summary>
        /// Add a configured endpoint.
        /// </summary>
        Add,

        /// <summary>
        /// Remove a configured endpoint.
        /// </summary>
        Remove
    }

    /// <summary>
    /// Parsed interactive endpoint command request.
    /// </summary>
    public sealed class EndpointCommandRequest
    {
        /// <summary>
        /// The requested endpoint action.
        /// </summary>
        public EndpointCommandAction Action { get; set; }

        /// <summary>
        /// Optional endpoint name for switch/show/remove actions.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional endpoint configuration for add actions.
        /// </summary>
        public EndpointConfig? Endpoint { get; set; }
    }

    /// <summary>
    /// Result of parsing an interactive endpoint command.
    /// </summary>
    public sealed class EndpointCommandParseResult
    {
        /// <summary>
        /// Whether parsing succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The parsed request when successful.
        /// </summary>
        public EndpointCommandRequest? Request { get; set; }

        /// <summary>
        /// The human-readable parse error when parsing fails.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses interactive <c>/endpoint</c> commands.
    /// </summary>
    public static class EndpointCommandParser
    {
        /// <summary>
        /// Usage for listing or switching endpoints.
        /// </summary>
        public const string BasicUsage =
            "Usage: /endpoint, /endpoint list, /endpoint <name>, /endpoint show <name>, /endpoint remove <name>, or /endpoint add <name> --adapter <type> --base-url <url> --model <name> [--default] [--temperature <float>] [--max-tokens <int>] [--context-window <int>] [--timeout-ms <int>] [--header <key=value>]";

        /// <summary>
        /// Usage for adding endpoints.
        /// </summary>
        public const string AddUsage =
            "Usage: /endpoint add <name> --adapter <type> --base-url <url> --model <name> [--default] [--temperature <float>] [--max-tokens <int>] [--context-window <int>] [--timeout-ms <int>] [--header <key=value>]";

        /// <summary>
        /// Parses the argument text that follows <c>/endpoint</c>.
        /// </summary>
        /// <param name="argument">The argument text to parse.</param>
        /// <returns>The parse result.</returns>
        public static EndpointCommandParseResult Parse(string argument)
        {
            if (!TryTokenize(argument ?? string.Empty, out List<string> tokens, out string errorMessage))
            {
                return Error(errorMessage);
            }

            if (tokens.Count == 0)
            {
                return Success(new EndpointCommandRequest
                {
                    Action = EndpointCommandAction.List
                });
            }

            string action = tokens[0].ToLowerInvariant();
            switch (action)
            {
                case "list":
                    if (tokens.Count > 1)
                    {
                        return Error(BasicUsage);
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.List
                    });

                case "show":
                    if (tokens.Count != 2)
                    {
                        return Error("Usage: /endpoint show <name>");
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.Show,
                        Name = tokens[1]
                    });

                case "remove":
                    if (tokens.Count != 2)
                    {
                        return Error("Usage: /endpoint remove <name>");
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.Remove,
                        Name = tokens[1]
                    });

                case "add":
                    return ParseAdd(tokens);

                default:
                    if (tokens.Count != 1)
                    {
                        return Error(BasicUsage);
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.Switch,
                        Name = tokens[0]
                    });
            }
        }

        private static EndpointCommandParseResult ParseAdd(List<string> tokens)
        {
            if (tokens.Count < 2)
            {
                return Error(AddUsage);
            }

            EndpointConfig endpoint = new EndpointConfig
            {
                Name = tokens[1],
                Headers = new Dictionary<string, string>()
            };

            bool hasAdapter = false;
            bool hasBaseUrl = false;
            bool hasModel = false;

            for (int i = 2; i < tokens.Count; i++)
            {
                string option = tokens[i].ToLowerInvariant();
                switch (option)
                {
                    case "--adapter":
                    case "--adapter-type":
                        if (!TryReadOptionValue(tokens, option, ref i, out string adapterValue, out EndpointCommandParseResult? adapterError))
                        {
                            return adapterError!;
                        }

                        if (!TryParseAdapterType(adapterValue, out AdapterTypeEnum adapterType))
                        {
                            return Error($"Unknown adapter type '{adapterValue}'. Expected: ollama, openai, vllm, openai-compatible.");
                        }

                        endpoint.AdapterType = adapterType;
                        endpoint.Quirks = Defaults.QuirksForAdapter(adapterType);
                        hasAdapter = true;
                        break;

                    case "--base-url":
                        if (!TryReadOptionValue(tokens, option, ref i, out string baseUrlValue, out EndpointCommandParseResult? baseUrlError))
                        {
                            return baseUrlError!;
                        }

                        endpoint.BaseUrl = baseUrlValue;
                        hasBaseUrl = true;
                        break;

                    case "--model":
                        if (!TryReadOptionValue(tokens, option, ref i, out string modelValue, out EndpointCommandParseResult? modelError))
                        {
                            return modelError!;
                        }

                        endpoint.Model = modelValue;
                        hasModel = true;
                        break;

                    case "--default":
                        endpoint.IsDefault = true;
                        break;

                    case "--temperature":
                        if (!TryReadOptionValue(tokens, option, ref i, out string temperatureValue, out EndpointCommandParseResult? temperatureError))
                        {
                            return temperatureError!;
                        }

                        if (!double.TryParse(temperatureValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
                        {
                            return Error($"Invalid temperature '{temperatureValue}'. Expected a number between 0.0 and 2.0.");
                        }

                        endpoint.Temperature = temperature;
                        break;

                    case "--max-tokens":
                        if (!TryReadOptionValue(tokens, option, ref i, out string maxTokensValue, out EndpointCommandParseResult? maxTokensError))
                        {
                            return maxTokensError!;
                        }

                        if (!int.TryParse(maxTokensValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxTokens))
                        {
                            return Error($"Invalid max tokens '{maxTokensValue}'. Expected an integer.");
                        }

                        endpoint.MaxTokens = maxTokens;
                        break;

                    case "--context-window":
                        if (!TryReadOptionValue(tokens, option, ref i, out string contextWindowValue, out EndpointCommandParseResult? contextWindowError))
                        {
                            return contextWindowError!;
                        }

                        if (!int.TryParse(contextWindowValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int contextWindow))
                        {
                            return Error($"Invalid context window '{contextWindowValue}'. Expected an integer.");
                        }

                        endpoint.ContextWindow = contextWindow;
                        break;

                    case "--timeout-ms":
                        if (!TryReadOptionValue(tokens, option, ref i, out string timeoutValue, out EndpointCommandParseResult? timeoutError))
                        {
                            return timeoutError!;
                        }

                        if (!int.TryParse(timeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeoutMs))
                        {
                            return Error($"Invalid timeout '{timeoutValue}'. Expected an integer number of milliseconds.");
                        }

                        endpoint.TimeoutMs = timeoutMs;
                        break;

                    case "--header":
                        if (!TryReadOptionValue(tokens, option, ref i, out string headerValue, out EndpointCommandParseResult? headerError))
                        {
                            return headerError!;
                        }

                        int separatorIndex = headerValue.IndexOf('=');
                        if (separatorIndex <= 0 || separatorIndex == headerValue.Length - 1)
                        {
                            return Error($"Invalid header '{headerValue}'. Expected key=value.");
                        }

                        string headerName = headerValue.Substring(0, separatorIndex).Trim();
                        string headerContent = headerValue.Substring(separatorIndex + 1).Trim();
                        if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(headerContent))
                        {
                            return Error($"Invalid header '{headerValue}'. Expected key=value.");
                        }

                        endpoint.Headers[headerName] = headerContent;
                        break;

                    default:
                        return Error($"Unknown option '{tokens[i]}'. {AddUsage}");
                }
            }

            if (!hasAdapter || !hasBaseUrl || !hasModel)
            {
                return Error($"Missing required endpoint fields. {AddUsage}");
            }

            return Success(new EndpointCommandRequest
            {
                Action = EndpointCommandAction.Add,
                Endpoint = endpoint,
                Name = endpoint.Name
            });
        }

        private static bool TryReadOptionValue(
            List<string> tokens,
            string option,
            ref int index,
            out string value,
            out EndpointCommandParseResult? error)
        {
            value = string.Empty;
            error = null;

            if (index + 1 >= tokens.Count)
            {
                error = Error($"Missing value for {option}. {AddUsage}");
                return false;
            }

            value = tokens[++index];
            return true;
        }

        private static bool TryTokenize(string input, out List<string> tokens, out string errorMessage)
        {
            tokens = new List<string>();
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            StringBuilder current = new StringBuilder();
            char quote = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    else if (c == '\\' && i + 1 < input.Length && (input[i + 1] == quote || input[i + 1] == '\\'))
                    {
                        current.Append(input[i + 1]);
                        i++;
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(c);
            }

            if (quote != '\0')
            {
                errorMessage = "Unterminated quote in /endpoint command.";
                return false;
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return true;
        }

        private static bool TryParseAdapterType(string value, out AdapterTypeEnum adapterType)
        {
            string normalized = value
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();

            switch (normalized)
            {
                case "ollama":
                    adapterType = AdapterTypeEnum.Ollama;
                    return true;
                case "openai":
                    adapterType = AdapterTypeEnum.OpenAi;
                    return true;
                case "vllm":
                    adapterType = AdapterTypeEnum.Vllm;
                    return true;
                case "openaicompatible":
                    adapterType = AdapterTypeEnum.OpenAiCompatible;
                    return true;
                default:
                    adapterType = AdapterTypeEnum.Ollama;
                    return false;
            }
        }

        private static EndpointCommandParseResult Success(EndpointCommandRequest request)
        {
            return new EndpointCommandParseResult
            {
                Success = true,
                Request = request
            };
        }

        private static EndpointCommandParseResult Error(string errorMessage)
        {
            return new EndpointCommandParseResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
