namespace Mux.Core.Cloud
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of <c>POST /v1.0/api/machines/enroll</c> on Mux Cloud. The password is used for this one request and
    /// never stored on the machine.
    /// </summary>
    public class CloudEnrollmentRequest
    {
        /// <summary>Account email.</summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Account password.</summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Display name for this machine.</summary>
        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = string.Empty;

        /// <summary>Host name.</summary>
        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = string.Empty;

        /// <summary>Operating system description.</summary>
        [JsonPropertyName("os")]
        public string Os { get; set; } = string.Empty;

        /// <summary>Installed mux version.</summary>
        [JsonPropertyName("muxVersion")]
        public string MuxVersion { get; set; } = string.Empty;

        /// <summary>
        /// Stable machine fingerprint (lowercase hex SHA-256 of host name and config directory). Re-enrolling the same
        /// fingerprint for the same account keeps the machine identifier and rotates the secret.
        /// </summary>
        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; set; } = string.Empty;
    }
}
