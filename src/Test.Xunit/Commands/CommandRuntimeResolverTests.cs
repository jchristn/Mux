namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for non-interactive approval policy resolution.
    /// </summary>
    public class CommandRuntimeResolverTests
    {
        [Fact]
        public void ResolveApprovalPolicy_EndpointAutoApprove_ReturnsAutoApprove()
        {
            PrintSettings settings = new PrintSettings();
            EndpointConfig endpoint = new EndpointConfig
            {
                AutoApproveTools = true
            };

            ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint);

            Assert.Equal(ApprovalPolicyEnum.AutoApprove, result);
        }

        [Fact]
        public void ResolveApprovalPolicy_ExplicitDeny_OverridesEndpointAutoApprove()
        {
            PrintSettings settings = new PrintSettings
            {
                ApprovalPolicy = "deny"
            };
            EndpointConfig endpoint = new EndpointConfig
            {
                AutoApproveTools = true
            };

            ApprovalPolicyEnum result = CommandRuntimeResolver.ResolveApprovalPolicy(settings, endpoint);

            Assert.Equal(ApprovalPolicyEnum.Deny, result);
        }
    }
}
