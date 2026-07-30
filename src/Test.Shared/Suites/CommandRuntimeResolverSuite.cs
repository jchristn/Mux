namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="CommandRuntimeResolver"/> non-interactive approval-policy and
    /// settings-override resolution. Ported from the <c>CommandRuntimeResolverTests</c> xUnit suite.
    /// </summary>
    public static class CommandRuntimeResolverSuite
    {
        /// <summary>
        /// Builds the command-runtime-resolver suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the command-runtime-resolver cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "CommandRuntimeResolver",
                "Non-interactive command runtime resolution",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("CommandRuntimeResolver", "ResolveApprovalPolicyEndpointAutoApprove", "Endpoint AutoApproveTools yields AutoApprove", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings();
                        EndpointConfig endpoint = new EndpointConfig { AutoApproveTools = true };
                        ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint);
                        MuxAssert.AreEqual(ApprovalPolicyEnum.AutoApprove, result, "policy");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "ResolveApprovalPolicyExplicitDenyOverrides", "Explicit deny overrides endpoint AutoApprove", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { ApprovalPolicy = "deny" };
                        EndpointConfig endpoint = new EndpointConfig { AutoApproveTools = true };
                        ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint);
                        MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, result, "policy");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "ResolveApprovalPolicyNonInteractiveDefaultsToDeny", "No policy in non-interactive mode defaults to Deny", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings();
                        EndpointConfig endpoint = new EndpointConfig();
                        ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint, allowAskApproval: false);
                        MuxAssert.AreEqual(ApprovalPolicyEnum.Deny, result, "policy");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "ResolveApprovalPolicyInteractiveDefaultsToAsk", "No policy in interactive mode defaults to Ask", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings();
                        EndpointConfig endpoint = new EndpointConfig();
                        ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint, allowAskApproval: true);
                        MuxAssert.AreEqual(ApprovalPolicyEnum.Ask, result, "policy");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "ApplyMuxSettingsOverridesIgnoreCertErrors", "IgnoreCertErrors override enables the setting", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { IgnoreCertErrors = true };
                        MuxSettings muxSettings = new MuxSettings();
                        CommandRuntimeResolver.ApplyMuxSettingsOverrides(settings, muxSettings);
                        MuxAssert.IsTrue(muxSettings.IgnoreCertErrors, "IgnoreCertErrors enabled");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
