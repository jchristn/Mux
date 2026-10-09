namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// Builds the process launch for a skill command: it maps an allowlisted interpreter to its executable
    /// and leading arguments, then appends the script file and the caller's arguments as separate argv
    /// entries. Passing arguments through <see cref="ProcessStartInfo.ArgumentList"/> rather than a single
    /// command string keeps the launch free of shell quoting and injection.
    /// </summary>
    public static class SkillInterpreterResolver
    {
        /// <summary>
        /// Builds a <see cref="ProcessStartInfo"/> that runs <paramref name="scriptPath"/> with the named
        /// interpreter and the supplied arguments.
        /// </summary>
        /// <param name="interpreter">An allowlisted interpreter name (see <see cref="SkillInterpreters"/>).</param>
        /// <param name="scriptPath">The absolute path of the script or materialized block file.</param>
        /// <param name="arguments">The caller-supplied arguments, appended after the script. May be null.</param>
        /// <returns>A configured start info with the file name and argument list populated.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="interpreter"/> or <paramref name="scriptPath"/> is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the interpreter is not on the allowlist.</exception>
        public static ProcessStartInfo BuildStartInfo(string interpreter, string scriptPath, IReadOnlyList<string>? arguments)
        {
            if (interpreter == null) throw new ArgumentNullException(nameof(interpreter));
            if (scriptPath == null) throw new ArgumentNullException(nameof(scriptPath));

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            switch (interpreter.ToLowerInvariant())
            {
                case "bash":
                    startInfo.FileName = "bash";
                    break;
                case "sh":
                    startInfo.FileName = "sh";
                    break;
                case "pwsh":
                    startInfo.FileName = "pwsh";
                    startInfo.ArgumentList.Add("-NoProfile");
                    startInfo.ArgumentList.Add("-NonInteractive");
                    startInfo.ArgumentList.Add("-File");
                    break;
                case "python":
                    startInfo.FileName = ResolvePython();
                    break;
                case "node":
                    startInfo.FileName = "node";
                    break;
                case "dotnet-script":
                    startInfo.FileName = "dotnet";
                    startInfo.ArgumentList.Add("script");
                    break;
                default:
                    throw new NotSupportedException($"Interpreter '{interpreter}' is not supported.");
            }

            startInfo.ArgumentList.Add(scriptPath);

            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument ?? string.Empty);
                }
            }

            return startInfo;
        }

        /// <summary>
        /// The Python executable for the <c>python</c> interpreter: the first of <c>python3</c> then <c>python</c> on
        /// PATH (macOS and many Linux systems ship only <c>python3</c>), or <c>python</c> then <c>py</c> on Windows. Falls
        /// back to <c>python</c> so a missing interpreter still fails with a clear "not found" error.
        /// </summary>
        /// <param name="pathVariable">The PATH to search, or null for the process PATH.</param>
        /// <param name="isWindows">Whether to use Windows names, or null for the current platform.</param>
        /// <returns>The executable name or full path.</returns>
        public static string ResolvePython(string? pathVariable = null, bool? isWindows = null)
        {
            bool windows = isWindows ?? OperatingSystem.IsWindows();
            string[] candidates = windows ? new[] { "python", "py" } : new[] { "python3", "python" };
            string[] suffixes = windows ? new[] { ".exe", ".cmd", ".bat" } : new[] { string.Empty };
            foreach (string candidate in candidates)
            {
                foreach (string directory in (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (string suffix in suffixes)
                    {
                        string full = System.IO.Path.Combine(directory.Trim(), candidate + suffix);
                        if (System.IO.File.Exists(full))
                        {
                            return full;
                        }
                    }
                }
            }

            return "python";
        }
    }
}
