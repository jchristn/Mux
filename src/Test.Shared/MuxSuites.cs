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
                    HeadlessFeaturesSuite.Create(),
                    HeadlessOutputModesSuite.Create(),
                    ToolGovernanceSuite.Create(),
                    Phase4FeaturesSuite.Create(),
                    InputFormatSuite.Create(),
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

                    // REST server (v0.9.0): settings + live route behavior.
                    RestServerSettingsSuite.Create(),
                    MuxServerRouteSuite.Create(),
                    SessionTitleHelperSuite.Create(),

                    // Mux.Desktop.Core logic suites (localization, formatters, thread/usage/conversation services).
                    DesktopLocalizationSuite.Create(),
                    DesktopFormattersSuite.Create(),
                    ThreadServiceSuite.Create(),
                    UsageWindowSuite.Create(),
                    UsageAnalyticsSuite.Create(),
                    TurnProjectionSuite.Create(),
                    ConversationServiceSuite.Create(),
                    WorkspaceSuite.Create(),
                    CodeTokenizerSuite.Create(),
                    DesktopPreferencesSuite.Create(),
                    PromptHistorySuite.Create(),
                    WelcomeQuipsSuite.Create(),

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

                    // Mux.Cli global settings editor: /settings modal + persist/apply round trip.
                    SettingsSuite.Create(),

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

                    // Live model enumeration: /v1/models discovery + per-adapter dispatch and failure capture.
                    EndpointModelListerSuite.Create(),

                    // Mux.Cli interactive MCP-server management (list / add / edit / remove).
                    McpManagementSuite.Create(),

                    // Mux.Cli optional dark-grey boundary lines (/borders toggle + rendering).
                    BoundaryLinesSuite.Create(),

                    // Skills: loader, parser, and validation matrix.
                    SkillLoaderSuite.Create(),

                    // Skills: execution and the skill / run_skill tools.
                    SkillProviderSuite.Create(),

                    // Skills: the trust boundary — arguments reach the interpreter without a shell.
                    SkillSecuritySuite.Create(),

                    // Skills: interactive runtime lifecycle and template composition.
                    SkillRuntimeSuite.Create(),
                    ExternalToolsBinderSuite.Create(),

                    // Skills: manager operations (create/enable/disable/remove/import) and the /skills surface.
                    SkillManagerSuite.Create(),
                    SkillManagementSuite.Create(),

                    // Skills: the mux skill CLI verb.
                    SkillCommandSuite.Create(),

                    // Skills: the seeded default library.
                    DefaultSkillsSuite.Create(),

                    // MCP runtime wiring: template binding (tools + prompt) and lifecycle.
                    McpTemplateBinderSuite.Create(),
                    McpRuntimeSuite.Create(),

                    // Command-menu column alignment (F1 / /? menu).
                    CommandMenuFormatterSuite.Create(),

                    // Background tasks: task-plan model, timing, readiness, and validation (M1).
                    TaskPlanSuite.Create(),

                    // Background tasks: model-facing tools + registration (M3).
                    TaskToolsSuite.Create(),

                    // Background tasks: AgentLoop task-plan event emission (M3).
                    TaskEventSuite.Create(),

                    // Background tasks: /tasks viewer + human annotation (M7).
                    TaskModalSuite.Create(),

                    // Background tasks: orchestrated parallel execution over the job manager (M8).
                    TaskOrchestratorSuite.Create(),

                    // Subagents: registry, spawn_subagent tool, and subagents.json persistence.
                    SubagentSuite.Create(),

                    // Session sharing: local Markdown/HTML export renderer.
                    SessionExporterSuite.Create(),

                    // Custom keybindings: catalog overrides + keybindings.json persistence.
                    KeybindingSuite.Create(),

                    // Undo/redo: git-checkpoint capture, restore, and history.
                    CheckpointSuite.Create(),

                    // Plugin system: event hooks, custom commands, and hooks.json persistence.
                    PluginSuite.Create(),

                    // Usage telemetry: SQLite store schema, inserts, retention, and concurrent writers.
                    UsageStoreSuite.Create(),

                    // Usage telemetry: live REST endpoints over a seeded store.
                    UsageRoutesSuite.Create()
                };
            }
        }
    }
}
