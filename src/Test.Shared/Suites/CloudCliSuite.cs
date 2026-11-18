namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// <c>mux cloud</c>: usage, sign-in from piped credentials against a fake Mux Cloud, the remote approval switch,
    /// status output in the shared format, and sign-out.
    /// </summary>
    public static class CloudCliSuite
    {
        private const string Suite = "CloudCli";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "mux cloud command", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "UsageWithoutVerb", "mux cloud without a verb prints usage and exits 2", (CancellationToken ct) =>
                {
                    StringWriter output = new StringWriter();
                    StringWriter error = new StringWriter();
                    int code = CloudCommand.Run(Array.Empty<string>(), new StringReader(string.Empty), output, error);
                    MuxAssert.AreEqual(CloudCommand.UsageExitCode, code, "exit code");
                    MuxAssert.Contains("login", error.ToString(), "usage lists login");
                    MuxAssert.Contains("remote-approval", error.ToString(), "usage lists remote-approval");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "LoginRemoteApprovalLogout", "Piped sign-in saves the credential without the password, remote-approval toggles, and logout clears it", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer();
                    string dir = Path.Combine(Path.GetTempPath(), "mux-cloudcli-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                    string? autostart = Environment.GetEnvironmentVariable("MUX_AGENT_AUTOSTART");
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                    Environment.SetEnvironmentVariable("MUX_AGENT_AUTOSTART", "0");
                    try
                    {
                        MuxSettings seed = SettingsLoader.LoadSettings();
                        seed.Rest.Port = 1;
                        SettingsLoader.SaveSettings(seed);

                        StringWriter output = new StringWriter();
                        StringWriter error = new StringWriter();
                        int code = await Task.Run(() => CloudCommand.Run(new[] { "login", "--email", "dev@example.test", "--password-stdin", "--server", cloud.Url, "--name", "cli-box" }, new StringReader("secret-pw\n"), output, error), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, code, "login exit code: " + error);
                        MuxAssert.Contains("approving tools", output.ToString(), "shows the remote-access notice first");
                        MuxAssert.Contains("No account yet? Create one at " + cloud.Url.TrimEnd('/') + "/signup", output.ToString(), "shows where to create an account on the chosen server");
                        MuxAssert.Contains("Signed in as dev@example.test", output.ToString(), "confirms the account");
                        MuxAssert.Contains("Mux Cloud : ", output.ToString(), "prints the status block");
                        CloudSettings saved = SettingsLoader.LoadSettings().Cloud;
                        MuxAssert.IsTrue(saved.Enabled, "enabled");
                        MuxAssert.AreEqual(cloud.MachineId, saved.MachineId, "machine id");
                        MuxAssert.AreEqual(cloud.Url, saved.ServerUrl, "server");
                        MuxAssert.DoesNotContain("secret-pw", File.ReadAllText(Path.Combine(dir, "settings.json")), "password never stored");
                        MuxAssert.Contains("cli-box", cloud.LastEnrollBody, "machine name sent");

                        MuxAssert.AreEqual(0, CloudCommand.Run(new[] { "remote-approval", "off" }, new StringReader(string.Empty), new StringWriter(), new StringWriter()), "remote-approval off");
                        MuxAssert.IsFalse(SettingsLoader.LoadSettings().Cloud.AllowRemoteToolApproval, "switched off");
                        MuxAssert.AreEqual(CloudCommand.UsageExitCode, CloudCommand.Run(new[] { "remote-approval", "maybe" }, new StringReader(string.Empty), new StringWriter(), new StringWriter()), "bad value");

                        StringWriter status = new StringWriter();
                        int statusCode = CloudCommand.Run(new[] { "status" }, new StringReader(string.Empty), status, new StringWriter());
                        MuxAssert.AreEqual(CloudCommand.FailedExitCode, statusCode, "not connected without a running hub");
                        MuxAssert.Contains("remote approval : off", status.ToString(), "status shows the switch");

                        StringWriter logoutOut = new StringWriter();
                        MuxAssert.AreEqual(0, CloudCommand.Run(new[] { "logout" }, new StringReader(string.Empty), logoutOut, new StringWriter()), "logout");
                        MuxAssert.AreEqual(1, cloud.ValidSignOuts, "the cloud was told, with a valid sign-out proof");
                        MuxAssert.Contains("Signed this machine out of Mux Cloud.", logoutOut.ToString(), "confirmed message");
                        CloudSettings after = SettingsLoader.LoadSettings().Cloud;
                        MuxAssert.IsFalse(after.IsEnrolled, "credential removed");
                        MuxAssert.IsFalse(after.Enabled, "connection off");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                        Environment.SetEnvironmentVariable("MUX_AGENT_AUTOSTART", autostart);
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }
                }),
                new TestCaseDescriptor(Suite, "LogoutWhenCloudUnreachable", "Logout still deletes the credential when Mux Cloud cannot be reached, and says the machine stays listed", async (CancellationToken ct) =>
                {
                    string dir = Path.Combine(Path.GetTempPath(), "mux-cloudcli-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                    string? autostart = Environment.GetEnvironmentVariable("MUX_AGENT_AUTOSTART");
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                    Environment.SetEnvironmentVariable("MUX_AGENT_AUTOSTART", "0");
                    try
                    {
                        using (FakeCloudServer cloud = new FakeCloudServer())
                        {
                            MuxSettings seed = SettingsLoader.LoadSettings();
                            seed.Rest.Port = 1;
                            SettingsLoader.SaveSettings(seed);
                            int code = await Task.Run(() => CloudCommand.Run(new[] { "login", "--email", "dev@example.test", "--password-stdin", "--server", cloud.Url }, new StringReader("secret-pw\n"), new StringWriter(), new StringWriter()), ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(0, code, "login");
                        }

                        StringWriter output = new StringWriter();
                        MuxAssert.AreEqual(0, await Task.Run(() => CloudCommand.Run(new[] { "logout" }, new StringReader(string.Empty), output, new StringWriter()), ct).ConfigureAwait(false), "logout");
                        MuxAssert.Contains("could not be reached", output.ToString(), "explains the machine stays listed");
                        MuxAssert.IsFalse(SettingsLoader.LoadSettings().Cloud.IsEnrolled, "credential removed anyway");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                        Environment.SetEnvironmentVariable("MUX_AGENT_AUTOSTART", autostart);
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }
                }),
                new TestCaseDescriptor(Suite, "LoginFailureExplained", "A rejected sign-in exits 1 with the cloud's explanation and saves nothing", async (CancellationToken ct) =>
                {
                    using FakeCloudServer cloud = new FakeCloudServer { EnrollStatus = 401 };
                    string dir = Path.Combine(Path.GetTempPath(), "mux-cloudcli-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                    try
                    {
                        StringWriter error = new StringWriter();
                        int code = await Task.Run(() => CloudCommand.Run(new[] { "login", "--email", "dev@example.test", "--password-stdin", "--server", cloud.Url }, new StringReader("bad\n"), new StringWriter(), error), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(CloudCommand.FailedExitCode, code, "exit code");
                        MuxAssert.Contains("not correct", error.ToString(), "explains");
                        MuxAssert.Contains("No account yet? Create one at " + cloud.Url.TrimEnd('/') + "/signup", error.ToString(), "a rejected password points at signup on the same server");
                        MuxAssert.IsFalse(SettingsLoader.LoadSettings().Cloud.IsEnrolled, "nothing saved");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }
                }),
                new TestCaseDescriptor(Suite, "StatusFormat", "The shared status block names the state, server, machine, account, and remote approval", (CancellationToken ct) =>
                {
                    CloudStatus status = new CloudStatus { State = CloudStateEnum.Connected, ServerUrl = "https://app.usemux.ai", MachineId = "mch_1", MachineName = "box", AccountEmail = "a@b.c", AllowRemoteToolApproval = true, LatencyMs = 42 };
                    StringWriter output = new StringWriter();
                    CloudCommand.WriteStatus(status, output);
                    string text = output.ToString();
                    MuxAssert.Contains("Mux Cloud : Connected", text, "state");
                    MuxAssert.DoesNotContain("Connected to", text, "connected status lists fields instead of repeating them");
                    MuxAssert.Contains("account         : a@b.c", text, "account");
                    MuxAssert.DoesNotContain("Sign in with", text, "no sign-in hint when connected");
                    MuxAssert.Contains("machine         : box (mch_1)", text, "machine");
                    MuxAssert.Contains("remote approval : on", text, "remote approval");
                    StringWriter signedOut = new StringWriter();
                    CloudCommand.WriteStatus(new CloudStatus { State = CloudStateEnum.Disabled, Reason = "This machine is not signed in to Mux Cloud.", ServerUrl = "https://app.usemux.ai", MachineName = "box" }, signedOut);
                    MuxAssert.DoesNotContain("remote approval", signedOut.ToString(), "no remote approval line before signing in");
                    MuxAssert.Contains("Sign in with: mux cloud login", signedOut.ToString(), "next step");
                    MuxAssert.Contains("No account yet? Create one at https://app.usemux.ai/signup", signedOut.ToString(), "where to create an account");
                    MuxAssert.DoesNotContain("No account yet", text, "no signup hint once connected");
                    MuxAssert.AreEqual("https://cloud.example.test/signup", CloudSignup.Url("https://cloud.example.test/"), "signup URL follows the server and drops a trailing slash");
                    MuxAssert.AreEqual("https://app.usemux.ai/signup", CloudSignup.Url("  "), "blank server uses the default");
                    MuxAssert.AreEqual("app.usemux.ai/signup", CloudSignup.DisplayUrl(null), "display form drops the scheme");
                    MuxAssert.AreEqual(1, signedOut.ToString().Split("not signed in").Length - 1, "reason shown once");
                    MuxAssert.Contains("latency         : 42 ms", text, "latency");
                    return Task.CompletedTask;
                })
            });
        }
    }
}
