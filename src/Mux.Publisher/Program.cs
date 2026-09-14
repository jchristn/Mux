namespace Mux.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher.Channels;
    using Mux.Publisher.Manifest;
    using Mux.Publisher.Publishing;

    /// <summary>
    /// Command-line entry point for the mux release orchestrator. Runs a single channel from
    /// <c>publisher.json</c> against a version supplied at build time. Invoked per-channel by the CI
    /// matrix (each OS job runs the channels it owns); can also run locally for the channels the current
    /// OS supports, or with <c>--dry-run</c> anywhere to preview a plan.
    /// </summary>
    public static class Program
    {
        /// <summary>Program entry point.</summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>0 on success; non-zero on error or invalid usage.</returns>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Dictionary<string, string> options = ParseArgs(args, out HashSet<string> flags);

                if (flags.Contains("help") || (args.Length == 0))
                {
                    PrintUsage();
                    return args.Length == 0 ? 1 : 0;
                }

                string repoRoot = options.TryGetValue("repo", out string? r) ? Path.GetFullPath(r) : FindRepoRoot(Directory.GetCurrentDirectory());

                if (flags.Contains("list"))
                {
                    Console.WriteLine("Known channels: " + string.Join(", ", DriverRegistry.KnownChannels));
                    return 0;
                }

                if (flags.Contains("validate"))
                {
                    return Validate(repoRoot);
                }

                if (options.TryGetValue("channels-for", out string? osArg))
                {
                    PublisherManifest m = PublisherManifest.Parse(File.ReadAllText(Path.Combine(repoRoot, "publisher.json")));
                    Console.WriteLine(string.Join(" ", ChannelPlacement.ChannelsFor(m, osArg)));
                    return 0;
                }

                if (!options.TryGetValue("channel", out string? channel) || string.IsNullOrWhiteSpace(channel))
                {
                    Console.Error.WriteLine("error: --channel is required.");
                    PrintUsage();
                    return 1;
                }

                bool dryRun = flags.Contains("dry-run");
                if (!options.TryGetValue("version", out string? version) || string.IsNullOrWhiteSpace(version))
                {
                    version = Environment.GetEnvironmentVariable("MUX_RELEASE_VERSION") ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(version) && !dryRun)
                {
                    Console.Error.WriteLine("error: --version is required (or set MUX_RELEASE_VERSION). Use --dry-run to preview without one.");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(version)) version = "0.0.0-dryrun";

                RunOptions runOptions = new RunOptions
                {
                    Channel = channel!,
                    Version = version!,
                    RepoRoot = repoRoot,
                    OutputRoot = options.TryGetValue("out", out string? o) ? Path.GetFullPath(o) : Path.Combine(repoRoot, "dist", "release"),
                    StagingRoot = options.TryGetValue("staging", out string? s) ? Path.GetFullPath(s) : Path.Combine(repoRoot, "dist", "staging"),
                    DryRun = dryRun,
                    Force = flags.Contains("force")
                };

                Orchestrator orchestrator = new Orchestrator(new ProcessRunner(), Console.Out);
                await orchestrator.RunChannelAsync(runOptions, CancellationToken.None).ConfigureAwait(false);
                return 0;
            }
            catch (ManifestValidationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 2;
            }
        }

        private static int Validate(string repoRoot)
        {
            string manifestPath = Path.Combine(repoRoot, "publisher.json");
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine("error: publisher.json not found at " + manifestPath);
                return 2;
            }

            PublisherManifest manifest = PublisherManifest.Parse(File.ReadAllText(manifestPath));
            List<string> errors = ManifestValidator.Collect(manifest);
            if (errors.Count == 0)
            {
                Console.WriteLine("publisher.json is valid. Channels: " + string.Join(", ", manifest.Channels.Keys));
                return 0;
            }

            Console.Error.WriteLine("publisher.json is invalid:");
            foreach (string error in errors) Console.Error.WriteLine("  - " + error);
            return 2;
        }

        private static Dictionary<string, string> ParseArgs(string[] args, out HashSet<string> flags)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dry-run", "force", "list", "validate", "help" };

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                string key = arg.Substring(2);

                if (knownFlags.Contains(key))
                {
                    flags.Add(key);
                    continue;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[key] = args[++i];
                }
                else
                {
                    flags.Add(key);
                }
            }

            return options;
        }

        private static string FindRepoRoot(string start)
        {
            DirectoryInfo? dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "publisher.json"))) return dir.FullName;
                dir = dir.Parent;
            }

            return start;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Mux.Publisher — build installers and package-manager entries from publisher.json");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  Mux.Publisher --channel <name> --version <X.Y.Z> [--repo <dir>] [--out <dir>] [--staging <dir>] [--dry-run] [--force]");
            Console.WriteLine("  Mux.Publisher --list        List known channels");
            Console.WriteLine("  Mux.Publisher --validate    Validate publisher.json");
            Console.WriteLine();
            Console.WriteLine("Channels: " + string.Join(", ", DriverRegistry.KnownChannels));
            Console.WriteLine("The version is always supplied here, never stored in publisher.json.");
        }
    }
}
