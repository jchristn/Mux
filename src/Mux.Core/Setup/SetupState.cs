namespace Mux.Core.Setup
{
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Shared logic for deciding whether the first-run setup wizard should be shown. Every surface (TUI,
    /// desktop, web, VS Code) uses this so the trigger is identical: the wizard is offered automatically when
    /// there is no usable endpoint AND the user has not already completed or dismissed setup
    /// (<see cref="MuxSettings.SetupCompleted"/>). A usable endpoint or a completed-setup flag each suppress
    /// it — the stricter of the two signals wins — and every surface also exposes a manual re-run entry point.
    /// </summary>
    public static class SetupState
    {
        /// <summary>
        /// Determines whether at least one configured endpoint is usable — that is, it names a model. An
        /// endpoint with no model cannot serve a request, so it does not count toward "already set up".
        /// </summary>
        /// <param name="endpoints">The configured endpoints, or null.</param>
        /// <returns>True when at least one endpoint names a model.</returns>
        public static bool HasUsableEndpoint(IEnumerable<EndpointConfig>? endpoints)
        {
            if (endpoints == null)
            {
                return false;
            }

            foreach (EndpointConfig endpoint in endpoints)
            {
                if (endpoint != null && !string.IsNullOrWhiteSpace(endpoint.Model))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the first-run setup wizard should be shown automatically: true only when there
        /// is no usable endpoint and setup has not previously been completed or dismissed.
        /// </summary>
        /// <param name="endpoints">The configured endpoints, or null.</param>
        /// <param name="setupCompleted">The persisted <see cref="MuxSettings.SetupCompleted"/> flag.</param>
        /// <returns>True when the wizard should be shown automatically.</returns>
        public static bool NeedsSetup(IEnumerable<EndpointConfig>? endpoints, bool setupCompleted)
        {
            return !setupCompleted && !HasUsableEndpoint(endpoints);
        }
    }
}
