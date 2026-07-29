namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ApprovalHandler"/> covering auto-approve, deny, and interactive
    /// ask policies including promotion behavior. Ported from the <c>ApprovalHandlerTests</c> xUnit suite.
    /// </summary>
    public static class ApprovalHandlerSuite
    {
        private static ToolCall SampleToolCall()
        {
            return new ToolCall { Id = "call_test1", Name = "read_file", Arguments = "{\"path\": \"test.txt\"}" };
        }

        /// <summary>
        /// Builds the approval-handler suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the approval-handler cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ApprovalHandler",
                "Approval handler policy behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AutoApproveAlwaysReturnsTrue",
                        "AutoApprove policy returns true without prompting",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.AutoApprove);
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => Task.FromResult("this should not be called");
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(result, "auto-approve result");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "DenyAlwaysReturnsFalse",
                        "Deny policy returns false without prompting",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Deny);
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => Task.FromResult("this should not be called");
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsFalse(result, "deny result");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AskYesInputReturnsTrue",
                        "Ask policy with 'y' input returns true",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => Task.FromResult("y");
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(result, "ask-yes result");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AskNoInputReturnsFalse",
                        "Ask policy with 'no' input returns false",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => Task.FromResult("no");
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsFalse(result, "ask-no result");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AskAlwaysInputPromotesToAutoApprove",
                        "Ask policy with 'always' input approves and promotes to auto-approve",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
                            int callCount = 0;
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => { callCount++; return Task.FromResult("always"); };

                            bool first = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(first, "first approval");
                            MuxAssert.AreEqual(1, callCount, "prompt count after first");

                            bool second = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(second, "second approval");
                            MuxAssert.AreEqual(1, callCount, "prompt not called again after promotion");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AskEmptyInputDefaultsToTrue",
                        "Ask policy with empty input defaults to approval",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => Task.FromResult("");
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(result, "empty-input default");
                        }),

                    new TestCaseDescriptor(
                        "ApprovalHandler",
                        "AskAfterPromoteSkipsPrompt",
                        "Ask policy skips the prompt after PromoteToAutoApprove",
                        async (CancellationToken ct) =>
                        {
                            ApprovalHandler handler = new ApprovalHandler(ApprovalPolicyEnum.Ask);
                            handler.PromoteToAutoApprove();
                            int callCount = 0;
                            Func<ToolCall, Task<string>> promptFunc = (ToolCall tc) => { callCount++; return Task.FromResult("n"); };
                            bool result = await handler.RequestApprovalAsync(SampleToolCall(), promptFunc).ConfigureAwait(false);
                            MuxAssert.IsTrue(result, "post-promote approval");
                            MuxAssert.AreEqual(0, callCount, "prompt skipped after promote");
                        })
                });
        }
    }
}
