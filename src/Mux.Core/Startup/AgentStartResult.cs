namespace Mux.Core.Startup
{
    /// <summary>
    /// The outcome of starting the tray agent (see <see cref="AgentLauncher.Start"/>).
    /// </summary>
    public enum AgentStartResult
    {
        /// <summary>An agent from this build was already running, so nothing was started.</summary>
        AlreadyRunning,

        /// <summary>The agent was started (replacing one from another version, if any).</summary>
        Started,

        /// <summary>No agent executable was found (see <see cref="AgentLocator"/>).</summary>
        NotFound,

        /// <summary>An agent executable was found but could not be started.</summary>
        Failed
    }
}
