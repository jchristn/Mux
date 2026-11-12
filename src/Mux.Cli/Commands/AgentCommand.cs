namespace Mux.Cli.Commands
{
    using System;
    using System.IO;
    using Mux.Core.Startup;

    /// <summary>
    /// <c>mux agent start</c> starts the tray agent (the host of the local REST server, usage telemetry, and the web
    /// dashboard) unless one from this build is already running, replacing one from another version.
    /// <c>mux agent status</c> reports whether it runs and which executable would be started. The VS Code extension
    /// runs <c>mux agent start</c> when it activates, so every user surface brings the agent up.
    /// </summary>
    public static class AgentCommand
    {
        #region Public-Members

        /// <summary>Exit code when no agent executable can be found.</summary>
        public const int NotFoundExitCode = 3;

        /// <summary>Exit code for <c>status</c> when no agent is running.</summary>
        public const int NotRunningExitCode = 1;

        /// <summary>Exit code for a usage error.</summary>
        public const int UsageExitCode = 2;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the command.
        /// </summary>
        /// <param name="args">The arguments after <c>agent</c>: <c>start</c> or <c>status</c>.</param>
        /// <param name="output">Receives normal output.</param>
        /// <param name="error">Receives errors.</param>
        /// <returns>0 on success (started, already running, or running for status); see the exit-code constants.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            string verb = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
            switch (verb)
            {
                case "start":
                    return Start(output, error);
                case "status":
                    return Status(output);
                default:
                    error.WriteLine("Usage: mux agent <start|status>");
                    error.WriteLine("  start   Start the tray agent unless one from this build is already running.");
                    error.WriteLine("  status  Show whether the tray agent is running and where it is installed.");
                    return UsageExitCode;
            }
        }

        /// <summary>The message for a start result.</summary>
        /// <param name="result">The result.</param>
        /// <param name="executable">The executable that was started, if any.</param>
        /// <returns>One line of text.</returns>
        public static string Describe(AgentStartResult result, string? executable)
        {
            switch (result)
            {
                case AgentStartResult.AlreadyRunning:
                    return "The mux agent is already running.";
                case AgentStartResult.Started:
                    return "Started the mux agent: " + executable;
                case AgentStartResult.Failed:
                    return "Could not start the mux agent at " + executable + ".";
                default:
                    return "The mux agent is not installed. Install it with the install-tool script for your OS "
                        + "(scripts/<os>/install-tool), or set " + AgentLocator.OverrideVariable + " to its path.";
            }
        }

        #endregion

        #region Private-Methods

        private static int Start(TextWriter output, TextWriter error)
        {
            AgentStartResult result = AgentLauncher.Start(out string? executable);
            string message = Describe(result, executable);
            switch (result)
            {
                case AgentStartResult.AlreadyRunning:
                case AgentStartResult.Started:
                    output.WriteLine(message);
                    return 0;
                case AgentStartResult.NotFound:
                    error.WriteLine(message);
                    return NotFoundExitCode;
                default:
                    error.WriteLine(message);
                    return 1;
            }
        }

        private static int Status(TextWriter output)
        {
            bool running = AgentLauncher.IsRunning();
            string? executable = AgentLocator.Locate();
            output.WriteLine(running ? "The mux agent is running." : "The mux agent is not running.");
            output.WriteLine(executable == null ? "No agent executable was found." : "Agent executable: " + executable);
            return running ? 0 : NotRunningExitCode;
        }

        #endregion
    }
}
