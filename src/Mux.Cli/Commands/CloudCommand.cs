namespace Mux.Cli.Commands
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Startup;

    /// <summary>
    /// <c>mux cloud login|logout|status|remote-approval</c>: connect this machine to Mux Cloud so the cloud dashboard
    /// and mobile app can use it from anywhere. <c>login</c> exchanges an email and password for a machine credential
    /// (the password is never stored) and starts the tray agent, which runs the tunnel.
    /// </summary>
    public static class CloudCommand
    {
        #region Public-Members

        /// <summary>Exit code for a usage error.</summary>
        public const int UsageExitCode = 2;

        /// <summary>Exit code when sign-in fails or the machine is not connected (status).</summary>
        public const int FailedExitCode = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the command.
        /// </summary>
        /// <param name="args">The arguments after <c>cloud</c>.</param>
        /// <param name="input">Reads the email and password when not given as options.</param>
        /// <param name="output">Receives normal output.</param>
        /// <param name="error">Receives errors.</param>
        /// <returns>0 on success; see the exit-code constants.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            string verb = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
            try
            {
                switch (verb)
                {
                    case "login":
                        return LoginAsync(args, input, output, error, CancellationToken.None).GetAwaiter().GetResult();
                    case "logout":
                        CloudAccountService.Logout();
                        output.WriteLine("Signed this machine out of Mux Cloud. It stays listed in your account (offline) until you remove it there.");
                        return 0;
                    case "status":
                        return Status(output);
                    case "remote-approval":
                        return RemoteApproval(args, output, error);
                    default:
                        PrintUsage(error);
                        return UsageExitCode;
                }
            }
            catch (ArgumentException e)
            {
                error.WriteLine("mux cloud: " + e.Message);
                return UsageExitCode;
            }
            catch (CloudEnrollmentException e)
            {
                error.WriteLine("mux cloud: " + e.Message);
                return FailedExitCode;
            }
        }

        /// <summary>
        /// Print a status in the shared multi-line format every surface uses.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="output">Destination.</param>
        public static void WriteStatus(CloudStatus status, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(status);
            ArgumentNullException.ThrowIfNull(output);
            output.WriteLine("Mux Cloud : " + status.StateName);
            output.WriteLine("  " + status.Describe());
            if (!string.IsNullOrEmpty(status.Reason) && status.State != CloudStateEnum.Reconnecting && status.State != CloudStateEnum.SignedOut) output.WriteLine("  reason          : " + status.Reason);
            output.WriteLine("  server          : " + status.ServerUrl);
            if (!string.IsNullOrEmpty(status.MachineName)) output.WriteLine("  machine         : " + status.MachineName + (string.IsNullOrEmpty(status.MachineId) ? string.Empty : " (" + status.MachineId + ")"));
            if (!string.IsNullOrEmpty(status.AccountEmail)) output.WriteLine("  account         : " + status.AccountEmail);
            output.WriteLine("  remote approval : " + (status.AllowRemoteToolApproval ? "on (remote users can approve tools; turn off with: mux cloud remote-approval off)" : "off (remote runs deny tools that would prompt)"));
            if (status.LatencyMs.HasValue) output.WriteLine("  latency         : " + status.LatencyMs.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            if (status.RetryInSeconds.HasValue) output.WriteLine("  next retry      : " + status.RetryInSeconds.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s");
        }

        #endregion

        #region Private-Methods

        private static void PrintUsage(TextWriter error)
        {
            error.WriteLine("Usage: mux cloud <login|logout|status|remote-approval>");
            error.WriteLine("  login [--email E] [--password-stdin] [--server URL] [--name NAME]");
            error.WriteLine("                         Sign this machine in to Mux Cloud (default https://app.usemux.ai)");
            error.WriteLine("  logout                 Sign this machine out and delete its credential");
            error.WriteLine("  status                 Show the Mux Cloud connection");
            error.WriteLine("  remote-approval <on|off>  Allow or refuse tool approvals from Mux Cloud");
        }

        private static async Task<int> LoginAsync(string[] args, TextReader input, TextWriter output, TextWriter error, CancellationToken token)
        {
            string? email = null;
            string? server = null;
            string? name = null;
            bool passwordStdin = false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if ((a == "--email" || a == "-e") && i + 1 < args.Length) email = args[++i];
                else if (a == "--server" && i + 1 < args.Length) server = args[++i];
                else if (a == "--name" && i + 1 < args.Length) name = args[++i];
                else if (a == "--password-stdin") passwordStdin = true;
                else throw new ArgumentException("Unknown option '" + a + "'. Run mux cloud for usage.");
            }

            output.WriteLine(CloudAccountService.RemoteAccessNotice);
            output.WriteLine();
            if (string.IsNullOrWhiteSpace(email))
            {
                output.Write("Email: ");
                email = input.ReadLine();
            }

            string? password;
            if (passwordStdin || Console.IsInputRedirected || !ReferenceEquals(input, Console.In))
            {
                if (!passwordStdin) output.Write("Password: ");
                password = input.ReadLine();
            }
            else
            {
                output.Write("Password: ");
                password = ReadMasked();
                output.WriteLine();
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) throw new ArgumentException("An email and password are required.");
            CloudSettings saved = await CloudAccountService.LoginAsync(email, password, server, name, token).ConfigureAwait(false);
            output.WriteLine("Signed in as " + saved.AccountEmail + ". This machine is \"" + saved.MachineName + "\" (" + saved.MachineId + ").");

            AgentLauncher.EnsureRunning();
            CloudStatus status = await WaitForConnectionAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            WriteStatus(status, output);
            return 0;
        }

        private static int Status(TextWriter output)
        {
            CloudStatus status = CloudHubStatusClient.GetOrOfflineAsync(CancellationToken.None).GetAwaiter().GetResult();
            WriteStatus(status, output);
            return status.State == CloudStateEnum.Connected ? 0 : FailedExitCode;
        }

        private static int RemoteApproval(string[] args, TextWriter output, TextWriter error)
        {
            string value = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : string.Empty;
            if (value != "on" && value != "off")
            {
                error.WriteLine("Usage: mux cloud remote-approval <on|off>");
                return UsageExitCode;
            }

            MuxSettings settings = SettingsLoader.LoadSettings();
            settings.Cloud.AllowRemoteToolApproval = value == "on";
            SettingsLoader.SaveSettings(settings);
            output.WriteLine(value == "on"
                ? "Remote tool approval is on: users signed in to Mux Cloud can approve tools on this machine."
                : "Remote tool approval is off: runs started through Mux Cloud deny tools that would edit files or run commands.");
            return 0;
        }

        private static async Task<CloudStatus> WaitForConnectionAsync(TimeSpan timeout, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int unanswered = 0;
            while (true)
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                CloudStatus? status = await CloudHubStatusClient.GetAsync(settings, token).ConfigureAwait(false);
                if (status == null)
                {
                    // No hub yet: the agent may still be starting. Give up after a few tries; the tunnel connects
                    // whenever the agent (or mux serve) runs.
                    if (++unanswered >= 4 || DateTime.UtcNow >= deadline)
                    {
                        return CloudAccountService.OfflineStatus(settings.Cloud, "The mux agent is not running yet. The connection starts when it runs (mux agent start).");
                    }
                }
                else if (status.State == CloudStateEnum.Connected || status.State == CloudStateEnum.SignedOut || status.State == CloudStateEnum.Revoked || DateTime.UtcNow >= deadline)
                {
                    return status;
                }

                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }

        private static string ReadMasked()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) sb.Length--;
                    continue;
                }

                if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
            }

            return sb.ToString();
        }

        #endregion
    }
}
