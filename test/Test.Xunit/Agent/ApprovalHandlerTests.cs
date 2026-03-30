namespace Test.Xunit.Agent
{
    using System;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="ApprovalHandler"/> class.
    /// Tests auto-approve, deny, and interactive ask policies including promotion behavior.
    /// </summary>
    public class ApprovalHandlerTests
    {
        #region Private-Members

        private readonly ToolCall _SampleToolCall = new ToolCall
        {
            Id = "call_test1",
            Name = "read_file",
            Arguments = "{\"path\": \"test.txt\"}"
        };

        #endregion

        #region AutoApprove

        /// <summary>
        /// Verifies that the AutoApprove policy always returns true without prompting.
        /// </summary>
        [Fact]
        public async Task AutoApprove_AlwaysReturnsTrue()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.AutoApprove);

            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
                Task.FromResult("this should not be called");

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.True(result);
        }

        #endregion

        #region Deny

        /// <summary>
        /// Verifies that the Deny policy always returns false without prompting.
        /// </summary>
        [Fact]
        public async Task Deny_AlwaysReturnsFalse()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Deny);

            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
                Task.FromResult("this should not be called");

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.False(result);
        }

        #endregion

        #region Ask

        /// <summary>
        /// Verifies that a "y" response results in approval.
        /// </summary>
        [Fact]
        public async Task Ask_YesInput_ReturnsTrue()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);

            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
                Task.FromResult("y");

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.True(result);
        }

        /// <summary>
        /// Verifies that a "no" response results in denial.
        /// </summary>
        [Fact]
        public async Task Ask_NoInput_ReturnsFalse()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);

            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
                Task.FromResult("no");

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.False(result);
        }

        /// <summary>
        /// Verifies that an "always" response approves and promotes to auto-approve for future calls.
        /// </summary>
        [Fact]
        public async Task Ask_AlwaysInput_PromotesToAutoApprove()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);

            int callCount = 0;
            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
            {
                callCount++;
                return Task.FromResult("always");
            };

            bool first = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);
            Assert.True(first);
            Assert.Equal(1, callCount);

            // Second call should auto-approve without prompting
            bool second = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);
            Assert.True(second);
            Assert.Equal(1, callCount); // promptFunc should not be called again
        }

        /// <summary>
        /// Verifies that empty input defaults to approval.
        /// </summary>
        [Fact]
        public async Task Ask_EmptyInput_DefaultsToTrue()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);

            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
                Task.FromResult("");

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.True(result);
        }

        /// <summary>
        /// Verifies that after PromoteToAutoApprove is called, the handler skips the prompt.
        /// </summary>
        [Fact]
        public async Task Ask_AfterPromote_SkipsPrompt()
        {
            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
            handler.PromoteToAutoApprove();

            int callCount = 0;
            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) =>
            {
                callCount++;
                return Task.FromResult("n");
            };

            bool result = await handler.RequestApprovalAsync(_SampleToolCall, promptFunc);

            Assert.True(result);
            Assert.Equal(0, callCount);
        }

        #endregion
    }
}
