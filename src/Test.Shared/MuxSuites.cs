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
                    LineBufferSuite.Create(),
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

                    // Mux.Core / Llm unit suites (ported from Test.Xunit/Llm).
                    OpenAiAdapterSuite.Create(),
                    OllamaAdapterSuite.Create(),
                    LlmClientSuite.Create(),
                    GenericOpenAiAdapterSuite.Create(),

                    // Settings + non-interactive CLI command unit suites (ported from Test.Xunit).
                    EndpointConfigSuite.Create(),
                    SettingsLoaderSuite.Create(),
                    SessionTitleHelperSuite.Create(),
                    CommandRuntimeResolverSuite.Create(),
                    EndpointCommandParserSuite.Create(),
                    StructuredOutputFormatterSuite.Create(),
                    CliCommandSuite.Create(),

                    // Mux.Search unit suite (ported from Test.Xunit/Search).
                    WebSearchServiceSuite.Create()
                };
            }
        }
    }
}
