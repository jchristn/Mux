namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Aggregates every Touchstone <see cref="TestSuiteDescriptor"/> for MUX so that any runner
    /// (console, xUnit, NUnit) can consume the full set with a single call. New suites are added
    /// to <see cref="All"/> as they are ported to Touchstone.
    /// </summary>
    public static class MuxSuites
    {
        static MuxSuites()
        {
            // Warm the thread pool once before any runner executes suites. The concurrency and
            // agent-loop integration suites spin up many Task workers and async continuations; a cold
            // pool can delay their scheduling enough to intermittently trip timing-sensitive assertions
            // under some runners on net8. Runs before the first access to All on every runner.
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(Math.Max(workerThreads, 32), Math.Max(completionPortThreads, 32));
        }

        /// <summary>
        /// Gets all registered MUX test suites. The list is rebuilt on each access so callers
        /// always receive fresh descriptor instances.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    SingleTurnSuite.Create(),
                    ToolUseSuite.Create(),
                    PrintModeSuite.Create(),
                    CliContractSuite.Create(),
                    ApprovalPolicySuite.Create(),
                    MultiEditSuite.Create(),
                    EndpointSwitchingSuite.Create(),
                    McpIntegrationSuite.Create(),

                    // Mux.Core / Agent unit suites (ported from Test.Xunit/Agent).
                    ContextWindowManagerSuite.Create(),
                    ApprovalRoutingSuite.Create(),
                    ConversationCompactionPlannerSuite.Create(),
                    ConversationTrimCompactorSuite.Create(),
                    AgentLoopOptionsSuite.Create(),
                    ExternalToolProviderSuite.Create(),
                    JobManagerSuite.Create(),
                    WriteLeaseSuite.Create(),
                    WriteLeaseIntegrationSuite.Create(),

                    // Mux.Core / Tools unit suites (ported from Test.Xunit/Tools).
                    BuiltInToolRegistrySuite.Create(),
                    ReadFileToolSuite.Create(),
                    WriteFileToolSuite.Create(),
                    GlobToolSuite.Create(),
                    GrepToolSuite.Create(),
                    EditFileToolSuite.Create(),
                    MultiEditToolSuite.Create(),
                    RunProcessToolSuite.Create(),
                    WebRetrieveToolSuite.Create(),
                    McpToolManagerSuite.Create(),

                    // Mux.Core / Llm bridge suite (PolyPrompt-backed LlmClient against mock servers).
                    LlmBridgeSuite.Create(),

                    // Settings + non-interactive CLI command unit suites (ported from Test.Xunit).
                    EndpointConfigSuite.Create(),
                    SettingsLoaderSuite.Create(),
                    SessionTitleHelperSuite.Create(),
                    CommandRuntimeResolverSuite.Create(),
                    EndpointCommandParserSuite.Create(),
                    StructuredOutputFormatterSuite.Create(),
                    CliCommandSuite.Create(),

                    // Mux.Search unit suite (ported from Test.Xunit/Search).
                    WebSearchServiceSuite.Create(),

                    // Mux.Core / Sessions persistence suite (M4).
                    SessionStoreSuite.Create(),

                    // Mux.Cli TUIKit interactive shell suite (M6).
                    TuiShellSuite.Create(),

                    // Mux.Cli AgentEvent projector suite (M7).
                    ProjectorSuite.Create(),

                    // Mux.Cli sidebar + responsive collapse suite (M8).
                    SidebarSuite.Create(),

                    // Mux.Cli composer + prompt history + submit chooser suite (M9).
                    ComposerSuite.Create(),

                    // Prompt-profile resolution + prompt editor modal.
                    PromptsSuite.Create(),

                    // Mux.Cli command surfaces: slash / keybinding / menu (M10).
                    CommandSurfacesSuite.Create(),

                    // Mux.Cli modals: approval + jobs (M11).
                    ModalsSuite.Create(),

                    // Mux.Cli persistence UX: snapshot/save/load/restore/browser (M12).
                    PersistenceUxSuite.Create(),

                    // Mux.Cli headless frame rendering + golden snapshots (M13).
                    FrameSnapshotSuite.Create(),

                    // Mux.Cli polish: theme / mouse / resize (M14).
                    PolishSuite.Create(),

                    // Mux.Cli interactive endpoint/model management (post-migration restoration).
                    EndpointManagementSuite.Create(),

                    // Ollama completion-model importer: URL normalization + /api/tags discovery.
                    OllamaImportSuite.Create(),

                    // Mux.Cli interactive MCP-server management (list / add / edit / remove).
                    McpManagementSuite.Create(),

                    // Mux.Cli optional dark-grey boundary lines (/borders toggle + rendering).
                    BoundaryLinesSuite.Create(),

                    // Skills: loader, parser, and validation matrix.
                    SkillLoaderSuite.Create(),

                    // Skills: execution and the skill / run_skill tools.
                    SkillProviderSuite.Create(),

                    // Skills: interactive runtime lifecycle and template composition.
                    SkillRuntimeSuite.Create(),
                    ExternalToolsBinderSuite.Create(),

                    // MCP runtime wiring: template binding (tools + prompt) and lifecycle.
                    McpTemplateBinderSuite.Create(),
                    McpRuntimeSuite.Create(),

                    // Command-menu column alignment (F1 / /? menu).
                    CommandMenuFormatterSuite.Create()
                };
            }
        }
    }
}
