namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Models;
    using Mux.Core.Enums;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// Touchstone suite for the TUI form and editor modals on TUIKit's 1.8/1.9 form support: pastes reach the
    /// focused field, validation errors are drawn under their fields with a live footer count, a form taller than
    /// the box scrolls to keep the focused field in view, and the Markdown viewer scrolls through the view's own
    /// keyboard handling.
    /// </summary>
    public static class FormModalsSuite
    {
        private const string SuiteId = "FormModals";

        /// <summary>
        /// Builds the form-modals suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the form and editor modal cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Form and editor modals",
                new List<TestCaseDescriptor>
                {
                    Case("OllamaCloudFormUsesFixedBearerEndpoint", "Editing a cloud endpoint with no URL preserves its provider and key reference", async (CancellationToken ct) =>
                    {
                        foreach (var provider in new[] { AdapterTypeEnum.OllamaCloud, AdapterTypeEnum.XAi })
                        {
                        EndpointConfig existing = new EndpointConfig
                        {
                            Name = "cloud", AdapterType = provider,
                            Model = "deepseek-v4.1-flash", ApiKey = "${OLLAMA_API_KEY}",
                            AuthPlacement = AuthPlacementEnum.Query
                        };
                        EndpointFormModal modal = new EndpointFormModal("Edit endpoint", existing);
                        string screen = Render(modal, 100, 60);
                        MuxAssert.Contains(provider.ToKebab(), screen, "discrete provider offered");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsTrue(modal.IsClosed, "fixed cloud URL needs no user input");
                        EndpointConfig? result = await modal.Completion.ConfigureAwait(false) as EndpointConfig;
                        MuxAssert.IsNotNull(result, "endpoint returned");
                        MuxAssert.AreEqual(provider, result!.AdapterType, "provider retained");
                        MuxAssert.AreEqual(provider == AdapterTypeEnum.XAi ? "https://api.x.ai/v1" : "https://ollama.com", result.BaseUrl, "native cloud root");
                        MuxAssert.AreEqual("${OLLAMA_API_KEY}", result.ApiKey, "key reference retained");
                        MuxAssert.AreEqual(AuthPlacementEnum.Bearer, result.AuthPlacement, "fixed bearer authentication");
                        MuxAssert.IsNull(result.AuthParameterName, "no query credential parameter");
                        }
                    }),
                    Case("EndpointFormPasteReachesFocusedField", "A paste into the endpoint form lands in the focused text field", (CancellationToken ct) =>
                    {
                        EndpointFormModal modal = new EndpointFormModal("Add endpoint");
                        Render(modal, 80, 40);

                        MuxAssert.IsTrue(modal.HandlePaste("my-endpoint\n"), "paste consumed");
                        string screen = Render(modal, 80, 40);
                        MuxAssert.Contains("my-endpoint", screen, "pasted name shown in the Name field");
                        return Task.CompletedTask;
                    }),

                    Case("EndpointFormErrorsDrawnUnderFields", "Submitting an invalid endpoint form shows each error under its field and a footer count", (CancellationToken ct) =>
                    {
                        EndpointFormModal modal = new EndpointFormModal("Add endpoint");
                        Render(modal, 80, 60);

                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        string screen = Render(modal, 80, 60);
                        MuxAssert.IsFalse(modal.IsClosed, "invalid form stays open");
                        MuxAssert.Contains("Name is required.", screen, "name error under its field");
                        MuxAssert.Contains("Model is required.", screen, "model error under its field");
                        MuxAssert.Contains("2 fields need fixing", screen, "footer counts the errors");
                        return Task.CompletedTask;
                    }),

                    Case("EndpointFormErrorClearsAsFieldIsFixed", "Fixing a field clears its error and the footer count follows", async (CancellationToken ct) =>
                    {
                        EndpointFormModal modal = new EndpointFormModal("Add endpoint");
                        Render(modal, 80, 60);
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));

                        // Submit focused the first invalid field (Name); type a value into it.
                        modal.HandlePaste("local");
                        string screen = Render(modal, 80, 60);
                        MuxAssert.IsFalse(screen.Contains("Name is required.", StringComparison.Ordinal), "name error cleared");
                        MuxAssert.Contains("1 field needs fixing", screen, "footer count drops");

                        // Fill the model field and submit: the form completes with the pasted values.
                        FocusFieldContaining(modal, "Model is required.");
                        modal.HandlePaste("llama3");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsTrue(modal.IsClosed, "valid form closes");
                        EndpointConfig? result = await modal.Completion.ConfigureAwait(false) as EndpointConfig;
                        MuxAssert.IsNotNull(result, "endpoint returned");
                        MuxAssert.AreEqual("local", result!.Name, "pasted name kept");
                        MuxAssert.AreEqual("llama3", result.Model, "pasted model kept");
                    }),

                    Case("SettingsFormScrollsToFocusedField", "A settings form taller than the box scrolls to keep the focused field visible", (CancellationToken ct) =>
                    {
                        SettingsFormModal modal = new SettingsFormModal("Settings", new MuxSettings());
                        string top = Render(modal, 80, 24);
                        MuxAssert.Contains("Max agent iterations", top, "first field visible at the top");
                        MuxAssert.Contains("Agent", top, "first section heading shown");

                        // Shift+Tab from the first field wraps to the last one.
                        modal.HandleKey(KeyEvent.Special(KeyCode.Tab, KeyModifiers.Shift));

                        string bottom = Render(modal, 80, 24);
                        MuxAssert.Contains("Summary cache retention", bottom, "last field scrolled into view");
                        MuxAssert.IsFalse(bottom.Contains("Max agent iterations", StringComparison.Ordinal), "first field scrolled out");
                        return Task.CompletedTask;
                    }),

                    Case("McpFormPasteIntoMaskedTokenRevealable", "A token pasted into the masked MCP field is hidden until Ctrl+R reveals it", async (CancellationToken ct) =>
                    {
                        McpServerFormModal modal = new McpServerFormModal("Add MCP server", new McpServerConfig
                        {
                            Name = "remote",
                            Transport = Mux.Core.Enums.McpTransportTypeEnum.Http,
                            Url = "http://localhost:9000",
                            Auth = new McpAuthConfig { Type = Mux.Core.Enums.McpAuthTypeEnum.Bearer }
                        });
                        Render(modal, 80, 40);

                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        string screen = Render(modal, 80, 40);
                        MuxAssert.Contains("Bearer token is required.", screen, "token error shown under the field");

                        modal.HandlePaste("s3cret-token");
                        screen = Render(modal, 80, 40);
                        MuxAssert.IsFalse(screen.Contains("s3cret-token", StringComparison.Ordinal), "token masked");

                        modal.HandleKey(KeyEvent.Char('r', KeyModifiers.Ctrl));
                        screen = Render(modal, 80, 40);
                        MuxAssert.Contains("s3cret-token", screen, "Ctrl+R reveals the token");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        McpServerConfig? result = await modal.Completion.ConfigureAwait(false) as McpServerConfig;
                        MuxAssert.IsNotNull(result, "server returned");
                        MuxAssert.AreEqual("s3cret-token", result!.Auth!.BearerToken, "pasted token saved");
                    }),

                    Case("MarkdownBoxScrollsWithViewKeys", "The Markdown viewer scrolls with the view's own keys and closes on Escape", (CancellationToken ct) =>
                    {
                        string markdown = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "Paragraph " + i));
                        MarkdownBoxModal modal = new MarkdownBoxModal("doc", markdown);
                        Render(modal, 80, 24);
                        MuxAssert.AreEqual(0, modal.ScrollOffset, "starts at the top");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Down));
                        MuxAssert.AreEqual(1, modal.ScrollOffset, "Down moves a row");

                        modal.HandleKey(KeyEvent.Special(KeyCode.End));
                        string screen = Render(modal, 80, 24);
                        MuxAssert.Contains("Paragraph 80", screen, "End shows the last paragraph");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Home));
                        MuxAssert.AreEqual(0, modal.ScrollOffset, "Home returns to the top");

                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));
                        MuxAssert.IsTrue(modal.IsClosed, "Escape closes");
                        return Task.CompletedTask;
                    }),
                });
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static string Render(Modal modal, int width, int height)
        {
            CellBuffer buffer = new CellBuffer(width, height);
            modal.Render(new BufferSurface(buffer));

            List<string> rows = new List<string>();
            for (int y = 0; y < height; y++)
            {
                char[] row = new char[width];
                for (int x = 0; x < width; x++)
                {
                    string grapheme = buffer.Get(x, y).Grapheme ?? " ";
                    row[x] = grapheme.Length > 0 ? grapheme[0] : ' ';
                }

                rows.Add(new string(row));
            }

            return string.Join("\n", rows);
        }

        private static void FocusFieldContaining(Modal modal, string text)
        {
            // Tab forward until the rendered row marked as focused ("> ") is followed by the given error.
            for (int i = 0; i < 40; i++)
            {
                string[] rows = Render(modal, 80, 60).Split('\n');
                int focused = Array.FindIndex(rows, r => r.Contains("> ", StringComparison.Ordinal));
                if (focused >= 0 && rows.Skip(focused).Take(4).Any(r => r.Contains(text, StringComparison.Ordinal)))
                {
                    return;
                }

                modal.HandleKey(KeyEvent.Special(KeyCode.Tab));
            }

            throw new InvalidOperationException("Could not focus the field showing: " + text);
        }
    }
}
