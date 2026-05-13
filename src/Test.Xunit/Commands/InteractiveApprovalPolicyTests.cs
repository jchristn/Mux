namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for interactive approval policy resolution.
    /// </summary>
    public class InteractiveApprovalPolicyTests
    {
        [Fact]
        public void ResolveApprovalPolicy_Yolo_ReturnsAutoApprove()
        {
            InteractiveSettings settings = new InteractiveSettings
            {
                Yolo = true
            };

            ApprovalPolicyEnum result = InteractiveCommand.ResolveApprovalPolicy(settings, new MuxSettings());

            Assert.Equal(ApprovalPolicyEnum.AutoApprove, result);
        }

        [Fact]
        public void ResolveApprovalPolicy_AutoString_ReturnsAutoApprove()
        {
            InteractiveSettings settings = new InteractiveSettings
            {
                ApprovalPolicy = "auto"
            };

            ApprovalPolicyEnum result = InteractiveCommand.ResolveApprovalPolicy(settings, new MuxSettings());

            Assert.Equal(ApprovalPolicyEnum.AutoApprove, result);
        }

        [Fact]
        public void ResolveApprovalPolicy_DefaultAutoSetting_ReturnsAutoApprove()
        {
            MuxSettings muxSettings = new MuxSettings
            {
                DefaultApprovalPolicy = "auto"
            };

            ApprovalPolicyEnum result = InteractiveCommand.ResolveApprovalPolicy(new InteractiveSettings(), muxSettings);

            Assert.Equal(ApprovalPolicyEnum.AutoApprove, result);
        }
    }
}
