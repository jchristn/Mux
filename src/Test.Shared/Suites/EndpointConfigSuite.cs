namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="EndpointConfig"/> serialization round-trip, clamping, and
    /// null-guard validation. Ported from the <c>EndpointConfigTests</c> xUnit suite.
    /// </summary>
    public static class EndpointConfigSuite
    {
        /// <summary>
        /// Builds the endpoint-config suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the endpoint-config cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "EndpointConfig",
                "Endpoint config serialization, clamping, and validation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("EndpointConfig", "SerializationRoundTripPreservesAllFields", "Serializing and deserializing preserves all fields", (CancellationToken ct) =>
                    {
                        EndpointConfig original = new EndpointConfig
                        {
                            Name = "test-endpoint",
                            AdapterType = AdapterTypeEnum.OpenAi,
                            BaseUrl = "https://api.openai.com/v1",
                            Model = "gpt-4o",
                            IsDefault = true,
                            MaxTokens = 16384,
                            Temperature = 0.7,
                            ContextWindow = 128000,
                            MaxAgentIterations = 75,
                            AutoApproveTools = true,
                            Headers = new Dictionary<string, string> { { "Authorization", "Bearer sk-test-key" } },
                            Quirks = new BackendQuirks { SupportsParallelToolCalls = true, AssembleToolCallDeltas = false }
                        };

                        string json = JsonSerializer.Serialize(original);
                        EndpointConfig? deserialized = JsonSerializer.Deserialize<EndpointConfig>(json);

                        MuxAssert.IsNotNull(deserialized, "deserialized");
                        MuxAssert.AreEqual(original.Name, deserialized!.Name, "Name");
                        MuxAssert.AreEqual(original.AdapterType, deserialized.AdapterType, "AdapterType");
                        MuxAssert.AreEqual(original.BaseUrl, deserialized.BaseUrl, "BaseUrl");
                        MuxAssert.AreEqual(original.Model, deserialized.Model, "Model");
                        MuxAssert.AreEqual(original.IsDefault, deserialized.IsDefault, "IsDefault");
                        MuxAssert.AreEqual(original.MaxTokens, deserialized.MaxTokens, "MaxTokens");
                        MuxAssert.AreEqual(original.Temperature, deserialized.Temperature, "Temperature");
                        MuxAssert.AreEqual(original.ContextWindow, deserialized.ContextWindow, "ContextWindow");
                        MuxAssert.AreEqual(original.MaxAgentIterations, deserialized.MaxAgentIterations, "MaxAgentIterations");
                        MuxAssert.AreEqual(original.AutoApproveTools, deserialized.AutoApproveTools, "AutoApproveTools");
                        MuxAssert.AreEqual(original.Headers["Authorization"], deserialized.Headers["Authorization"], "Authorization header");
                        MuxAssert.IsNotNull(deserialized.Quirks, "Quirks");
                        MuxAssert.IsTrue(deserialized.Quirks!.SupportsParallelToolCalls, "SupportsParallelToolCalls");
                        MuxAssert.IsFalse(deserialized.Quirks.AssembleToolCallDeltas, "AssembleToolCallDeltas");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "ReasoningEffortRoundTrips", "Reasoning effort survives a serialization round-trip", (CancellationToken ct) =>
                    {
                        EndpointConfig original = new EndpointConfig
                        {
                            Name = "reasoning",
                            AdapterType = AdapterTypeEnum.OpenAi,
                            BaseUrl = "https://api.openai.com/v1",
                            Model = "gpt-5",
                            ReasoningEffort = new ReasoningEffortConfig
                            {
                                Level = ReasoningLevelEnum.High,
                                GeminiThinkingBudget = 16000
                            }
                        };

                        string json = JsonSerializer.Serialize(original);
                        EndpointConfig? deserialized = JsonSerializer.Deserialize<EndpointConfig>(json);

                        MuxAssert.IsNotNull(deserialized, "deserialized");
                        MuxAssert.IsNotNull(deserialized!.ReasoningEffort, "ReasoningEffort");
                        MuxAssert.AreEqual(ReasoningLevelEnum.High, deserialized.ReasoningEffort!.Level, "level");
                        MuxAssert.AreEqual(16000, deserialized.ReasoningEffort!.GeminiThinkingBudget, "budget");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "ReasoningEffortDefaultsToNull", "Reasoning effort defaults to null and is omitted from JSON", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig { Name = "n", BaseUrl = "u", Model = "m" };
                        MuxAssert.IsNull(config.ReasoningEffort, "default null");
                        string json = JsonSerializer.Serialize(config);
                        MuxAssert.IsFalse(json.Contains("reasoningEffort", System.StringComparison.Ordinal), "omitted when null");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "ReasoningEffortOverridesClamp", "Reasoning effort overrides clamp and normalize", (CancellationToken ct) =>
                    {
                        ReasoningEffortConfig config = new ReasoningEffortConfig
                        {
                            GeminiThinkingBudget = -5,
                            OpenAiValue = " HIGH ",
                            OllamaThink = "maybe"
                        };
                        MuxAssert.AreEqual(-1, config.GeminiThinkingBudget, "budget clamps to floor");
                        MuxAssert.AreEqual("high", config.OpenAiValue, "openai value normalized");
                        MuxAssert.IsNull(config.OllamaThink, "unrecognized ollama think reverts to null");

                        config.GeminiThinkingBudget = 999999;
                        MuxAssert.AreEqual(32768, config.GeminiThinkingBudget, "budget clamps to ceiling");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "MaxTokensClampedToRange", "MaxTokens values are clamped to range", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig();
                        config.MaxTokens = 100;
                        MuxAssert.AreEqual(1024, config.MaxTokens, "clamped low");
                        config.MaxTokens = 200000;
                        MuxAssert.AreEqual(131072, config.MaxTokens, "clamped high");
                        config.MaxTokens = 8192;
                        MuxAssert.AreEqual(8192, config.MaxTokens, "within range");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "TemperatureClampedToRange", "Temperature values outside [0.0, 2.0] are clamped", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig();
                        config.Temperature = -1.0;
                        MuxAssert.AreEqual(0.0, config.Temperature, "clamped low");
                        config.Temperature = 5.0;
                        MuxAssert.AreEqual(2.0, config.Temperature, "clamped high");
                        config.Temperature = 1.5;
                        MuxAssert.AreEqual(1.5, config.Temperature, "within range");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "MaxAgentIterationsNullableAndClamped", "MaxAgentIterations is nullable and clamps to range when set", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig();
                        MuxAssert.IsNull(config.MaxAgentIterations, "initially null");
                        config.MaxAgentIterations = 0;
                        MuxAssert.AreEqual(1, config.MaxAgentIterations, "clamped low");
                        config.MaxAgentIterations = 200;
                        MuxAssert.AreEqual(100, config.MaxAgentIterations, "clamped high");
                        config.MaxAgentIterations = 60;
                        MuxAssert.AreEqual(60, config.MaxAgentIterations, "within range");
                        config.MaxAgentIterations = null;
                        MuxAssert.IsNull(config.MaxAgentIterations, "reset to null");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointConfig", "NameNullThrows", "Setting Name to null throws ArgumentNullException", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig();
                        MuxAssert.Throws<ArgumentNullException>(() => config.Name = null!, "null name");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
