namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Setup;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SetupState"/> (the shared first-run-wizard trigger) and the persistence
    /// of <see cref="MuxSettings.SetupCompleted"/>.
    /// </summary>
    public static class SetupStateSuite
    {
        private const string SuiteId = "SetupState";

        /// <summary>
        /// Builds the setup-state suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the setup-state cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "First-run setup trigger and flag persistence",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "NoEndpointsNeedsSetup", "No endpoints and flag unset needs setup", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(SetupState.HasUsableEndpoint(null), "null endpoints not usable");
                        MuxAssert.IsFalse(SetupState.HasUsableEndpoint(new List<EndpointConfig>()), "empty endpoints not usable");
                        MuxAssert.IsTrue(SetupState.NeedsSetup(null, false), "needs setup when nothing configured");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "EndpointWithoutModelNotUsable", "An endpoint with no model does not count as usable", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "e", AdapterType = AdapterTypeEnum.OpenAi, BaseUrl = "http://x", Model = string.Empty }
                        };
                        MuxAssert.IsFalse(SetupState.HasUsableEndpoint(endpoints), "no-model endpoint not usable");
                        MuxAssert.IsTrue(SetupState.NeedsSetup(endpoints, false), "still needs setup");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "UsableEndpointSuppressesSetup", "A usable endpoint suppresses the wizard", (CancellationToken ct) =>
                    {
                        List<EndpointConfig> endpoints = new List<EndpointConfig>
                        {
                            new EndpointConfig { Name = "e", AdapterType = AdapterTypeEnum.OpenAi, BaseUrl = "http://x", Model = "gpt-5" }
                        };
                        MuxAssert.IsTrue(SetupState.HasUsableEndpoint(endpoints), "usable");
                        MuxAssert.IsFalse(SetupState.NeedsSetup(endpoints, false), "no setup needed with a usable endpoint");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "CompletedFlagSuppressesSetup", "The completed flag suppresses the wizard even with no endpoint", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(SetupState.NeedsSetup(null, true), "completed flag suppresses the wizard");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "SetupCompletedRoundTrips", "SetupCompleted round-trips and defaults to false", (CancellationToken ct) =>
                    {
                        MuxSettings fresh = new MuxSettings();
                        MuxAssert.IsFalse(fresh.SetupCompleted, "defaults to false");

                        MuxSettings original = new MuxSettings { SetupCompleted = true };
                        string json = JsonSerializer.Serialize(original);
                        MuxSettings? deserialized = JsonSerializer.Deserialize<MuxSettings>(json);
                        MuxAssert.IsNotNull(deserialized, "deserialized");
                        MuxAssert.IsTrue(deserialized!.SetupCompleted, "SetupCompleted survives round-trip");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
