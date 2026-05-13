namespace Test.Xunit.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="EndpointConfig"/> class.
    /// Tests serialization round-trip, clamping behavior, and null-guard validation.
    /// </summary>
    public class EndpointConfigTests
    {
        #region Serialization

        /// <summary>
        /// Verifies that serializing and deserializing an EndpointConfig preserves all fields.
        /// </summary>
        [Fact]
        public void Serialization_RoundTrip_PreservesAllFields()
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
                Headers = new Dictionary<string, string> { { "Authorization", "Bearer sk-test-key" } },
                Quirks = new BackendQuirks
                {
                    SupportsParallelToolCalls = true,
                    AssembleToolCallDeltas = false
                }
            };

            string json = JsonSerializer.Serialize(original);
            EndpointConfig? deserialized = JsonSerializer.Deserialize<EndpointConfig>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(original.Name, deserialized!.Name);
            Assert.Equal(original.AdapterType, deserialized.AdapterType);
            Assert.Equal(original.BaseUrl, deserialized.BaseUrl);
            Assert.Equal(original.Model, deserialized.Model);
            Assert.Equal(original.IsDefault, deserialized.IsDefault);
            Assert.Equal(original.MaxTokens, deserialized.MaxTokens);
            Assert.Equal(original.Temperature, deserialized.Temperature);
            Assert.Equal(original.ContextWindow, deserialized.ContextWindow);
            Assert.Equal(original.Headers["Authorization"], deserialized.Headers["Authorization"]);
            Assert.NotNull(deserialized.Quirks);
            Assert.True(deserialized.Quirks!.SupportsParallelToolCalls);
            Assert.False(deserialized.Quirks.AssembleToolCallDeltas);
        }

        #endregion

        #region Clamping

        /// <summary>
        /// Verifies that MaxTokens values below the minimum are clamped to 1024.
        /// </summary>
        [Fact]
        public void MaxTokens_Clamped_ToRange()
        {
            EndpointConfig config = new EndpointConfig();

            config.MaxTokens = 100;
            Assert.Equal(1024, config.MaxTokens);

            config.MaxTokens = 200000;
            Assert.Equal(131072, config.MaxTokens);

            config.MaxTokens = 8192;
            Assert.Equal(8192, config.MaxTokens);
        }

        /// <summary>
        /// Verifies that Temperature values outside [0.0, 2.0] are clamped.
        /// </summary>
        [Fact]
        public void Temperature_Clamped_ToRange()
        {
            EndpointConfig config = new EndpointConfig();

            config.Temperature = -1.0;
            Assert.Equal(0.0, config.Temperature);

            config.Temperature = 5.0;
            Assert.Equal(2.0, config.Temperature);

            config.Temperature = 1.5;
            Assert.Equal(1.5, config.Temperature);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Verifies that setting the Name property to null throws an ArgumentNullException.
        /// </summary>
        [Fact]
        public void Name_NullOrEmpty_Throws()
        {
            EndpointConfig config = new EndpointConfig();
            Assert.Throws<ArgumentNullException>(() => config.Name = null!);
        }

        #endregion
    }
}
