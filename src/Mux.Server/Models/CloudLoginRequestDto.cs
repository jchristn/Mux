namespace Mux.Server.Models
{
    /// <summary>
    /// Body of <c>POST /v1.0/api/cloud/login</c>: sign this machine in to Mux Cloud. The password is sent to Mux Cloud
    /// once and never stored.
    /// </summary>
    public class CloudLoginRequestDto
    {
        /// <summary>Account email.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Account password.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Mux Cloud server URL, or null to keep the configured one (default <c>https://app.usemux.ai</c>).</summary>
        public string? ServerUrl { get; set; }

        /// <summary>Display name for this machine, or null for the configured name or the host name.</summary>
        public string? MachineName { get; set; }
    }
}
