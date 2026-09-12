namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the shared enum/telemetry mapping helpers promoted into Mux.Core so the front
    /// ends and routes stop hand-rolling them: <see cref="RoleEnumExtensions"/> (role ⇄ wire string),
    /// <see cref="AdapterTypeEnumExtensions"/> (adapter ⇄ kebab), and <see cref="UsageEvent.FromCall"/> /
    /// <see cref="UsageEvent.HostFromUrl"/>.
    /// </summary>
    public static class CoreMappingSuite
    {
        private const string SuiteId = "CoreMapping";

        /// <summary>
        /// Builds the core-mapping suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for mapping cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Shared enum + telemetry mappings",
                new List<TestCaseDescriptor>
                {
                    Case("RoleWireRoundTrips", "Role ⇄ wire string round-trips and parses tolerantly", (CancellationToken ct) =>
                    {
                        foreach (RoleEnum role in new[] { RoleEnum.System, RoleEnum.User, RoleEnum.Assistant, RoleEnum.Tool })
                        {
                            MuxAssert.AreEqual(role, RoleEnumExtensions.ParseRole(role.ToWire()), "round-trip " + role);
                        }

                        MuxAssert.AreEqual("assistant", RoleEnum.Assistant.ToWire(), "assistant wire");
                        MuxAssert.AreEqual(RoleEnum.System, RoleEnumExtensions.ParseRole("  SYSTEM "), "case/space tolerant");
                        MuxAssert.AreEqual(RoleEnum.User, RoleEnumExtensions.ParseRole(null), "null defaults to user");
                        MuxAssert.AreEqual(RoleEnum.User, RoleEnumExtensions.ParseRole("nonsense"), "unknown defaults to user");
                        return Task.CompletedTask;
                    }),

                    Case("AdapterKebabRoundTrips", "Adapter ⇄ kebab round-trips and accepts snake input", (CancellationToken ct) =>
                    {
                        foreach (AdapterTypeEnum a in new[] { AdapterTypeEnum.Ollama, AdapterTypeEnum.OpenAiCompatible, AdapterTypeEnum.AzureOpenAi, AdapterTypeEnum.Bedrock })
                        {
                            MuxAssert.AreEqual(a, AdapterTypeEnumExtensions.FromKebab(a.ToKebab()), "round-trip " + a);
                        }

                        MuxAssert.AreEqual("openai-compatible", AdapterTypeEnum.OpenAiCompatible.ToKebab(), "kebab form");
                        MuxAssert.AreEqual(AdapterTypeEnum.OpenAiCompatible, AdapterTypeEnumExtensions.FromKebab("openai_compatible"), "snake input accepted");
                        MuxAssert.AreEqual(AdapterTypeEnum.OpenAiCompatible, AdapterTypeEnumExtensions.FromKebab("nope"), "unknown falls back");
                        MuxAssert.AreEqual(AdapterTypeEnum.Ollama, AdapterTypeEnumExtensions.FromKebab("nope", AdapterTypeEnum.Ollama), "custom fallback");
                        return Task.CompletedTask;
                    }),

                    Case("HostFromUrlExtracts", "HostFromUrl returns the host or null", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("view.homedns.org", UsageEvent.HostFromUrl("http://view.homedns.org:8900/v1"), "host extracted");
                        MuxAssert.IsTrue(UsageEvent.HostFromUrl(null) == null, "null in null out");
                        MuxAssert.IsTrue(UsageEvent.HostFromUrl("not a url") == null, "non-url null");
                        return Task.CompletedTask;
                    }),

                    Case("FromCallWithNoMetricsUsesFallbacks", "FromCall with no call/usage yields zero tokens and fallback timing", (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = new EndpointConfig { Name = "e", Model = "m", BaseUrl = "http://localhost:11434", AdapterType = AdapterTypeEnum.Ollama };
                        UsageEvent u = UsageEvent.FromCall(endpoint, null, null, UsageCallKindEnum.Chat, "serve", success: true, fallbackTtftMs: 10, fallbackTotalMs: 25);

                        MuxAssert.AreEqual("e", u.EndpointName, "endpoint name");
                        MuxAssert.AreEqual("m", u.Model, "model from endpoint when no metrics");
                        MuxAssert.AreEqual("localhost", u.BaseHost, "host extracted");
                        MuxAssert.AreEqual(0, u.InputTokens, "no tokens without usage");
                        MuxAssert.AreEqual(10L, u.TimeToFirstTokenMs ?? -1, "ttft from fallback");
                        MuxAssert.AreEqual(25L, u.TotalMs ?? -1, "total from fallback");
                        MuxAssert.AreEqual(15L, u.StreamingMs ?? -1, "streaming = total - ttft");
                        MuxAssert.AreEqual("serve", u.Command, "command");
                        MuxAssert.IsTrue(u.Success, "success");
                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
