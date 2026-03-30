namespace Test.Xunit.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="AgentLoop"/> and <see cref="AgentLoopOptions"/> classes.
    /// Tests options validation, construction, and default behavior.
    /// Full integration tests with MockHttpServer are in Test.Automated.
    /// </summary>
    public class AgentLoopTests
    {
        #region AgentLoopOptions Validation

        /// <summary>
        /// Verifies that AgentLoopOptions requires a non-null endpoint.
        /// </summary>
        [Fact]
        public void AgentLoopOptions_NullEndpoint_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AgentLoopOptions(null!));
        }

        /// <summary>
        /// Verifies that AgentLoopOptions sets default values correctly.
        /// </summary>
        [Fact]
        public void AgentLoopOptions_Defaults_AreCorrect()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model"
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);

            Assert.Equal(endpoint, options.Endpoint);
            Assert.NotNull(options.ConversationHistory);
            Assert.Empty(options.ConversationHistory);
            Assert.Equal(string.Empty, options.SystemPrompt);
            Assert.Equal(ApprovalPolicyEnum.Ask, options.ApprovalPolicy);
            Assert.Equal(25, options.MaxIterations);
            Assert.False(options.Verbose);
            Assert.Null(options.AdditionalTools);
            Assert.Null(options.PromptUserFunc);
            Assert.Null(options.ExternalToolExecutor);
        }

        /// <summary>
        /// Verifies that MaxIterations is clamped to the valid range.
        /// </summary>
        [Fact]
        public void AgentLoopOptions_MaxIterations_Clamped()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model"
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);

            options.MaxIterations = 0;
            Assert.Equal(1, options.MaxIterations);

            options.MaxIterations = 200;
            Assert.Equal(100, options.MaxIterations);

            options.MaxIterations = 50;
            Assert.Equal(50, options.MaxIterations);
        }

        /// <summary>
        /// Verifies that setting ConversationHistory to null results in an empty list.
        /// </summary>
        [Fact]
        public void AgentLoopOptions_NullConversationHistory_BecomesEmptyList()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model"
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);
            options.ConversationHistory = null!;

            Assert.NotNull(options.ConversationHistory);
            Assert.Empty(options.ConversationHistory);
        }

        /// <summary>
        /// Verifies that setting SystemPrompt to null results in an empty string.
        /// </summary>
        [Fact]
        public void AgentLoopOptions_NullSystemPrompt_BecomesEmpty()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model"
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);
            options.SystemPrompt = null!;

            Assert.Equal(string.Empty, options.SystemPrompt);
        }

        #endregion

        #region AgentLoop Construction

        /// <summary>
        /// Verifies that AgentLoop requires a non-null options argument.
        /// </summary>
        [Fact]
        public void AgentLoop_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AgentLoop(null!));
        }

        /// <summary>
        /// Verifies that AgentLoop can be constructed and disposed without errors.
        /// </summary>
        [Fact]
        public void AgentLoop_ConstructAndDispose_NoErrors()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.Ollama
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);

            using (AgentLoop loop = new AgentLoop(options))
            {
                Assert.NotNull(loop);
            }
        }

        /// <summary>
        /// Verifies that RunAsync throws when given an empty prompt.
        /// </summary>
        [Fact]
        public async Task AgentLoop_EmptyPrompt_Throws()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:11434",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.Ollama
            };

            AgentLoopOptions options = new AgentLoopOptions(endpoint);

            using (AgentLoop loop = new AgentLoop(options))
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                {
                    await foreach (AgentEvent agentEvent in loop.RunAsync(""))
                    {
                        // Should not reach here
                    }
                });
            }
        }

        #endregion
    }
}
