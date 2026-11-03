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
                    RunOutcomeSuite.Create(),
                    ApprovalPolicySuite.Create(),
                    MultiEditSuite.Create(),
                    EndpointSwitchingSuite.Create(),
                    McpIntegrationSuite.Create(),

                    // Mux.Core / Agent unit suites (ported from Test.Xunit/Agent).
                    ContextWindowManagerSuite.Create(),
                    ApprovalRoutingSuite.Create(),
                    PermissionPromptSuite.Create(),
                    ConversationCompactionPlannerSuite.Create(),
                    ConversationCompactorSuite.Create(),
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

                    // OpenTelemetry instrumentation (Mux meter/activity source, Watson wiring, export host).
                    TelemetryCoreSuite.Create(),
                    TelemetryHostSuite.Create(),

                    // Settings + non-interactive CLI command unit suites (ported from Test.Xunit).
                    EndpointConfigSuite.Create(),
                    EndpointAuthPlacementSuite.Create(),
                    UsageChartsModalSuite.Create(),
                    SetupStateSuite.Create(),
                    SettingsLoaderSuite.Create(),

                    // REST server (v0.9.0): settings + live route behavior.
                    RestServerSettingsSuite.Create(),
                    MuxServerRouteSuite.Create(),
                    RunRegistrySuite.Create(),
                    SessionTitleHelperSuite.Create(),

                    // Mux.Desktop.Core logic suites (localization, formatters, thread/usage/conversation services).
                    DesktopLocalizationSuite.Create(),
                    DesktopFormattersSuite.Create(),
                    DesktopTurnInfoSuite.Create(),
                    DesktopEndpointSelectionSuite.Create(),
                    ThreadServiceSuite.Create(),
                    UsageWindowSuite.Create(),
                    UsageAnalyticsSuite.Create(),
                    TurnProjectionSuite.Create(),
                    ConversationStatsSuite.Create(),
                    ConversationControllerSuite.Create(),
                    CoreMappingSuite.Create(),
                    SystemPromptResolverSuite.Create(),
                    ConversationServiceSuite.Create(),
                    EmbeddedServerSuite.Create(),
                    WorkspaceSuite.Create(),
                    CodeTokenizerSuite.Create(),
                    DesktopPreferencesSuite.Create(),
                    PromptHistorySuite.Create(),
                    KeybindingEditorSuite.Create(),
                    WelcomeQuipsSuite.Create(),

                    CommandRuntimeResolverSuite.Create(),
                    EndpointCommandParserSuite.Create(),
                    StructuredOutputFormatterSuite.Create(),
                    AgentEventSerializerSuite.Create(),
                    CliCommandSuite.Create(),

                    // Mux.Search unit suite (ported from Test.Xunit/Search).
                    WebSearchServiceSuite.Create(),

                    // Mux.Core / Sessions persistence suite (M4).
                    SessionStoreSuite.Create(),

                    // Mux.Core / consolidated session lifecycle (merge policy + shared persist).
                    SessionServiceSuite.Create(),

                    // Mux.Core / Sessions shared management verbs (cross-surface parity).
                    SessionManagerSuite.Create(),
                    SessionMetadataNormalizerSuite.Create(),

                    // Mux.Core / Utility: shared /cwd path resolver.
                    WorkingDirectoryResolverSuite.Create(),

                    // Mux.Cli TUIKit interactive shell suite (M6).
                    TuiShellSuite.Create(),
                    SeverityToastSuite.Create(),
                    SlashCommandCompletionSuite.Create(),

                    // Mux.Cli AgentEvent projector suite (M7).
                    ProjectorSuite.Create(),

                    // Mux.Cli form and editor modals on TUIKit's form support (paste, field errors, scrolling).
                    FormModalsSuite.Create(),

                    // Mux.Cli sidebar + responsive collapse suite (M8).
                    SidebarSuite.Create(),

                    // Mux.Cli composer + prompt history + submit chooser suite (M9).
                    ComposerSuite.Create(),

                    // Prompt-profile resolution + prompt editor modal.
                    PromptsSuite.Create(),

                    // Prompt catalog foundation: catalog, resolver, operational overrides, validation.
                    PromptCatalogSuite.Create(),

                    // TUI operational-prompt catalog browser modal.
                    PromptCatalogModalSuite.Create(),

                    // Desktop operational-prompt catalog view-model logic.
                    DesktopPromptCatalogSuite.Create(),

                    // Surface-wiring correctness: Prompts/Pricing collision regression + edit-gating contract.
                    SurfaceWiringSuite.Create(),

                    // Large-file context: map/summarize/truncate builder, summary cache, agent read_file path.
                    FileContextSuite.Create(),

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
                    ToolRuntimeBinderSuite.Create(),

                    // Skills: manager operations (create/enable/disable/remove/import) and the /skills surface.
                    SkillManagerSuite.Create(),
                    SkillManagementSuite.Create(),
                    SkillEditorCopySuite.Create(),
                    SkillCategoriesSuite.Create(),

                    // Skills: the mux skill CLI verb.
                    SkillCommandSuite.Create(),

                    // Skills: the seeded default library.
                    DefaultSkillsSuite.Create(),

                    // Skills: project scope, trust, invocation by name, and relevance-gated listing.
                    ProjectSkillsSuite.Create(),
                    SkillInvocationSuite.Create(),
                    SkillListingSuite.Create(),

                    // Project instruction files (MUX.md / AGENTS.md / CLAUDE.md) in the system prompt.
                    ProjectInstructionsSuite.Create(),

                    // Phase 2 toolchain skills (JavaScript, Python, project detection) in dry-run mode.
                    ToolchainSkillsSuite.Create(),

                    // Phase 3 review and playbook skills against real git repositories.
                    ReviewSkillsSuite.Create(),
                    LoopSkillsSuite.Create(),
                    LoopSchedulerSuite.Create(),
                    BackgroundProcessSuite.Create(),
                    McpServerSuite.Create(),
                    PlanModeSuite.Create(),
                    AskUserSuite.Create(),
                    WorktreeIsolationSuite.Create(),
                    MemorySuite.Create(),
                    FileMentionsSuite.Create(),
                    TerminalLaunchSuite.Create(),
                    ImportedSkillsSuite.Create(),
                    ScriptCommandsSuite2.Create(),
                    ScriptCommandsSuite1.Create(),
                    ScriptCommandsSuite3.Create(),
                    SkillScriptRuntimeSuite.Create(),
                    ShippedSkillsSuite.Create(),
                    SkillSelectionEvalSuite.Create(),
                    SkillSelectionLiveSuite.Create(),
                    DependencyAuditSuite.Create(),
                    ScriptLintSuite.Create(),
                    DatabaseSkillsSuite.Create(),
                    UpgradeSkillsSuite.Create(),
                    MobileLanguageSkillsSuite.Create(),
                    WebFrameworkSkillsSuite.Create(),
                    RuntimeSkillsSuite.Create(),
                    InfraSkillsSuite.Create(),
                    ApiContractSkillsSuite.Create(),
                    DockerServicesSuite.Create(),

                    // MCP runtime wiring: template binding (tools + prompt) and lifecycle.
                    McpTemplateBinderSuite.Create(),
                    McpRuntimeSuite.Create(),
                    McpDiagnosticsSuite.Create(),
                    McpClientSuite.Create(),
                    McpStdioClientSuite.Create(),
                    McpConfigSuite.Create(),
                    McpServerToolsSuite.Create(),
                    McpEndToEndSuite.Create(),

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
                    ToolHooksSuite.Create(),

                    // Usage telemetry: SQLite store schema, inserts, retention, and concurrent writers.
                    UsageStoreSuite.Create(),

                    // Usage telemetry: filter/breakdown by session label/tag (query-time join).
                    UsageMetadataFilterSuite.Create(),

                    // Usage telemetry: live REST endpoints over a seeded store.
                    UsageRoutesSuite.Create(),

                    // Mux.Publisher: manifest parsing/validation, publish args + checksums, channel
                    // driver rendering/command plans, and first-run startup registration recipes.
                    PublisherManifestSuite.Create(),
                    PublishServiceSuite.Create(),
                    PublisherDriversSuite.Create(),
                    StartupRegistrarSuite.Create(),

                    // Meta: the registry verifies itself (group coverage, duplicate ids, group hygiene).
                    SuiteRegistrySuite.Create()
                };
            }
        }

        /// <summary>
        /// A named, cross-runner group of suites: the same id selects the same suites in the console
        /// runner (<c>--group</c>), the xUnit hosts (<c>Groups.&lt;Name&gt;SuiteTests</c>), and the NUnit
        /// fixtures. Groups partition <see cref="All"/>; <see cref="SuiteRegistrySuite"/> enforces it.
        /// </summary>
        public sealed class GroupInfo
        {
            /// <summary>Creates a group descriptor.</summary>
            /// <param name="id">Lowercase, kebab-case id used on the command line (for example <c>skill-scripts</c>).</param>
            /// <param name="description">One-line summary of what the group covers.</param>
            /// <param name="suites">Factory producing fresh descriptors for the group's suites.</param>
            public GroupInfo(string id, string description, Func<List<TestSuiteDescriptor>> suites)
            {
                Id = id;
                Description = description;
                _Suites = suites;
            }

            private readonly Func<List<TestSuiteDescriptor>> _Suites;

            /// <summary>Gets the group id used on the command line and in host class names.</summary>
            public string Id { get; }

            /// <summary>Gets the one-line summary of what the group covers.</summary>
            public string Description { get; }

            /// <summary>Builds fresh descriptors for the group's suites.</summary>
            /// <returns>The group's suites, in canonical order.</returns>
            public List<TestSuiteDescriptor> CreateSuites()
            {
                return _Suites();
            }
        }

        /// <summary>
        /// Gets the registered groups in display order.
        /// </summary>
        public static IReadOnlyList<GroupInfo> Groups { get; } = new List<GroupInfo>
        {
            new GroupInfo("headless", "Non-interactive CLI contracts: print mode, input formats, run outcomes, approvals, endpoint switch, MCP headless", () => new List<TestSuiteDescriptor>
            {
                SingleTurnSuite.Create(), ToolUseSuite.Create(), PrintModeSuite.Create(),
                HeadlessFeaturesSuite.Create(), HeadlessOutputModesSuite.Create(), ToolGovernanceSuite.Create(),
                Phase4FeaturesSuite.Create(), InputFormatSuite.Create(), CliContractSuite.Create(),
                RunOutcomeSuite.Create(), ApprovalPolicySuite.Create(), MultiEditSuite.Create(),
                EndpointSwitchingSuite.Create(), McpIntegrationSuite.Create()
            }),
            new GroupInfo("core", "Agent runtime core: context window, compaction, conversation loop, jobs, leases, LLM bridge, telemetry, memory", () => new List<TestSuiteDescriptor>
            {
                ContextWindowManagerSuite.Create(), ApprovalRoutingSuite.Create(), PermissionPromptSuite.Create(),
                ConversationCompactionPlannerSuite.Create(), ConversationCompactorSuite.Create(), ConversationTrimCompactorSuite.Create(),
                AgentLoopOptionsSuite.Create(), ExternalToolProviderSuite.Create(), JobManagerSuite.Create(),
                WriteLeaseSuite.Create(), WriteLeaseIntegrationSuite.Create(), LlmBridgeSuite.Create(),
                TelemetryCoreSuite.Create(), TelemetryHostSuite.Create(), AgentEventSerializerSuite.Create(),
                MemorySuite.Create(), LoopSchedulerSuite.Create(), ProjectInstructionsSuite.Create(), FileContextSuite.Create(),
                PlanModeSuite.Create()
            }),
            new GroupInfo("tools", "Built-in tools and execution plumbing: file/glob/grep/edit/process/web tools, checkpoints, plugins, hooks, background processes", () => new List<TestSuiteDescriptor>
            {
                BuiltInToolRegistrySuite.Create(), ReadFileToolSuite.Create(), WriteFileToolSuite.Create(),
                GlobToolSuite.Create(), GrepToolSuite.Create(), EditFileToolSuite.Create(), MultiEditToolSuite.Create(),
                RunProcessToolSuite.Create(), WebRetrieveToolSuite.Create(), McpToolManagerSuite.Create(),
                WorkingDirectoryResolverSuite.Create(), WorktreeIsolationSuite.Create(), CheckpointSuite.Create(),
                PluginSuite.Create(), ToolHooksSuite.Create(), TerminalLaunchSuite.Create(),
                BackgroundProcessSuite.Create(), WebSearchServiceSuite.Create(), AskUserSuite.Create()
            }),
            new GroupInfo("cli", "Command plumbing: command runtime resolver, endpoint parser, structured output, the skill CLI verb, F1 menu", () => new List<TestSuiteDescriptor>
            {
                CommandRuntimeResolverSuite.Create(), EndpointCommandParserSuite.Create(),
                StructuredOutputFormatterSuite.Create(), CliCommandSuite.Create(), CommandMenuFormatterSuite.Create()
            }),
            new GroupInfo("settings", "Settings schema and loaders: endpoint config and auth placement, setup state, settings loader", () => new List<TestSuiteDescriptor>
            {
                EndpointConfigSuite.Create(), EndpointAuthPlacementSuite.Create(),
                SetupStateSuite.Create(), SettingsLoaderSuite.Create()
            }),
            new GroupInfo("server", "Embedded REST server: settings, routes, run registry, session titles, Markdown/HTML export", () => new List<TestSuiteDescriptor>
            {
                RestServerSettingsSuite.Create(), MuxServerRouteSuite.Create(), RunRegistrySuite.Create(),
                SessionTitleHelperSuite.Create(), SessionExporterSuite.Create()
            }),
            new GroupInfo("sessions", "Session persistence and lifecycle: store, service, manager verbs, metadata normalization", () => new List<TestSuiteDescriptor>
            {
                SessionStoreSuite.Create(), SessionServiceSuite.Create(),
                SessionManagerSuite.Create(), SessionMetadataNormalizerSuite.Create()
            }),
            new GroupInfo("desktop", "Desktop view-model logic: localization, formatters, turn/thread/conversation services, workspace, preferences", () => new List<TestSuiteDescriptor>
            {
                DesktopLocalizationSuite.Create(), DesktopFormattersSuite.Create(), DesktopTurnInfoSuite.Create(),
                DesktopEndpointSelectionSuite.Create(), ThreadServiceSuite.Create(), TurnProjectionSuite.Create(),
                ConversationStatsSuite.Create(), ConversationControllerSuite.Create(), CoreMappingSuite.Create(),
                SystemPromptResolverSuite.Create(), ConversationServiceSuite.Create(), EmbeddedServerSuite.Create(),
                WorkspaceSuite.Create(), CodeTokenizerSuite.Create(), DesktopPreferencesSuite.Create(),
                PromptHistorySuite.Create(), KeybindingEditorSuite.Create(), WelcomeQuipsSuite.Create(),
                DesktopPromptCatalogSuite.Create()
            }),
            new GroupInfo("tui", "Interactive terminal UI: shell, composer, sidebar, modals, frames, prompts, endpoint/MCP management, keybindings", () => new List<TestSuiteDescriptor>
            {
                TuiShellSuite.Create(), SeverityToastSuite.Create(), SlashCommandCompletionSuite.Create(),
                ProjectorSuite.Create(), FormModalsSuite.Create(), SidebarSuite.Create(), ComposerSuite.Create(),
                PromptsSuite.Create(), PromptCatalogSuite.Create(), PromptCatalogModalSuite.Create(),
                SurfaceWiringSuite.Create(), CommandSurfacesSuite.Create(), SettingsSuite.Create(),
                ModalsSuite.Create(), PersistenceUxSuite.Create(), FrameSnapshotSuite.Create(), PolishSuite.Create(),
                EndpointManagementSuite.Create(), OllamaImportSuite.Create(), EndpointModelListerSuite.Create(),
                McpManagementSuite.Create(), BoundaryLinesSuite.Create(), FileMentionsSuite.Create(),
                TaskModalSuite.Create(), KeybindingSuite.Create()
            }),
            new GroupInfo("usage", "Usage telemetry end to end: SQLite store, metadata filters, REST routes, window math, desktop analytics, TUI charts", () => new List<TestSuiteDescriptor>
            {
                UsageStoreSuite.Create(), UsageMetadataFilterSuite.Create(), UsageRoutesSuite.Create(),
                UsageWindowSuite.Create(), UsageAnalyticsSuite.Create(), UsageChartsModalSuite.Create()
            }),
            new GroupInfo("skills", "Skill platform: loader, provider, runtime, security, manager, defaults, project skills, selection", () => new List<TestSuiteDescriptor>
            {
                SkillLoaderSuite.Create(), SkillProviderSuite.Create(), SkillSecuritySuite.Create(),
                SkillRuntimeSuite.Create(), ExternalToolsBinderSuite.Create(), ToolRuntimeBinderSuite.Create(),
                SkillManagerSuite.Create(), SkillManagementSuite.Create(), SkillEditorCopySuite.Create(),
                SkillCategoriesSuite.Create(), SkillCommandSuite.Create(), DefaultSkillsSuite.Create(),
                ProjectSkillsSuite.Create(), SkillInvocationSuite.Create(), SkillListingSuite.Create(),
                SkillScriptRuntimeSuite.Create(), SkillSelectionEvalSuite.Create(), SkillSelectionLiveSuite.Create()
            }),
            new GroupInfo("skill-scripts", "Shipped skill scripts executed for real: toolchain, review, loops, script commands, audits, lint, upgrades, Docker (slow)", () => new List<TestSuiteDescriptor>
            {
                ToolchainSkillsSuite.Create(), ReviewSkillsSuite.Create(), LoopSkillsSuite.Create(),
                ImportedSkillsSuite.Create(), ScriptCommandsSuite1.Create(), ScriptCommandsSuite2.Create(),
                ScriptCommandsSuite3.Create(), ShippedSkillsSuite.Create(), DependencyAuditSuite.Create(),
                ScriptLintSuite.Create(), DatabaseSkillsSuite.Create(), UpgradeSkillsSuite.Create(),
                MobileLanguageSkillsSuite.Create(), WebFrameworkSkillsSuite.Create(), RuntimeSkillsSuite.Create(),
                InfraSkillsSuite.Create(), ApiContractSkillsSuite.Create(), DockerServicesSuite.Create()
            }),
            new GroupInfo("mcp", "MCP wiring: server, template binder, runtime, diagnostics, stdio/HTTP clients, config, end-to-end", () => new List<TestSuiteDescriptor>
            {
                McpServerSuite.Create(), McpTemplateBinderSuite.Create(), McpRuntimeSuite.Create(),
                McpDiagnosticsSuite.Create(), McpClientSuite.Create(), McpStdioClientSuite.Create(),
                McpConfigSuite.Create(), McpServerToolsSuite.Create(), McpEndToEndSuite.Create()
            }),
            new GroupInfo("tasks", "Background tasks and subagents: plan model, tools, events, orchestrator, subagent registry", () => new List<TestSuiteDescriptor>
            {
                TaskPlanSuite.Create(), TaskToolsSuite.Create(), TaskEventSuite.Create(),
                TaskOrchestratorSuite.Create(), SubagentSuite.Create()
            }),
            new GroupInfo("publisher", "Mux.Publisher: manifest, publish service, channel drivers, startup registration", () => new List<TestSuiteDescriptor>
            {
                PublisherManifestSuite.Create(), PublishServiceSuite.Create(),
                PublisherDriversSuite.Create(), StartupRegistrarSuite.Create()
            }),
            new GroupInfo("meta", "The registry verifies itself: group coverage, unique ids, well-formed group names", () => new List<TestSuiteDescriptor>
            {
                SuiteRegistrySuite.Create()
            })
        };

        /// <summary>
        /// Looks up a group by id, case-insensitively.
        /// </summary>
        /// <param name="id">The group id (for example <c>usage</c> or <c>skill-scripts</c>).</param>
        /// <returns>The group, or null when no group has that id.</returns>
        public static GroupInfo? FindGroup(string id)
        {
            foreach (GroupInfo group in Groups)
            {
                if (string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase)) return group;
            }

            return null;
        }

        /// <summary>
        /// Builds fresh descriptors for one group's suites.
        /// </summary>
        /// <param name="id">The group id.</param>
        /// <returns>The group's suites.</returns>
        /// <exception cref="ArgumentException">Thrown when no group has that id; the message lists the valid ids.</exception>
        public static List<TestSuiteDescriptor> Group(string id)
        {
            GroupInfo? group = FindGroup(id);
            if (group != null) return group.CreateSuites();

            List<string> ids = new List<string>();
            foreach (GroupInfo known in Groups) ids.Add(known.Id);
            throw new ArgumentException("Unknown group '" + id + "'. Valid groups: " + string.Join(", ", ids) + ".", nameof(id));
        }
    }
}
