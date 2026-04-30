namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text;

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
        /// Start the guided endpoint creation workflow.
        /// </summary>
        Add,

        /// <summary>
        /// Start the guided endpoint edit workflow.
        /// </summary>
        Edit,

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
        /// Optional endpoint name for switch/show/add/edit/remove actions.
        /// </summary>
        public string? Name { get; set; }
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
        /// Usage for interactive endpoint commands.
        /// </summary>
        public const string BasicUsage =
            "Usage: /endpoint, /endpoint list, /endpoint <name>, /endpoint show <name>, /endpoint add, /endpoint edit <name>, or /endpoint remove <name>";

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
                    if (tokens.Count != 1)
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

                case "add":
                    if (tokens.Count > 2)
                    {
                        return Error("Usage: /endpoint add");
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.Add,
                        Name = tokens.Count == 2 ? tokens[1] : null
                    });

                case "edit":
                    if (tokens.Count != 2)
                    {
                        return Error("Usage: /endpoint edit <name>");
                    }

                    return Success(new EndpointCommandRequest
                    {
                        Action = EndpointCommandAction.Edit,
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
