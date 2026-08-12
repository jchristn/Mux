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
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "EffortFlagSetsEndpointLevel", "--effort sets the endpoint reasoning level", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { Effort = "high" };
                        EndpointConfig endpoint = new EndpointConfig();
                        CommandRuntimeResolver.ApplyReasoningOverride(endpoint, settings);
                        MuxAssert.IsNotNull(endpoint.ReasoningEffort, "ReasoningEffort set");
                        MuxAssert.AreEqual(ReasoningLevelEnum.High, endpoint.ReasoningEffort!.Level, "level");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "EffortOffClearsEndpointLevel", "--effort off disables even when the endpoint sets a level", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { Effort = "off" };
                        EndpointConfig endpoint = new EndpointConfig
                        {
                            ReasoningEffort = new ReasoningEffortConfig { Level = ReasoningLevelEnum.High }
                        };
                        CommandRuntimeResolver.ApplyReasoningOverride(endpoint, settings);
                        MuxAssert.IsNull(endpoint.ReasoningEffort, "ReasoningEffort cleared");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "EffortProviderOverrideAppliesWhenLevelActive", "Provider overrides apply on top of an existing endpoint level", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { EffortGeminiBudget = 16000 };
                        EndpointConfig endpoint = new EndpointConfig
                        {
                            ReasoningEffort = new ReasoningEffortConfig { Level = ReasoningLevelEnum.High }
                        };
                        CommandRuntimeResolver.ApplyReasoningOverride(endpoint, settings);
                        MuxAssert.AreEqual(16000, endpoint.ReasoningEffort!.GeminiThinkingBudget, "budget override");
                        MuxAssert.AreEqual(ReasoningLevelEnum.High, endpoint.ReasoningEffort!.Level, "level preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "EffortProviderOverrideInertWithoutLevel", "Provider overrides stay inert with no active level", (CancellationToken ct) =>
                    {
                        PrintSettings settings = new PrintSettings { EffortGeminiBudget = 16000 };
                        EndpointConfig endpoint = new EndpointConfig();
                        CommandRuntimeResolver.ApplyReasoningOverride(endpoint, settings);
                        MuxAssert.IsNull(endpoint.ReasoningEffort, "no reasoning config without a level");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CommandRuntimeResolver", "HasReasoningOverrideDetectsFlags", "HasReasoningOverride reflects the effort flags", (CancellationToken ct) =>
                    {
                        MuxAssert.IsFalse(CommandRuntimeResolver.HasReasoningOverride(new PrintSettings()), "none");
                        MuxAssert.IsTrue(CommandRuntimeResolver.HasReasoningOverride(new PrintSettings { Effort = "low" }), "level flag");
                        MuxAssert.IsTrue(CommandRuntimeResolver.HasReasoningOverride(new PrintSettings { EffortOllamaThink = "high" }), "provider flag");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
