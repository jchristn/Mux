namespace Mux.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Finds the mux command-line program for launchers that cannot rely on the user's shell PATH, such as the tray
    /// agent started from the Dock, a login item, or a desktop shortcut (macOS gives those apps a minimal PATH).
    /// The search order is: the <c>MUX_CLI</c> override (a file or a directory), <c>mux</c> on PATH, the .NET global
    /// tools folder, a copy beside the running program, and finally the newest build in a repository checkout found
    /// by walking up from the given roots.
    /// </summary>
    public static class MuxCliLocator
    {
        #region Public-Members

        /// <summary>The environment variable that overrides the search.</summary>
        public const string OverrideVariable = "MUX_CLI";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds the mux CLI using the process environment, the running program's folder, and the current directory.
        /// </summary>
        /// <returns>The location, or null when no copy was found.</returns>
        public static MuxCliLocation? Locate()
        {
            return Locate(
                Environment.GetEnvironmentVariable(OverrideVariable),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                new[] { AppContext.BaseDirectory, Environment.CurrentDirectory },
                OperatingSystem.IsWindows());
        }

        /// <summary>
        /// Finds the mux CLI from explicit inputs (used directly by tests).
        /// </summary>
        /// <param name="overridePath">The <c>MUX_CLI</c> value: a file, or a directory holding <c>mux</c> or <c>Mux.Cli</c>. Ignored when blank or missing.</param>
        /// <param name="pathVariable">The PATH value to search.</param>
        /// <param name="homeDirectory">The user's home directory (for <c>~/.dotnet/tools</c>).</param>
        /// <param name="roots">Folders to check beside and to walk up from, in order.</param>
        /// <param name="isWindows">Whether to look for <c>.exe</c> names.</param>
        /// <returns>The location, or null when no copy was found.</returns>
        public static MuxCliLocation? Locate(string? overridePath, string? pathVariable, string? homeDirectory, IEnumerable<string?> roots, bool isWindows)
        {
            string[] names = isWindows ? new[] { "mux.exe", "Mux.Cli.exe" } : new[] { "mux", "Mux.Cli" };

            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (File.Exists(overridePath))
                {
                    return FromFile(overridePath, "override");
                }

                MuxCliLocation? inDirectory = FindIn(overridePath, names, "override");
                if (inDirectory != null)
                {
                    return inDirectory;
                }
            }

            foreach (string directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory.Trim(), isWindows ? "mux.exe" : "mux");
                if (File.Exists(candidate))
                {
                    return new MuxCliLocation { Executable = candidate, Source = "path" };
                }
            }

            if (!string.IsNullOrWhiteSpace(homeDirectory))
            {
                string tool = Path.Combine(homeDirectory, ".dotnet", "tools", isWindows ? "mux.exe" : "mux");
                if (File.Exists(tool))
                {
                    return new MuxCliLocation { Executable = tool, Source = "dotnet-tools" };
                }
            }

            List<string> rootList = new List<string>();
            foreach (string? root in roots ?? Array.Empty<string?>())
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    rootList.Add(root);
                }
            }

            foreach (string root in rootList)
            {
                MuxCliLocation? beside = FindIn(root, names, "beside");
                if (beside != null)
                {
                    return beside;
                }
            }

            foreach (string root in rootList)
            {
                MuxCliLocation? built = FindInCheckout(root, isWindows);
                if (built != null)
                {
                    return built;
                }
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static MuxCliLocation? FindIn(string directory, string[] names, string source)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return new MuxCliLocation { Executable = candidate, Source = source };
                }
            }

            string dll = Path.Combine(directory, "Mux.Cli.dll");
            return File.Exists(dll) ? FromFile(dll, source) : null;
        }

        private static MuxCliLocation FromFile(string file, string source)
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return new MuxCliLocation { Executable = "dotnet", LeadingArguments = new List<string> { Path.GetFullPath(file) }, Source = source };
            }

            return new MuxCliLocation { Executable = Path.GetFullPath(file), Source = source };
        }

        // Walks up from a folder to a checkout (a folder containing src/Mux.Cli) and takes the newest built CLI.
        private static MuxCliLocation? FindInCheckout(string start, bool isWindows)
        {
            try
            {
                DirectoryInfo? directory = new DirectoryInfo(start);
                for (int depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
                {
                    string cliProject = Path.Combine(directory.FullName, "src", "Mux.Cli");
                    if (!Directory.Exists(cliProject))
                    {
                        continue;
                    }

                    string bin = Path.Combine(cliProject, "bin");
                    if (!Directory.Exists(bin))
                    {
                        return null;
                    }

                    string appHost = isWindows ? "Mux.Cli.exe" : "Mux.Cli";
                    FileInfo? newest = new DirectoryInfo(bin).EnumerateFiles(appHost, SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (newest != null)
                    {
                        return new MuxCliLocation { Executable = newest.FullName, Source = "checkout" };
                    }

                    FileInfo? dll = new DirectoryInfo(bin).EnumerateFiles("Mux.Cli.dll", SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                    return dll == null ? null : FromFile(dll.FullName, "checkout");
                }
            }
            catch (Exception)
            {
                // An unreadable folder ends the search.
            }

            return null;
        }

        #endregion
    }
}
