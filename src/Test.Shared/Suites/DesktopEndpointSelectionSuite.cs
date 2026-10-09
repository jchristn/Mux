namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="EndpointSelection"/>: after the desktop endpoint list is reloaded, the picker
    /// keeps the endpoint in use, falls back to the default endpoint when the one in use was deleted or is unknown,
    /// then to the first endpoint, and selects nothing for an empty list.
    /// </summary>
    public static class DesktopEndpointSelectionSuite
    {
        #region Private-Members

        private const string SuiteId = "DesktopEndpointSelection";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the endpoint-selection suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the selection cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Desktop endpoint picker selection after reload",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "KeepsEndpointInUse", "The endpoint in use stays selected even when another one is the default", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = Build("ollama-local", "ollama-local", "gpt-oss-20b");
                        MuxAssert.AreEqual("gpt-oss-20b", EndpointSelection.Resolve(endpoints, "gpt-oss-20b")?.Name, "selection follows the endpoint in use");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MatchesNameIgnoringCase", "An endpoint name that differs only in case still matches", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = Build("Default", "Default", "GPT-OSS");
                        MuxAssert.AreEqual("GPT-OSS", EndpointSelection.Resolve(endpoints, "gpt-oss")?.Name, "case-insensitive match");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DeletedEndpointFallsBackToDefault", "A deleted endpoint falls back to the default endpoint", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = Build("gpt-oss-20b", "first", "gpt-oss-20b");
                        MuxAssert.AreEqual("gpt-oss-20b", EndpointSelection.Resolve(endpoints, "ollama-local")?.Name, "default chosen when ollama-local is gone");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NoDefaultFallsBackToFirst", "With no default endpoint, the first endpoint is chosen", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = Build(null, "alpha", "beta");
                        MuxAssert.AreEqual("alpha", EndpointSelection.Resolve(endpoints, "deleted")?.Name, "first endpoint");
                        MuxAssert.AreEqual("alpha", EndpointSelection.Resolve(endpoints, null)?.Name, "no current endpoint");
                        MuxAssert.AreEqual("alpha", EndpointSelection.Resolve(endpoints, "   ")?.Name, "blank current endpoint");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EmptyListSelectsNothing", "An empty or missing list selects nothing", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(EndpointSelection.Resolve(new List<EndpointConfig>(), "anything"), "empty list");
                        MuxAssert.IsNull(EndpointSelection.Resolve(null, "anything"), "null list");
                        return Task.CompletedTask;
                    })
                });
        }

        #endregion

        #region Private-Methods

        private static List<EndpointConfig> Build(string? defaultName, params string[] names)
        {
            List<EndpointConfig> endpoints = new List<EndpointConfig>();
            foreach (string name in names)
            {
                endpoints.Add(new EndpointConfig { Name = name, IsDefault = name == defaultName });
            }

            return endpoints;
        }

        #endregion
    }
}
