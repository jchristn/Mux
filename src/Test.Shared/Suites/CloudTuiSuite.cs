namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// The terminal UI's Mux Cloud controls: <c>/cloud</c> status with the shared state names and a next step, usage for
    /// unknown verbs, the remote approval switch, the sign-in form (masked password, remote-access notice), and the
    /// Mux Cloud section of the settings form. Set <c>MUX_UX_DUMP</c> to a folder to save the rendered frames.
    /// </summary>
    public static class CloudTuiSuite
    {
        private const string Suite = "CloudTui";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Terminal UI Mux Cloud controls", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "StatusUsageAndSwitch", "/cloud shows the state and next step, /cloud bogus shows usage, and remote-approval toggles the setting", async (CancellationToken ct) =>
                {
                    await WithConfigAsync(async () =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 40);
                        await using (JobManager manager = new JobManager(Echo, maxConcurrency: 1))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            app.Start();
                            MuxAssert.IsTrue(app.Catalog.Commands.Any(c => c.MatchesSlash("cloud")), "/cloud is in the command catalog");
                            backend.FeedInput("/cloud\r");
                            app.PumpInputOnce();
                            await WaitForAsync(() => Join(app.TranscriptSnapshot()).Contains("Sign in with: /cloud login", StringComparison.Ordinal), ct).ConfigureAwait(false);
                            string status = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Mux Cloud : Disabled", status, "shared state name");
                            MuxAssert.Contains("not signed in to Mux Cloud", status, "reason");
                            MuxAssert.DoesNotContain("remote approval", status, "no remote approval line before signing in");
                            MuxAssert.DoesNotContain("mux cloud login", status, "the terminal UI names its own command");
                            MuxAssert.AreEqual(1, status.Split("Sign in with").Length - 1, "one sign-in hint");
                            MuxAssert.Contains("No account yet? Create one at https://app.usemux.ai/signup", status, "where to create an account");
                            Dump(backend, app, "tui-cloud-status");

                            backend.FeedInput("/cloud bogus\r");
                            app.PumpInputOnce();
                            MuxAssert.Contains("Usage: /cloud [status | login | logout | remote-approval on|off]", Join(app.TranscriptSnapshot()), "usage");

                            backend.FeedInput("/cloud remote-approval off\r");
                            app.PumpInputOnce();
                            MuxAssert.IsFalse(SettingsLoader.LoadSettings().Cloud.AllowRemoteToolApproval, "switched off");
                            MuxAssert.Contains("Remote tool approval is off", Join(app.TranscriptSnapshot()), "confirms");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "local commands make no model call");
                        }
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "LoginFormRenders", "/cloud login opens a form with the remote-access notice and a masked password", async (CancellationToken ct) =>
                {
                    await WithConfigAsync(async () =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 40);
                        await using (JobManager manager = new JobManager(Echo, maxConcurrency: 1))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            app.Start();
                            backend.FeedInput("/cloud login\r");
                            app.PumpInputOnce();
                            backend.FeedInput("dev@example.test\tsupersecret");
                            app.PumpInputOnce();
                            app.RenderOnce();
                            string frame = Screen(backend.TakeOutput(), 120, 40);
                            MuxAssert.Contains("Mux Cloud", frame, "title");
                            MuxAssert.Contains("Anyone signed in to this Mux Cloud", frame, "notice");
                            MuxAssert.Contains("dev@example.test", frame, "email echoes");
                            MuxAssert.DoesNotContain("supersecret", frame, "password is masked");
                            MuxAssert.Contains("Enter sign in", frame, "hint");
                            MuxAssert.Contains("No account yet? Create one at https://app.usemux.ai/signup", frame, "where to create an account");
                            MuxAssert.Contains("remote-approval off).", frame, "the notice ends with the terminal command");
                            MuxAssert.Contains("(/cloud", frame, "the notice names the terminal command");
                            Save("tui-cloud-login", frame);
                        }
                    }).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "SettingsSection", "The settings form has a Mux Cloud section with the server, machine name, and remote approval", (CancellationToken ct) =>
                {
                    MuxSettings settings = new MuxSettings();
                    SettingsFormModal modal = new SettingsFormModal("Settings", settings);
                    TUIKit.CellBuffer buffer = new TUIKit.CellBuffer(100, 200);
                    modal.Render(new TUIKit.BufferSurface(buffer));
                    string frame = Plain(buffer);
                    MuxAssert.Contains("Mux Cloud (sign in with /cloud login)", frame, "section");
                    MuxAssert.Contains("Server URL", frame, "server field");
                    MuxAssert.Contains("Remote users may approve tools", frame, "remote approval");
                    Save("tui-settings", frame);
                    return Task.CompletedTask;
                })
            });
        }

        private static async IAsyncEnumerable<AgentEvent> Echo(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
        }

        private static async Task WithConfigAsync(Func<Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-cloudtui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            try
            {
                MuxSettings seed = new MuxSettings();
                seed.Rest.Port = 1;
                SettingsLoader.SaveSettings(seed);
                await body().ConfigureAwait(false);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !condition())
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        private static string Plain(TUIKit.CellBuffer buffer)
        {
            List<string> rows = new List<string>();
            for (int y = 0; y < buffer.Height; y++)
            {
                System.Text.StringBuilder line = new System.Text.StringBuilder();
                for (int x = 0; x < buffer.Width; x++)
                {
                    TUIKit.Cell cell = buffer.Get(x, y);
                    if (!cell.IsContinuation) line.Append(string.IsNullOrEmpty(cell.Grapheme) ? " " : cell.Grapheme);
                }

                rows.Add(line.ToString().TrimEnd());
            }

            return string.Join("\n", rows).TrimEnd();
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static void Dump(HeadlessBackend backend, MuxTuiApp app, string name)
        {
            app.RenderOnce();
            Save(name, Screen(backend.TakeOutput(), 120, 40));
        }

        private static void Save(string name, string frame)
        {
            string? dir = Environment.GetEnvironmentVariable("MUX_UX_DUMP");
            if (string.IsNullOrWhiteSpace(dir)) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".txt"), frame);
        }

        // Replays cursor-positioning output onto a character grid (enough of VT100 for TUIKit's frames).
        private static string Screen(string ansi, int width, int height)
        {
            char[][] grid = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(' ', width).ToArray()).ToArray();
            int row = 0, col = 0;
            for (int i = 0; i < ansi.Length; i++)
            {
                char c = ansi[i];
                if (c == '\u001b' && i + 1 < ansi.Length && ansi[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < ansi.Length && !(ansi[j] >= '@' && ansi[j] <= '~')) j++;
                    if (j >= ansi.Length) break;
                    string args = ansi.Substring(i + 2, j - i - 2);
                    if (ansi[j] == 'H')
                    {
                        string[] parts = args.Split(';');
                        row = parts.Length > 0 && int.TryParse(parts[0], out int r) ? r - 1 : 0;
                        col = parts.Length > 1 && int.TryParse(parts[1], out int k) ? k - 1 : 0;
                    }

                    i = j;
                    continue;
                }

                if (c == '\u001b' || c < ' ') continue;
                if (row >= 0 && row < height && col >= 0 && col < width) grid[row][col] = c;
                col++;
            }

            return string.Join("\n", grid.Select(g => new string(g).TrimEnd()));
        }
    }
}
