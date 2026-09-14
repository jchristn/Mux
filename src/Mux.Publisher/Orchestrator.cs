namespace Mux.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher.Channels;
    using Mux.Publisher.Manifest;
    using Mux.Publisher.Publishing;

    /// <summary>
    /// Runs one channel end to end: load and validate the manifest, self-contained-publish the channel's
    /// artifact (and archive it) when required, gather cross-channel checksums, plan the channel, and
    /// either execute the plan or print it under <c>--dry-run</c>. A release is this run repeated per
    /// channel across the CI matrix.
    /// </summary>
    public sealed class Orchestrator
    {
        private readonly PublishService _publishService;
        private readonly IProcessRunner _runner;
        private readonly TextWriter _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="Orchestrator"/> class.
        /// </summary>
        /// <param name="runner">The process runner for publish/package commands.</param>
        /// <param name="log">Where progress is written.</param>
        public Orchestrator(IProcessRunner runner, TextWriter log)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _publishService = new PublishService(runner);
        }

        /// <summary>
        /// Executes a single channel.
        /// </summary>
        /// <param name="options">The run options (channel, version, roots, dry-run).</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The channel plan that was executed or previewed.</returns>
        public async Task<ChannelPlan> RunChannelAsync(RunOptions options, CancellationToken ct)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            string manifestPath = Path.Combine(options.RepoRoot, "publisher.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("publisher.json not found at repo root.", manifestPath);
            }

            PublisherManifest manifest = PublisherManifest.Parse(File.ReadAllText(manifestPath));
            ManifestValidator.Validate(manifest);

            if (!manifest.Channels.TryGetValue(options.Channel, out ChannelConfig? config) || config == null)
            {
                throw new KeyNotFoundException("Channel '" + options.Channel + "' is not present in publisher.json.");
            }

            if (!config.Enabled)
            {
                _log.WriteLine("Channel '" + options.Channel + "' is disabled in publisher.json; nothing to do.");
                return new ChannelPlan(options.Channel);
            }

            IChannelDriver driver = DriverRegistry.Resolve(string.IsNullOrWhiteSpace(config.Driver) ? options.Channel : config.Driver);
            GuardOs(driver, options);

            ArtifactInfo artifact = manifest.GetArtifact(config.Artifact);
            string tfm = manifest.Build.Frameworks.Count > 0 ? manifest.Build.Frameworks[0] : "net10.0";
            string outputRoot = options.OutputRoot;
            string stagingRoot = options.StagingRoot;
            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(stagingRoot);

            List<PublishedArtifact> published = new List<PublishedArtifact>();
            if (driver.NeedsSelfContainedPublish && !options.DryRun)
            {
                published = await PublishAndArchiveAsync(manifest, config, artifact, tfm, driver.RequiredOs, options, ct).ConfigureAwait(false);
            }
            else if (driver.NeedsSelfContainedPublish)
            {
                // Dry-run: describe the intended publishes without invoking dotnet.
                foreach (string rid in ChannelHelpers.ResolveRuntimes(manifest, config, driver.RequiredOs))
                {
                    string dir = Path.Combine(stagingRoot, "publish", artifact.Id, rid);
                    published.Add(new PublishedArtifact
                    {
                        ArtifactId = artifact.Id,
                        Rid = rid,
                        Tfm = tfm,
                        PublishDir = dir,
                        PrimaryBinary = PublishService.ResolvePrimaryBinary(dir, rid),
                        Sha256 = "(dry-run)",
                        ArchiveSha256 = "(dry-run)"
                    });
                }
            }

            Dictionary<string, string> assetChecksums = ScanOutputChecksums(outputRoot);
            foreach (PublishedArtifact item in published)
            {
                if (!string.IsNullOrEmpty(item.ArchivePath))
                {
                    assetChecksums[Path.GetFileName(item.ArchivePath)] = item.ArchiveSha256;
                }
            }

            PublisherContext context = new PublisherContext(
                manifest,
                options.Version,
                options.RepoRoot,
                stagingRoot,
                outputRoot,
                artifact,
                published,
                fileName => ReleaseAssetUrl(manifest, options.Version, fileName),
                assetChecksums);

            ChannelPlan plan = driver.Plan(context, config);

            if (options.DryRun)
            {
                PrintPlan(plan);
                return plan;
            }

            await ExecutePlanAsync(plan, stagingRoot, ct).ConfigureAwait(false);
            ReportPlan(plan);
            return plan;
        }

        private async Task<List<PublishedArtifact>> PublishAndArchiveAsync(
            PublisherManifest manifest, ChannelConfig config, ArtifactInfo artifact, string tfm, TargetOs requiredOs, RunOptions options, CancellationToken ct)
        {
            List<PublishedArtifact> results = new List<PublishedArtifact>();
            foreach (string rid in ChannelHelpers.ResolveRuntimes(manifest, config, requiredOs))
            {
                // Publish output is an intermediate; keep it in staging so OutputRoot holds only deliverables.
                string publishDir = Path.Combine(options.StagingRoot, "publish", artifact.Id, rid);
                _log.WriteLine("Publishing " + artifact.Id + " (" + rid + ", " + tfm + ") ...");
                PublishedArtifact published = await _publishService.PublishAsync(artifact, tfm, rid, options.RepoRoot, publishDir, options.Version, ct).ConfigureAwait(false);

                string archiveName = Naming.Archive(manifest.Project.Name, options.Version, rid);
                string archivePath = Path.Combine(options.OutputRoot, ArchiveSubdir(rid), archiveName);
                Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
                ArchiveService.Create(publishDir, rid, archivePath);
                published.ArchivePath = archivePath;
                published.ArchiveSha256 = PublishService.ComputeSha256(archivePath);
                results.Add(published);
            }

            return results;
        }

        private static string ArchiveSubdir(string rid)
        {
            switch (ChannelHelpers.OsForRid(rid))
            {
                case TargetOs.Windows: return "windows";
                case TargetOs.MacOs: return "macos";
                case TargetOs.Linux: return "linux";
                default: return "misc";
            }
        }

        private void GuardOs(IChannelDriver driver, RunOptions options)
        {
            if (driver.RequiredOs == TargetOs.Any || options.DryRun || options.Force) return;

            TargetOs current = CurrentOs();
            if (current != driver.RequiredOs)
            {
                throw new InvalidOperationException(
                    "Channel '" + driver.Name + "' must run on " + driver.RequiredOs + " (native packaging/signing), but this host is " + current +
                    ". Run it in the matching CI matrix job, or pass --dry-run to preview the plan.");
            }
        }

        private static TargetOs CurrentOs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return TargetOs.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return TargetOs.MacOs;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return TargetOs.Linux;
            return TargetOs.Any;
        }

        private static Dictionary<string, string> ScanOutputChecksums(string outputRoot)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(outputRoot)) return map;

            foreach (string file in Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                // Only hash finished, uploadable artifacts, not intermediate publish output.
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    if (!map.ContainsKey(name)) map[name] = PublishService.ComputeSha256(file);
                }
            }

            return map;
        }

        /// <summary>
        /// Builds the GitHub Release download URL for an asset uploaded under a version tag.
        /// </summary>
        /// <param name="manifest">The manifest (for the repo slug).</param>
        /// <param name="version">The release version.</param>
        /// <param name="assetFileName">The asset file name.</param>
        /// <returns>The absolute download URL.</returns>
        public static string ReleaseAssetUrl(PublisherManifest manifest, string version, string assetFileName)
        {
            string repo = manifest.Project.Repo;
            return "https://github.com/" + repo + "/releases/download/v" + version + "/" + assetFileName;
        }

        private async Task ExecutePlanAsync(ChannelPlan plan, string stagingRoot, CancellationToken ct)
        {
            foreach (GeneratedFile file in plan.Files)
            {
                string path = Path.Combine(stagingRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content);
                _log.WriteLine("  wrote " + file.RelativePath);
            }

            foreach (ShellCommand command in plan.Commands)
            {
                _log.WriteLine("  $ " + command.ToDisplayString());
                string workDir = command.WorkingDirectory ?? stagingRoot;

                // Expand $VAR / ${VAR} secret references from the environment at execution time. Plans stay
                // readable and testable (they carry the literal $VAR); only the actual run resolves them.
                List<string> resolved = new List<string>(command.Arguments.Count);
                foreach (string argument in command.Arguments) resolved.Add(ExpandEnvironment(argument));

                ProcessResult result = await _runner.RunAsync(command.Executable, resolved, workDir, ct).ConfigureAwait(false);
                if (result.ExitCode != 0 && !command.ContinueOnError)
                {
                    throw new InvalidOperationException(
                        "Command failed (" + command.Executable + ", exit " + result.ExitCode + "): " + command.Description + Environment.NewLine + result.StandardError);
                }

                if (result.ExitCode != 0)
                {
                    _log.WriteLine("    (non-zero exit " + result.ExitCode + ", continuing: " + command.Description + ")");
                }
            }
        }

        /// <summary>
        /// Expands <c>$NAME</c> and <c>${NAME}</c> environment-variable references in a token. An
        /// undefined variable expands to empty (the underlying command then reports the missing credential).
        /// </summary>
        /// <param name="token">The argument token.</param>
        /// <returns>The token with environment references resolved.</returns>
        public static string ExpandEnvironment(string token)
        {
            if (string.IsNullOrEmpty(token) || token.IndexOf('$') < 0) return token;

            System.Text.StringBuilder builder = new System.Text.StringBuilder(token.Length);
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (c != '$')
                {
                    builder.Append(c);
                    continue;
                }

                int start = i + 1;
                bool braced = start < token.Length && token[start] == '{';
                if (braced) start++;

                int end = start;
                while (end < token.Length && (char.IsLetterOrDigit(token[end]) || token[end] == '_')) end++;

                string name = token.Substring(start, end - start);
                if (name.Length == 0)
                {
                    builder.Append(c);
                    continue;
                }

                builder.Append(Environment.GetEnvironmentVariable(name) ?? string.Empty);
                i = braced && end < token.Length && token[end] == '}' ? end : end - 1;
            }

            return builder.ToString();
        }

        private void PrintPlan(ChannelPlan plan)
        {
            _log.WriteLine("== DRY RUN: channel '" + plan.Channel + "' ==");
            _log.WriteLine("Files (" + plan.Files.Count + "):");
            foreach (GeneratedFile file in plan.Files)
            {
                _log.WriteLine("  - " + file.RelativePath + " (" + file.Content.Length + " chars)");
            }

            _log.WriteLine("Commands (" + plan.Commands.Count + "):");
            foreach (ShellCommand command in plan.Commands)
            {
                _log.WriteLine("  $ " + command.ToDisplayString() + (command.ContinueOnError ? "   [continue-on-error]" : string.Empty));
            }

            ReportPlan(plan);
        }

        private void ReportPlan(ChannelPlan plan)
        {
            if (plan.Notes.Count > 0)
            {
                _log.WriteLine("Notes:");
                foreach (string note in plan.Notes)
                {
                    _log.WriteLine("  * " + note);
                }
            }

            if (plan.EndsAtExternalGate)
            {
                _log.WriteLine("STATUS: submitted; PENDING external review — not yet live.");
            }
        }
    }

    /// <summary>Options for a single channel run.</summary>
    public sealed class RunOptions
    {
        /// <summary>The channel to run.</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>The release version (supplied at build time; never from the manifest).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Repository root containing <c>publisher.json</c>.</summary>
        public string RepoRoot { get; set; } = string.Empty;

        /// <summary>Directory for final, uploadable artifacts.</summary>
        public string OutputRoot { get; set; } = string.Empty;

        /// <summary>Directory for recipes and intermediate packaging inputs.</summary>
        public string StagingRoot { get; set; } = string.Empty;

        /// <summary>When true, print the plan without publishing or packaging.</summary>
        public bool DryRun { get; set; }

        /// <summary>When true, skip the OS guard (advanced/testing only).</summary>
        public bool Force { get; set; }
    }
}
