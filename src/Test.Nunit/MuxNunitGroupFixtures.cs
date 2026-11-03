namespace Test.Nunit.Groups
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using global::NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Builds the NUnit test-case source for one named <see cref="MuxSuites"/> group. The group id
    /// selects the same suites in every runner, so <c>--where "test ~ Groups.Usage"</c> matches what
    /// <c>--group usage</c> runs in the console runner and the xUnit hosts.
    /// </summary>
    internal static class GroupCaseSource
    {
        /// <summary>
        /// Gets the Touchstone cases of one group as an NUnit test-case source.
        /// </summary>
        /// <param name="groupId">The group id registered in <see cref="MuxSuites.Groups"/>.</param>
        /// <returns>A test-case source yielding the group's cases.</returns>
        public static IEnumerable For(string groupId)
        {
            return new TouchstoneTestCaseSource(MuxSuites.Group(groupId));
        }
    }

    /// <summary>Base for the per-group fixtures: runs each case of the group.</summary>
    public abstract class GroupSuiteFixture
    {
        /// <summary>
        /// Executes a single Touchstone case.
        /// </summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        protected static async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Headless CLI contract suites (group: headless).</summary>
    [TestFixture]
    public sealed class HeadlessSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("headless");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Agent runtime core suites (group: core).</summary>
    [TestFixture]
    public sealed class CoreSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("core");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Built-in tool suites (group: tools).</summary>
    [TestFixture]
    public sealed class ToolsSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("tools");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Command plumbing suites (group: cli).</summary>
    [TestFixture]
    public sealed class CliSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("cli");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Settings schema suites (group: settings).</summary>
    [TestFixture]
    public sealed class SettingsSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("settings");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>REST server suites (group: server).</summary>
    [TestFixture]
    public sealed class ServerSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("server");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Session persistence suites (group: sessions).</summary>
    [TestFixture]
    public sealed class SessionsSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("sessions");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Desktop view-model suites (group: desktop).</summary>
    [TestFixture]
    public sealed class DesktopSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("desktop");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Interactive TUI suites (group: tui).</summary>
    [TestFixture]
    public sealed class TuiSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("tui");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Usage telemetry suites (group: usage).</summary>
    [TestFixture]
    public sealed class UsageSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("usage");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Skill platform suites (group: skills).</summary>
    [TestFixture]
    public sealed class SkillsSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("skills");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Shipped skill script suites (group: skill-scripts).</summary>
    [TestFixture]
    public sealed class SkillScriptsSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("skill-scripts");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>MCP wiring suites (group: mcp).</summary>
    [TestFixture]
    public sealed class McpSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("mcp");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Background task and subagent suites (group: tasks).</summary>
    [TestFixture]
    public sealed class TasksSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("tasks");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Publisher suites (group: publisher).</summary>
    [TestFixture]
    public sealed class PublisherSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("publisher");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }

    /// <summary>Registry self-verification suite (group: meta).</summary>
    [TestFixture]
    public sealed class MetaSuiteFixture : GroupSuiteFixture
    {
        private static IEnumerable TestCases()
        {
            return GroupCaseSource.For("meta");
        }

        /// <summary>Executes one case of the group.</summary>
        /// <param name="testCase">The descriptor supplied by the test-case source.</param>
        /// <returns>A task representing the asynchronous test run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await Run(testCase).ConfigureAwait(false);
        }
    }
}
