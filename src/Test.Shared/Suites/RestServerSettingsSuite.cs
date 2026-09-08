namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="RestServerSettings"/> defaults, clamping, and round-trip through
    /// <see cref="MuxSettings"/>.
    /// </summary>
    public static class RestServerSettingsSuite
    {
        /// <summary>
        /// Builds the REST-server-settings suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "RestServerSettings",
                "REST server settings defaults, clamping, and round-trip",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RestServerSettings", "Defaults", "Defaults are loopback and opt-in", (CancellationToken ct) =>
                    {
                        RestServerSettings settings = new RestServerSettings();
                        MuxAssert.IsFalse(settings.Enabled, "Enabled");
                        MuxAssert.AreEqual("127.0.0.1", settings.Hostname, "Hostname");
                        MuxAssert.AreEqual(8710, settings.Port, "Port");
                        MuxAssert.IsFalse(settings.Ssl, "Ssl");
                        MuxAssert.IsNull(settings.ApiKey, "ApiKey");
                        MuxAssert.AreEqual("*", settings.CorsAllowOrigin, "CorsAllowOrigin");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RestServerSettings", "PortClamped", "Port is clamped to 1-65535", (CancellationToken ct) =>
                    {
                        RestServerSettings settings = new RestServerSettings();
                        settings.Port = 70000;
                        MuxAssert.AreEqual(65535, settings.Port, "PortHigh");
                        settings.Port = 0;
                        MuxAssert.AreEqual(1, settings.Port, "PortLow");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RestServerSettings", "BlankApiKeyBecomesNull", "Blank/whitespace API key normalizes to null", (CancellationToken ct) =>
                    {
                        RestServerSettings settings = new RestServerSettings();
                        settings.ApiKey = "   ";
                        MuxAssert.IsNull(settings.ApiKey, "ApiKey");
                        settings.ApiKey = "mux_abc";
                        MuxAssert.AreEqual("mux_abc", settings.ApiKey, "ApiKeySet");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RestServerSettings", "BlankHostnameFallsBack", "Blank hostname falls back to loopback", (CancellationToken ct) =>
                    {
                        RestServerSettings settings = new RestServerSettings();
                        settings.Hostname = "";
                        MuxAssert.AreEqual("127.0.0.1", settings.Hostname, "Hostname");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RestServerSettings", "RoundTripThroughMuxSettings", "MuxSettings.Rest round-trips through JSON", (CancellationToken ct) =>
                    {
                        MuxSettings original = new MuxSettings();
                        original.Rest.Enabled = true;
                        original.Rest.Hostname = "127.0.0.1";
                        original.Rest.Port = 9123;
                        original.Rest.ApiKey = "mux_secret";
                        original.Rest.CorsAllowOrigin = "https://example.test";

                        string json = JsonSerializer.Serialize(original);
                        MuxSettings? restored = JsonSerializer.Deserialize<MuxSettings>(json);

                        MuxAssert.IsNotNull(restored, "restored");
                        MuxAssert.IsTrue(restored!.Rest.Enabled, "Enabled");
                        MuxAssert.AreEqual(9123, restored.Rest.Port, "Port");
                        MuxAssert.AreEqual("mux_secret", restored.Rest.ApiKey, "ApiKey");
                        MuxAssert.AreEqual("https://example.test", restored.Rest.CorsAllowOrigin, "CorsAllowOrigin");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("RestServerSettings", "DefaultRestNotNull", "A fresh MuxSettings has a non-null Rest block", (CancellationToken ct) =>
                    {
                        MuxSettings settings = new MuxSettings();
                        MuxAssert.IsNotNull(settings.Rest, "Rest");
                        MuxAssert.AreEqual(8710, settings.Rest.Port, "DefaultPort");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
