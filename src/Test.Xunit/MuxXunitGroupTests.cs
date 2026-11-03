namespace Test.Xunit.Groups
{
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Test.Shared;
    using Touchstone.Core;

    /// <summary>
    /// Builds the xUnit theory data for one named <see cref="MuxSuites"/> group: one row per
    /// non-skipped case. The group id selects the same suites in every runner, so
    /// <c>--filter "FullyQualifiedName~Groups.Usage"</c> matches what <c>--group usage</c>
    /// runs in the console runner and the NUnit fixtures.
    /// </summary>
    internal static class GroupCaseData
    {
        /// <summary>
        /// Gets the non-skipped Touchstone cases of one group as xUnit theory data.
        /// </summary>
        /// <param name="groupId">The group id registered in <see cref="MuxSuites.Groups"/>.</param>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> For(string groupId)
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in MuxSuites.Group(groupId))
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip)
                    {
                        data.Add(testCase);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>Base for the per-group theory hosts: runs each non-skipped case of the group.</summary>
    public abstract class GroupSuiteTests
    {
        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        protected static async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }

    /// <summary>Headless CLI contract suites (group: headless).</summary>
    public sealed class HeadlessSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("headless");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Agent runtime core suites (group: core).</summary>
    public sealed class CoreSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("core");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Built-in tool suites (group: tools).</summary>
    public sealed class ToolsSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("tools");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Command plumbing suites (group: cli).</summary>
    public sealed class CliSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("cli");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Settings schema suites (group: settings).</summary>
    public sealed class SettingsSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("settings");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>REST server suites (group: server).</summary>
    public sealed class ServerSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("server");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Session persistence suites (group: sessions).</summary>
    public sealed class SessionsSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("sessions");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Desktop view-model suites (group: desktop).</summary>
    public sealed class DesktopSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("desktop");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Interactive TUI suites (group: tui).</summary>
    public sealed class TuiSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("tui");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Usage telemetry suites (group: usage).</summary>
    public sealed class UsageSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("usage");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Skill platform suites (group: skills).</summary>
    public sealed class SkillsSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("skills");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Shipped skill script suites (group: skill-scripts).</summary>
    public sealed class SkillScriptsSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("skill-scripts");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>MCP wiring suites (group: mcp).</summary>
    public sealed class McpSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("mcp");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Background task and subagent suites (group: tasks).</summary>
    public sealed class TasksSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("tasks");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Publisher suites (group: publisher).</summary>
    public sealed class PublisherSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("publisher");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }

    /// <summary>Registry self-verification suite (group: meta).</summary>
    public sealed class MetaSuiteTests : GroupSuiteTests
    {
        /// <summary>Gets the theory rows for this group.</summary>
        /// <returns>Theory data containing one row per non-skipped case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return GroupCaseData.For("meta");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the theory-data source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase);
        }
    }
}
