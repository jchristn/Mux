namespace Mux.Cli.App
{
    /// <summary>
    /// What the Mux Cloud sign-in form collects. The password is passed to Mux Cloud once and never stored.
    /// </summary>
    public sealed class CloudLoginInput
    {
        /// <summary>Account email.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Account password.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Server URL, or empty for the configured one.</summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>Machine display name, or empty for the configured name or host name.</summary>
        public string MachineName { get; set; } = string.Empty;
    }
}
