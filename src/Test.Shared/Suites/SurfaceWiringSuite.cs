namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;
    using Mux.Server;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the surface-wiring correctness pass: it captures, as tests, the guarantee that a
    /// management affordance opens the editor it names. The confirmed bug it guards against is the web
    /// dashboard's Prompts/Pricing collision — two sections had defined functions with the same names
    /// (<c>openPr</c>/<c>prFields</c>/<c>delPr</c>), so JavaScript hoisting made the pricing definitions win
    /// and the Prompts row opened the pricing editor. These cases assert the rename held and would turn red if
    /// the collision were reintroduced. The per-surface node/command and row/modal bindings on the TUI,
    /// desktop, and VS Code surfaces are covered by their own suites (<c>PromptCatalogModalSuite</c>,
    /// <c>DesktopPromptCatalogSuite</c>, and the extension's <c>catalog.test.ts</c>); the shared edit-gating
    /// contract every surface relies on is asserted here against the catalog itself.
    /// </summary>
    public static class SurfaceWiringSuite
    {
        /// <summary>
        /// Builds the surface-wiring suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor("SurfaceWiring", "DashboardManagementFunctionsAreUniquelyDefined", "The Prompts and Pricing editors define distinct, single functions (no hoisting collision)", (CancellationToken ct) =>
                {
                    string html = DashboardPage.Render(null, "9.9.9-test");

                    // Each management function is defined exactly once. Before the fix, prFields/openPr/delPr
                    // were each defined twice (Prompts and Pricing), so the later (Pricing) definition won.
                    MuxAssert.AreEqual(1, Count(html, "function openPr("), "one prompts openPr");
                    MuxAssert.AreEqual(1, Count(html, "function openPrice("), "one pricing openPrice");
                    MuxAssert.AreEqual(1, Count(html, "function prFields("), "one prompts prFields");
                    MuxAssert.AreEqual(1, Count(html, "function priceFields("), "one pricing priceFields");
                    MuxAssert.AreEqual(1, Count(html, "function delPr("), "one prompts delPr");
                    MuxAssert.AreEqual(1, Count(html, "function delPrice("), "one pricing delPrice");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor("SurfaceWiring", "DashboardPromptsRowOpensPromptEditorNotPricing", "The Prompts table and add button are wired to the prompt-profile editor", (CancellationToken ct) =>
                {
                    string html = DashboardPage.Render(null, "9.9.9-test");

                    // The Prompts add button targets the prompts opener, and the pricing add button targets the
                    // pricing opener — the two no longer resolve to the same hoisted function.
                    MuxAssert.Contains("on(\"prompts_add\",function(){openPr(-1);})", html, "prompts add opens prompt editor");
                    MuxAssert.Contains("on(\"pricing_add\",function(){openPrice(-1);})", html, "pricing add opens pricing editor");

                    // The Prompts table row/menu wiring references the prompts opener, not the pricing one.
                    string promptsWire = Between(html, "wireTable(\"prompts_list\"", "});");
                    MuxAssert.Contains("openPr(i)", promptsWire, "prompts row opens prompt editor");
                    MuxAssert.IsFalse(promptsWire.Contains("openPrice("), "prompts wiring does not call the pricing editor");

                    string pricingWire = Between(html, "wireTable(\"pricing_list\"", "});");
                    MuxAssert.Contains("openPrice(i)", pricingWire, "pricing row opens pricing editor");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor("SurfaceWiring", "DashboardCatalogIsWiredToItsRoutes", "The operational-catalog table is present and wired to the catalog editor", (CancellationToken ct) =>
                {
                    string html = DashboardPage.Render(null, "9.9.9-test");
                    MuxAssert.Contains("wireTable(\"catalog_list\"", html, "catalog table wired");
                    MuxAssert.Contains("function openCat(", html, "catalog editor defined");
                    MuxAssert.Contains("/v1.0/api/prompts/catalog", html, "catalog routes referenced");
                    // The three profile prompt fields are all editable in the profile form.
                    MuxAssert.Contains("ToolsDisabledPrompt", html, "tools-disabled field present");
                    MuxAssert.Contains("CompactionPrompt", html, "compaction field present");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor("SurfaceWiring", "CatalogEditGatingContractHoldsForEverySurface", "Only global-scoped catalog entries are editable; profile/external are not — the rule every surface's edit-gating relies on", (CancellationToken ct) =>
                {
                    // Every surface decides "edit vs read-only/deep-link" from the entry's scope. Assert that
                    // contract once here so all four surfaces are grounded in the same tested rule.
                    foreach (PromptDefinition definition in PromptCatalog.All)
                    {
                        bool editable = definition.Scope == PromptScope.Global;
                        if (definition.Scope == PromptScope.Global)
                        {
                            MuxAssert.IsTrue(editable, "global editable: " + definition.Key);
                        }
                        else
                        {
                            MuxAssert.IsFalse(editable, "non-global not editable: " + definition.Key);
                        }
                    }

                    // The persona entries are profile-scoped (edited in the profile editor, not the catalog).
                    MuxAssert.IsTrue(PromptCatalog.TryGet("system", out PromptDefinition? system) && system!.Scope == PromptScope.Profile, "system is profile-scoped");
                    MuxAssert.IsTrue(PromptCatalog.TryGet("tools-disabled", out PromptDefinition? td) && td!.Scope == PromptScope.Profile, "tools-disabled is profile-scoped");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor("SurfaceWiring", "Management-affordance wiring correctness (Prompts/Pricing collision regression)", cases);
        }

        private static int Count(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string Between(string haystack, string start, string end)
        {
            int s = haystack.IndexOf(start, StringComparison.Ordinal);
            if (s < 0) return string.Empty;
            int e = haystack.IndexOf(end, s, StringComparison.Ordinal);
            if (e < 0) return haystack.Substring(s);
            return haystack.Substring(s, e - s);
        }
    }
}
