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
                    startInfo.FileName = "python";
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
    }
}
