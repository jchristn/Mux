namespace Mux.Core.Cloud
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Sign this machine in to and out of Mux Cloud, shared by every surface (CLI, terminal UI, desktop app, local
    /// dashboard, VS Code). Signing in exchanges the email and password for a machine credential and saves it in
    /// <c>settings.json</c>; the password is never stored. Signing out revokes the machine in Mux Cloud (best effort)
    /// and deletes the credential.
    /// <para>Thread safety: safe to call concurrently; settings writes are last-writer-wins.</para>
    /// </summary>
    public static class CloudAccountService
    {
        #region Public-Members

        /// <summary>Notice every connect screen shows before signing in.</summary>
        public const string RemoteAccessNotice =
            "Anyone signed in to this Mux Cloud account can use this machine remotely, as if they were at it, including approving tools that edit files and run commands. To keep remote use read-only for those tools, turn remote tool approval off (mux cloud remote-approval off).";

        /// <summary>
        /// The remote-access notice with the way to turn remote approval off on a given surface.
        /// </summary>
        /// <param name="howToTurnOff">The command or control, for example <c>/cloud remote-approval off</c>.</param>
        /// <returns>Notice text.</returns>
        public static string RemoteAccessNoticeFor(string howToTurnOff)
        {
            return "Anyone signed in to this Mux Cloud account can use this machine remotely, as if they were at it, including approving tools that edit files and run commands. To keep remote use read-only for those tools, turn remote tool approval off (" + (string.IsNullOrWhiteSpace(howToTurnOff) ? "mux cloud remote-approval off" : howToTurnOff) + ").";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enroll this machine and save the credential.
        /// </summary>
        /// <param name="email">Account email.</param>
        /// <param name="password">Account password (sent once, never stored).</param>
        /// <param name="serverUrl">Server URL, or null to keep the configured one.</param>
        /// <param name="machineName">Display name, or null for the configured name or the host name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="client">Optional enrollment client (tests).</param>
        /// <returns>The saved cloud settings.</returns>
        /// <exception cref="ArgumentException">The email or password is blank.</exception>
        /// <exception cref="CloudEnrollmentException">The cloud refused the request or could not be reached.</exception>
        public static async Task<CloudSettings> LoginAsync(string email, string password, string? serverUrl, string? machineName, CancellationToken token, CloudEnrollmentClient? client = null)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            CloudSettings cloud = settings.Cloud;
            if (!string.IsNullOrWhiteSpace(serverUrl)) cloud.ServerUrl = serverUrl;
            string name = !string.IsNullOrWhiteSpace(machineName) ? machineName.Trim() : (cloud.MachineName ?? CloudMachineInfo.Hostname());

            CloudEnrollmentRequest request = new CloudEnrollmentRequest
            {
                Email = (email ?? string.Empty).Trim(),
                Password = password ?? string.Empty,
                MachineName = name,
                Hostname = CloudMachineInfo.Hostname(),
                Os = CloudMachineInfo.Os(),
                MuxVersion = CloudMachineInfo.MuxVersion(),
                Fingerprint = CloudMachineInfo.Fingerprint(SettingsLoader.GetConfigDirectory())
            };

            CloudEnrollmentResponse response = await (client ?? new CloudEnrollmentClient()).EnrollAsync(cloud.ServerUrl, request, cloud.AllowInvalidCertificates, token).ConfigureAwait(false);

            string serverUrlToSave = cloud.ServerUrl;
            return SettingsLoader.UpdateCloud(c =>
            {
                c.ServerUrl = serverUrlToSave;
                c.MachineId = response.MachineId;
                c.MachineSecret = response.MachineSecret;
                c.MachineName = string.IsNullOrWhiteSpace(response.MachineName) ? name : response.MachineName;
                c.AccountEmail = string.IsNullOrWhiteSpace(response.AccountEmail) ? request.Email : response.AccountEmail;
                c.Enabled = true;
            });
        }

        /// <summary>
        /// Sign this machine out: tell Mux Cloud first (best effort, so the cloud revokes the machine instead of listing
        /// it as offline), then delete the credential and turn the connection off. The local sign-out always happens,
        /// even when the cloud cannot be reached.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="client">Optional enrollment client (tests).</param>
        /// <returns>True when Mux Cloud confirmed the sign-out; false when it could not be reached.</returns>
        public static async Task<bool> LogoutAsync(CancellationToken token, CloudEnrollmentClient? client = null)
        {
            CloudSettings cloud = SettingsLoader.LoadSettings().Cloud;
            bool confirmed = false;
            if (cloud.IsEnrolled)
            {
                CloudEnrollmentClient signOut = client ?? new CloudEnrollmentClient { Timeout = TimeSpan.FromSeconds(10) };
                confirmed = await signOut.SignOutAsync(cloud.ServerUrl, cloud.MachineId ?? string.Empty, cloud.MachineSecret ?? string.Empty, cloud.AllowInvalidCertificates, token).ConfigureAwait(false);
            }

            Logout();
            return confirmed;
        }

        /// <summary>
        /// What to tell the user after <see cref="LogoutAsync"/>.
        /// </summary>
        /// <param name="confirmed">Whether Mux Cloud confirmed the sign-out.</param>
        /// <returns>Message.</returns>
        public static string LogoutMessage(bool confirmed)
        {
            return confirmed
                ? "Signed this machine out of Mux Cloud. It can no longer be used from your account until you sign in again."
                : "Signed this machine out locally. Mux Cloud could not be reached, so the machine stays listed in your account (offline) until you remove it there.";
        }

        /// <summary>
        /// Delete the machine credential and turn the connection off, without telling Mux Cloud. Prefer
        /// <see cref="LogoutAsync"/>, which also revokes the machine in the cloud.
        /// </summary>
        /// <returns>The saved cloud settings.</returns>
        public static CloudSettings Logout()
        {
            return SettingsLoader.UpdateCloud(c =>
            {
                c.Enabled = false;
                c.MachineSecret = null;
                c.MachineId = null;
                c.AccountEmail = null;
            });
        }

        /// <summary>
        /// The status to show when no process is running the tunnel: derived from settings alone.
        /// </summary>
        /// <param name="cloud">Cloud settings.</param>
        /// <param name="reason">Reason to show, or null.</param>
        /// <returns>Status.</returns>
        public static CloudStatus OfflineStatus(CloudSettings cloud, string? reason)
        {
            CloudSettings c = cloud ?? new CloudSettings();
            return new CloudStatus
            {
                State = CloudStateEnum.Disabled,
                Reason = reason ?? (c.IsEnrolled ? (c.Enabled ? "The mux agent is not running." : "The connection is turned off.") : "This machine is not signed in to Mux Cloud."),
                ServerUrl = c.ServerUrl,
                MachineId = c.MachineId ?? string.Empty,
                MachineName = c.MachineName ?? CloudMachineInfo.Hostname(),
                AccountEmail = c.AccountEmail ?? string.Empty,
                AllowRemoteToolApproval = c.AllowRemoteToolApproval,
                HostedHere = false
            };
        }

        #endregion
    }
}
