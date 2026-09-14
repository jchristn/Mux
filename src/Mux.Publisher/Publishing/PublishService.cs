namespace Mux.Publisher.Publishing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher.Channels;
    using Mux.Publisher.Manifest;

    /// <summary>
    /// Wraps the .NET build steps every channel starts from: a self-contained, single-file publish per
    /// runtime identifier (the exact invocation mandated by the packaging standard) and a framework-
    /// dependent <c>dotnet pack</c> for .NET-tool artifacts. Every produced artifact gets a SHA-256
    /// checksum that package-manager manifests reference. The argument builders are pure and unit
    /// tested; execution shells out through an injected <see cref="IProcessRunner"/>.
    /// </summary>
    public sealed class PublishService
    {
        private readonly IProcessRunner _runner;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishService"/> class.
        /// </summary>
        /// <param name="runner">The process runner used to invoke <c>dotnet</c>.</param>
        public PublishService(IProcessRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>
        /// Builds the argument list for a self-contained, single-file publish. This is the canonical
        /// invocation from the packaging standard:
        /// <c>dotnet publish &lt;csproj&gt; -c Release -f &lt;tfm&gt; -r &lt;rid&gt; --self-contained true
        /// -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o &lt;out&gt;</c>.
        /// A version, when supplied, is threaded through <c>-p:Version</c>.
        /// </summary>
        /// <param name="csproj">The project to publish.</param>
        /// <param name="tfm">The target framework.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <param name="outputDir">The publish output directory.</param>
        /// <param name="version">The version to stamp, or null to leave the project default.</param>
        /// <returns>The ordered argument list for <c>dotnet</c>.</returns>
        public static List<string> BuildPublishArgs(string csproj, string tfm, string rid, string outputDir, string? version)
        {
            if (string.IsNullOrWhiteSpace(csproj)) throw new ArgumentException("csproj is required.", nameof(csproj));
            if (string.IsNullOrWhiteSpace(tfm)) throw new ArgumentException("tfm is required.", nameof(tfm));
            if (string.IsNullOrWhiteSpace(rid)) throw new ArgumentException("rid is required.", nameof(rid));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("outputDir is required.", nameof(outputDir));

            List<string> args = new List<string>
            {
                "publish",
                csproj,
                "-c", "Release",
                "-f", tfm,
                "-r", rid,
                "--self-contained", "true",
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:DebugType=none",
                "-o", outputDir
            };

            if (!string.IsNullOrWhiteSpace(version))
            {
                args.Add("-p:Version=" + version);
            }

            return args;
        }

        /// <summary>
        /// Builds the argument list for a framework-dependent NuGet pack of a .NET tool.
        /// </summary>
        /// <param name="csproj">The tool project to pack.</param>
        /// <param name="outputDir">The directory for the produced <c>.nupkg</c>/<c>.snupkg</c>.</param>
        /// <param name="version">The version to stamp, or null to leave the project default.</param>
        /// <returns>The ordered argument list for <c>dotnet</c>.</returns>
        public static List<string> BuildPackArgs(string csproj, string outputDir, string? version)
        {
            if (string.IsNullOrWhiteSpace(csproj)) throw new ArgumentException("csproj is required.", nameof(csproj));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("outputDir is required.", nameof(outputDir));

            List<string> args = new List<string>
            {
                "pack",
                csproj,
                "-c", "Release",
                "-o", outputDir
            };

            if (!string.IsNullOrWhiteSpace(version))
            {
                args.Add("-p:Version=" + version);
            }

            return args;
        }

        /// <summary>
        /// Runs a self-contained publish for one artifact/runtime and returns the described output with
        /// its computed SHA-256 checksum.
        /// </summary>
        /// <param name="artifact">The artifact to publish.</param>
        /// <param name="tfm">The target framework.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <param name="repoRoot">Repository root (for resolving the relative csproj path).</param>
        /// <param name="outputDir">The publish output directory.</param>
        /// <param name="version">The version to stamp.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The published-artifact description.</returns>
        public async Task<PublishedArtifact> PublishAsync(
            ArtifactInfo artifact,
            string tfm,
            string rid,
            string repoRoot,
            string outputDir,
            string version,
            CancellationToken ct)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));

            string csproj = Path.Combine(repoRoot, artifact.Csproj.Replace('/', Path.DirectorySeparatorChar));
            List<string> args = BuildPublishArgs(csproj, tfm, rid, outputDir, version);

            ProcessResult result = await _runner.RunAsync("dotnet", args, repoRoot, ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("dotnet publish failed (" + rid + "): exit " + result.ExitCode + Environment.NewLine + result.StandardError);
            }

            string primary = ResolvePrimaryBinary(outputDir, rid);
            PublishedArtifact published = new PublishedArtifact
            {
                ArtifactId = artifact.Id,
                Rid = rid,
                Tfm = tfm,
                PublishDir = outputDir,
                PrimaryBinary = primary,
                Sha256 = File.Exists(primary) ? ComputeSha256(primary) : string.Empty
            };

            return published;
        }

        /// <summary>
        /// Resolves the primary launcher inside a publish directory: on Windows the largest matching
        /// <c>.exe</c>, elsewhere the extension-less executable of the same name.
        /// </summary>
        /// <param name="publishDir">The publish output directory.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>The best-guess path to the primary binary.</returns>
        public static string ResolvePrimaryBinary(string publishDir, string rid)
        {
            bool isWindows = ChannelHelpers.OsForRid(rid) == TargetOs.Windows;
            if (!Directory.Exists(publishDir)) return Path.Combine(publishDir, isWindows ? "app.exe" : "app");

            string best = string.Empty;
            long bestSize = -1;
            foreach (string file in Directory.GetFiles(publishDir))
            {
                string name = Path.GetFileName(file);
                bool candidate = isWindows
                    ? name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    : (!name.Contains('.') || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) candidate = false;
                if (!candidate) continue;

                long size = new FileInfo(file).Length;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = file;
                }
            }

            return string.IsNullOrEmpty(best) ? Path.Combine(publishDir, isWindows ? "Mux.Desktop.exe" : "Mux.Desktop") : best;
        }

        /// <summary>
        /// Computes the lowercase-hex SHA-256 of a file.
        /// </summary>
        /// <param name="path">The file to hash.</param>
        /// <returns>The lowercase-hex digest.</returns>
        public static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        /// <summary>
        /// Computes the lowercase-hex SHA-256 of a byte buffer.
        /// </summary>
        /// <param name="bytes">The bytes to hash.</param>
        /// <returns>The lowercase-hex digest.</returns>
        public static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
