namespace Test.Xunit.Rendering
{
    using global::Xunit;
    using Mux.Cli.Rendering;

    /// <summary>
    /// Unit tests for certificate warning hint detection.
    /// </summary>
    public class CertificateWarningHintTests
    {
        /// <summary>
        /// Verifies that the known Node/Playwright certificate error code displays the hint.
        /// </summary>
        [Fact]
        public void ShouldDisplay_KnownSelfSignedCertificateCode_ReturnsTrue()
        {
            bool result = CertificateWarningHint.ShouldDisplay(
                "SELF_SIGNED_CERT_IN_CHAIN",
                null,
                ignoreCertErrors: false);

            Assert.True(result);
        }

        /// <summary>
        /// Verifies that the known self-signed certificate message displays the hint.
        /// </summary>
        [Fact]
        public void ShouldDisplay_KnownSelfSignedCertificateMessage_ReturnsTrue()
        {
            bool result = CertificateWarningHint.ShouldDisplay(
                null,
                "Error: self-signed certificate in certificate chain",
                ignoreCertErrors: false);

            Assert.True(result);
        }

        /// <summary>
        /// Verifies that the hint is suppressed when TLS bypass is already enabled.
        /// </summary>
        [Fact]
        public void ShouldDisplay_WhenIgnoreCertErrorsEnabled_ReturnsFalse()
        {
            bool result = CertificateWarningHint.ShouldDisplay(
                "SELF_SIGNED_CERT_IN_CHAIN",
                "Error: self-signed certificate in certificate chain",
                ignoreCertErrors: true);

            Assert.False(result);
        }

        /// <summary>
        /// Verifies that unrelated errors do not display certificate guidance.
        /// </summary>
        [Fact]
        public void ShouldDisplay_UnrelatedError_ReturnsFalse()
        {
            bool result = CertificateWarningHint.ShouldDisplay(
                "llm_connection_error",
                "Connection refused",
                ignoreCertErrors: false);

            Assert.False(result);
        }

        /// <summary>
        /// Verifies the human-facing guidance stays concise and points to the short flag.
        /// </summary>
        [Fact]
        public void Message_UsesShortInsecureFlag()
        {
            Assert.Equal(
                "You can suppress certificate warnings using --insecure.",
                CertificateWarningHint.Message);
        }
    }
}
