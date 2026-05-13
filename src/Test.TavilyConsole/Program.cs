namespace Test.TavilyConsole
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Mux.Search.Exceptions;
    using Mux.Search.Providers.Tavily;
    using SearchConsoleShared;

    /// <summary>
    /// Interactive console harness for exercising Tavily search integration.
    /// </summary>
    public class Program
    {
        private static bool _RunForever = true;
        private static TavilyConsoleSettings _Settings = new TavilyConsoleSettings();

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static async Task Main(string[] args)
        {
            if (HasHelpArgument(args))
            {
                Menu(includeStartupArgs: true);
                return;
            }

            ApplyArguments(args);

            string? startupQuery = ConsoleArguments.GetValue(args, "--query", "-q");

            Menu();

            if (!string.IsNullOrWhiteSpace(startupQuery))
            {
                await RunSearch(startupQuery).ConfigureAwait(false);
            }

            while (_RunForever)
            {
                string userInput = ConsoleInput.GetString("Command [? for help]:", null, false);
                await HandleCommand(userInput).ConfigureAwait(false);
            }
        }

        private static async Task HandleCommand(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return;
            }

            if (userInput.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
            {
                await RunSearch(userInput[7..].Trim()).ConfigureAwait(false);
                return;
            }

            switch (userInput.Trim().ToLowerInvariant())
            {
                case "?":
                    Menu();
                    break;
                case "q":
                    _RunForever = false;
                    break;
                case "cls":
                    Console.Clear();
                    break;
                case "config":
                    ShowConfig();
                    break;
                case "endpoint":
                    _Settings.Endpoint = ConsoleInput.GetString("Endpoint:", _Settings.Endpoint, false);
                    break;
                case "apikey":
                    SetApiKey();
                    break;
                case "count":
                    _Settings.MaxResults = ConsoleInput.GetInt("Max results (1-20):", _Settings.MaxResults, 1, 20);
                    break;
                case "depth":
                    _Settings.SearchDepth = ConsoleInput.GetString("Search depth:", _Settings.SearchDepth, false);
                    break;
                case "topic":
                    _Settings.Topic = ConsoleInput.GetString("Topic:", _Settings.Topic, false);
                    break;
                case "timerange":
                    _Settings.TimeRange = NormalizeOptionalValue(
                        ConsoleInput.GetString("Time range (blank to clear):", _Settings.TimeRange, true));
                    break;
                case "answer":
                    _Settings.IncludeAnswerMode = NormalizeOptionalValue(
                        ConsoleInput.GetString("Include answer mode (basic/advanced/true/false):", _Settings.IncludeAnswerMode, true));
                    break;
                case "raw":
                    _Settings.IncludeRawContentMode = NormalizeOptionalValue(
                        ConsoleInput.GetString("Include raw content mode (markdown/text/true/false):", _Settings.IncludeRawContentMode, true));
                    break;
                case "images":
                    _Settings.IncludeImages = ConsoleInput.GetBoolean("Include images:", _Settings.IncludeImages);
                    _Settings.IncludeImageDescriptions = _Settings.IncludeImages
                        && ConsoleInput.GetBoolean("Include image descriptions:", _Settings.IncludeImageDescriptions);
                    break;
                case "domains":
                    _Settings.IncludeDomains = ConsoleInput.GetCsv("Include domains (comma-separated):", _Settings.IncludeDomains);
                    break;
                case "excludedomains":
                    _Settings.ExcludeDomains = ConsoleInput.GetCsv("Exclude domains (comma-separated):", _Settings.ExcludeDomains);
                    break;
                case "auto":
                    _Settings.AutoParameters = ConsoleInput.GetBoolean("Enable auto-parameters:", _Settings.AutoParameters);
                    break;
                case "safe":
                    _Settings.SafeSearch = ConsoleInput.GetBoolean("Enable safe search:", _Settings.SafeSearch);
                    break;
                case "defaults":
                    ResetDefaults();
                    break;
                case "search":
                    await RunSearch(null).ConfigureAwait(false);
                    break;
                default:
                    Console.WriteLine("Unknown command. Use ? for help.");
                    break;
            }
        }

        private static void ApplyArguments(string[] args)
        {
            _Settings.Endpoint = ConsoleArguments.GetValue(args, "--endpoint", "-e") ?? _Settings.Endpoint;
            _Settings.ApiKey = ConsoleArguments.GetValue(args, "--api-key", "-k") ?? _Settings.ApiKey;

            string? maxResults = ConsoleArguments.GetValue(args, "--max-results", "--count");
            if (int.TryParse(maxResults, out int parsedMaxResults))
            {
                _Settings.MaxResults = Math.Clamp(parsedMaxResults, 1, 20);
            }

            _Settings.SearchDepth = ConsoleArguments.GetValue(args, "--depth") ?? _Settings.SearchDepth;
            _Settings.Topic = ConsoleArguments.GetValue(args, "--topic") ?? _Settings.Topic;
            _Settings.TimeRange = ConsoleArguments.GetValue(args, "--time-range") ?? _Settings.TimeRange;
        }

        private static bool HasHelpArgument(string[] args)
        {
            return args.Any(arg =>
                arg.Equals("/?", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-h", StringComparison.OrdinalIgnoreCase));
        }

        private static void SetApiKey()
        {
            string input = ConsoleInput.GetString("API key (leave blank to keep current):", null, true);
            if (!string.IsNullOrWhiteSpace(input))
            {
                _Settings.ApiKey = input;
            }
        }

        private static void ResetDefaults()
        {
            string currentApiKey = _Settings.ApiKey;
            _Settings = new TavilyConsoleSettings();

            if (string.IsNullOrWhiteSpace(_Settings.ApiKey))
            {
                _Settings.ApiKey = currentApiKey;
            }
        }

        private static async Task RunSearch(string? queryText)
        {
            string resolvedQuery = string.IsNullOrWhiteSpace(queryText)
                ? ConsoleInput.GetString("Query:", null, false)
                : queryText.Trim();

            if (string.IsNullOrWhiteSpace(_Settings.ApiKey))
            {
                _Settings.ApiKey = ConsoleInput.GetString("Tavily API key:", null, false);
            }

            try
            {
                using TavilySearchClient client = new TavilySearchClient(_Settings.ToProviderOptions());
                TavilySearchResponse response = await client
                    .SearchAsync(_Settings.ToQuery(resolvedQuery))
                    .ConfigureAwait(false);

                WriteResponse(response);
            }
            catch (SearchProviderException ex)
            {
                Console.WriteLine("");
                Console.WriteLine("[ERROR] " + ex.Message);

                if (ex.StatusCode.HasValue)
                {
                    Console.WriteLine("Status Code: " + (int)ex.StatusCode.Value);
                }

                if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
                {
                    Console.WriteLine(ex.ResponseBody);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("");
                Console.WriteLine("[ERROR] " + ex.Message);
            }
        }

        private static void WriteResponse(TavilySearchResponse response)
        {
            Console.WriteLine("");
            Console.WriteLine("=== RESPONSE SUMMARY ===");
            Console.WriteLine("Provider   : " + response.ProviderName);
            Console.WriteLine("Query      : " + response.Query);

            if (!string.IsNullOrWhiteSpace(response.RequestId))
            {
                Console.WriteLine("Request ID : " + response.RequestId);
            }

            if (response.LatencySeconds.HasValue)
            {
                Console.WriteLine("Latency    : " + response.LatencySeconds.Value.ToString("0.000") + " sec");
            }

            if (!string.IsNullOrWhiteSpace(response.Answer))
            {
                Console.WriteLine("Answer     : " + response.Answer);
            }

            foreach (var section in response.Sections)
            {
                Console.WriteLine($"Section    : {section.Key} ({section.Value.Count} results)");

                foreach (var result in section.Value.Take(3))
                {
                    Console.WriteLine("  - " + result.Title);
                    Console.WriteLine("    " + result.Url);
                }
            }

            Console.WriteLine("");
            Console.WriteLine("=== STRUCTURED RESPONSE ===");
            JsonConsoleWriter.WriteObject(response);
            Console.WriteLine("");
        }

        private static void ShowConfig()
        {
            JsonConsoleWriter.WriteObject(new
            {
                _Settings.Endpoint,
                ApiKey = MaskApiKey(_Settings.ApiKey),
                _Settings.TimeoutSeconds,
                _Settings.MaxResults,
                _Settings.SearchDepth,
                _Settings.Topic,
                _Settings.TimeRange,
                _Settings.IncludeAnswerMode,
                _Settings.IncludeRawContentMode,
                _Settings.IncludeImages,
                _Settings.IncludeImageDescriptions,
                _Settings.IncludeFavicon,
                _Settings.AutoParameters,
                _Settings.ExactMatch,
                _Settings.IncludeUsage,
                _Settings.SafeSearch,
                _Settings.IncludeDomains,
                _Settings.ExcludeDomains
            });
        }

        private static string? NormalizeOptionalValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return value.Trim();
        }

        private static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }

            if (apiKey.Length <= 8)
            {
                return new string('*', apiKey.Length);
            }

            return apiKey[..4] + new string('*', apiKey.Length - 8) + apiKey[^4..];
        }

        private static void Menu(bool includeStartupArgs = false)
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            Console.WriteLine("  ?               help, this menu");
            Console.WriteLine("  q               quit");
            Console.WriteLine("  cls             clear the screen");
            Console.WriteLine("  config          show current configuration");
            Console.WriteLine("  endpoint        set the Tavily endpoint");
            Console.WriteLine("  apikey          set or replace the Tavily API key");
            Console.WriteLine("  count           set max results");
            Console.WriteLine("  depth           set search depth");
            Console.WriteLine("  topic           set search topic");
            Console.WriteLine("  timerange       set or clear the relative time range");
            Console.WriteLine("  answer          set include-answer mode");
            Console.WriteLine("  raw             set include-raw-content mode");
            Console.WriteLine("  images          set image options");
            Console.WriteLine("  domains         set include domains");
            Console.WriteLine("  excludedomains  set exclude domains");
            Console.WriteLine("  auto            toggle auto-parameters");
            Console.WriteLine("  safe            toggle safe search");
            Console.WriteLine("  defaults        reset settings to defaults");
            Console.WriteLine("  search          prompt for and run a search");
            Console.WriteLine("  search [text]   run a search immediately");

            if (includeStartupArgs)
            {
                Console.WriteLine("");
                Console.WriteLine("Startup args:");
                Console.WriteLine("  --endpoint      set the Tavily endpoint");
                Console.WriteLine("  --api-key       set the Tavily API key");
                Console.WriteLine("  --query         run a search immediately on startup");
                Console.WriteLine("  --max-results   set result count");
                Console.WriteLine("  --depth         set search depth");
                Console.WriteLine("  --topic         set search topic");
            }

            Console.WriteLine("");
        }
    }
}
