namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Theming;
    using Mux.Core.Utility;
    using Mux.Server;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the shared color palettes and the full-adapter endpoint editors: the built-in catalog
    /// and its contrast floors, custom palette validation, inheritance, and persistence, the TUI palette and
    /// themed caret, the REST palette and model-import routes, the TUI endpoint form across all eleven adapters,
    /// and model import.
    /// </summary>
    public static class PalettesSuite
    {
        private const string SuiteId = "Palettes";

        /// <summary>
        /// Builds the palettes suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the palette and endpoint-editor cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Color palettes and full-adapter endpoint editors",
                new List<TestCaseDescriptor>
                {
                    Case("CatalogHasADozenDistinctPalettes", "At least twelve built-in palettes with unique ids and every role set", (CancellationToken ct) =>
                    {
                        IReadOnlyList<ColorPalette> builtIn = ColorPaletteCatalog.BuiltIn;
                        MuxAssert.IsTrue(builtIn.Count >= 12, "a dozen or more palettes");
                        MuxAssert.AreEqual(builtIn.Count, builtIn.Select(p => p.Id).Distinct().Count(), "unique ids");
                        MuxAssert.AreEqual(ColorPaletteCatalog.DefaultId, builtIn[0].Id, "mux first");
                        MuxAssert.IsTrue(builtIn.Any(p => !p.Dark) && builtIn.Count(p => !p.Dark) >= 3, "several light palettes");
                        foreach (ColorPalette palette in builtIn)
                        {
                            MuxAssert.IsTrue(palette.BuiltIn, palette.Id + " flagged built-in");
                            foreach (string role in ColorPaletteColors.RoleNames)
                            {
                                MuxAssert.IsTrue(ColorPaletteCatalog.TryNormalizeHex(palette.Colors.Get(role), out _), palette.Id + "." + role + " is hex");
                            }
                        }

                        return Task.CompletedTask;
                    }),

                    Case("BuiltInsMeetContrastFloors", "Every built-in palette meets its text, accent, caret, and status contrast floors", (CancellationToken ct) =>
                    {
                        foreach (ColorPalette p in ColorPaletteCatalog.BuiltIn)
                        {
                            ColorPaletteColors c = p.Colors;
                            AtLeast(p.Id, "text/background", c.Text!, c.Background!, 7.0);
                            AtLeast(p.Id, "text/surfaceAlt", c.Text!, c.SurfaceAlt!, 7.0);
                            AtLeast(p.Id, "muted/background", c.Muted!, c.Background!, 4.5);
                            AtLeast(p.Id, "muted/surface", c.Muted!, c.Surface!, 4.5);
                            AtLeast(p.Id, "accent/background", c.Accent!, c.Background!, 4.5);
                            AtLeast(p.Id, "accentText/accent", c.AccentText!, c.Accent!, 4.5);
                            AtLeast(p.Id, "text/selection", c.Text!, c.Selection!, 4.5);
                            AtLeast(p.Id, "cursor/surfaceAlt", c.Cursor!, c.SurfaceAlt!, 3.0);
                            AtLeast(p.Id, "error/surface", c.Error!, c.Surface!, 3.0);
                            AtLeast(p.Id, "warning/surface", c.Warning!, c.Surface!, 3.0);
                            AtLeast(p.Id, "success/surface", c.Success!, c.Surface!, 3.0);
                            AtLeast(p.Id, "info/surface", c.Info!, c.Surface!, 3.0);
                        }

                        return Task.CompletedTask;
                    }),

                    Case("HexAndMixHelpers", "Hex normalization, mixing, and contrast math", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(ColorPaletteCatalog.TryNormalizeHex("ABC", out string shortHex) && shortHex == "#aabbcc", "short hex expands");
                        MuxAssert.IsTrue(ColorPaletteCatalog.TryNormalizeHex("#1E293B", out string full) && full == "#1e293b", "lowercased");
                        MuxAssert.IsFalse(ColorPaletteCatalog.TryNormalizeHex("red", out _), "names rejected");
                        MuxAssert.IsFalse(ColorPaletteCatalog.TryNormalizeHex("#12345", out _), "five digits rejected");
                        MuxAssert.AreEqual("#808080", ColorPaletteCatalog.Mix("#000000", "#ffffff", 0.5), "midpoint");
                        MuxAssert.AreEqual("#000000", ColorPaletteCatalog.Mix("#000000", "#ffffff", -3), "clamped");
                        MuxAssert.IsTrue(Math.Abs(ColorPaletteCatalog.ContrastRatio("#000000", "#ffffff") - 21.0) < 0.01, "black on white is 21:1");
                        return Task.CompletedTask;
                    }),

                    Case("ValidateRejectsBadCustomPalettes", "Custom palettes need a kebab id that is not built-in, a name, hex colors, and a real base", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(ColorPaletteCatalog.Validate(Custom("team-dark", "#101010"), null), "a good palette passes");
                        MuxAssert.Contains("kebab-case", ColorPaletteCatalog.Validate(Custom("Team Dark", null), null), "spaces rejected");
                        MuxAssert.Contains("built-in", ColorPaletteCatalog.Validate(Custom("midnight", null), null), "built-in id rejected");
                        MuxAssert.Contains("built-in", ColorPaletteCatalog.Validate(Custom("system", null), null), "system reserved");
                        MuxAssert.Contains("hex color", ColorPaletteCatalog.Validate(Custom("team", "blue"), null), "named color rejected");
                        ColorPalette noName = Custom("team", null);
                        noName.Name = " ";
                        MuxAssert.Contains("name is required", ColorPaletteCatalog.Validate(noName, null), "name required");
                        ColorPalette badBase = Custom("team", null);
                        badBase.Base = "nope";
                        MuxAssert.Contains("does not exist", ColorPaletteCatalog.Validate(badBase, null), "unknown base");
                        ColorPalette selfBase = Custom("team", null);
                        selfBase.Base = "team";
                        MuxAssert.Contains("itself", ColorPaletteCatalog.Validate(selfBase, null), "self base");
                        ColorPalette customBase = Custom("team-2", null);
                        customBase.Base = "team";
                        MuxAssert.IsNull(ColorPaletteCatalog.Validate(customBase, new[] { Custom("team", null) }), "a custom base is allowed");
                        return Task.CompletedTask;
                    }),

                    Case("CustomPalettesInheritFromTheirBase", "Blank roles come from the base, chains resolve, cycles and duplicates are skipped", (CancellationToken ct) =>
                    {
                        ColorPalette first = Custom("team", "#123456");
                        first.Base = "ocean";
                        ColorPalette second = Custom("team-alt", null);
                        second.Base = "team";
                        second.Colors.Cursor = "#ff00ff";
                        ColorPalette cycleA = Custom("loop-a", null);
                        cycleA.Base = "loop-b";
                        ColorPalette cycleB = Custom("loop-b", null);
                        cycleB.Base = "loop-a";
                        ColorPalette duplicate = Custom("team", "#000000");

                        List<ColorPalette> all = ColorPaletteCatalog.GetAll(new[] { first, second, cycleA, cycleB, duplicate });
                        ColorPalette ocean = ColorPaletteCatalog.BuiltIn.First(p => p.Id == "ocean");
                        ColorPalette team = all.Single(p => p.Id == "team");
                        MuxAssert.IsFalse(team.BuiltIn, "custom flagged");
                        MuxAssert.AreEqual("#123456", team.Colors.Background, "own color kept");
                        MuxAssert.AreEqual(ocean.Colors.Accent, team.Colors.Accent, "inherited from ocean");
                        ColorPalette alt = all.Single(p => p.Id == "team-alt");
                        MuxAssert.AreEqual("#123456", alt.Colors.Background, "inherited through the custom base");
                        MuxAssert.AreEqual("#ff00ff", alt.Colors.Cursor, "own cursor");
                        MuxAssert.IsFalse(all.Any(p => p.Id.StartsWith("loop-", StringComparison.Ordinal)), "cycles skipped");
                        MuxAssert.AreEqual(ColorPaletteCatalog.BuiltIn.Count + 2, all.Count, "duplicate id ignored");
                        MuxAssert.AreEqual(ColorPaletteCatalog.DefaultId, ColorPaletteCatalog.Resolve("nope", all).Id, "unknown falls back to the default");
                        MuxAssert.AreEqual(ColorPaletteCatalog.LightId, ColorPaletteCatalog.Resolve("system", null, systemPrefersDark: false).Id, "system follows the OS");
                        return Task.CompletedTask;
                    }),

                    Case("LegacyThemesMapToPalettes", "Old TUI and desktop theme names map onto palettes", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("mux", ColorPaletteCatalog.FromLegacyTheme("mux", fromTui: true), "tui mux");
                        MuxAssert.AreEqual("graphite", ColorPaletteCatalog.FromLegacyTheme("dark", fromTui: true), "tui dark preset");
                        MuxAssert.AreEqual("mux-light", ColorPaletteCatalog.FromLegacyTheme("light", fromTui: true), "tui light");
                        MuxAssert.AreEqual("high-contrast", ColorPaletteCatalog.FromLegacyTheme("highcontrast", fromTui: false), "high contrast");
                        MuxAssert.AreEqual("mux", ColorPaletteCatalog.FromLegacyTheme("dark", fromTui: false), "desktop dark");
                        MuxAssert.AreEqual("system", ColorPaletteCatalog.FromLegacyTheme("system", fromTui: false), "desktop system");

                        MuxSettings settings = new MuxSettings { TuiTheme = "light" };
                        MuxAssert.AreEqual("mux-light", ColorPaletteSettings.EffectiveTuiId(settings), "legacy tuiTheme until a palette is chosen");
                        settings.ColorPalette = "Ocean";
                        MuxAssert.AreEqual("ocean", ColorPaletteSettings.EffectiveTuiId(settings), "shared palette wins, normalized");
                        settings.ColorPalette = "system";
                        MuxAssert.AreEqual("mux", ColorPaletteSettings.EffectiveTuiId(settings), "a terminal reads system as the default");
                        return Task.CompletedTask;
                    }),

                    Case("SettingsRoundTripPalettes", "colorPalette, customPalettes, and cursorBlink survive settings.json", async (CancellationToken ct) =>
                    {
                        await WithConfigDirAsync(dir =>
                        {
                            MuxAssert.IsNull(SettingsLoader.LoadSettings().ColorPalette, "no palette chosen on a fresh config");
                            MuxAssert.IsTrue(SettingsLoader.LoadSettings().CursorBlink, "blink on by default");

                            ColorPalette saved = ColorPaletteSettings.SaveCustom(Custom("Team-Dark", "#1E1E2E"), activate: true);
                            MuxAssert.AreEqual("team-dark", saved.Id, "id normalized");
                            MuxSettings reloaded = SettingsLoader.LoadSettings();
                            MuxAssert.AreEqual("team-dark", reloaded.ColorPalette, "activated");
                            MuxAssert.AreEqual("#1e1e2e", reloaded.CustomPalettes.Single().Colors.Background, "stored normalized");
                            MuxAssert.IsNull(reloaded.CustomPalettes.Single().Colors.Accent, "unset roles stay unset (inherit)");
                            string json = File.ReadAllText(Path.Combine(dir, "settings.json"));
                            MuxAssert.Contains("\"colorPalette\"", json, "colorPalette persisted");
                            MuxAssert.Contains("\"customPalettes\"", json, "customPalettes persisted");

                            MuxSettings flags = SettingsLoader.LoadSettings();
                            flags.CursorBlink = false;
                            flags.TuiTheme = "light";
                            SettingsLoader.SaveSettings(flags);
                            MuxSettings flagsBack = SettingsLoader.LoadSettings();
                            MuxAssert.IsFalse(flagsBack.CursorBlink, "cursorBlink survives a load");
                            MuxAssert.AreEqual("light", flagsBack.TuiTheme, "tuiTheme survives a load (it used to be dropped)");
                            MuxAssert.AreEqual("team-dark", flagsBack.ColorPalette, "colorPalette survives a load");

                            ColorPaletteSettings.SetActive("Midnight");
                            MuxAssert.AreEqual("midnight", SettingsLoader.LoadSettings().ColorPalette, "set active");
                            MuxAssert.IsTrue(Throws(() => ColorPaletteSettings.SetActive("nope")), "unknown palette rejected");
                            MuxAssert.IsTrue(Throws(() => ColorPaletteSettings.SaveCustom(Custom("glacier", null), false)), "built-in id rejected on save");

                            ColorPaletteSettings.SetActive("team-dark");
                            MuxAssert.IsTrue(ColorPaletteSettings.DeleteCustom("team-dark"), "deleted");
                            MuxAssert.AreEqual(ColorPaletteCatalog.DefaultId, SettingsLoader.LoadSettings().ColorPalette, "active falls back to the default");
                            MuxAssert.IsFalse(ColorPaletteSettings.DeleteCustom("team-dark"), "already gone");
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                    }),

                    Case("TuiAppliesEveryPalette", "The TUI cycles every palette and opens in the shared choice", async (CancellationToken ct) =>
                    {
                        await WithConfigDirAsync(async dir =>
                        {
                            ColorPaletteSettings.SetActive("ember");
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out _, manager))
                            {
                                MuxAssert.AreEqual("ember", app.PaletteId, "opens in the shared palette");
                                MuxAssert.AreEqual(ColorPaletteCatalog.BuiltIn.Count, app.PaletteCount, "every built-in offered");
                                HashSet<string> seen = new HashSet<string>();
                                for (int i = 0; i < app.PaletteCount; i++)
                                {
                                    seen.Add(app.PaletteId);
                                    MuxAssert.Contains("mux", app.RenderRegion("transcript", 80, 10), "renders under " + app.PaletteId);
                                    app.CycleTheme();
                                }

                                MuxAssert.AreEqual(app.PaletteCount, seen.Count, "each palette visited once");
                                app.ApplyThemeByName("light", persist: false);
                                MuxAssert.AreEqual("mux-light", app.PaletteId, "legacy name maps to a palette");
                            }
                        }).ConfigureAwait(false);
                    }),

                    Case("PaletteSlashCommandOpensPicker", "/palette opens the palette picker in the terminal", async (CancellationToken ct) =>
                    {
                        await WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                            {
                                backend.FeedInput("/palette\r");
                                app.PumpInputOnce();
                                MuxAssert.IsTrue(app.IsModalActive, "palette picker open");
                            }
                        }).ConfigureAwait(false);
                    }),

                    Case("TuiPaintsBackgroundAndThemedCaret", "A painted palette fills the screen and draws the caret in its cursor color", async (CancellationToken ct) =>
                    {
                        await WithConfigDirAsync(async dir =>
                        {
                            MuxSettings settings = SettingsLoader.LoadSettings();
                            settings.CursorBlink = false; // a steady caret makes the frame deterministic
                            settings.ColorPalette = "midnight";
                            SettingsLoader.SaveSettings(settings);
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out _, manager))
                            {
                                app.Start();
                                app.RenderOnce();
                                CellBuffer? frame = app.CaptureFrame();
                                MuxAssert.IsNotNull(frame, "frame captured");
                                Color background = Color.FromRgb(0x0b, 0x10, 0x20);
                                Color cursor = Color.FromRgb(0x7a, 0xa2, 0xff);
                                MuxAssert.AreEqual(background, frame!.Get(0, 0).Style.Background, "top-left painted with the palette background");
                                MuxAssert.IsTrue(CountBackground(frame, cursor) == 1, "exactly one caret cell in the cursor color");
                                MuxAssert.IsFalse(AnyReverse(frame), "the reverse-video caret was replaced");

                                app.ApplyThemeByName("mux", persist: false);
                                app.RenderOnce();
                                CellBuffer native = app.CaptureFrame()!;
                                MuxAssert.AreEqual(Color.Default, native.Get(0, 0).Style.Background, "the mux default keeps the terminal background");
                                MuxAssert.AreEqual(1, CountBackground(native, Color.FromRgb(0x4a, 0xde, 0x80)), "the mux caret is green");
                            }
                        }).ConfigureAwait(false);
                    }),

                    Case("CaretBlinkPhases", "The caret stays solid while typing, then alternates; off means always on", (CancellationToken ct) =>
                    {
                        int phase = TuiPalette.BlinkPhaseMilliseconds;
                        MuxAssert.IsTrue(TuiPalette.CaretVisible(1000, 1000, true), "visible right after input");
                        MuxAssert.IsTrue(TuiPalette.CaretVisible(1000 + phase - 1, 1000, true), "visible for the first phase");
                        MuxAssert.IsFalse(TuiPalette.CaretVisible(1000 + phase, 1000, true), "hidden in the second phase");
                        MuxAssert.IsTrue(TuiPalette.CaretVisible(1000 + (2 * phase), 1000, true), "visible again in the third");
                        MuxAssert.IsTrue(TuiPalette.CaretVisible(1000 + phase, 1000, false), "always visible when blink is off");

                        CellBuffer buffer = new CellBuffer(4, 1);
                        BufferSurface surface = new BufferSurface(buffer);
                        CellStyle normal = new CellStyle(Color.FromRgb(200, 200, 200), Color.FromRgb(20, 20, 20));
                        surface.Set(1, 0, Cell.Glyph("x", normal.WithAttribute(CellAttributes.Reverse, true), 1));
                        Color cursor = Color.FromRgb(255, 0, 0);
                        MuxAssert.IsTrue(TuiPalette.RestyleCaret(surface, new Rect(0, 0, 4, 1), cursor, Color.FromRgb(0, 0, 0), visible: true), "caret found");
                        MuxAssert.AreEqual(cursor, buffer.Get(1, 0).Style.Background, "painted in the cursor color");
                        MuxAssert.AreEqual("x", buffer.Get(1, 0).Grapheme, "glyph kept");
                        surface.Set(1, 0, Cell.Glyph("x", normal.WithAttribute(CellAttributes.Reverse, true), 1));
                        TuiPalette.RestyleCaret(surface, new Rect(0, 0, 4, 1), cursor, Color.FromRgb(0, 0, 0), visible: false);
                        MuxAssert.AreEqual(normal, buffer.Get(1, 0).Style, "off phase draws plain text");
                        return Task.CompletedTask;
                    }),

                    Case("PaletteFormBuildsAndValidates", "The TUI palette form returns a palette and flags bad hex values", async (CancellationToken ct) =>
                    {
                        ColorPalette seed = ColorPaletteCatalog.Resolve("forest", null);
                        PaletteFormModal modal = new PaletteFormModal("New color palette", seed, editing: false, null);
                        Render(modal, 90, 60);
                        modal.HandlePaste("my-forest");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Tab));
                        modal.HandlePaste("My Forest");
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsTrue(modal.IsClosed, "valid form closes");
                        ColorPalette? result = await modal.Completion.ConfigureAwait(false) as ColorPalette;
                        MuxAssert.IsNotNull(result, "palette returned");
                        MuxAssert.AreEqual("my-forest", result!.Id, "id");
                        MuxAssert.AreEqual("My Forest", result.Name, "name");
                        MuxAssert.AreEqual("forest", result.Base, "built-in seed becomes the base");
                        MuxAssert.AreEqual(seed.Colors.Accent, result.Colors.Accent, "colors prefilled from the seed");

                        PaletteFormModal bad = new PaletteFormModal("New color palette", seed, editing: false, null);
                        Render(bad, 90, 60);
                        bad.HandlePaste("Bad Id");
                        bad.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsFalse(bad.IsClosed, "invalid form stays open");
                        MuxAssert.Contains("kebab-case", Render(bad, 90, 60), "id error shown");
                    }),

                    Case("TuiEndpointFormCoversEveryAdapter", "Editing an endpoint of every adapter keeps its adapter and cloud fields", async (CancellationToken ct) =>
                    {
                        foreach (AdapterTypeEnum adapter in Enum.GetValues(typeof(AdapterTypeEnum)))
                        {
                            EndpointConfig existing = new EndpointConfig
                            {
                                Name = "ep-" + adapter.ToKebab(),
                                AdapterType = adapter,
                                Model = "model-x",
                                BaseUrl = adapter == AdapterTypeEnum.AzureOpenAi || adapter == AdapterTypeEnum.Ollama || adapter == AdapterTypeEnum.OpenAiCompatible || adapter == AdapterTypeEnum.Vllm
                                    ? "https://host.example/v1"
                                    : string.Empty,
                                Region = adapter == AdapterTypeEnum.Vertex || adapter == AdapterTypeEnum.Bedrock ? "us-east-1" : null,
                                Project = adapter == AdapterTypeEnum.Vertex ? "my-project" : null,
                                ApiVersion = adapter == AdapterTypeEnum.AzureOpenAi ? "2024-10-21" : null,
                                ApiKey = adapter == AdapterTypeEnum.Vertex || adapter == AdapterTypeEnum.Bedrock ? null : "${KEY}"
                            };

                            EndpointFormModal modal = new EndpointFormModal("Edit endpoint", existing);
                            string screen = Render(modal, 100, 80);
                            MuxAssert.Contains(adapter.ToKebab(), screen, adapter + " offered");
                            modal.HandleKey(KeyEvent.Special(KeyCode.Enter));
                            MuxAssert.IsTrue(modal.IsClosed, adapter + " saves without extra input");
                            EndpointConfig? result = await modal.Completion.ConfigureAwait(false) as EndpointConfig;
                            MuxAssert.IsNotNull(result, adapter + " returned");
                            MuxAssert.AreEqual(adapter, result!.AdapterType, adapter + " adapter kept (never rewritten to openai-compatible)");
                            MuxAssert.AreEqual(existing.Region, result.Region, adapter + " region kept");
                            MuxAssert.AreEqual(existing.Project, result.Project, adapter + " project kept");
                            MuxAssert.AreEqual(existing.ApiVersion, result.ApiVersion, adapter + " api-version kept");
                            MuxAssert.AreEqual(existing.ApiKey, result.ApiKey, adapter + " key kept");
                        }
                    }),

                    Case("TuiEndpointFormAdaptsFieldsToAdapter", "Cloud adapters require region/project and hide the API key; hosted APIs need no base URL", async (CancellationToken ct) =>
                    {
                        EndpointFormModal vertex = new EndpointFormModal("Edit endpoint", new EndpointConfig { Name = "v", AdapterType = AdapterTypeEnum.Vertex, Model = "gemini-x" });
                        string vertexScreen = Render(vertex, 100, 80);
                        MuxAssert.DoesNotContain("API key", vertexScreen, "no API key field for Vertex");
                        MuxAssert.Contains("Application Default Credentials", vertexScreen, "credential hint shown");
                        vertex.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        string vertexErrors = Render(vertex, 100, 80);
                        MuxAssert.IsFalse(vertex.IsClosed, "missing region and project block save");
                        MuxAssert.Contains("Region is required", vertexErrors, "region error");
                        MuxAssert.Contains("Project is required", vertexErrors, "project error");

                        EndpointFormModal bedrock = new EndpointFormModal("Edit endpoint", new EndpointConfig { Name = "b", AdapterType = AdapterTypeEnum.Bedrock, Model = "anthropic.claude", Region = "us-west-2" });
                        string bedrockScreen = Render(bedrock, 100, 80);
                        MuxAssert.Contains("AWS_ACCESS_KEY_ID", bedrockScreen, "AWS credential hint");
                        MuxAssert.DoesNotContain("Google Cloud project", bedrockScreen, "no project field for Bedrock");

                        EndpointFormModal azure = new EndpointFormModal("Edit endpoint", new EndpointConfig { Name = "a", AdapterType = AdapterTypeEnum.AzureOpenAi, Model = "deploy", BaseUrl = string.Empty });
                        MuxAssert.Contains("Deployment name", Render(azure, 100, 80), "Azure labels the model as a deployment");
                        azure.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.Contains("Base URL is required", Render(azure, 100, 80), "Azure needs its resource URL");

                        EndpointFormModal anthropic = new EndpointFormModal("Edit endpoint", new EndpointConfig { Name = "c", AdapterType = AdapterTypeEnum.Anthropic, Model = "claude-sonnet-5-5", BaseUrl = string.Empty });
                        MuxAssert.Contains("ANTHROPIC_API_KEY", Render(anthropic, 100, 80), "key fallback named");
                        anthropic.HandleKey(KeyEvent.Special(KeyCode.Enter));
                        MuxAssert.IsTrue(anthropic.IsClosed, "the public Anthropic API needs no base URL");
                        EndpointConfig? saved = await anthropic.Completion.ConfigureAwait(false) as EndpointConfig;
                        MuxAssert.AreEqual(AuthPlacementEnum.Bearer, saved!.AuthPlacement, "fixed scheme, no placement");
                    }),

                    Case("ModelImportCopiesTheSourceEndpoint", "Imported models copy credentials and cloud fields, skip duplicates, and get unique names", (CancellationToken ct) =>
                    {
                        EndpointConfig source = new EndpointConfig
                        {
                            Name = "bedrock", AdapterType = AdapterTypeEnum.Bedrock, Model = "anthropic.claude-sonnet", Region = "us-east-1", IsDefault = true, MaxTokens = 4096
                        };
                        List<EndpointConfig> existing = new List<EndpointConfig>
                        {
                            source,
                            new EndpointConfig { Name = "anthropic.claude-opus", AdapterType = AdapterTypeEnum.Ollama, Model = "x" }
                        };

                        List<EndpointConfig> created = EndpointModelImport.CreateEndpoints(source, new[] { "anthropic.claude-sonnet", "anthropic.claude-opus", " ", "anthropic.claude-haiku" }, existing);
                        MuxAssert.AreEqual(2, created.Count, "configured and blank models skipped");
                        MuxAssert.AreEqual("anthropic.claude-opus (2)", created[0].Name, "name made unique");
                        MuxAssert.AreEqual("anthropic.claude-haiku", created[1].Name, "model id as the name");
                        MuxAssert.IsTrue(created.All(e => !e.IsDefault && e.Region == "us-east-1" && e.AdapterType == AdapterTypeEnum.Bedrock && e.MaxTokens == 4096), "copies keep the source settings and are not default");
                        MuxAssert.IsTrue(EndpointModelImport.IsConfigured(source, "ANTHROPIC.CLAUDE-SONNET", existing), "case-insensitive match");
                        return Task.CompletedTask;
                    }),

                    Case("RestPaletteAndImportRoutes", "The palette and endpoint-import REST routes list, choose, save, delete, and validate", async (CancellationToken ct) =>
                    {
                        await WithConfigDirAsync(async dir =>
                        {
                            SettingsLoader.SaveEndpoints(new List<EndpointConfig>
                            {
                                new EndpointConfig { Name = "src", AdapterType = AdapterTypeEnum.Anthropic, Model = "claude-a", ApiKey = "sk-secret", IsDefault = true }
                            });

                            // The server's request threads do not inherit the AsyncLocal override, so point the
                            // process-wide config directory at this test's directory too (restored below).
                            string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
                            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "palkey" };
                            MuxServer? server = null;
                            int port = 0;
                            for (int attempt = 0; attempt < 10 && server == null; attempt++)
                            {
                                port = FreePort();
                                rest.Port = port;
                                MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(dir, "sessions")), () => SettingsLoader.LoadEndpoints(), null);
                                try { candidate.Start(); server = candidate; }
                                catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                            }

                            MuxAssert.IsNotNull(server, "server started");
                            string api = "http://127.0.0.1:" + port + "/v1.0/api";
                            try
                            {
                                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                                {
                                    for (int i = 0; i < 20; i++) { try { await http.GetAsync(api + "/health").ConfigureAwait(false); break; } catch (Exception) { Thread.Sleep(100); } }
                                    MuxAssert.AreEqual(401, (int)(await http.GetAsync(api + "/palettes").ConfigureAwait(false)).StatusCode, "auth required");
                                    http.DefaultRequestHeaders.Add("Authorization", "Bearer palkey");

                                    string listed = await http.GetStringAsync(api + "/palettes").ConfigureAwait(false);
                                    MuxAssert.Contains("\"Id\":\"midnight\"", listed, "built-ins listed");
                                    MuxAssert.DoesNotContain("\"Active\":", listed, "nothing chosen yet (null is omitted)");
                                    MuxAssert.Contains("\"surfaceAlt\"", listed, "role names on the wire");

                                    HttpResponseMessage chosen = await http.PutAsync(api + "/palettes/active", Json(new { Id = "ocean" })).ConfigureAwait(false);
                                    MuxAssert.AreEqual(200, (int)chosen.StatusCode, "set active");
                                    MuxAssert.AreEqual("ocean", SettingsLoader.LoadSettings().ColorPalette, "saved to settings.json");
                                    MuxAssert.AreEqual(404, (int)(await http.PutAsync(api + "/palettes/active", Json(new { Id = "nope" })).ConfigureAwait(false)).StatusCode, "unknown palette");
                                    MuxAssert.AreEqual(400, (int)(await http.PutAsync(api + "/palettes/active", Json(new { })).ConfigureAwait(false)).StatusCode, "id required");

                                    object palette = new { Palette = new { Id = "team", Name = "Team", Dark = true, Base = "ocean", Colors = new Dictionary<string, string> { ["accent"] = "#FF8800" } }, Activate = true };
                                    HttpResponseMessage saved = await http.PutAsync(api + "/palettes", Json(palette)).ConfigureAwait(false);
                                    string savedBody = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    MuxAssert.AreEqual(200, (int)saved.StatusCode, "custom saved: " + savedBody);
                                    MuxAssert.Contains("\"Active\":\"team\"", savedBody, "activated");
                                    MuxAssert.Contains("#ff8800", savedBody, "normalized color");
                                    HttpResponseMessage reserved = await http.PutAsync(api + "/palettes", Json(new { Palette = new { Id = "mux", Name = "x" } })).ConfigureAwait(false);
                                    MuxAssert.AreEqual(400, (int)reserved.StatusCode, "built-in id rejected");
                                    MuxAssert.AreEqual(400, (int)(await http.DeleteAsync(api + "/palettes?id=midnight").ConfigureAwait(false)).StatusCode, "built-ins cannot be deleted");
                                    MuxAssert.AreEqual(200, (int)(await http.DeleteAsync(api + "/palettes?id=team").ConfigureAwait(false)).StatusCode, "custom deleted");
                                    MuxAssert.AreEqual(404, (int)(await http.DeleteAsync(api + "/palettes?id=team").ConfigureAwait(false)).StatusCode, "already gone");

                                    MuxAssert.AreEqual(404, (int)(await http.GetAsync(api + "/endpoints/models?name=nope").ConfigureAwait(false)).StatusCode, "unknown endpoint");
                                    HttpResponseMessage imported = await http.PostAsync(api + "/endpoints/import", Json(new { Source = "src", Models = new[] { "claude-a", "claude-b" } })).ConfigureAwait(false);
                                    MuxAssert.AreEqual(200, (int)imported.StatusCode, "imported");
                                    List<EndpointConfig> after = SettingsLoader.LoadEndpoints();
                                    EndpointConfig copy = after.Single(e => e.Model == "claude-b");
                                    MuxAssert.AreEqual("sk-secret", copy.ApiKey, "the stored key carried over server-side");
                                    MuxAssert.AreEqual(2, after.Count, "the already-configured model was skipped");
                                    MuxAssert.AreEqual(400, (int)(await http.PostAsync(api + "/endpoints/import", Json(new { Source = "src" })).ConfigureAwait(false)).StatusCode, "models required");
                                }
                            }
                            finally
                            {
                                server?.Stop();
                                server?.Dispose();
                                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                            }
                        }).ConfigureAwait(false);
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static ColorPalette Custom(string id, string? background)
        {
            ColorPalette palette = new ColorPalette { Id = id, Name = "Custom " + id, Dark = true };
            palette.Colors.Background = background;
            return palette;
        }

        private static void AtLeast(string palette, string pair, string a, string b, double floor)
        {
            double ratio = ColorPaletteCatalog.ContrastRatio(a, b);
            MuxAssert.IsTrue(ratio >= floor, $"{palette} {pair} contrast {ratio:0.00} >= {floor}");
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        private static int CountBackground(CellBuffer frame, Color color)
        {
            int count = 0;
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    if (frame.Get(x, y).Style.Background == color) count++;
                }
            }

            return count;
        }

        private static bool AnyReverse(CellBuffer frame)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    if (frame.Get(x, y).Style.HasAttribute(CellAttributes.Reverse)) return true;
                }
            }

            return false;
        }

        private static StringContent Json(object value)
        {
            return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WithConfigDirAsync(Func<string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-palettes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            using (SettingsLoader.PushConfigDirectoryOverride(dir))
            {
                try
                {
                    await body(dir).ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }
        }

        private static JobManager NewManager()
        {
            return new JobManager((job, prompt, ct) => Empty(), maxConcurrency: 1);
        }

        private static async IAsyncEnumerable<Mux.Core.Agent.AgentEvent> Empty()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(120, 30);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static string Render(TUIKit.Modals.Modal modal, int width, int height)
        {
            CellBuffer buffer = new CellBuffer(width, height);
            modal.Render(new BufferSurface(buffer));
            StringBuilder text = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    string grapheme = buffer.Get(x, y).Grapheme ?? " ";
                    text.Append(grapheme.Length > 0 ? grapheme[0] : ' ');
                }

                text.Append('\n');
            }

            return text.ToString();
        }

        #endregion
    }
}
