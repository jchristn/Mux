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
    /// Touchstone suite for restored interactive endpoint/model management (the `/endpoint` and `/model`
    /// commands) driven entirely through modals: list + switch, add (wizard), and remove (with confirm).
    /// Each case isolates <c>endpoints.json</c> to a temp config directory via <c>MUX_CONFIG_DIR</c> so it
    /// never touches the real config and resolves the same on every thread (the modal wizard saves on a
    /// continuation).
    /// </summary>
    public static class EndpointManagementSuite
    {
        private const string SuiteId = "EndpointManagement";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the endpoint-management suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for endpoint-management cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Interactive endpoint/model list, switch, add, and remove",
                new List<TestCaseDescriptor>
                {
                    Case("EndpointCommandOpensModal", "The /endpoint command opens a selection modal", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Endpoint("alpha", AdapterTypeEnum.OpenAiCompatible, "m-a"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, "alpha", null))
                            {
                                Feed(backend, app, "/endpoint" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(app.IsModalActive, "endpoint modal open");
                            }
                        })),

                    Case("ModelAliasSwitchesEndpoint", "Selecting an endpoint switches the active one and fires the callback", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(
                                Endpoint("alpha", AdapterTypeEnum.OpenAiCompatible, "m-a"),
                                Endpoint("beta", AdapterTypeEnum.Ollama, "m-b"));

                            EndpointConfig? switched = null;
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, "alpha", e => switched = e))
                            {
                                MuxAssert.AreEqual("alpha", app.ActiveEndpointName, "starts on alpha");

                                Feed(backend, app, "/model" + "\r"); // /model is an alias for /endpoint
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, Esc + "[B"); // Down -> beta (index 1)
                                Feed(backend, app, "\r");       // Enter -> switch

                                await WaitUntilAsync(() => switched != null, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual("beta", switched!.Name, "callback got beta");
                                MuxAssert.AreEqual("beta", app.ActiveEndpointName, "active is beta");
                                // The callback is wired to `template.Endpoint = endpoint`, so the full
                                // endpoint (adapter + model), not just the display name, must propagate to
                                // the runtime that builds each turn's LlmClient.
                                MuxAssert.AreEqual(AdapterTypeEnum.Ollama, switched!.AdapterType, "runtime adapter switched to beta's");
                                MuxAssert.AreEqual("m-b", switched!.Model, "runtime model switched to beta's");
                            }
                        })),

                    Case("AddEndpointFormPersists", "The add form creates and persists a new endpoint", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, string.Empty, null))
                            {
                                Feed(backend, app, "/endpoint" + "\r");   // no endpoints -> only "+ Add endpoint…"
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");                 // choose Add -> opens the single form
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // One form: type the name, Tab past Adapter (default) and Base URL (default)
                                // to the Model field, fill it, then Enter to validate and save.
                                Feed(backend, app, "brandnew");
                                Feed(backend, app, "\t\t\t");
                                Feed(backend, app, "the-model");
                                Feed(backend, app, "\r");

                                await WaitUntilAsync(() => HasEndpoint("brandnew"), ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(HasEndpoint("brandnew"), "endpoint persisted");
                            }
                        })),

                    Case("EditEndpointFormUpdates", "The edit form updates an existing endpoint's model", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Endpoint("solo", AdapterTypeEnum.OpenAiCompatible, "old-model"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, "solo", null))
                            {
                                // Options: [solo, (blank), + Add, ✎ Edit, - Remove, ⬇ Import] -> Edit is index 3.
                                Feed(backend, app, "/endpoint" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, Esc + "[B"); // Down -> (blank separator)
                                Feed(backend, app, Esc + "[B"); // Down -> + Add
                                Feed(backend, app, Esc + "[B"); // Down -> ✎ Edit
                                Feed(backend, app, "\r");       // choose Edit
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // pick "solo" (index 0) -> opens the form pre-filled
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // Tab to the Model field (Name -> Adapter -> Base URL -> Model), replace it.
                                Feed(backend, app, "\t\t\t");
                                Feed(backend, app, "new-model");
                                Feed(backend, app, "\r");

                                await WaitUntilAsync(() => EndpointModelContains("solo", "new-model"), ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(EndpointModelContains("solo", "new-model"), "endpoint model updated");
                            }
                        })),

                    Case("RemoveEndpointDeletes", "The remove flow deletes the chosen endpoint after confirmation", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            Seed(Endpoint("solo", AdapterTypeEnum.OpenAiCompatible, "m"));
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, "solo", null))
                            {
                                // Options: [solo, (blank), + Add, ✎ Edit, - Remove, ⬇ Import] -> Remove is index 4.
                                Feed(backend, app, "/endpoint" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, Esc + "[B"); // Down -> (blank separator)
                                Feed(backend, app, Esc + "[B"); // Down -> + Add
                                Feed(backend, app, Esc + "[B"); // Down -> ✎ Edit
                                Feed(backend, app, Esc + "[B"); // Down -> - Remove
                                Feed(backend, app, "\r");       // choose Remove
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // pick "solo" (index 0)
                                await WaitModal(app, ct).ConfigureAwait(false);
                                Feed(backend, app, "\r");       // confirm "Remove" (index 0)

                                await WaitUntilAsync(() => !HasEndpoint("solo"), ct).ConfigureAwait(false);
                                MuxAssert.IsFalse(HasEndpoint("solo"), "endpoint removed");
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
            // MUX_CONFIG_DIR env var, so a fire-and-forget modal resolution continuation that outlives this
            // test still writes into this test's own temp directory (the override flows to thread-pool
            // continuations via ExecutionContext) instead of racing another test through the shared env var.
            string dir = Path.Combine(Path.GetTempPath(), "mux-endpoints-" + Guid.NewGuid().ToString("N"));
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

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager, string endpointName, Action<EndpointConfig>? onSwitch)
        {
            backend = new HeadlessBackend(100, 30);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, null, endpointName, "model", onSwitch);
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static EndpointConfig Endpoint(string name, AdapterTypeEnum adapter, string model)
        {
            return new EndpointConfig { Name = name, AdapterType = adapter, BaseUrl = "http://localhost:11434/v1", Model = model };
        }

        private static void Seed(params EndpointConfig[] endpoints)
        {
            SettingsLoader.SaveEndpoints(new List<EndpointConfig>(endpoints));
        }

        private static bool HasEndpoint(string name)
        {
            foreach (EndpointConfig endpoint in SettingsLoader.LoadEndpoints())
            {
                if (string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EndpointModelContains(string name, string modelSubstring)
        {
            foreach (EndpointConfig endpoint in SettingsLoader.LoadEndpoints())
            {
                if (string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase)
                    && endpoint.Model.Contains(modelSubstring, StringComparison.Ordinal))
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
