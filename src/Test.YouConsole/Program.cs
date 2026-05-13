namespace Test.YouConsole
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Mux.Search.Exceptions;
    using Mux.Search.Providers.You;
    using SearchConsoleShared;

    /// <summary>
    /// Interactive console harness for exercising You.com search integration.
    /// </summary>
    public class Program
    {
        private static bool _RunForever = true;
        private static YouConsoleSettings _Settings = new YouConsoleSettings();

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
                    _Settings.MaxResults = ConsoleInput.GetInt("Max results (1-100):", _Settings.MaxResults, 1, 100);
                    break;
                case "offset":
                    _Settings.Offset = ConsoleInput.GetInt("Offset (0-9):", _Settings.Offset, 0, 9);
                    break;
                case "language":
                    _Settings.Language = ConsoleInput.GetString("Language:", _Settings.Language, false);
                    break;
                case "country":
                    _Settings.Country = NormalizeOptionalValue(
                        ConsoleInput.GetString("Country (blank to clear):", _Settings.Country, true));
                    break;
                case "freshness":
                    _Settings.Freshness = NormalizeOptionalValue(
                        ConsoleInput.GetString("Freshness (blank to clear):", _Settings.Freshness, true));
                    break;
                case "safe":
                    _Settings.SafeSearch = ConsoleInput.GetString("Safe search:", _Settings.SafeSearch, false);
                    break;
                case "livecrawl":
                    _Settings.Livecrawl = NormalizeOptionalValue(
                        ConsoleInput.GetString("Livecrawl mode (blank to clear):", _Settings.Livecrawl, true));
                    break;
                case "formats":
                    _Settings.LivecrawlFormats = ConsoleInput.GetCsv("Livecrawl formats (comma-separated):", _Settings.LivecrawlFormats);
                    break;
                case "domains":
                    _Settings.IncludeDomains = ConsoleInput.GetCsv("Include domains (comma-separated):", _Settings.IncludeDomains);
                    _Settings.ExcludeDomains.Clear();
                    break;
                case "excludedomains":
                    _Settings.ExcludeDomains = ConsoleInput.GetCsv("Exclude domains (comma-separated):", _Settings.ExcludeDomains);
                    _Settings.IncludeDomains.Clear();
                    break;
                case "boostdomains":
                    _Settings.BoostDomains = ConsoleInput.GetCsv("Boost domains (comma-separated):", _Settings.BoostDomains);
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
                _Settings.MaxResults = Math.Clamp(parsedMaxResults, 1, 100);
            }

            string? offset = ConsoleArguments.GetValue(args, "--offset");
            if (int.TryParse(offset, out int parsedOffset))
            {
                _Settings.Offset = Math.Clamp(parsedOffset, 0, 9);
            }

            _Settings.Language = ConsoleArguments.GetValue(args, "--language") ?? _Settings.Language;
            _Settings.Country = ConsoleArguments.GetValue(args, "--country") ?? _Settings.Country;
            _Settings.Freshness = ConsoleArguments.GetValue(args, "--freshness") ?? _Settings.Freshness;
            _Settings.SafeSearch = ConsoleArguments.GetValue(args, "--safe-search") ?? _Settings.SafeSearch;
            _Settings.Livecrawl = ConsoleArguments.GetValue(args, "--livecrawl") ?? _Settings.Livecrawl;
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
            _Settings = new YouConsoleSettings();

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
                _Settings.ApiKey = ConsoleInput.GetString("You.com API key:", null, false);
            }

            try
            {
                using YouSearchClient client = new YouSearchClient(_Settings.ToProviderOptions());
                YouSearchResponse response = await client
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

        private static void WriteResponse(YouSearchResponse response)
        {
            Console.WriteLine("");
            Console.WriteLine("=== RESPONSE SUMMARY ===");
            Console.WriteLine("Provider   : " + response.ProviderName);
            Console.WriteLine("Query      : " + response.Query);

            if (!string.IsNullOrWhiteSpace(response.SearchUuid))
            {
                Console.WriteLine("Search UUID: " + response.SearchUuid);
            }

            if (response.LatencySeconds.HasValue)
            {
                Console.WriteLine("Latency    : " + response.LatencySeconds.Value.ToString("0.000") + " sec");
            }

            foreach (var section in response.Sections.Where(section => section.Value.Count > 0))
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
                _Settings.Offset,
                _Settings.Language,
                _Settings.Country,
                _Settings.Freshness,
                _Settings.SafeSearch,
                _Settings.Livecrawl,
                _Settings.CrawlTimeoutSeconds,
                _Settings.LivecrawlFormats,
                _Settings.IncludeDomains,
                _Settings.ExcludeDomains,
                _Settings.BoostDomains
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
            Console.WriteLine("  endpoint        set the You.com endpoint");
            Console.WriteLine("  apikey          set or replace the You.com API key");
            Console.WriteLine("  count           set max results");
            Console.WriteLine("  offset          set paging offset");
            Console.WriteLine("  language        set UI language");
            Console.WriteLine("  country         set or clear country");
            Console.WriteLine("  freshness       set or clear freshness");
            Console.WriteLine("  safe            set safe-search mode");
            Console.WriteLine("  livecrawl       set or clear livecrawl mode");
            Console.WriteLine("  formats         set livecrawl formats");
            Console.WriteLine("  domains         set include domains");
            Console.WriteLine("  excludedomains  set exclude domains");
            Console.WriteLine("  boostdomains    set boost domains");
            Console.WriteLine("  defaults        reset settings to defaults");
            Console.WriteLine("  search          prompt for and run a search");
            Console.WriteLine("  search [text]   run a search immediately");

            if (includeStartupArgs)
            {
                Console.WriteLine("");
                Console.WriteLine("Startup args:");
                Console.WriteLine("  --endpoint      set the You.com endpoint");
                Console.WriteLine("  --api-key       set the You.com API key");
                Console.WriteLine("  --query         run a search immediately on startup");
                Console.WriteLine("  --max-results   set result count");
                Console.WriteLine("  --offset        set paging offset");
                Console.WriteLine("  --language      set UI language");
                Console.WriteLine("  --country       set country");
                Console.WriteLine("  --freshness     set freshness");
                Console.WriteLine("  --safe-search   set safe-search mode");
                Console.WriteLine("  --livecrawl     set livecrawl mode");
            }

            Console.WriteLine("");
        }
    }
}
