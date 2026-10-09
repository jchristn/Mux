namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs throwaway Docker containers for integration tests. Docker tests are opt-in (they pull large images and
    /// need the network): they run only when <c>MUX_TEST_DOCKER=1</c> (Test.Automated: <c>--docker</c>) and a Docker
    /// engine answers on a Linux or macOS host. Every container is labeled, started with <c>--rm</c>, and removed by
    /// <see cref="DockerContainer.Dispose"/>, so a failing test still cleans up.
    /// </summary>
    public static class DockerHarness
    {
        #region Private-Members

        private static readonly Lazy<bool> _EngineAvailable = new Lazy<bool>(() => Run(TimeSpan.FromSeconds(30), "info", "--format", "{{.ServerVersion}}").ExitCode == 0, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Members

        /// <summary>The variable that opts in to Docker tests.</summary>
        public const string EnableVariable = "MUX_TEST_DOCKER";

        /// <summary>The label every test container carries.</summary>
        public const string Label = "mux-test=1";

        /// <summary>Whether Docker tests were requested.</summary>
        public static bool Requested
        {
            get => Environment.GetEnvironmentVariable(EnableVariable) == "1";
        }

        /// <summary>Whether Docker tests can run here: requested, a Unix host (the shims are shell scripts and the images are Linux), and a responsive engine.</summary>
        public static bool IsEnabled
        {
            get => Requested && !OperatingSystem.IsWindows() && _EngineAvailable.Value;
        }

        /// <summary>Why Docker tests are skipped, for skip reasons.</summary>
        public static string SkipReason
        {
            get
            {
                if (!Requested) return "Docker integration tests are opt-in: pass --docker to Test.Automated or set MUX_TEST_DOCKER=1";
                if (OperatingSystem.IsWindows()) return "Docker integration tests need a Linux or macOS host";
                return "no Docker engine is running";
            }
        }

        /// <summary>Whether the host is ARM64, where some images (SQL Server) only run under emulation.</summary>
        public static bool IsArm64Host
        {
            get => RuntimeInformation.OSArchitecture == Architecture.Arm64;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs a docker command and captures its output.
        /// </summary>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="arguments">The docker arguments.</param>
        /// <returns>The result.</returns>
        public static DockerResult Run(TimeSpan timeout, params string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            try
            {
                using (Process process = Process.Start(info)!)
                {
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { process.Kill(true); } catch (Exception) { }
                        return new DockerResult(-1, stdout.IsCompleted ? stdout.Result : string.Empty, "timed out after " + timeout.TotalSeconds + "s");
                    }

                    process.WaitForExit();
                    return new DockerResult(process.ExitCode, stdout.Result, stderr.Result);
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return new DockerResult(-1, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Starts a container, detached, labeled, and removed on stop.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <param name="runArguments">Extra <c>docker run</c> arguments (environment, ports, volumes).</param>
        /// <param name="command">The container command, or empty for the image default.</param>
        /// <returns>The container.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the container does not start.</exception>
        public static DockerContainer Start(string image, IEnumerable<string>? runArguments = null, IEnumerable<string>? command = null)
        {
            string name = "mux-test-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            List<string> arguments = new List<string> { "run", "-d", "--rm", "--name", name, "--label", Label };
            if (runArguments != null) arguments.AddRange(runArguments);
            arguments.Add(image);
            if (command != null) arguments.AddRange(command);
            DockerResult started = Run(TimeSpan.FromMinutes(10), arguments.ToArray());
            if (started.ExitCode != 0)
            {
                Run(TimeSpan.FromSeconds(60), "rm", "-f", name);
                throw new InvalidOperationException("docker run " + image + " failed: " + started.StandardError.Trim());
            }

            return new DockerContainer(name, image);
        }

        /// <summary>
        /// Writes executable shell shims named after client tools that forward each call into a container with
        /// <c>docker exec -i</c>, passing the named environment variables through, so a skill that runs <c>psql</c>
        /// on the host really runs the server image's own client.
        /// </summary>
        /// <param name="directory">The folder to write the shims into (created when missing).</param>
        /// <param name="container">The container.</param>
        /// <param name="tools">Shim name to the command inside the container.</param>
        /// <param name="passThrough">Environment variables to forward from the caller.</param>
        /// <returns>The folder, for prepending to PATH.</returns>
        public static string WriteShims(string directory, DockerContainer container, IReadOnlyDictionary<string, string> tools, IEnumerable<string> passThrough)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            Directory.CreateDirectory(directory);
            List<string> envFlags = new List<string>();
            foreach (string variable in passThrough) envFlags.Add("-e " + variable);
            foreach (KeyValuePair<string, string> tool in tools)
            {
                string path = Path.Combine(directory, tool.Key);
                File.WriteAllText(path, "#!/bin/sh\nexec docker exec -i " + string.Join(" ", envFlags) + " " + container.Name + " " + tool.Value + " \"$@\"\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
                }
            }

            return directory;
        }

        /// <summary>
        /// A PATH value with a folder in front of the current PATH.
        /// </summary>
        /// <param name="directory">The folder.</param>
        /// <returns>The PATH value.</returns>
        public static string PathWith(string directory)
        {
            return directory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        }

        #endregion
    }
}
