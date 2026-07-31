namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
    /// Touchstone suite for interactive MCP-server management (the `/mcp` command) driven entirely
    /// through modals: list, add (form), edit (select a server -> form), and remove (with confirm). Each
    /// case isolates <c>mcp-servers.json</c> to a temp config directory via <c>MUX_CONFIG_DIR</c> so it
    /// never touches the real config and resolves the same on every thread (the modal form saves on a
    /// continuation).
    /// </summary>
    public static class McpManagementSuite
    {
        private const string SuiteId = "McpManagement";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the MCP-management suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for MCP-management cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Interactive MCP-server list, add, edit, and remove",
                new List<TestCaseDescriptor>
                {
                    Case("McpCommandOpensModal", "The /mcp command opens a selection modal", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Server("filesystem", "npx"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                            {
                                Feed(backend, app, "/mcp" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(app.IsModalActive, "mcp modal open");
                            }
                        })),

                    Case("AddMcpServerPersists", "The add form creates and persists a new stdio server", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                            {
                                Feed(backend, app, "/mcp" + "\r");   // no servers -> only "+ Add MCP server…"
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");            // choose Add -> opens the form
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // Fields: Name -> Transport (stdio default) -> Command -> …. Type the name,
                                // Tab past Transport to Command, fill it, then Enter to validate and save.
                                Feed(backend, app, "brandnew");
                                Feed(backend, app, "\t\t");
                                Feed(backend, app, "run-server");
                                Feed(backend, app, "\r");

                                await WaitUntilAsync(() => HasServer("brandnew"), ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(HasServer("brandnew"), "server persisted");
                            }
                        })),

                    Case("EditMcpServerUpdates", "Selecting a server opens the edit form and updates its command", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Server("solo", "old-cmd"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                            {
                                // Options: [solo, (blank), + Add, - Remove]. The server row (index 0) edits.
                                Feed(backend, app, "/mcp" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // pick "solo" -> opens the form pre-filled
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // Tab to the Command field (Name -> Transport -> Command), append text.
                                Feed(backend, app, "\t\t");
                                Feed(backend, app, "-new");
                                Feed(backend, app, "\r");

                                await WaitUntilAsync(() => ServerCommandContains("solo", "new"), ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(ServerCommandContains("solo", "new"), "server command updated");
                            }
                        })),

                    Case("RemoveMcpServerDeletes", "The remove flow deletes the chosen server after confirmation", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Server("solo", "cmd"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                            {
                                // Options: [solo, (blank), + Add, - Remove] -> Remove is index 3.
                                Feed(backend, app, "/mcp" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, Esc + "[B"); // Down -> (blank separator)
                                Feed(backend, app, Esc + "[B"); // Down -> + Add
                                Feed(backend, app, Esc + "[B"); // Down -> - Remove
                                Feed(backend, app, "\r");       // choose Remove
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // pick "solo" (index 0)
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // confirm "Remove" (index 0)

                                await WaitUntilAsync(() => !HasServer("solo"), ct).ConfigureAwait(false);
                                MuxAssert.IsFalse(HasServer("solo"), "server removed");
                            }
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task WithConfigDirAsync(Func<string, Task> body)
        {
            // Isolate the config directory via the AsyncLocal override rather than the process-global
            // MUX_CONFIG_DIR env var. The override flows with the async context (including the modal
            // resolution continuations spawned on the thread pool, via ExecutionContext), so a fire-and-forget
            // UI continuation that outlives this test still writes into this test's own temp directory instead
            // of racing another test's config through the shared env var.
            string dir = Path.Combine(Path.GetTempPath(), "mux-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            using (SettingsLoader.PushConfigDirectoryOverride(dir))
            {
                try
                {
                    await body(dir).ConfigureAwait(false);
                }
                finally
                {
                    TryDelete(dir);
                }
            }
        }

        private static JobManager NewManager()
        {
            return new JobManager(EchoRunner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(100, 30);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, null, "endpoint", "model");
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static McpServerConfig Server(string name, string command)
        {
            return new McpServerConfig { Name = name, Transport = McpTransportTypeEnum.Stdio, Command = command };
        }

        private static void Seed(params McpServerConfig[] servers)
        {
            SettingsLoader.SaveMcpServers(new List<McpServerConfig>(servers));
        }

        private static bool HasServer(string name)
        {
            foreach (McpServerConfig server in SettingsLoader.LoadMcpServers())
            {
                if (string.Equals(server.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ServerCommandContains(string name, string commandSubstring)
        {
            foreach (McpServerConfig server in SettingsLoader.LoadMcpServers())
            {
                if (string.Equals(server.Name, name, StringComparison.OrdinalIgnoreCase)
                    && server.Command.Contains(commandSubstring, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static Task WaitModal(MuxTuiApp app, CancellationToken ct)
        {
            return WaitUntilAsync(() => app.IsModalActive, ct);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!condition())
                {
                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
