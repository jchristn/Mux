namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The available filter values for the usage dashboard: the distinct endpoints and models seen in the
    /// telemetry store, plus whether telemetry is enabled at all. When telemetry is disabled the dashboard
    /// renders an empty state explaining that recording is off.
    /// </summary>
    public class UsageFiltersDto
    {
        /// <summary>Whether usage telemetry is enabled and queryable on this server.</summary>
        public bool Enabled { get; set; }

        /// <summary>The distinct endpoint names seen in the store.</summary>
        public List<string> Endpoints { get; set; } = new List<string>();

        /// <summary>The distinct models seen in the store.</summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>The distinct session labels available to filter by.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>The distinct session tags available to filter by.</summary>
        public List<UsageTagDto> Tags { get; set; } = new List<UsageTagDto>();
    }

    /// <summary>
    /// A key/value session tag exposed to the dashboard filter controls.
    /// </summary>
    public class UsageTagDto
    {
        /// <summary>The normalized tag key.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The tag value.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// The result of deleting one or more usage events.
    /// </summary>
    public class UsageDeleteResult
    {
        /// <summary>The number of rows deleted (0 or 1 for a single-id delete).</summary>
        public int Deleted { get; set; }
    }
}
