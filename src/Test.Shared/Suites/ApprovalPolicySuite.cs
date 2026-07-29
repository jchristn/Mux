namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for approval-policy behavior during tool execution. Ported from the legacy
    /// <c>ApprovalPolicyTests</c> suite, whose cases were unimplemented placeholders; they are carried
    /// forward as skipped descriptors until real coverage lands (tracked by the v0.3.0 approval work).
    /// </summary>
    public static class ApprovalPolicySuite
    {
        /// <summary>
        /// Builds the approval-policy suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing the (skipped) approval-policy cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "ApprovalPolicy",
                "Approval policy behavior during tool execution",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "ApprovalPolicy",
                        "AutoApproveExecutesTools",
                        "AutoApprove policy executes tools without prompting",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Not yet implemented; covered by the v0.3.0 per-job approval routing work."),

                    new TestCaseDescriptor(
                        "ApprovalPolicy",
                        "DenyRejectsTools",
                        "Deny policy rejects all tool calls",
                        (CancellationToken ct) => Task.CompletedTask,
                        skip: true,
                        skipReason: "Not yet implemented; covered by the v0.3.0 per-job approval routing work.")
                });
        }
    }
}
