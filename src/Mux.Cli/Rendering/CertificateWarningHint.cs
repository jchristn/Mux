namespace Mux.Cli.Rendering
{
    using System;
    using Mux.Core.Utility;

    /// <summary>
    /// Formats guidance for certificate-chain failures when TLS validation is enabled.
    /// </summary>
    public static class CertificateWarningHint
    {
        #region Public-Members

        /// <summary>
        /// Human-facing guidance shown for known certificate-chain failures.
        /// </summary>
        public const string Message = "You can suppress certificate warnings using --insecure.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determines whether the certificate warning hint should be shown.
        /// </summary>
        /// <param name="errorCode">The machine-readable error code, when available.</param>
        /// <param name="message">The human-readable error text.</param>
        /// <param name="ignoreCertErrors">True when mux is already bypassing certificate validation.</param>
        /// <returns>True if the hint should be shown.</returns>
        public static bool ShouldDisplay(string? errorCode, string? message, bool ignoreCertErrors)
        {
            if (ignoreCertErrors)
            {
                return false;
            }

            return ContainsSelfSignedCertificateChainError(errorCode)
                || ContainsSelfSignedCertificateChainError(message);
        }

        /// <summary>
        /// Writes the certificate warning hint to stderr when it applies.
        /// </summary>
        /// <param name="errorCode">The machine-readable error code, when available.</param>
        /// <param name="message">The human-readable error text.</param>
        /// <param name="ignoreCertErrors">True when mux is already bypassing certificate validation.</param>
        public static void WriteIfNeeded(string? errorCode, string? message, bool ignoreCertErrors)
        {
            if (ShouldDisplay(errorCode, message, ignoreCertErrors))
            {
                Console.Error.WriteLine(ConsoleMessageStyler.Notification(Message));
            }
        }

        #endregion

        #region Private-Methods

        private static bool ContainsSelfSignedCertificateChainError(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.Contains("SELF_SIGNED_CERT_IN_CHAIN", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("self-signed certificate in certificate chain", StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
